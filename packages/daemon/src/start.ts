import { consoleLogger } from '@quaero/core';
import { createRuntime, type CreateRuntimeOptions, type DaemonRuntime } from './runtime.js';
import { buildServer } from './server.js';
import { Scheduler } from './scheduler.js';
import type { FastifyInstance } from 'fastify';

export interface StartDaemonOptions extends CreateRuntimeOptions {
  host?: string;
  port?: number;
  schedulerIntervalMs?: number;
  /** When true (default), installs SIGINT/SIGTERM handlers for graceful shutdown. */
  installSignalHandlers?: boolean;
  logger?: boolean;
}

export interface RunningDaemon {
  runtime: DaemonRuntime;
  server: FastifyInstance;
  scheduler: Scheduler;
  address: string;
  stop(): Promise<void>;
}

export async function startDaemon(options: StartDaemonOptions = {}): Promise<RunningDaemon> {
  const runtime = createRuntime(options);
  const server = buildServer({ runtime, configPath: options.configPath, logger: options.logger });
  const scheduler = new Scheduler(runtime.indexingService, {
    intervalMs: options.schedulerIntervalMs,
    logger: consoleLogger,
  });

  const host = options.host ?? '127.0.0.1';
  const port = options.port ?? portFromBaseUrl(runtime.config.serverBaseUrl) ?? 5055;
  const address = await server.listen({ host, port });
  scheduler.start();

  let stopped = false;
  const stop = async () => {
    if (stopped) return;
    stopped = true;
    scheduler.stop();
    try {
      await server.close();
    } finally {
      runtime.close();
    }
  };

  if (options.installSignalHandlers !== false) {
    const onSignal = (signal: NodeJS.Signals) => {
      consoleLogger.info(`received ${signal}, shutting down`);
      stop().catch((err) => {
        consoleLogger.error('shutdown failed', {
          error: err instanceof Error ? err.message : String(err),
        });
        process.exit(1);
      });
    };
    process.once('SIGINT', () => onSignal('SIGINT'));
    process.once('SIGTERM', () => onSignal('SIGTERM'));
  }

  consoleLogger.info('quaero-daemon listening', { address });
  return { runtime, server, scheduler, address, stop };
}

function portFromBaseUrl(url: string | undefined): number | null {
  if (!url) return null;
  try {
    const parsed = new URL(url);
    if (parsed.port) return Number(parsed.port);
    return parsed.protocol === 'https:' ? 443 : 80;
  } catch {
    return null;
  }
}
