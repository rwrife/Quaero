/**
 * @quaero/plugin-text — indexes plain-text files. Title is the filename
 * (without extension), summary is a truncated leading slice of the content.
 * Honours `lastSuccessfulRun` for incremental discovery using mtime.
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

export const PLUGIN_ID = 'quaero.plugin.text';

const DEFAULT_GLOB = '**/*.{txt,log}';
const SUMMARY_MAX = 280;

const SETTINGS: readonly PluginSettingDescriptor[] = [
  {
    key: 'rootPath',
    displayName: 'Root folder',
    description: 'Folder containing text files to index.',
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
  name: 'Plain-text files',
  description: 'Indexes .txt/.log files from a folder.',
  version: '0.2.0',
  supportedFileExtensions: ['.txt', '.log'],
};

export class TextPlugin implements ISearchPlugin {
  readonly metadata = METADATA;
  readonly settingDescriptors = SETTINGS;

  private rootPath: string | null = null;
  private glob: string = DEFAULT_GLOB;
  private lastSuccessfulRun: Date | null = null;

  async initialize(configuration: PluginConfiguration): Promise<void> {
    const root = configuration.settings.rootPath?.trim();
    if (!root) {
      throw new Error('plugin-text: rootPath setting is required');
    }
    this.rootPath = path.resolve(root);
    this.glob = configuration.settings.glob?.trim() || DEFAULT_GLOB;
    this.lastSuccessfulRun = configuration.lastSuccessfulRun;
  }

  async *discoverDocuments(signal?: AbortSignal): AsyncIterable<DiscoveredDocument> {
    if (!this.rootPath) {
      throw new Error('plugin-text: not initialized');
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
      const title = path.basename(file, path.extname(file));
      const summary = truncateSummary(raw);
      yield {
        type: 'text',
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

export function truncateSummary(raw: string, max = SUMMARY_MAX): string {
  const collapsed = raw.replace(/\s+/g, ' ').trim();
  if (collapsed.length <= max) return collapsed;
  return collapsed.slice(0, max - 1).trimEnd() + '…';
}

const plugin = new TextPlugin();
export { plugin };
export default plugin;
