import { Command } from 'commander';
import { createApiClient } from '../lib/api-client.js';
import { formatTable, printJson } from '../lib/format.js';

interface PluginInfoDto {
  pluginModule: string;
  pluginExport?: string;
  id: string;
  name: string;
  description?: string;
  version?: string;
  settings?: Array<{
    key: string;
    displayName: string;
    settingType: string;
    isRequired: boolean;
  }>;
}

export function registerPluginsCommand(program: Command): void {
  const plugins = program.command('plugins').description('Inspect installed plugins');

  plugins
    .command('list')
    .description('List discovered plugins')
    .option('--api <url>', 'override daemon base URL')
    .option('--json', 'output raw JSON')
    .action(async (opts) => {
      const client = createApiClient(opts.api as string | undefined);
      const items = await client.request<PluginInfoDto[]>('GET', '/api/plugins');
      if (opts.json) return printJson(items);
      if (items.length === 0) {
        process.stdout.write('No plugins discovered.\n');
        return;
      }
      const rows = items.map((p) => ({
        id: p.id,
        name: p.name,
        version: p.version ?? '',
        module: p.pluginExport ? `${p.pluginModule}#${p.pluginExport}` : p.pluginModule,
        settings: (p.settings ?? []).map((s) => s.key).join(','),
      }));
      process.stdout.write(formatTable(rows, ['id', 'name', 'version', 'module', 'settings']) + '\n');
    });
}
