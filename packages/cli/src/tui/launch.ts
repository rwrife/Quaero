import React from 'react';
import { render } from 'ink';
import { App } from './app.js';

export interface LaunchOptions {
  apiUrl?: string;
}

export async function launchTui(options: LaunchOptions = {}): Promise<void> {
  if (!process.stdout.isTTY) {
    process.stderr.write(
      'The interactive Quaero TUI requires a TTY. Try a sub-command, e.g. `quaero --help`.\n',
    );
    process.exitCode = 1;
    return;
  }
  const { waitUntilExit } = render(React.createElement(App, { apiUrl: options.apiUrl }));
  await waitUntilExit();
}
