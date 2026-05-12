import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import {
  encryptString,
  decryptString,
  sha256Hex,
  IndexStore,
  DataSourceStore,
  PluginLoader,
  IndexingService,
  loadConfiguration,
  saveConfiguration,
  defaultIndexConfiguration,
  newDocumentId,
  localMachineName,
  type IndexedDocument,
  type IndexConfiguration,
  type DataSource,
} from '../src/index.js';
import type { ISearchPlugin } from '@quaero/plugin-api';

let tmp: string;
beforeEach(() => {
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'quaero-core-'));
});
afterEach(() => {
  try {
    fs.rmSync(tmp, { recursive: true, force: true });
  } catch {
    // best-effort cleanup
  }
});

function makeConfig(overrides: Partial<IndexConfiguration> = {}): IndexConfiguration {
  return {
    ...defaultIndexConfiguration(),
    databasePath: path.join(tmp, 'index.db'),
    pluginsDirectory: path.join(tmp, 'plugins'),
    encryptionEnabled: false,
    encryptionKey: undefined,
    ...overrides,
  };
}

function makeDoc(over: Partial<IndexedDocument> = {}): IndexedDocument {
  const content = over.content ?? 'hello world';
  return {
    id: newDocumentId(),
    machine: localMachineName(),
    type: 'text',
    provider: 'test',
    location: '/tmp/sample.txt',
    title: 'sample',
    summary: 'a sample doc',
    content,
    extendedData: {},
    indexedAt: new Date(),
    contentHash: sha256Hex(content),
    ...over,
  };
}

describe('crypto', () => {
  it('round-trips AES-256-GCM payloads', () => {
    const enc = encryptString('top secret', 'pw');
    expect(enc).not.toBe('top secret');
    expect(decryptString(enc, 'pw')).toBe('top secret');
  });

  it('rejects tampered payloads', () => {
    const enc = encryptString('hello', 'pw');
    const buf = Buffer.from(enc, 'base64');
    buf[buf.length - 1] ^= 1;
    expect(() => decryptString(buf.toString('base64'), 'pw')).toThrow();
  });

  it('rejects wrong key', () => {
    const enc = encryptString('hello', 'pw');
    expect(() => decryptString(enc, 'wrong')).toThrow();
  });

  it('sha256Hex is deterministic', () => {
    expect(sha256Hex('abc')).toBe(sha256Hex('abc'));
    expect(sha256Hex('abc')).not.toBe(sha256Hex('abd'));
  });
});

describe('configuration', () => {
  it('returns defaults when file missing', () => {
    const cfg = loadConfiguration(path.join(tmp, 'nope.json'));
    expect(cfg.indexIntervalMinutes).toBeGreaterThan(0);
    expect(cfg.encryptionEnabled).toBe(false);
  });

  it('round-trips via save/load', () => {
    const file = path.join(tmp, 'cfg.json');
    const cfg = makeConfig({ encryptionEnabled: true, encryptionKey: 'k' });
    saveConfiguration(cfg, file);
    const loaded = loadConfiguration(file);
    expect(loaded.encryptionEnabled).toBe(true);
    expect(loaded.encryptionKey).toBe('k');
    expect(loaded.databasePath).toBe(cfg.databasePath);
  });
});

describe('DataSourceStore', () => {
  it('add/list/update/remove with persistence', () => {
    const file = path.join(tmp, 'ds.json');
    const store = new DataSourceStore(file);
    expect(store.list()).toHaveLength(0);
    const created = store.add({
      name: 'docs',
      pluginModule: '@quaero/plugin-text',
      enabled: true,
      cronSchedule: '*/5 * * * *',
      settings: { rootPath: '/tmp' },
    });
    expect(created.id).toMatch(/^[0-9a-f]{32}$/i);

    const reopened = new DataSourceStore(file);
    expect(reopened.list()).toHaveLength(1);
    expect(reopened.getEnabled()).toHaveLength(1);
    expect(reopened.getById(created.id)?.name).toBe('docs');

    const updated: DataSource = { ...created, enabled: false };
    expect(reopened.update(updated)).toBe(true);
    expect(reopened.getEnabled()).toHaveLength(0);

    expect(reopened.remove(created.id)).toBe(true);
    expect(reopened.list()).toHaveLength(0);
    expect(reopened.remove('nope')).toBe(false);
  });
});

