#!/usr/bin/env node
import { startDaemon } from '../index.js';

const portEnv = process.env.QUAERO_PORT;
const hostEnv = process.env.QUAERO_HOST;

startDaemon({
  host: hostEnv && hostEnv.trim() ? hostEnv.trim() : undefined,
  port: portEnv && portEnv.trim() ? Number(portEnv) : undefined,
}).catch((err) => {
  console.error('[err] daemon failed to start', err);
  process.exit(1);
});
