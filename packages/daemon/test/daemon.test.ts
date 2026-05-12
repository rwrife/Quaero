import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {
  DataSourceStore,
  IndexStore,
  IndexingService,
  PluginLoader,
  defaultIndexConfiguration,
  newDocumentId,
  localMachineName,
  type IndexConfiguration,
} from '@quaero/core';
import type { ISearchPlugin } from '@quaero/plugin-api';
import { buildServer } from '../src/server.js';
import { Scheduler } from '../src/scheduler.js';
import type { DaemonRuntime } from '../src/runtime.js';

let tmp: string;
let runtime: DaemonRuntime;
let configPath: string;

class StubPlugin implements ISearchPlugin {
  readonly metadata = {
    id: 'stub',
    name: 'Stub',
    description: 'stub plugin',
    version: '0.0.1',
  };
  readonly settingDescriptors = [
    {
      key: 'path',
      displayName: 'Path',
      description: 'where to scan',
      settingType: 'folderPath' as const,
      defaultValue: '',
      isRequired: true,
    },
  ];
  async initialize(): Promise<void> {}
  async *discoverDocuments(): AsyncIterable<never> {
    return;
  }
}

function makeConfig(over: Partial<IndexConfiguration> = {}): IndexConfiguration {
  return {
    ...defaultIndexConfiguration(),
    databasePath: path.join(tmp, 'index.db'),
    pluginsDirectory: path.join(tmp, 'plugins'),
    encryptionEnabled: false,
    encryptionKey: undefined,
    serverBaseUrl: 'http://127.0.0.1:5099',
    ...over,
  };
}

function buildRuntime(): DaemonRuntime {
  const config = makeConfig();
  const indexStore = new IndexStore(config);
  const dataSourceStore = new DataSourceStore(path.join(tmp, 'datasources.json'));
  const pluginLoader = new PluginLoader(config.pluginsDirectory);
  const indexingService = new IndexingService(indexStore, dataSourceStore, pluginLoader);
  return {
    config,
    indexStore,
    dataSourceStore,
    pluginLoader,
    indexingService,
    close() {
      indexStore.close();
    },
  };
}

beforeEach(() => {
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'quaero-daemon-'));
  configPath = path.join(tmp, 'config.json');
  runtime = buildRuntime();
});

afterEach(() => {
  runtime.close();
  try {
    fs.rmSync(tmp, { recursive: true, force: true });
  } catch {
    // best-effort
  }
});

