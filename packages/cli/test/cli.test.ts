import { describe, it, expect } from 'vitest';
import { buildProgram, CLI_NAME, CLI_VERSION } from '../src/index.js';

describe('@quaero/cli program', () => {
  it('exposes constants', () => {
    expect(CLI_NAME).toBe('quaero');
    expect(CLI_VERSION).toMatch(/^\d+\.\d+\.\d+/);
  });

  it('registers expected sub-commands', () => {
    const program = buildProgram();
    const names = program.commands.map((c) => c.name()).sort();
    // Top-level commands.
    expect(names).toContain('search');
    expect(names).toContain('ds');
    expect(names).toContain('daemon');
    expect(names).toContain('plugins');
  });

  it('registers ds sub-commands', () => {
    const program = buildProgram();
    const ds = program.commands.find((c) => c.name() === 'ds');
    expect(ds).toBeDefined();
    const subs = ds!.commands.map((c) => c.name()).sort();
    expect(subs).toEqual(expect.arrayContaining(['add', 'edit', 'list', 'remove', 'run', 'runs']));
  });

  it('registers daemon sub-commands', () => {
    const program = buildProgram();
    const daemon = program.commands.find((c) => c.name() === 'daemon');
    expect(daemon).toBeDefined();
    const subs = daemon!.commands.map((c) => c.name()).sort();
    expect(subs).toEqual(expect.arrayContaining(['start', 'status', 'stop']));
  });

  it('produces help text without throwing', () => {
    const program = buildProgram();
    const help = program.helpInformation();
    expect(help).toMatch(/quaero/);
    expect(help).toMatch(/search/);
    expect(help).toMatch(/daemon/);
  });
});
