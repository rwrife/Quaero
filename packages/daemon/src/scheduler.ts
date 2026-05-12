import type { IndexingService } from '@quaero/core';

export interface SchedulerOptions {
  intervalMs?: number;
  logger?: {
    info(msg: string, meta?: Record<string, unknown>): void;
    error(msg: string, meta?: Record<string, unknown>): void;
  };
}

const DEFAULT_INTERVAL_MS = 60_000;

/**
 * Periodically invokes {@link IndexingService.evaluateAndRun}. The loop is
 * serial: a new tick will not start until the previous one resolves.
 */
export class Scheduler {
  private timer: NodeJS.Timeout | null = null;
  private running = false;
  private readonly abort = new AbortController();
  private readonly intervalMs: number;
  private readonly logger: SchedulerOptions['logger'];

  constructor(
    private readonly indexingService: IndexingService,
    options: SchedulerOptions = {},
  ) {
    this.intervalMs = options.intervalMs ?? DEFAULT_INTERVAL_MS;
    this.logger = options.logger;
  }

  start(): void {
    if (this.timer) return;
    this.timer = setInterval(() => {
      void this.tick();
    }, this.intervalMs);
    if (typeof this.timer.unref === 'function') this.timer.unref();
  }

  async tick(): Promise<void> {
    if (this.running) return;
    this.running = true;
    try {
      await this.indexingService.evaluateAndRun(this.abort.signal);
    } catch (err) {
      this.logger?.error('scheduler tick failed', {
        error: err instanceof Error ? err.message : String(err),
      });
    } finally {
      this.running = false;
    }
  }

  stop(): void {
    if (this.timer) {
      clearInterval(this.timer);
      this.timer = null;
    }
    this.abort.abort();
  }
}