describe('IndexStore', () => {
  it('upserts, searches via FTS, and tracks change detection', () => {
    const store = new IndexStore(makeConfig());
    const doc = makeDoc({ content: 'the quick brown fox jumps' });
    store.upsertDocument(doc);

    const hits = store.search({ queryText: 'quick' });
    expect(hits).toHaveLength(1);
    expect(hits[0].document.title).toBe('sample');

    expect(store.hasChanged(doc.location, doc.contentHash)).toBe(false);
    expect(store.hasChanged(doc.location, 'different')).toBe(true);

    expect(store.getDocumentCount()).toBe(1);
    expect(store.getProviders()).toEqual(['test']);

    // metadata patch
    store.ensureDataSourceMetadataByLocation(doc.location, 'ds-1', 'My DS');
    const filtered = store.search({ queryText: 'quick', dataSourceId: 'ds-1' });
    expect(filtered).toHaveLength(1);
    expect(store.search({ queryText: 'quick', dataSourceId: 'other' })).toHaveLength(0);

    store.close();
  });

  it('filters by provider and type', () => {
    const store = new IndexStore(makeConfig());
    store.upsertDocument(makeDoc({ provider: 'one', type: 'text', content: 'apple' }));
    store.upsertDocument(makeDoc({ provider: 'two', type: 'markdown', content: 'apple banana' }));
    expect(store.search({ queryText: 'apple', provider: 'one' })).toHaveLength(1);
    expect(store.search({ queryText: 'apple', provider: 'two' })).toHaveLength(1);
    expect(store.search({ queryText: 'apple', type: 'markdown' })).toHaveLength(1);
    expect(store.search({ queryText: 'apple', type: 'text', provider: 'two' })).toHaveLength(0);
    expect(store.getProviders().sort()).toEqual(['one', 'two']);
    store.close();
  });

  it('encrypts content at rest when configured', () => {
    const cfg = makeConfig({ encryptionEnabled: true, encryptionKey: 'secret-key' });
    const store = new IndexStore(cfg);
    const doc = makeDoc({ content: 'classified payload' });
    store.upsertDocument(doc);

    const raw = store.database.prepare('SELECT content FROM documents WHERE id = ?').get(doc.id) as
      | { content: string }
      | undefined;
    expect(raw?.content).toBeDefined();
    expect(raw?.content).not.toContain('classified');

    const round = store.search({ queryText: 'classified' });
    expect(round).toHaveLength(1);
    expect(round[0].document.content).toBe('classified payload');

    store.close();
  });

  it('records run log lifecycle', () => {
    const store = new IndexStore(makeConfig());
    const id = store.startRunLog('ds-1');
    store.completeRunLog(id, 'success', 3);
    const last = store.getLatestRun('ds-1');
    expect(last?.status).toBe('success');
    expect(last?.documentCount).toBe(3);
    expect(store.getLastSuccessfulRun('ds-1')).toBeInstanceOf(Date);

    const id2 = store.startRunLog('ds-1');
    store.completeRunLog(id2, 'error', 0, 'boom');
    const history = store.getRunHistory('ds-1');
    expect(history).toHaveLength(2);
    expect(history[0].status).toBe('error');
    expect(history[0].errorMessage).toBe('boom');
    store.close();
  });
});

describe('PluginLoader', () => {
  it('loads a `.plugin.js` file from the plugins directory', async () => {
    const dir = path.join(tmp, 'plugins');
    fs.mkdirSync(dir, { recursive: true });
    const file = path.join(dir, 'fake.plugin.mjs');
    fs.writeFileSync(
      file,
      `export const plugin = {
         metadata: { id: 'fake', name: 'Fake', description: 'd', version: '0.0.1' },
         settingDescriptors: [],
         async initialize() {},
         async *discoverDocuments() {
           yield { type: 't', provider: 'p', location: '/x', title: 'x', summary: 's', content: 'c' };
         }
       };\n`,
      'utf8',
    );

    const loader = new PluginLoader(dir);
    const plug = await loader.createPlugin(file);
    expect(plug).not.toBeNull();
    expect(plug!.metadata.id).toBe('fake');

    const discovered = await loader.discover();
    expect(discovered.some((p) => p.instance.metadata.id === 'fake')).toBe(true);
  });

  it('honours plugins.json manifest entries', async () => {
    const dir = path.join(tmp, 'plugins');
    fs.mkdirSync(dir, { recursive: true });
    const target = path.join(dir, 'manifest-plugin.mjs');
    fs.writeFileSync(
      target,
      `export default {
         metadata: { id: 'manifest', name: 'M', description: '', version: '1' },
         settingDescriptors: [],
         async initialize() {},
         async *discoverDocuments() { /* none */ }
       };\n`,
      'utf8',
    );
    fs.writeFileSync(
      path.join(dir, 'plugins.json'),
      JSON.stringify([target]),
      'utf8',
    );
    const loader = new PluginLoader(dir);
    const found = await loader.discover();
    expect(found.some((p) => p.instance.metadata.id === 'manifest')).toBe(true);
  });

  it('returns null for invalid module specifiers', async () => {
    const loader = new PluginLoader(path.join(tmp, 'plugins'));
    const result = await loader.createPlugin('does-not-exist-xyz');
    expect(result).toBeNull();
  });
});

