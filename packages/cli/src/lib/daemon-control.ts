import { spawn } from 'node:child_process';
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { quaeroDataDir, loadConfiguration } from '@quaero/core';

export function daemonPidFile(): string {
  return path.join(quaeroDataDir(), 'daemon.pid');
}

export interface DaemonStatus {
  running: boolean;
  pid?: number;
  address?: string;
  documentCount?: number;
  state?: string;
  lastRunTime?: string | null;
  lastError?: string | null;
  reachable: boolean;
}

function readPid(): number | undefined {
  const file = daemonPidFile();
  if (!fs.existsSync(file)) return undefined;
  const raw = fs.readFileSync(file, 'utf8').trim();
  const pid = Number(raw);
  if (!Number.isFinite(pid) || pid <= 0) return undefined;
  return pid;
}

function isAlive(pid: number): boolean {
  try {
    process.kill(pid, 0);
    return true;
  } catch {
    return false;
  }
}

export function resolveDaemonEntry(): string {
  // We are bundled as @quaero/cli/dist/lib/daemon-control.js;
  // the daemon entry lives at @quaero/daemon/dist/bin/quaero-daemon.js.
  const here = fileURLToPath(import.meta.url);
  // packages/cli/dist/lib/daemon-control.js -> packages/cli
  const cliPkgDir = path.resolve(path.dirname(here), '..', '..');
  // Walk up to monorepo root and locate daemon package's bin
  const candidates = [
    path.resolve(cliPkgDir, '..', 'daemon', 'dist', 'bin', 'quaero-daemon.js'),
    path.resolve(cliPkgDir, 'node_modules', '@quaero', 'daemon', 'dist', 'bin', 'quaero-daemon.js'),
  ];
  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) return candidate;
  }
  // Fall back to first candidate; daemon start will fail with a clear error.
  return candidates[0];
}

export async function startDaemonProcess(options: {
  host?: string;
  port?: number;
  detached?: boolean;
} = {}): Promise<{ pid: number; address: string }> {
  const existingPid = readPid();
  if (existingPid && isAlive(existingPid)) {
    throw new Error(`daemon already running (pid ${existingPid})`);
  }

  const entry = resolveDaemonEntry();
  if (!fs.existsSync(entry)) {
    throw new Error(`daemon entry not found at ${entry}. Run \`npm run build\`.`);
  }

  const config = loadConfiguration();
  const baseUrl = config.serverBaseUrl || 'http://localhost:5055';

  const env: NodeJS.ProcessEnv = { ...process.env };
  if (options.port !== undefined) env.QUAERO_PORT = String(options.port);
  if (options.host) env.QUAERO_HOST = options.host;

  fs.mkdirSync(quaeroDataDir(), { recursive: true });
  const logPath = path.join(quaeroDataDir(), 'daemon.log');
  const out = fs.openSync(logPath, 'a');
  const err = fs.openSync(logPath, 'a');

  const detached = options.detached !== false;
  const child = spawn(process.execPath, [entry], {
    detached,
    stdio: ['ignore', out, err],
    env,
  });
  if (!child.pid) {
    throw new Error('failed to spawn daemon process');
  }
  if (detached) child.unref();
  fs.writeFileSync(daemonPidFile(), String(child.pid), 'utf8');

  return { pid: child.pid, address: baseUrl };
}

export async function stopDaemonProcess(timeoutMs = 5000): Promise<{ stopped: boolean; pid?: number }> {
  const pid = readPid();
  if (!pid) return { stopped: false };
  if (!isAlive(pid)) {
    fs.rmSync(daemonPidFile(), { force: true });
    return { stopped: false, pid };
  }
  try {
    process.kill(pid, 'SIGTERM');
  } catch (e) {
    throw new Error(`failed to signal daemon pid ${pid}: ${(e as Error).message}`);
  }
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    if (!isAlive(pid)) break;
    await new Promise((r) => setTimeout(r, 100));
  }
  if (isAlive(pid)) {
    try {
      process.kill(pid, 'SIGKILL');
    } catch {
      // ignore
    }
  }
  fs.rmSync(daemonPidFile(), { force: true });
  return { stopped: true, pid };
}

export async function daemonStatus(baseUrl?: string): Promise<DaemonStatus> {
  const pid = readPid();
  const alive = pid ? isAlive(pid) : false;
  const config = loadConfiguration();
  const url = (baseUrl && baseUrl.trim()) || process.env.QUAERO_API_URL || config.serverBaseUrl || 'http://localhost:5055';
  let reachable = false;
  let info: Partial<DaemonStatus> = {};
  try {
    const res = await fetch(url, { method: 'GET' });
    if (res.ok) {
      reachable = true;
      const body = (await res.json()) as Record<string, unknown>;
      info = {
        address: url,
        documentCount: typeof body.documentCount === 'number' ? body.documentCount : undefined,
        state: typeof body.state === 'string' ? body.state : undefined,
        lastRunTime: (body.lastRunTime as string | null) ?? null,
        lastError: (body.lastError as string | null) ?? null,
      };
    }
  } catch {
    reachable = false;
  }
  return {
    running: alive,
    pid: alive ? pid : undefined,
    reachable,
    address: info.address ?? url,
    documentCount: info.documentCount,
    state: info.state,
    lastRunTime: info.lastRunTime ?? null,
    lastError: info.lastError ?? null,
  };
}
