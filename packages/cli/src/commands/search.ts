import { Command } from 'commander';
import { createApiClient } from '../lib/api-client.js';
import { formatTable, printJson } from '../lib/format.js';

interface SearchResultDto {
  documentId?: string;
  title?: string;
  url?: string;
  filePath?: string;
  provider?: string;
  type?: string;
  dataSourceName?: string;
  score?: number;
  snippet?: string;
  modifiedAt?: string | null;
}

export function registerSearchCommand(program: Command): void {
  program
    .command('search <query...>')
    .description('Search the index via the running daemon')
    .option('-n, --max <count>', 'maximum results', (v) => Number(v), 20)
    .option('-o, --offset <n>', 'offset for pagination', (v) => Number(v), 0)
    .option('--data-source <id>', 'restrict to a data source id')
    .option('--data-source-name <name>', 'restrict to a data source name')
    .option('--provider <name>', 'restrict to a provider')
    .option('--type <name>', 'restrict to a document type')
    .option('--machine <name>', 'restrict to a machine')
    .option('--api <url>', 'override daemon base URL')
    .option('--json', 'output raw JSON')
    .action(async (queryWords: string[], opts) => {
      const queryText = queryWords.join(' ').trim();
      const client = createApiClient(opts.api as string | undefined);
      const body = {
        queryText,
        maxResults: opts.max,
        offset: opts.offset,
        dataSourceId: opts.dataSource,
        dataSourceName: opts.dataSourceName,
        provider: opts.provider,
        type: opts.type,
        machine: opts.machine,
      };
      const result = await client.searchPost<{
        results: SearchResultDto[];
        count: number;
      }>(body);
      if (opts.json) {
        printJson(result);
        return;
      }
      if (result.count === 0) {
        process.stdout.write('No results.\n');
        return;
      }
      process.stdout.write(`${result.count} result${result.count === 1 ? '' : 's'}\n\n`);
      const rows = result.results.map((r) => ({
        title: truncate(r.title ?? r.documentId ?? '(untitled)', 50),
        source: r.dataSourceName ?? r.provider ?? '',
        type: r.type ?? '',
        score: r.score !== undefined ? r.score.toFixed(2) : '',
        location: truncate(r.url ?? r.filePath ?? '', 60),
      }));
      process.stdout.write(formatTable(rows, ['title', 'source', 'type', 'score', 'location']) + '\n');
    });
}

function truncate(s: string, n: number): string {
  if (s.length <= n) return s;
  return s.slice(0, n - 1) + '…';
}