describe('IndexingService', () => {
  function inlinePlugin(docs: Array<{ location: string; content: string }>): ISearchPlugin {
    return {
      metadata: { id: 'inline', name: 'Inline', description: '', version: '1' },
      settingDescriptors: [],
      async initialize() {},
      async *discoverDocuments() {
        for (const d of docs) {
          yield {
            type: 'text',
            provider: 'inline',
            location: d.location,
            title: d.location,
            summary: 's',
            content: d.content,
          };
        }
      },
    };
  }

  function makeService(plugin: ISearchPlugin) {
    const cfg = makeConfig();
    const store = new IndexStore(cfg);
    const dsStore = new DataSourceStore(path.join(tmp, 'ds.json'));
    const loader = new PluginLoader(cfg.pluginsDirectory) as PluginLoader & {
      createPlugin: PluginLoader['createPlugin'];
    };
    // monkey-patch loader to return our inline plugin
    (loader as unknown as { createPlugin: () => Promise<ISearchPlugin> }).createPlugin = async () =>
      plugin;
    const svc = new IndexingService(store, dsStore, loader);
    return { svc, store, dsStore };
  }

  it('runs a data source, dedupes unchanged docs, and writes a success run log', async () => {
    const docs = [
      { location: '/a.txt', content: 'alpha' },
      { location: '/b.txt', content: 'bravo' },
    ];
    const { svc, store, dsStore } = makeService(inlinePlugin(docs));
    const ds = dsStore.add({
      name: 'inline',
      pluginModule: 'inline',
      enabled: true,
      cronSchedule: '* * * * *',
      settings: {},
    });

    await svc.runDataSource(ds);
    expect(store.getDocumentCount()).toBe(2);
    let last = store.getLatestRun(ds.id);
    expect(last?.status).toBe('success');
    expect(last?.documentCount).toBe(2);

    // Second run with identical hashes should add 0 new docs.
    await svc.runDataSource(ds);
    expect(store.getDocumentCount()).toBe(2);
    last = store.getLatestRun(ds.id);
    expect(last?.documentCount).toBe(0);

    // search filter by data source id should now work because metadata is attached.
    const hits = store.search({ queryText: 'alpha', dataSourceId: ds.id });
    expect(hits).toHaveLength(1);
    store.close();
  });

  it('isDue returns true for never-run sources and false right after success', async () => {
    const { svc, store, dsStore } = makeService(inlinePlugin([]));
    const ds = dsStore.add({
      name: 'inline',
      pluginModule: 'inline',
      enabled: true,
      cronSchedule: '0 0 1 1 *', // once a year
      settings: {},
    });
    expect(svc.isDue(ds)).toBe(true);
    await svc.runDataSource(ds);
    expect(svc.isDue(ds)).toBe(false);
    store.close();
  });

  it('records error run log when plugin throws', async () => {
    const broken: ISearchPlugin = {
      metadata: { id: 'bad', name: 'bad', description: '', version: '1' },
      settingDescriptors: [],
      async initialize() {
        throw new Error('init failed');
      },
      async *discoverDocuments() {
        // unreachable
      },
    };
    const { svc, store, dsStore } = makeService(broken);
    const ds = dsStore.add({
      name: 'bad',
      pluginModule: 'bad',
      enabled: true,
      cronSchedule: '* * * * *',
      settings: {},
    });
    await svc.runDataSource(ds);
    const last = store.getLatestRun(ds.id);
    expect(last?.status).toBe('error');
    expect(last?.errorMessage).toContain('init failed');
    expect(svc.lastError).toContain('init failed');
    store.close();
  });
});
