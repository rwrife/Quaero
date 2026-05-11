/**
 * @quaero/plugin-markdown — discovers Markdown files under a configured root
 * directory and yields a {@link DiscoveredDocument} per file. Title is taken
 * from the first H1 (falls back to the filename); summary is the first
 * non-empty paragraph (truncated).
 */
import { promises as fs } from 'node:fs';
import * as path from 'node:path';
import fg from 'fast-glob';
import MarkdownIt from 'markdown-it';
import type {
  DiscoveredDocument,
  ISearchPlugin,
  PluginConfiguration,
  PluginMetadata,
  PluginSettingDescriptor,
} from '@quaero/plugin-api';

export const PLUGIN_ID = 'quaero.plugin.markdown';

const DEFAULT_GLOB = '**/*.{md,markdown}';
const SUMMARY_MAX = 280;

const SETTINGS: readonly PluginSettingDescriptor[] = [
  {
    key: 'rootPath',
    displayName: 'Root folder',
    description: 'Folder containing markdown files to index.',
    settingType: 'folderPath',
    isRequired: true,
  },
  {
    key: 'glob',
    displayName: 'Glob pattern',
    description: 'Glob (relative to rootPath) selecting files.',
    settingType: 'globPattern',
    defaultValue: DEFAULT_GLOB,
  },
];

const METADATA: PluginMetadata = {
  id: PLUGIN_ID,
  name: 'Markdown files',
  description: 'Indexes .md/.markdown files from a folder.',
  version: '0.2.0',
  supportedFileExtensions: ['.md', '.markdown'],
};

export class MarkdownPlugin implements ISearchPlugin {
  readonly metadata = METADATA;
  readonly settingDescriptors = SETTINGS;

  private rootPath: string | null = null;
  private glob: string = DEFAULT_GLOB;
  private lastSuccessfulRun: Date | null = null;
  private readonly md = new MarkdownIt({ html: false, linkify: false });

  async initialize(configuration: PluginConfiguration): Promise<void> {
    const root = configuration.settings.rootPath?.trim();
    if (!root) {
      throw new Error('plugin-markdown: rootPath setting is required');
    }
    this.rootPath = path.resolve(root);
    this.glob = configuration.settings.glob?.trim() || DEFAULT_GLOB;
    this.lastSuccessfulRun = configuration.lastSuccessfulRun;
  }

  async *discoverDocuments(signal?: AbortSignal): AsyncIterable<DiscoveredDocument> {
    if (!this.rootPath) {
      throw new Error('plugin-markdown: not initialized');
    }
    const stream = fg.stream(this.glob, {
      cwd: this.rootPath,
      absolute: true,
      onlyFiles: true,
      dot: false,
      followSymbolicLinks: false,
    });
    for await (const entry of stream as AsyncIterable<string>) {
      if (signal?.aborted) return;
      const file = entry;
      let stat;
      try {
        stat = await fs.stat(file);
      } catch {
        continue;
      }
      if (this.lastSuccessfulRun && stat.mtime <= this.lastSuccessfulRun) {
        continue;
      }
      let raw: string;
      try {
        raw = await fs.readFile(file, 'utf8');
      } catch {
        continue;
      }
      const { title, summary } = extractTitleAndSummary(raw, file, this.md);
      yield {
        type: 'markdown',
        provider: PLUGIN_ID,
        location: file,
        title,
        summary,
        content: raw,
        extendedData: {
          mtime: stat.mtime.toISOString(),
          size: String(stat.size),
        },
      };
    }
  }
}

export function extractTitleAndSummary(
  raw: string,
  filePath: string,
  md: MarkdownIt = new MarkdownIt({ html: false, linkify: false }),
): { title: string; summary: string } {
  const tokens = md.parse(raw, {});
  let title = '';
  let summary = '';
  for (let i = 0; i < tokens.length; i++) {
    const t = tokens[i];
    if (!title && t.type === 'heading_open' && t.tag === 'h1') {
      const inline = tokens[i + 1];
      if (inline?.type === 'inline') title = (inline.content ?? '').trim();
    }
    if (!summary && t.type === 'paragraph_open') {
      const inline = tokens[i + 1];
      if (inline?.type === 'inline') {
        const text = (inline.content ?? '').trim();
        if (text) summary = text;
      }
    }
    if (title && summary) break;
  }
  if (!title) title = path.basename(filePath, path.extname(filePath));
  if (summary.length > SUMMARY_MAX) {
    summary = summary.slice(0, SUMMARY_MAX - 1).trimEnd() + '…';
  }
  return { title, summary };
}

const plugin = new MarkdownPlugin();
export { plugin };
export default plugin;
