/**
 * @quaero/plugin-json — indexes JSON files. The user can configure dot-path
 * mappings for `title`, `summary`, and `content`; if a mapping is missing, the
 * plugin falls back to auto-detection (common keys + JSON stringification).
 */
import { promises as fs } from 'node:fs';
import * as path from 'node:path';
import fg from 'fast-glob';
import type {
  DiscoveredDocument,
  ISearchPlugin,
  PluginConfiguration,
  PluginMetadata,
  PluginSettingDescriptor,
} from '@quaero/plugin-api';

export const PLUGIN_ID = 'quaero.plugin.json';

const DEFAULT_GLOB = '**/*.json';
const SUMMARY_MAX = 280;

const TITLE_CANDIDATES = ['title', 'name', 'subject', 'headline', 'id'];
const SUMMARY_CANDIDATES = ['summary', 'description', 'abstract', 'snippet'];
const CONTENT_CANDIDATES = ['content', 'body', 'text', 'markdown'];

const SETTINGS: readonly PluginSettingDescriptor[] = [
  {
    key: 'rootPath',
    displayName: 'Root folder',
    description: 'Folder containing JSON files to index.',
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
  {
    key: 'titlePath',
    displayName: 'Title dot-path',
    description: 'Dot-path into each JSON document for the title field.',
    settingType: 'text',
  },
  {
    key: 'summaryPath',
    displayName: 'Summary dot-path',
    description: 'Dot-path into each JSON document for the summary field.',
    settingType: 'text',
  },
  {
    key: 'contentPath',
    displayName: 'Content dot-path',
    description: 'Dot-path into each JSON document for the content field.',
    settingType: 'text',
  },
];

const METADATA: PluginMetadata = {
  id: PLUGIN_ID,
  name: 'JSON files',
  description: 'Indexes .json files from a folder with optional dot-path mappings.',
  version: '0.2.0',
  supportedFileExtensions: ['.json'],
};

export class JsonPlugin implements ISearchPlugin {
  readonly metadata = METADATA;
  readonly settingDescriptors = SETTINGS;

  private rootPath: string | null = null;
  private glob: string = DEFAULT_GLOB;
  private titlePath: string | null = null;
  private summaryPath: string | null = null;
  private contentPath: string | null = null;
  private lastSuccessfulRun: Date | null = null;

  async initialize(configuration: PluginConfiguration): Promise<void> {
    const root = configuration.settings.rootPath?.trim();
    if (!root) {
      throw new Error('plugin-json: rootPath setting is required');
    }
    this.rootPath = path.resolve(root);
    this.glob = configuration.settings.glob?.trim() || DEFAULT_GLOB;
    this.titlePath = configuration.settings.titlePath?.trim() || null;
    this.summaryPath = configuration.settings.summaryPath?.trim() || null;
    this.contentPath = configuration.settings.contentPath?.trim() || null;
    this.lastSuccessfulRun = configuration.lastSuccessfulRun;
  }

  async *discoverDocuments(signal?: AbortSignal): AsyncIterable<DiscoveredDocument> {
    if (!this.rootPath) {
      throw new Error('plugin-json: not initialized');
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
      let parsed: unknown;
      try {
        parsed = JSON.parse(raw);
      } catch {
        // Skip malformed JSON.
        continue;
      }
      const title =
        valueAsString(getDotPath(parsed, this.titlePath)) ||
        autoDetect(parsed, TITLE_CANDIDATES) ||
        path.basename(file, path.extname(file));
      const summarySource =
        valueAsString(getDotPath(parsed, this.summaryPath)) ||
        autoDetect(parsed, SUMMARY_CANDIDATES) ||
        '';
      const content =
        valueAsString(getDotPath(parsed, this.contentPath)) ||
        autoDetect(parsed, CONTENT_CANDIDATES) ||
        raw;
      const summary = truncate(summarySource || content, SUMMARY_MAX);
      yield {
        type: 'json',
        provider: PLUGIN_ID,
        location: file,
        title,
        summary,
        content,
        extendedData: {
          mtime: stat.mtime.toISOString(),
          size: String(stat.size),
        },
      };
    }
  }
}

export function getDotPath(value: unknown, dotPath: string | null): unknown {
  if (!dotPath) return undefined;
  const parts = dotPath.split('.').filter(Boolean);
  let current: unknown = value;
  for (const part of parts) {
    if (current === null || current === undefined) return undefined;
    if (typeof current !== 'object') return undefined;
    current = (current as Record<string, unknown>)[part];
  }
  return current;
}

function autoDetect(value: unknown, keys: string[]): string {
  if (!value || typeof value !== 'object') return '';
  const obj = value as Record<string, unknown>;
  for (const k of keys) {
    const v = obj[k];
    const s = valueAsString(v);
    if (s) return s;
  }
  return '';
}

function valueAsString(v: unknown): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'string') return v;
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  try {
    return JSON.stringify(v);
  } catch {
    return '';
  }
}

function truncate(s: string, max: number): string {
  const collapsed = s.replace(/\s+/g, ' ').trim();
  if (collapsed.length <= max) return collapsed;
  return collapsed.slice(0, max - 1).trimEnd() + '…';
}

const plugin = new JsonPlugin();
export { plugin };
export default plugin;
