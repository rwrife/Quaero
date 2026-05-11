import os from 'node:os';
import path from 'node:path';

/**
 * Resolves the per-user Quaero data directory across platforms,
 * mirroring the C# `LocalApplicationData/Quaero` convention.
 */
export function quaeroDataDir(): string {
  const env = process.env.QUAERO_DATA_DIR;
  if (env && env.trim()) return env.trim();
  const home = os.homedir();
  switch (process.platform) {
    case 'win32':
      return path.join(process.env.LOCALAPPDATA || path.join(home, 'AppData', 'Local'), 'Quaero');
    case 'darwin':
      return path.join(home, 'Library', 'Application Support', 'Quaero');
    default:
      return path.join(process.env.XDG_DATA_HOME || path.join(home, '.local', 'share'), 'quaero');
  }
}

export function defaultDatabasePath(): string {
  return path.join(quaeroDataDir(), 'index.db');
}

export function defaultDataSourcesPath(): string {
  return path.join(quaeroDataDir(), 'datasources.json');
}

export function defaultConfigPath(): string {
  return path.join(quaeroDataDir(), 'config.json');
}

export function defaultPluginsDirectory(): string {
  const env = process.env.QUAERO_PLUGINS_DIR;
  if (env && env.trim()) return env.trim();
  return path.join(quaeroDataDir(), 'plugins');
}
