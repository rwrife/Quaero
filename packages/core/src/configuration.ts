import fs from 'node:fs';
import path from 'node:path';
import { defaultConfigPath } from './paths.js';
import { defaultIndexConfiguration, type IndexConfiguration } from './models.js';

export function loadConfiguration(filePath: string = defaultConfigPath()): IndexConfiguration {
  if (!fs.existsSync(filePath)) return defaultIndexConfiguration();
  try {
    const raw = fs.readFileSync(filePath, 'utf8');
    const parsed = JSON.parse(raw) as Partial<IndexConfiguration>;
    return { ...defaultIndexConfiguration(), ...parsed };
  } catch {
    return defaultIndexConfiguration();
  }
}

export function saveConfiguration(
  config: IndexConfiguration,
  filePath: string = defaultConfigPath(),
): void {
  fs.mkdirSync(path.dirname(filePath), { recursive: true });
  fs.writeFileSync(filePath, JSON.stringify(config, null, 2), 'utf8');
}
