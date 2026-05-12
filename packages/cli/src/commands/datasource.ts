import { Command } from 'commander';
import { createApiClient } from '../lib/api-client.js';
import { formatTable, printJson } from '../lib/format.js';

interface DataSourceDto {
  id: string;
  name: string;
  pluginModule: string;
  pluginExport?: string;
  enabled: boolean;
  cronSchedule: string;
  settings?: Record<string, string>;
  latestRun?: RunDto | null;
}

interface RunDto {
  id: string;
  dataSourceId: string;
  startedAt: string;
  completedAt: string | null;
  status: string;
  documentCount: number;
  errorMessage: string | null;
}

function parseKeyValueList(values: string[] | undefined): Record<string, string> {
  const out: Record<string, string> = {};
  if (!values) return out;
  for (const raw of values) {
    const eq = raw.indexOf('=');
    if (eq <= 0) throw new Error(`invalid --set value (expected key=value): ${raw}`);
    const key = raw.slice(0, eq).trim();
    const val = raw.slice(eq + 1);
    if (!key) throw new Error(`invalid --set value (empty key): ${raw}`);
    out[key] = val;
  }
  return out;
}

export function registerDataSourceCommand(program: Command): void {
  const ds = program
    .command('ds')
    .alias('datasource')
    .description('Manage data sources');

  ds
    .command('list')
    .description('List data sources')
    .option('--api <url>', 'override daemon base URL')
    .option('--json', 'output raw JSON')
    .action(async (opts) => {
      const client = createApiClient(opts.api as string | undefined);
      const items = await client.request<DataSourceDto[]>('GET', '/api/datasources');
      if (opts.json) return printJson(items);
      if (items.length === 0) {
        process.stdout.write('No data sources configured.\n');
        return;
      }
      const rows = items.map((d) => ({
        id: d.id,
        name: d.name,
        plugin: d.pluginExport ? `${d.pluginModule}#${d.pluginExport}` : d.pluginModule,
        enabled: d.enabled ? 'yes' : 'no',
        cron: d.cronSchedule,
        lastRun: d.latestRun?.startedAt ?? '',
        status: d.latestRun?.status ?? '',
      }));
      process.stdout.write(
        formatTable(rows, ['id', 'name', 'plugin', 'enabled', 'cron', 'lastRun', 'status']) + '\n',
      );
    });

  ds
    .command('add')
    .description('Add a data source')
    .requiredOption('--name <name>', 'human-readable name')
    .requiredOption('--plugin <module>', 'plugin module specifier')
    .option('--export <name>', 'named export inside the plugin module')
    .option('--cron <expr>', 'cron schedule', '*/30 * * * *')
    .option('--disabled', 'create disabled', false)
    .option('--set <key=value>', 'plugin setting (repeatable)', (val: string, prev: string[] = []) => {
      prev.push(val);
      return prev;
    })
    .option('--api <url>', 'override daemon base URL')
    .option('--json', 'output raw JSON')
    .action(async (opts) => {
      const client = createApiClient(opts.api as string | undefined);
      const body = {
        name: opts.name,
        pluginModule: opts.plugin,
        pluginExport: opts.export,
        enabled: !opts.disabled,
        cronSchedule: opts.cron,
        settings: parseKeyValueList(opts.set as string[] | undefined),
      };
      const created = await client.request<DataSourceDto>('POST', '/api/datasources', body);
      if (opts.json) return printJson(created);
      process.stdout.write(`Created data source ${created.id} (${created.name}).\n`);
    });

  ds
    .command('edit <id>')
    .description('Edit a data source')
    .option('--name <name>')
    .option('--plugin <module>')
    .option('--export <name>')
    .option('--cron <expr>')
    .option('--enable', 'enable the data source')
    .option('--disable', 'disable the data source')
    .option('--set <key=value>', 'replace settings (repeatable)', (val: string, prev: string[] = []) => {
      prev.push(val);
      return prev;
    })
    .option('--api <url>', 'override daemon base URL')
    .option('--json', 'output raw JSON')
    .action(async (id: string, opts) => {
      const client = createApiClient(opts.api as string | undefined);
      const patch: Record<string, unknown> = {};
      if (opts.name !== undefined) patch.name = opts.name;
      if (opts.plugin !== undefined) patch.pluginModule = opts.plugin;
      if (opts.export !== undefined) patch.pluginExport = opts.export;
      if (opts.cron !== undefined) patch.cronSchedule = opts.cron;
      if (opts.enable) patch.enabled = true;
      if (opts.disable) patch.enabled = false;
      if (opts.set !== undefined) patch.settings = parseKeyValueList(opts.set as string[]);
      const updated = await client.request<DataSourceDto>('PUT', `/api/datasources/${encodeURIComponent(id)}`, patch);
      if (opts.json) return printJson(updated);
      process.stdout.write(`Updated data source ${updated.id}.\n`);
    });

  ds
    .command('remove <id>')
    .alias('rm')
    .description('Remove a data source')
    .option('--api <url>', 'override daemon base URL')
    .action(async (id: string, opts) => {
      const client = createApiClient(opts.api as string | undefined);
      await client.request('DELETE', `/api/datasources/${encodeURIComponent(id)}`);
      process.stdout.write(`Removed data source ${id}.\n`);
    });

  ds
    .command('run <id>')
    .description('Trigger an indexing run for a data source')
    .option('--api <url>', 'override daemon base URL')
    .option('--json', 'output raw JSON')
    .action(async (id: string, opts) => {
      const client = createApiClient(opts.api as string | undefined);
      const res = await client.request<{ accepted: boolean; dataSourceId: string }>(
        'POST',
        `/api/datasources/${encodeURIComponent(id)}/run`,
      );
      if (opts.json) return printJson(res);
      process.stdout.write(`Queued run for ${res.dataSourceId}.\n`);
    });

  ds
    .command('runs <id>')
    .description('Show recent run history for a data source')
    .option('-n, --limit <count>', 'how many runs to show', (v) => Number(v), 10)
    .option('--api <url>', 'override daemon base URL')
    .option('--json', 'output raw JSON')
    .action(async (id: string, opts) => {
      const client = createApiClient(opts.api as string | undefined);
      const runs = await client.request<RunDto[]>(
        'GET',
        `/api/datasources/${encodeURIComponent(id)}/runs?limit=${opts.limit}`,
      );
      if (opts.json) return printJson(runs);
      if (runs.length === 0) {
        process.stdout.write('No runs recorded.\n');
        return;
      }
      const rows = runs.map((r) => ({
        startedAt: r.startedAt,
        completedAt: r.completedAt ?? '',
        status: r.status,
        documents: r.documentCount,
        error: r.errorMessage ?? '',
      }));
      process.stdout.write(formatTable(rows, ['startedAt', 'completedAt', 'status', 'documents', 'error']) + '\n');
    });
}
