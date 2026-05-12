import { describe, it, expect, beforeEach, afterEach } from 'vitest';
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import { MarkdownPlugin, PLUGIN_ID, extractTitleAndSummary } from '../src/index.js';
import type { DiscoveredDocument, PluginConfiguration } from '@quaero/plugin-api';

let tmp: string;
beforeEach(() => {
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'quaero-plugin-md-'));
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

async function collect(plugin: MarkdownPlugin): Promise<DiscoveredDocument[]> {
  const out: DiscoveredDocument[] = [];
  for await (const d of plugin.discoverDocuments()) out.push(d);
  return out;
}

describe('MarkdownPlugin', () => {
  it('discovers markdown files and extracts H1 + first paragraph', async () => {
    fs.writeFileSync(
      path.join(tmp, 'one.md'),
      '# Title One\n\nFirst paragraph.\n\nSecond.\n',
    );
    fs.writeFileSync(path.join(tmp, 'two.markdown'), 'no h1 here\n');
    fs.writeFileSync(path.join(tmp, 'skip.txt'), 'ignored');
    const p = new MarkdownPlugin();
    await p.initialize(cfg());
    const docs = await collect(p);
    expect(docs).toHaveLength(2);
    const titled = docs.find((d) => d.title === 'Title One');
    expect(titled?.summary).toBe('First paragraph.');
    expect(titled?.type).toBe('markdown');
    expect(titled?.provider).toBe(PLUGIN_ID);
    const fallback = docs.find((d) => d.title === 'two');
    expect(fallback).toBeDefined();
    expect(fallback?.summary).toBe('no h1 here');
  });

  it('extractTitleAndSummary falls back to filename when no H1', () => {
    const out = extractTitleAndSummary('just content\n', '/x/foo.md');
    expect(out.title).toBe('foo');
    expect(out.summary).toBe('just content');
  });

  it('truncates long summaries with ellipsis', () => {
    const long = 'x'.repeat(500);
    const out = extractTitleAndSummary(`# T\n\n${long}\n`, '/x/y.md');
    expect(out.title).toBe('T');
    expect(out.summary.length).toBeLessThanOrEqual(280);
    expect(out.summary.endsWith('…')).toBe(true);
  });

  it('requires rootPath', async () => {
    const p = new MarkdownPlugin();
    await expect(
      p.initialize({ settings: {}, lastSuccessfulRun: null }),
    ).rejects.toThrow(/rootPath/);
  });
});
