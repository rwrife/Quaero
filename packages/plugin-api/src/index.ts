/**
 * Plugin metadata, settings, contracts, and the ISearchPlugin interface.
 *
 * Plugins are plain ES modules that default-export (or named-export `plugin`)
 * an object implementing {@link ISearchPlugin}.
 */

export type PluginSettingType =
  | 'text'
  | 'folderPath'
  | 'password'
  | 'number'
  | 'boolean'
  | 'globPattern';

export interface PluginSettingDescriptor {
  key: string;
  displayName: string;
  description?: string;
  settingType: PluginSettingType;
  defaultValue?: string;
  isRequired?: boolean;
}

export interface PluginMetadata {
  id: string;
  name: string;
  description: string;
  version: string;
  supportedFileExtensions?: string[];
}

export interface PluginConfiguration {
  enabled: boolean;
  settings: Record<string, string>;
  /**
   * Last time this data source was successfully indexed (ISO string parsed → Date).
   * Plugins should use this for incremental indexing. Null => first run.
   */
  lastSuccessfulRun: Date | null;
}

export interface DiscoveredDocument {
  type: string;
  provider: string;
  location: string;
  title: string;
  summary: string;
  content: string;
  extendedData?: Record<string, string>;
  /**
   * Stable hash of the source content. If omitted, the indexing service
   * will compute SHA-256 over `content`.
   */
  contentHash?: string;
}

export interface ISearchPlugin {
  readonly metadata: PluginMetadata;
  readonly settingDescriptors: readonly PluginSettingDescriptor[];

  initialize(configuration: PluginConfiguration, signal?: AbortSignal): Promise<void>;

  /**
   * Stream discovered documents. Implementations should respect the
   * AbortSignal and stop yielding promptly when aborted.
   */
  discoverDocuments(signal?: AbortSignal): AsyncIterable<DiscoveredDocument>;
}
