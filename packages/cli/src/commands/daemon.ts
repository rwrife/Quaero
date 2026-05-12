import { Command } from 'commander';
import { daemonStatus, startDaemonProcess, stopDaemonProcess } from '../lib/daemon-control.js';
import { printJson } from '../lib/format.js';

export function registerDaemonCommand(program: Command): void {
  const daemon = program.command('daemon').description('Control the Quaero background daemon');

  daemon
    .command('start')
    .description('Start the daemon (detached)')
    .option('--host <host>', 'bind host', '127.0.0.1')
    .option('--port <port>', 'bind port', (v) => Number(v))
    .option('--foreground', 'run attached in the foreground', false)
    .option('--json', 'output raw JSON')
    .action(async (opts) => {
      const result = await startDaemonProcess({
        host: opts.host,
        port: opts.port,
        detached: !opts.foreground,
      });
      if (opts.json) return printJson(result);
      process.stdout.write(`Daemon started (pid ${result.pid}) at ${result.address}.\n`);
      if (opts.foreground) {
        // When attached, wait forever; user will Ctrl+C.
        await new Promise(() => {});
      }
    });

  daemon
    .command('stop')
    .description('Stop the daemon')
    .option('--json', 'output raw JSON')
    .action(async (opts) => {
      const result = await stopDaemonProcess();
      if (opts.json) return printJson(result);
      if (!result.pid) {
        process.stdout.write('Daemon not running.\n');
        return;
      }
      if (!result.stopped) {
        process.stdout.write(`Daemon pid ${result.pid} already gone.\n`);
        return;
      }
      process.stdout.write(`Stopped daemon (pid ${result.pid}).\n`);
    });

  daemon
    .command('status')
    .description('Show daemon status')
    .option('--api <url>', 'override daemon base URL')
    .option('--json', 'output raw JSON')
    .action(async (opts) => {
      const status = await daemonStatus(opts.api as string | undefined);
      if (opts.json) return printJson(status);
      const lines: string[] = [];
      lines.push(`address      : ${status.address ?? '(unknown)'}`);
      lines.push(`process      : ${status.running ? `running (pid ${status.pid})` : 'not running'}`);
      lines.push(`reachable    : ${status.reachable ? 'yes' : 'no'}`);
      if (status.state) lines.push(`state        : ${status.state}`);
      if (typeof status.documentCount === 'number') lines.push(`documents    : ${status.documentCount}`);
      if (status.lastRunTime) lines.push(`lastRunTime  : ${status.lastRunTime}`);
      if (status.lastError) lines.push(`lastError    : ${status.lastError}`);
      process.stdout.write(lines.join('\n') + '\n');
    });
}
