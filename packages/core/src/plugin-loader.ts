import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
import { createRequire } from 'node:module';
import type { ISearchPlugin } from '@quaero/plugin-api';

/**
 * Loads plugins by module specifier. Resolution order for `pluginModule`:
 *
 *  1. If absolute / starts with `./` or `/` → load via dynamic import directly.
 *  2. If a directory with that name exists under `pluginsDirectory` → import its `package.json` main.
 *  3. Try `pluginsDirectory/<name>.js` and `<name>.mjs`.
 *  4. Otherwise, attempt a normal `import()` (resolves from this package's node_modules).
 *
 * `pluginExport` selects the named export; defaults to `default` then `plugin`.
 */
export class PluginLoader {
  private readonly cache = new Map<string, ISearchPlugin>();
  private readonly require: NodeRequire;

  constructor(public readonly pluginsDirectory: string) {
    fs.mkdirSync(pluginsDirectory, { recursive: true });
    // require() resolution anchored at the plugins dir, so users can `npm i` plugins there.
    this.require = createRequire(path.join(pluginsDirectory, 'noop.js'));
  }

  async createPlugin(pluginModule: string, pluginExport?: string): Promise<ISearchPlugin | null> {
    const key = `${pluginModule}::${pluginExport ?? ''}`;
    const cached = this.cache.get(key);
    if (cached) return cached;

    const resolved = await this.resolveAndImport(pluginModule);
    if (!resolved) return null;

    const exportName = pluginExport ?? (resolved.default !== undefined ? 'default' : 'plugin');
    const candidate = (resolved as Record<string, unknown>)[exportName];

    let instance: ISearchPlugin | null = null;
    if (typeof candidate === 'function') {
      instance = new (candidate as new () => ISearchPlugin)();
    } else if (candidate && typeof candidate === 'object') {
      instance = candidate as ISearchPlugin;
    }

    if (!instance || !isSearchPlugin(instance)) return null;
    this.cache.set(key, instance);
    return instance;
  }

  /**
   * Discovers plugins by scanning `pluginsDirectory` for sub-packages with
   * `quaero` field in package.json or a `plugin.js` entry, plus any `*.plugin.js`
   * files at the top level. Also includes any explicit modules listed in
   * `<pluginsDirectory>/plugins.json`.
   */
  async discover(): Promise<Array<{ pluginModule: string; pluginExport: string; instance: ISearchPlugin }>> {
    const out: Array<{ pluginModule: string; pluginExport: string; instance: ISearchPlugin }> = [];
    const seen = new Set<string>();

    const tryAdd = async (mod: string, exp?: string) => {
      if (seen.has(`${mod}::${exp ?? ''}`)) return;
      const inst = await this.createPlugin(mod, exp);
      if (inst) {
        out.push({ pluginModule: mod, pluginExport: exp ?? (inst ? 'default' : 'plugin'), instance: inst });
        seen.add(`${mod}::${exp ?? ''}`);
      }
    };

    // 1. plugins.json manifest
    const manifest = path.join(this.pluginsDirectory, 'plugins.json');
    if (fs.existsSync(manifest)) {
      try {
        const list = JSON.parse(fs.readFileSync(manifest, 'utf8')) as Array<
          string | { module: string; export?: string }
        >;
        for (const entry of list) {
          if (typeof entry === 'string') await tryAdd(entry);
          else await tryAdd(entry.module, entry.export);
        }
      } catch {
        // ignore malformed manifest
      }
    }

    // 2. sub-directories that look like packages
    if (fs.existsSync(this.pluginsDirectory)) {
      for (const entry of fs.readdirSync(this.pluginsDirectory, { withFileTypes: true })) {
        if (entry.isDirectory()) {
          const pkgJson = path.join(this.pluginsDirectory, entry.name, 'package.json');
          if (fs.existsSync(pkgJson)) await tryAdd(path.join(this.pluginsDirectory, entry.name));
        } else if (entry.isFile() && /\.plugin\.(m?js|cjs)$/i.test(entry.name)) {
          await tryAdd(path.join(this.pluginsDirectory, entry.name));
        }
      }
    }

    return out;
  }

  private async resolveAndImport(spec: string): Promise<Record<string, unknown> | null> {
    try {
      // Absolute or relative paths
      if (path.isAbsolute(spec) || spec.startsWith('./') || spec.startsWith('../')) {
        let target = spec;
        const stat = fs.existsSync(spec) ? fs.statSync(spec) : null;
        if (stat?.isDirectory()) {
          const pkg = path.join(spec, 'package.json');
          if (fs.existsSync(pkg)) {
            const json = JSON.parse(fs.readFileSync(pkg, 'utf8')) as { main?: string; module?: string };
            target = path.join(spec, json.module ?? json.main ?? 'index.js');
          } else {
            target = path.join(spec, 'index.js');
          }
        }
        return (await import(pathToFileURL(target).href)) as Record<string, unknown>;
      }

      // Look for an installed package under pluginsDirectory/node_modules first
      try {
        const resolved = this.require.resolve(spec);
        return (await import(pathToFileURL(resolved).href)) as Record<string, unknown>;
      } catch {
        // fall through
      }

      // Last resort: import from the daemon/cli's own node_modules
      return (await import(spec)) as Record<string, unknown>;
    } catch {
      return null;
    }
  }
}

function isSearchPlugin(value: unknown): value is ISearchPlugin {
  if (!value || typeof value !== 'object') return false;
  const v = value as Record<string, unknown>;
  return (
    typeof v.metadata === 'object' &&
    Array.isArray(v.settingDescriptors) &&
    typeof v.initialize === 'function' &&
    typeof v.discoverDocuments === 'function'
  );
}
