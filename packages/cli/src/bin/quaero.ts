#!/usr/bin/env node
import { runCli } from '../index.js';

runCli().then(
  (code) => {
    if (code) process.exit(code);
  },
  (err) => {
    const msg = err instanceof Error ? err.message : String(err);
    process.stderr.write(`error: ${msg}\n`);
    process.exit(1);
  },
);
