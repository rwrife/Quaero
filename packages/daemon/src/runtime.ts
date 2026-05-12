import type { IndexConfiguration } from '@quaero/core';
import {
  DataSourceStore,
  IndexStore,
  IndexingService,
  PluginLoader,
  consoleLogger,
  loadConfiguration,
} from '@quaero/core';

/**
 * Wires together the persistent services used by the daemon (and tests).
 * Callers own the lifetime of the runtime and must call {@link DaemonRuntime.close}
 * to release the SQLite handle.
 */
export interface DaemonRuntime {
  config: IndexConfiguration;
  indexStore: IndexStore;
  dataSourceStore: DataSourceStore;
  pluginLoader: PluginLoader;
  indexingService: IndexingService;
  close(): void;
}

export interface CreateRuntimeOptions {
  config?: IndexConfiguration;
  configPath?: string;
  dataSourcesPath?: string;
}

export function createRuntime(options: CreateRuntimeOptions = {}): DaemonRuntime {
  const config = options.config ?? loadConfiguration(options.configPath);
  const indexStore = new IndexStore(config);
  const dataSourceStore = new DataSourceStore(options.dataSourcesPath);
  const pluginLoader = new PluginLoader(config.pluginsDirectory);
  const indexingService = new IndexingService(
    indexStore,
    dataSourceStore,
    pluginLoader,
    consoleLogger,
  );
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
