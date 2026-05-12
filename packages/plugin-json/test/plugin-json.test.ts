import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { JsonPlugin, PLUGIN_ID, getDotPath } from '../src/index.js';
import type { DiscoveredDocument, PluginConfiguration } from '@quaero/plugin-api';

let tmp: string;
beforeEach(() => {
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'quaero-plugin-json-'));
});
afterEach(() => {
  try {
    fs.rmSync(tmp, { recursive: true, force: true });
  } catch {
    /* ignore */
  }
});

function cfg(overrides: Partial<PluginConfiguration> = {}): PluginConfiguration {
  return { settings: { rootPath: tmp }, lastSuccessfulRun: null, ...overrides };
}

async function collect(plugin: JsonPlugin): Promise<DiscoveredDocument[]> {
  const out: DiscoveredDocument[] = [];
  for await (const d of plugin.discoverDocuments()) out.push(d);
  return out;
}

describe('JsonPlugin', () => {
  it('auto-detects common title/summary/content keys', async () => {
    fs.writeFileSync(
      path.join(tmp, 'a.json'),
      JSON.stringify({ title: 'My Title', summary: 'Brief', content: 'Body text' }),
    );
    const p = new JsonPlugin();
    await p.initialize(cfg());
    const docs = await collect(p);
    expect(docs).toHaveLength(1);
    expect(docs[0].title).toBe('My Title');
    expect(docs[0].summary).toBe('Brief');
    expect(docs[0].content).toBe('Body text');
    expect(docs[0].type).toBe('json');
    expect(docs[0].provider).toBe(PLUGIN_ID);
  });

  it('uses configured dot-path mappings', async () => {
    fs.writeFileSync(
      path.join(tmp, 'a.json'),
      JSON.stringify({ meta: { heading: 'H' }, payload: { text: 'P' } }),
    );
    const p = new JsonPlugin();
    await p.initialize(
      cfg({
        settings: {
          rootPath: tmp,
          titlePath: 'meta.heading',
          contentPath: 'payload.text',
        },
      }),
    );
    const docs = await collect(p);
    expect(docs[0].title).toBe('H');
    expect(docs[0].content).toBe('P');
  });

  it('falls back to filename and raw content when nothing matches', async () => {
    fs.writeFileSync(path.join(tmp, 'plain.json'), JSON.stringify({ foo: 'bar' }));
    const p = new JsonPlugin();
    await p.initialize(cfg());
    const docs = await collect(p);
    expect(docs[0].title).toBe('plain');
    expect(docs[0].content).toContain('bar');
  });

  it('skips malformed JSON', async () => {
    fs.writeFileSync(path.join(tmp, 'broken.json'), '{not valid');
    fs.writeFileSync(path.join(tmp, 'good.json'), JSON.stringify({ name: 'ok' }));
    const p = new JsonPlugin();
    await p.initialize(cfg());
    const docs = await collect(p);
    expect(docs).toHaveLength(1);
    expect(docs[0].title).toBe('ok');
  });

  it('getDotPath resolves and rejects safely', () => {
    expect(getDotPath({ a: { b: 1 } }, 'a.b')).toBe(1);
    expect(getDotPath({ a: { b: 1 } }, 'a.c')).toBeUndefined();
    expect(getDotPath(null, 'a')).toBeUndefined();
    expect(getDotPath({ a: 1 }, null)).toBeUndefined();
  });
});
