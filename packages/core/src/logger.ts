export interface ConsoleLikeLogger {
  info(msg: string, meta?: Record<string, unknown>): void;
  warn(msg: string, meta?: Record<string, unknown>): void;
  error(msg: string, meta?: Record<string, unknown>): void;
  debug?(msg: string, meta?: Record<string, unknown>): void;
}

export const consoleLogger: ConsoleLikeLogger = {
  info: (m, meta) => console.log(`[info] ${m}`, meta ?? ''),
  warn: (m, meta) => console.warn(`[warn] ${m}`, meta ?? ''),
  error: (m, meta) => console.error(`[err]  ${m}`, meta ?? ''),
  debug: (m, meta) => {
    if (process.env.QUAERO_DEBUG) console.log(`[dbg]  ${m}`, meta ?? '');
  },
};
