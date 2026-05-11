import { Cron } from 'croner';
import type { DiscoveredDocument, ISearchPlugin } from '@quaero/plugin-api';
import { sha256Hex } from './crypto.js';
import { DataSourceStore } from './data-source-store.js';
import { IndexStore, localMachineName, newDocumentId } from './index-store.js';
import type { DataSource, IndexedDocument } from './models.js';
import type { PluginLoader } from './plugin-loader.js';

export type IndexerState = 'idle' | 'running';

export interface IndexingServiceLogger {
  info(msg: string, meta?: Record<string, unknown>): void;
  warn(msg: string, meta?: Record<string, unknown>): void;
  error(msg: string, meta?: Record<string, unknown>): void;
  debug?(msg: string, meta?: Record<string, unknown>): void;
}

const NOOP_LOGGER: IndexingServiceLogger = {
  info() {},
  warn() {},
  error() {},
  debug() {},
};

export class IndexingService {
  state: IndexerState = 'idle';
  lastRunTime: Date | null = null;
  lastError: string | null = null;

  constructor(
    public readonly store: IndexStore,
    public readonly dataSourceStore: DataSourceStore,
    public readonly pluginLoader: PluginLoader,
    private readonly logger: IndexingServiceLogger = NOOP_LOGGER,
  ) {}

  /** Evaluate cron schedules and run any data source that is due. */
  async evaluateAndRun(signal?: AbortSignal): Promise<void> {
    this.dataSourceStore.reload();
    for (const ds of this.dataSourceStore.getEnabled()) {
      if (signal?.aborted) break;
      if (this.isDue(ds)) {
        await this.runDataSource(ds, signal);
      } else {
        this.logger.debug?.('skip not due', { name: ds.name });
      }
    }
    this.lastRunTime = new Date();
  }

  /** Force-run all enabled data sources, ignoring schedule. */
  async runAll(signal?: AbortSignal): Promise<void> {
    this.dataSourceStore.reload();
    for (const ds of this.dataSourceStore.getEnabled()) {
      if (signal?.aborted) break;
      await this.runDataSource(ds, signal);
    }
    this.lastRunTime = new Date();
  }

  /** Force-run a single data source, ignoring schedule. */
  async runDataSource(ds: DataSource, signal?: AbortSignal): Promise<void> {
    this.state = 'running';
    this.lastError = null;
    const logId = this.store.startRunLog(ds.id);
    let docCount = 0;

    try {
      const plugin = await this.pluginLoader.createPlugin(ds.pluginModule, ds.pluginExport);
      if (!plugin) {
        const msg = `Could not load plugin ${ds.pluginModule}${ds.pluginExport ? '#' + ds.pluginExport : ''}`;
        this.logger.error(msg);
        this.store.completeRunLog(logId, 'error', 0, msg);
        return;
      }

      const lastSuccess = this.store.getLastSuccessfulRun(ds.id);
      await plugin.initialize(
        { enabled: true, settings: ds.settings, lastSuccessfulRun: lastSuccess },
        signal,
      );

      this.logger.info('Indexing data source', {
        name: ds.name,
        plugin: plugin.metadata.name,
        lastSuccess: lastSuccess?.toISOString() ?? null,
      });

      for await (const discovered of plugin.discoverDocuments(signal)) {
        if (signal?.aborted) break;
        const hash = discovered.contentHash ?? sha256Hex(discovered.content);
        if (!this.store.hasChanged(discovered.location, hash)) {
          this.store.ensureDataSourceMetadataByLocation(discovered.location, ds.id, ds.name);
          continue;
        }
        const doc = toIndexedDocument(discovered, hash, ds);
        this.store.upsertDocument(doc);
        docCount++;
      }

      this.store.completeRunLog(logId, 'success', docCount);
      this.logger.info('Data source indexed', { name: ds.name, docCount });
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      this.lastError = message;
      this.logger.error('Indexing failed', { name: ds.name, error: message });
      this.store.completeRunLog(logId, 'error', docCount, message);
    } finally {
      this.state = 'idle';
    }
  }

  /** Returns true when the data source is due based on its cron schedule. */
  isDue(ds: DataSource, now: Date = new Date()): boolean {
    try {
      const cron = new Cron(ds.cronSchedule, { timezone: 'UTC' });
      const last = this.store.getLastSuccessfulRun(ds.id);
      if (last == null) return true;
      const next = cron.nextRun(last);
      return !!next && next.getTime() <= now.getTime();
    } catch (err) {
      this.logger.warn('Invalid cron expression — running anyway', {
        name: ds.name,
        cron: ds.cronSchedule,
        error: err instanceof Error ? err.message : String(err),
      });
      return true;
    }
  }
}

function toIndexedDocument(d: DiscoveredDocument, hash: string, ds: DataSource): IndexedDocument {
  const extended = { ...(d.extendedData ?? {}) };
  extended.DataSourceId = ds.id;
  extended.DataSourceName = ds.name;
  return {
    id: newDocumentId(),
    machine: localMachineName(),
    type: d.type,
    provider: d.provider,
    location: d.location,
    title: d.title,
    summary: d.summary,
    content: d.content,
    extendedData: extended,
    indexedAt: new Date(),
    contentHash: hash,
  };
}
