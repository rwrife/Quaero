// @quaero/daemon — Fastify HTTP API + scheduler
export { createRuntime, type DaemonRuntime, type CreateRuntimeOptions } from './runtime.js';
export { buildServer, type ServerOptions } from './server.js';
export { Scheduler, type SchedulerOptions } from './scheduler.js';
export { startDaemon, type StartDaemonOptions, type RunningDaemon } from './start.js';

export const DAEMON_NAME = 'quaero-daemon';
