import Fastify, { type FastifyInstance, type FastifyReply, type FastifyRequest } from 'fastify';
import { randomUUID } from 'node:crypto';
import {
  defaultConfigPath,
  saveConfiguration,
  type DataSource,
  type IndexConfiguration,
  type IndexRunLog,
  type PluginInfoDto,
  type SearchQuery,
} from '@quaero/core';
import type { DaemonRuntime } from './runtime.js';

export interface ServerOptions {
  runtime: DaemonRuntime;
  configPath?: string;
  logger?: boolean;
}

const DEFAULT_CRON = '*/30 * * * *';

interface DataSourceInput {
  id?: string;
  name?: string;
  pluginModule?: string;
  pluginExport?: string;
  enabled?: boolean;
  cronSchedule?: string;
  settings?: Record<string, string>;
}

export function buildServer(options: ServerOptions): FastifyInstance {
  const { runtime } = options;
  const configPath = options.configPath ?? defaultConfigPath();
  const app = Fastify({ logger: options.logger ?? false });

  app.get('/', async () => ({
    name: 'quaero-daemon',
    version: '0.2.0',
    state: runtime.indexingService.state,
    lastRunTime: runtime.indexingService.lastRunTime,
    lastError: runtime.indexingService.lastError,
    documentCount: runtime.indexStore.getDocumentCount(),
  }));

  app.get('/api/config', async () => publicConfig(runtime.config));

  app.put(
    '/api/config/server-base-url',
    async (req: FastifyRequest, reply: FastifyReply) => {
      const body = (req.body ?? {}) as { serverBaseUrl?: string };
      const url = (body.serverBaseUrl ?? '').trim();
      if (!url) return reply.code(400).send({ error: 'serverBaseUrl is required' });
      runtime.config.serverBaseUrl = url;
      saveConfiguration(runtime.config, configPath);
      return publicConfig(runtime.config);
    },
  );

  app.get('/api/plugins', async () => {
    const discovered = await runtime.pluginLoader.discover();
    const result: PluginInfoDto[] = discovered.map((d) => ({
      pluginModule: d.pluginModule,
      pluginExport: d.pluginExport,
      id: d.instance.metadata.id,
      name: d.instance.metadata.name,
      description: d.instance.metadata.description,
      version: d.instance.metadata.version,
      settings: d.instance.settingDescriptors.map((s) => ({
        key: s.key,
        displayName: s.displayName,
        description: s.description ?? '',
        settingType: s.settingType,
        defaultValue: s.defaultValue ?? '',
        isRequired: s.isRequired ?? false,
      })),
    }));
    return result;
  });

  app.get('/api/datasources', async () => {
    runtime.dataSourceStore.reload();
    return runtime.dataSourceStore.list().map((ds) => decorateDataSource(ds, runtime));
  });

  app.post('/api/datasources', async (req: FastifyRequest, reply: FastifyReply) => {
    const input = (req.body ?? {}) as DataSourceInput;
    const error = validateDataSource(input, true);
    if (error) return reply.code(400).send({ error });
    const created = runtime.dataSourceStore.add({
      id: input.id ?? randomUUID().replace(/-/g, ''),
      name: input.name!,
      pluginModule: input.pluginModule!,
      pluginExport: input.pluginExport,
      enabled: input.enabled ?? true,
      cronSchedule: input.cronSchedule ?? DEFAULT_CRON,
      settings: input.settings ?? {},
    });
    return reply.code(201).send(decorateDataSource(created, runtime));
  });

  app.put('/api/datasources/:id', async (req: FastifyRequest, reply: FastifyReply) => {
    const { id } = req.params as { id: string };
    const existing = runtime.dataSourceStore.getById(id);
    if (!existing) return reply.code(404).send({ error: 'not found' });
    const input = (req.body ?? {}) as DataSourceInput;
    const merged: DataSource = {
      ...existing,
      name: input.name ?? existing.name,
      pluginModule: input.pluginModule ?? existing.pluginModule,
      pluginExport: input.pluginExport !== undefined ? input.pluginExport : existing.pluginExport,
      enabled: input.enabled !== undefined ? input.enabled : existing.enabled,
      cronSchedule: input.cronSchedule ?? existing.cronSchedule,
      settings: input.settings ?? existing.settings,
    };
    const error = validateDataSource(merged, false);
    if (error) return reply.code(400).send({ error });
    runtime.dataSourceStore.update(merged);
    return decorateDataSource(merged, runtime);
  });

  app.delete('/api/datasources/:id', async (req: FastifyRequest, reply: FastifyReply) => {
    const { id } = req.params as { id: string };
    const removed = runtime.dataSourceStore.remove(id);
    if (!removed) return reply.code(404).send({ error: 'not found' });
    return reply.code(204).send();
  });

  app.post('/api/datasources/:id/run', async (req: FastifyRequest, reply: FastifyReply) => {
    const { id } = req.params as { id: string };
    const ds = runtime.dataSourceStore.getById(id);
    if (!ds) return reply.code(404).send({ error: 'not found' });
    // fire-and-forget; surface errors via lastError + run log
    void runtime.indexingService.runDataSource(ds);
    return reply.code(202).send({ accepted: true, dataSourceId: id });
  });

  app.get('/api/datasources/:id/runs', async (req: FastifyRequest, reply: FastifyReply) => {
    const { id } = req.params as { id: string };
    const ds = runtime.dataSourceStore.getById(id);
    if (!ds) return reply.code(404).send({ error: 'not found' });
    const query = (req.query ?? {}) as { limit?: string };
    const limit = query.limit ? Math.max(1, Math.min(200, Number(query.limit))) : 20;
    const history = runtime.indexStore.getRunHistory(id, limit);
    return history.map(runLogDto);
  });

  app.post('/api/search', async (req: FastifyRequest, reply: FastifyReply) => {
    const body = (req.body ?? {}) as Partial<SearchQuery>;
    if (typeof body.queryText !== 'string') {
      return reply.code(400).send({ error: 'queryText is required' });
    }
    const results = runtime.indexStore.search({
      queryText: body.queryText,
      dataSourceId: body.dataSourceId,
      dataSourceName: body.dataSourceName,
      provider: body.provider,
      type: body.type,
      machine: body.machine,
      maxResults: body.maxResults,
      offset: body.offset,
    });
    return { results, count: results.length };
  });

  return app;
}

