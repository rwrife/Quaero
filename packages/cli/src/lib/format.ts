export function formatTable(rows: Array<Record<string, unknown>>, columns?: string[]): string {
  if (rows.length === 0) return '(none)';
  const cols = columns ?? Object.keys(rows[0]);
  const widths = cols.map((c) =>
    Math.max(c.length, ...rows.map((r) => stringify(r[c]).length)),
  );
  const header = cols.map((c, i) => c.padEnd(widths[i])).join('  ');
  const sep = widths.map((w) => '-'.repeat(w)).join('  ');
  const body = rows
    .map((r) => cols.map((c, i) => stringify(r[c]).padEnd(widths[i])).join('  '))
    .join('\n');
  return `${header}\n${sep}\n${body}`;
}

export function stringify(v: unknown): string {
  if (v === null || v === undefined) return '';
  if (typeof v === 'string') return v;
  if (typeof v === 'number' || typeof v === 'boolean') return String(v);
  return JSON.stringify(v);
}

export function printJson(value: unknown): void {
  process.stdout.write(JSON.stringify(value, null, 2) + '\n');
}
