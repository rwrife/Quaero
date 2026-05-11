import { defaultDatabasePath, defaultPluginsDirectory } from './paths.js';

export interface IndexConfiguration {
  databasePath: string;
  encryptionEnabled: boolean;
  encryptionKey?: string;
  pluginsDirectory: string;
  indexIntervalMinutes: number;
  serverBaseUrl: string;
}

export function defaultIndexConfiguration(): IndexConfiguration {
  return {
    databasePath: defaultDatabasePath(),
    encryptionEnabled: false,
    encryptionKey: undefined,
    pluginsDirectory: defaultPluginsDirectory(),
    indexIntervalMinutes: 30,
    serverBaseUrl:
      (process.env.QUAERO_SERVER_BASE_URL && process.env.QUAERO_SERVER_BASE_URL.trim()) ||
      'http://localhost:5055',
  };
}

export type DataSourceStatus = 'idle' | 'indexing' | 'success' | 'error';

export interface DataSource {
  id: string;
  name: string;
  /** Plugin module specifier — package name (e.g. `@quaero/plugin-markdown`) or absolute path. */
  pluginModule: string;
  /** Optional named export inside the module; defaults to `default` then `plugin`. */
  pluginExport?: string;
  enabled: boolean;
  /** 5-field cron expression. */
  cronSchedule: string;
  settings: Record<string, string>;
}

export interface IndexRunLog {
  id: number;
  dataSourceId: string;
  startedAt: Date;
  completedAt: Date | null;
  status: DataSourceStatus;
  documentCount: number;
  errorMessage: string | null;
}

export interface IndexedDocument {
  id: string;
  machine: string;
  type: string;
  provider: string;
  location: string;
  title: string;
  summary: string;
  content: string;
  extendedData: Record<string, string>;
  indexedAt: Date;
  contentHash: string;
}

export interface SearchQuery {
  queryText: string;
  dataSourceId?: string;
  dataSourceName?: string;
  provider?: string;
  type?: string;
  machine?: string;
  maxResults?: number;
  offset?: number;
}

export interface SearchResult {
  document: IndexedDocument;
  rank: number;
}

export interface PluginSettingDescriptorDto {
  key: string;
  displayName: string;
  description: string;
  settingType: string;
  defaultValue: string;
  isRequired: boolean;
}

export interface PluginInfoDto {
  pluginModule: string;
  pluginExport: string;
  id: string;
  name: string;
  description: string;
  version: string;
  settings: PluginSettingDescriptorDto[];
}