function publicConfig(config: IndexConfiguration): IndexConfiguration {
  // Never expose the encryption key over HTTP.
  return { ...config, encryptionKey: config.encryptionKey ? '***' : undefined };
}

function decorateDataSource(ds: DataSource, runtime: DaemonRuntime) {
  const latest = runtime.indexStore.getLatestRun(ds.id);
  return {
    ...ds,
    latestRun: latest ? runLogDto(latest) : null,
  };
}

function runLogDto(log: IndexRunLog) {
  return {
    id: log.id,
    dataSourceId: log.dataSourceId,
    startedAt: log.startedAt.toISOString(),
    completedAt: log.completedAt ? log.completedAt.toISOString() : null,
    status: log.status,
    documentCount: log.documentCount,
    errorMessage: log.errorMessage,
  };
}

function validateDataSource(input: DataSourceInput, requireAll: boolean): string | null {
  if (requireAll || input.name !== undefined) {
    if (!input.name || !input.name.trim()) return 'name is required';
  }
  if (requireAll || input.pluginModule !== undefined) {
    if (!input.pluginModule || !input.pluginModule.trim()) return 'pluginModule is required';
  }
  if (input.cronSchedule !== undefined && typeof input.cronSchedule !== 'string') {
    return 'cronSchedule must be a string';
  }
  if (input.settings !== undefined && (typeof input.settings !== 'object' || input.settings === null)) {
    return 'settings must be an object';
  }
  return null;
}
