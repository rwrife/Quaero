import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { TextPlugin, PLUGIN_ID, truncateSummary } from '../src/index.js';
import type { DiscoveredDocument, PluginConfiguration } from '@quaero/plugin-api';

let tmp: string;
beforeEach(() => {
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'quaero-plugin-text-'));
});
afterEach(() => {
  try {
    fs.rmSync(tmp, { recursive: true, force: true });
  } catch {
    /* ignore */
  }
});

function cfg(overrides: Partial<PluginConfiguration> = {}): PluginConfiguration {
  return {
    settings: { rootPath: tmp },
    lastSuccessfulRun: null,
    ...overrides,
  };
}

async function collect(plugin: TextPlugin): Promise<DiscoveredDocument[]> {
  const out: DiscoveredDocument[] = [];
  for await (const d of plugin.discoverDocuments()) out.push(d);
  return out;
}

describe('TextPlugin', () => {
  it('exposes id and required settings', () => {
    const p = new TextPlugin();
    expect(p.metadata.id).toBe(PLUGIN_ID);
    const root = p.settingDescriptors.find((s) => s.key === 'rootPath');
    expect(root?.isRequired).toBe(true);
  });

  it('throws when rootPath is missing', async () => {
    const p = new TextPlugin();
    await expect(
      p.initialize({ settings: {}, lastSuccessfulRun: null }),
    ).rejects.toThrow(/rootPath/);
  });

  it('discovers .txt and .log files from a folder', async () => {
    fs.writeFileSync(path.join(tmp, 'a.txt'), 'alpha contents');
    fs.writeFileSync(path.join(tmp, 'b.log'), 'bravo contents');
    fs.writeFileSync(path.join(tmp, 'c.md'), 'ignored');
    const p = new TextPlugin();
    await p.initialize(cfg());
    const docs = await collect(p);
    const titles = docs.map((d) => d.title).sort();
    expect(titles).toEqual(['a', 'b']);
    for (const d of docs) {
      expect(d.provider).toBe(PLUGIN_ID);
      expect(d.type).toBe('text');
      expect(d.content?.length ?? 0).toBeGreaterThan(0);
      expect(d.extendedData?.mtime).toBeDefined();
    }
  });

  it('honours lastSuccessfulRun (mtime) for incremental discovery', async () => {
    const file = path.join(tmp, 'a.txt');
    fs.writeFileSync(file, 'old');
    const past = new Date(Date.now() - 60_000);
    fs.utimesSync(file, past, past);
    const p = new TextPlugin();
    await p.initialize(cfg({ lastSuccessfulRun: new Date() }));
    const docs = await collect(p);
    expect(docs).toHaveLength(0);
  });

  it('truncateSummary collapses whitespace and clips with ellipsis', () => {
    expect(truncateSummary('a   b\n\nc', 80)).toBe('a b c');
    const long = 'x'.repeat(500);
    const out = truncateSummary(long, 100);
    expect(out.length).toBe(100);
    expect(out.endsWith('…')).toBe(true);
  });
});
