import { Command } from 'commander';
import { registerSearchCommand } from './commands/search.js';
import { registerDataSourceCommand } from './commands/datasource.js';
import { registerDaemonCommand } from './commands/daemon.js';
import { registerPluginsCommand } from './commands/plugins.js';
import { launchTui } from './tui/launch.js';

export const CLI_NAME = 'quaero';
export const CLI_VERSION = '0.2.0';

export interface RunOptions {
  argv?: string[];
  /** When true, exit handlers throw instead of calling process.exit (for tests). */
  noExit?: boolean;
}

export function buildProgram(): Command {
  const program = new Command();
  program
    .name(CLI_NAME)
    .description('Quaero — local-first personal indexing & search')
    .version(CLI_VERSION)
    .option('--api <url>', 'override daemon base URL for all sub-commands');

  registerSearchCommand(program);
  registerDataSourceCommand(program);
  registerDaemonCommand(program);
  registerPluginsCommand(program);

  // Default action: launch the TUI when no sub-command is given.
  program.action(async () => {
    const opts = program.opts<{ api?: string }>();
    await launchTui({ apiUrl: opts.api });
  });

  return program;
}

export async function runCli(options: RunOptions = {}): Promise<number> {
  const program = buildProgram();
  if (options.noExit) {
    program.exitOverride();
  }
  try {
    await program.parseAsync(options.argv ?? process.argv);
    return typeof process.exitCode === 'number' ? process.exitCode : 0;
  } catch (err) {
    if (options.noExit) throw err;
    const msg = err instanceof Error ? err.message : String(err);
    process.stderr.write(`error: ${msg}\n`);
    return 1;
  }
}

export { launchTui } from './tui/launch.js';