describe('daemon HTTP API', () => {
  it('GET / returns daemon status', async () => {
    const app = buildServer({ runtime, configPath });
    const res = await app.inject({ method: 'GET', url: '/' });
    expect(res.statusCode).toBe(200);
    const body = res.json();
    expect(body.name).toBe('quaero-daemon');
    expect(body.state).toBe('idle');
    expect(body.documentCount).toBe(0);
    await app.close();
  });

  it('GET /api/config returns sanitized config', async () => {
    runtime.config.encryptionEnabled = true;
    runtime.config.encryptionKey = 'super-secret';
    const app = buildServer({ runtime, configPath });
    const res = await app.inject({ method: 'GET', url: '/api/config' });
    expect(res.statusCode).toBe(200);
    const body = res.json();
    expect(body.encryptionKey).toBe('***');
    expect(body.serverBaseUrl).toContain('http');
    await app.close();
  });

  it('PUT /api/config/server-base-url persists the new value', async () => {
    const app = buildServer({ runtime, configPath });
    const res = await app.inject({
      method: 'PUT',
      url: '/api/config/server-base-url',
      payload: { serverBaseUrl: 'http://127.0.0.1:9999' },
    });
    expect(res.statusCode).toBe(200);
    expect(runtime.config.serverBaseUrl).toBe('http://127.0.0.1:9999');
    const saved = JSON.parse(fs.readFileSync(configPath, 'utf8'));
    expect(saved.serverBaseUrl).toBe('http://127.0.0.1:9999');

    const bad = await app.inject({
      method: 'PUT',
      url: '/api/config/server-base-url',
      payload: { serverBaseUrl: '' },
    });
    expect(bad.statusCode).toBe(400);
    await app.close();
  });

  it('GET /api/plugins discovers plugins via plugins.json', async () => {
    // create a tiny ESM plugin in the plugins dir and register it via plugins.json
    const pluginDir = runtime.config.pluginsDirectory;
    fs.mkdirSync(pluginDir, { recursive: true });
    const pluginPath = path.join(pluginDir, 'demo.plugin.mjs');
    fs.writeFileSync(
      pluginPath,
      `export const plugin = {
        metadata: { id: 'demo', name: 'Demo', description: 'd', version: '1.0.0' },
        settingDescriptors: [],
        async initialize() {},
        async *discoverDocuments() {}
      };\n`,
    );
    fs.writeFileSync(
      path.join(pluginDir, 'plugins.json'),
      JSON.stringify([{ module: pluginPath, export: 'plugin' }]),
    );
    const app = buildServer({ runtime, configPath });
    const res = await app.inject({ method: 'GET', url: '/api/plugins' });
    expect(res.statusCode).toBe(200);
    const body = res.json();
    expect(Array.isArray(body)).toBe(true);
    expect(body.find((p: { id: string }) => p.id === 'demo')).toBeTruthy();
    await app.close();
  });

  it('data source CRUD lifecycle works', async () => {
    const app = buildServer({ runtime, configPath });

    // create
    const create = await app.inject({
      method: 'POST',
      url: '/api/datasources',
      payload: {
        name: 'Notes',
        pluginModule: '@quaero/plugin-markdown',
        cronSchedule: '*/15 * * * *',
        settings: { rootPath: tmp },
      },
    });
    expect(create.statusCode).toBe(201);
    const created = create.json();
    expect(created.id).toBeTruthy();
    expect(created.name).toBe('Notes');
    expect(created.latestRun).toBeNull();

    // list
    const list = await app.inject({ method: 'GET', url: '/api/datasources' });
    expect(list.statusCode).toBe(200);
    expect(list.json()).toHaveLength(1);

    // update
    const update = await app.inject({
      method: 'PUT',
      url: `/api/datasources/${created.id}`,
      payload: { name: 'Notes (renamed)', enabled: false },
    });
    expect(update.statusCode).toBe(200);
    expect(update.json().name).toBe('Notes (renamed)');
    expect(update.json().enabled).toBe(false);

    // update missing
    const missing = await app.inject({
      method: 'PUT',
      url: '/api/datasources/does-not-exist',
      payload: { name: 'x' },
    });
    expect(missing.statusCode).toBe(404);

    // bad create
    const bad = await app.inject({
      method: 'POST',
      url: '/api/datasources',
      payload: { name: '' },
    });
    expect(bad.statusCode).toBe(400);

    // delete
    const del = await app.inject({ method: 'DELETE', url: `/api/datasources/${created.id}` });
    expect(del.statusCode).toBe(204);

    const delMissing = await app.inject({
      method: 'DELETE',
      url: `/api/datasources/${created.id}`,
    });
    expect(delMissing.statusCode).toBe(404);

    await app.close();
  });

  it('POST /api/datasources/:id/run accepts a run request', async () => {
    const ds = runtime.dataSourceStore.add({
      name: 'demo',
      pluginModule: '/non/existent/plugin',
      enabled: true,
      cronSchedule: '*/5 * * * *',
      settings: {},
    });
    let pending: Promise<void> = Promise.resolve();
    runtime.indexingService.runDataSource = async () => {
      pending = new Promise((r) => setTimeout(r, 1));
      await pending;
    };
    const app = buildServer({ runtime, configPath });
    const res = await app.inject({ method: 'POST', url: `/api/datasources/${ds.id}/run` });
    expect(res.statusCode).toBe(202);
    expect(res.json()).toMatchObject({ accepted: true, dataSourceId: ds.id });
    await pending;

    const notFound = await app.inject({ method: 'POST', url: '/api/datasources/nope/run' });
    expect(notFound.statusCode).toBe(404);
    await app.close();
  });

  it('GET /api/datasources/:id/runs returns history', async () => {
    const ds = runtime.dataSourceStore.add({
      name: 'demo',
      pluginModule: 'whatever',
      enabled: true,
      cronSchedule: '*/5 * * * *',
      settings: {},
    });
    const logId = runtime.indexStore.startRunLog(ds.id);
    runtime.indexStore.completeRunLog(logId, 'success', 3);

    const app = buildServer({ runtime, configPath });
    const res = await app.inject({ method: 'GET', url: `/api/datasources/${ds.id}/runs` });
    expect(res.statusCode).toBe(200);
    const body = res.json();
    expect(body).toHaveLength(1);
    expect(body[0].status).toBe('success');
    expect(body[0].documentCount).toBe(3);
    await app.close();
  });

  it('POST /api/search returns matching documents', async () => {
    runtime.indexStore.upsertDocument({
      id: newDocumentId(),
      machine: localMachineName(),
      type: 'note',
      provider: 'test',
      location: '/n/1',
      title: 'Quaero',
      summary: 's',
      content: 'the quick brown fox jumps over the lazy dog',
      extendedData: {},
      indexedAt: new Date(),
      contentHash: 'h1',
    });
    const app = buildServer({ runtime, configPath });
    const res = await app.inject({
      method: 'POST',
      url: '/api/search',
      payload: { queryText: 'fox' },
    });
    expect(res.statusCode).toBe(200);
    const body = res.json();
    expect(body.count).toBe(1);
    expect(body.results[0].document.title).toBe('Quaero');

    const bad = await app.inject({ method: 'POST', url: '/api/search', payload: {} });
    expect(bad.statusCode).toBe(400);
    await app.close();
  });
});

describe('Scheduler', () => {
  it('invokes evaluateAndRun on each tick and stops cleanly', async () => {
    let calls = 0;
    const fakeService = {
      async evaluateAndRun() {
        calls++;
      },
    } as unknown as IndexingService;
    const scheduler = new Scheduler(fakeService, { intervalMs: 1_000_000 });
    await scheduler.tick();
    await scheduler.tick();
    expect(calls).toBe(2);
    scheduler.start();
    scheduler.stop();
  });

  it('serializes overlapping ticks', async () => {
    let active = 0;
    let max = 0;
    let completions = 0;
    const fakeService = {
      async evaluateAndRun() {
        active++;
        max = Math.max(max, active);
        await new Promise((r) => setTimeout(r, 10));
        active--;
        completions++;
      },
    } as unknown as IndexingService;
    const scheduler = new Scheduler(fakeService, { intervalMs: 1_000_000 });
    await Promise.all([scheduler.tick(), scheduler.tick(), scheduler.tick()]);
    expect(max).toBe(1);
    expect(completions).toBe(1); // second & third skipped while first ran
    scheduler.stop();
  });
});

// silence unused warning when tsc strict checks find no reference
void StubPlugin;
