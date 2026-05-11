import fs from 'node:fs';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import { defaultDataSourcesPath } from './paths.js';
import type { DataSource } from './models.js';

/**
 * Persists data source configurations to a JSON file.
 * Runtime status is tracked in the SQLite run log, not here.
 */
export class DataSourceStore {
  private readonly filePath: string;
  private dataSources: DataSource[] = [];

  constructor(filePath?: string) {
    this.filePath = filePath ?? defaultDataSourcesPath();
    fs.mkdirSync(path.dirname(this.filePath), { recursive: true });
    this.load();
  }

  get path(): string {
    return this.filePath;
  }

  list(): readonly DataSource[] {
    return this.dataSources;
  }

  getEnabled(): DataSource[] {
    return this.dataSources.filter((d) => d.enabled);
  }

  getById(id: string): DataSource | undefined {
    return this.dataSources.find((d) => d.id === id);
  }

  add(ds: Omit<DataSource, 'id'> & { id?: string }): DataSource {
    const created: DataSource = { id: ds.id ?? randomUUID().replace(/-/g, ''), ...ds };
    this.dataSources.push(created);
    this.save();
    return created;
  }

  update(ds: DataSource): boolean {
    const i = this.dataSources.findIndex((d) => d.id === ds.id);
    if (i < 0) return false;
    this.dataSources[i] = ds;
    this.save();
    return true;
  }

  remove(id: string): boolean {
    const before = this.dataSources.length;
    this.dataSources = this.dataSources.filter((d) => d.id !== id);
    const removed = this.dataSources.length !== before;
    if (removed) this.save();
    return removed;
  }

  reload(): void {
    this.load();
  }

  private load(): void {
    if (!fs.existsSync(this.filePath)) {
      this.dataSources = [];
      return;
    }
    try {
      const raw = fs.readFileSync(this.filePath, 'utf8');
      const parsed = JSON.parse(raw) as DataSource[];
      this.dataSources = Array.isArray(parsed) ? parsed : [];
    } catch {
      this.dataSources = [];
    }
  }

  private save(): void {
    fs.writeFileSync(this.filePath, JSON.stringify(this.dataSources, null, 2), 'utf8');
  }
}
