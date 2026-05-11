import Database from 'better-sqlite3';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { randomUUID } from 'node:crypto';
import { decryptString, encryptString } from './crypto.js';
import type {
  DataSourceStatus,
  IndexConfiguration,
  IndexRunLog,
  IndexedDocument,
  SearchQuery,
  SearchResult,
} from './models.js';

/**
 * SQLite-backed index store with FTS5 full-text search and optional AES-256-GCM
 * encryption of document content at rest. Mirrors the C# IndexStore semantics.
 */
export class IndexStore {
  private readonly db: Database.Database;
  private readonly config: IndexConfiguration;
  private closed = false;

  constructor(config: IndexConfiguration) {
    this.config = config;
    const dir = path.dirname(config.databasePath);
    fs.mkdirSync(dir, { recursive: true });
    this.db = new Database(config.databasePath);
    this.db.pragma('journal_mode = WAL');
    this.initSchema();
  }

  get database(): Database.Database {
    return this.db;
  }

  private initSchema(): void {
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS documents (
        id TEXT PRIMARY KEY,
        machine TEXT NOT NULL,
        type TEXT NOT NULL,
        provider TEXT NOT NULL,
        location TEXT NOT NULL,
        title TEXT NOT NULL,
        summary TEXT NOT NULL,
        content TEXT NOT NULL,
        extended_data TEXT NOT NULL DEFAULT '{}',
        indexed_at TEXT NOT NULL,
        content_hash TEXT NOT NULL
      );
      CREATE INDEX IF NOT EXISTS idx_documents_provider ON documents(provider);
      CREATE INDEX IF NOT EXISTS idx_documents_type ON documents(type);
      CREATE INDEX IF NOT EXISTS idx_documents_machine ON documents(machine);
      CREATE INDEX IF NOT EXISTS idx_documents_hash ON documents(content_hash);
      CREATE INDEX IF NOT EXISTS idx_documents_location ON documents(location);

      CREATE TABLE IF NOT EXISTS index_run_log (
        id INTEGER PRIMARY KEY AUTOINCREMENT,
        data_source_id TEXT NOT NULL,
        started_at TEXT NOT NULL,
        completed_at TEXT,
        status TEXT NOT NULL DEFAULT 'indexing',
        document_count INTEGER NOT NULL DEFAULT 0,
        error_message TEXT
      );
      CREATE INDEX IF NOT EXISTS idx_run_log_ds ON index_run_log(data_source_id);
      CREATE INDEX IF NOT EXISTS idx_run_log_status ON index_run_log(data_source_id, status);

      CREATE VIRTUAL TABLE IF NOT EXISTS documents_fts USING fts5(
        id UNINDEXED, title, summary, content
      );
    `);
  }

  upsertDocument(doc: IndexedDocument): void {
    const content =
      this.config.encryptionEnabled && this.config.encryptionKey
        ? encryptString(doc.content, this.config.encryptionKey)
        : doc.content;

    const stmt = this.db.prepare(`
      INSERT INTO documents (id, machine, type, provider, location, title, summary, content, extended_data, indexed_at, content_hash)
      VALUES (@id, @machine, @type, @provider, @location, @title, @summary, @content, @extended, @indexedAt, @hash)
      ON CONFLICT(id) DO UPDATE SET
        title = excluded.title,
        summary = excluded.summary,
        content = excluded.content,
        extended_data = excluded.extended_data,
        indexed_at = excluded.indexed_at,
        content_hash = excluded.content_hash
    `);

    const fts = this.db.prepare(`
      INSERT OR REPLACE INTO documents_fts (id, title, summary, content)
      VALUES (?, ?, ?, ?)
    `);

    const tx = this.db.transaction(() => {
      stmt.run({
        id: doc.id,
        machine: doc.machine,
        type: doc.type,
        provider: doc.provider,
        location: doc.location,
        title: doc.title,
        summary: doc.summary,
        content,
        extended: JSON.stringify(doc.extendedData ?? {}),
        indexedAt: doc.indexedAt.toISOString(),
        hash: doc.contentHash,
      });
      fts.run(doc.id, doc.title, doc.summary, doc.content);
    });
    tx();
  }

  search(query: SearchQuery): SearchResult[] {
    const params: Record<string, unknown> = {};
    const hasFts = query.queryText && query.queryText.trim().length > 0;
    let sql: string;
    if (hasFts) {
      sql = `
        SELECT d.*, fts.rank AS rank
        FROM documents_fts fts
        JOIN documents d ON d.id = fts.id
        WHERE documents_fts MATCH @q`;
      params.q = query.queryText;
    } else {
      sql = `SELECT d.*, 0 AS rank FROM documents d WHERE 1=1`;
    }
    if (query.provider) {
      sql += ` AND d.provider = @provider COLLATE NOCASE`;
      params.provider = query.provider;
    }
    if (query.dataSourceId) {
      sql += ` AND json_extract(d.extended_data, '$.DataSourceId') = @dsId`;
      params.dsId = query.dataSourceId;
    }
    if (query.dataSourceName) {
      sql += ` AND json_extract(d.extended_data, '$.DataSourceName') = @dsName`;
      params.dsName = query.dataSourceName;
    }
    if (query.type) {
      sql += ` AND d.type = @type`;
      params.type = query.type;
    }
    if (query.machine) {
      sql += ` AND d.machine = @machine`;
      params.machine = query.machine;
    }
    sql += hasFts ? ` ORDER BY rank` : ` ORDER BY d.indexed_at DESC`;
    sql += ` LIMIT @limit OFFSET @offset`;
    params.limit = query.maxResults ?? 50;
    params.offset = query.offset ?? 0;

    const rows = this.db.prepare(sql).all(params) as Array<Record<string, unknown>>;
    return rows.map((r) => ({ document: this.rowToDoc(r), rank: Number(r.rank) }));
  }

  getByLocation(location: string): IndexedDocument | null {
    const row = this.db.prepare('SELECT * FROM documents WHERE location = ?').get(location) as
      | Record<string, unknown>
      | undefined;
    return row ? this.rowToDoc(row) : null;
  }

  hasChanged(location: string, contentHash: string): boolean {
    const row = this.db
      .prepare('SELECT content_hash FROM documents WHERE location = ?')
      .get(location) as { content_hash?: string } | undefined;
    if (!row) return true;
    return row.content_hash !== contentHash;
  }

  ensureDataSourceMetadataByLocation(
    location: string,
    dataSourceId: string,
    dataSourceName: string,
  ): void {
    this.db
      .prepare(
        `UPDATE documents
         SET extended_data = json_set(
              json_set(COALESCE(extended_data, '{}'), '$.DataSourceId', ?),
              '$.DataSourceName', ?)
         WHERE location = ?`,
      )
      .run(dataSourceId, dataSourceName, location);
  }

  getDocumentCount(): number {
    const r = this.db.prepare('SELECT COUNT(*) AS n FROM documents').get() as { n: number };
    return r.n;
  }

  getProviders(): string[] {
    return (
      this.db.prepare('SELECT DISTINCT provider FROM documents ORDER BY provider').all() as Array<{
        provider: string;
      }>
    ).map((r) => r.provider);
  }

  compact(): void {
    this.db.exec('VACUUM');
  }

  // --- run log ---

  startRunLog(dataSourceId: string): number {
    const info = this.db
      .prepare(
        `INSERT INTO index_run_log (data_source_id, started_at, status)
         VALUES (?, ?, 'indexing')`,
      )
      .run(dataSourceId, new Date().toISOString());
    return Number(info.lastInsertRowid);
  }

  completeRunLog(
    logId: number,
    status: DataSourceStatus,
    documentCount: number,
    error: string | null = null,
  ): void {
    this.db
      .prepare(
        `UPDATE index_run_log
         SET completed_at = ?, status = ?, document_count = ?, error_message = ?
         WHERE id = ?`,
      )
      .run(new Date().toISOString(), status, documentCount, error, logId);
  }

  getLastSuccessfulRun(dataSourceId: string): Date | null {
    const row = this.db
      .prepare(
        `SELECT completed_at FROM index_run_log
         WHERE data_source_id = ? AND status = 'success'
         ORDER BY completed_at DESC LIMIT 1`,
      )
      .get(dataSourceId) as { completed_at?: string } | undefined;
    return row?.completed_at ? new Date(row.completed_at) : null;
  }

  getLatestRun(dataSourceId: string): IndexRunLog | null {
    const row = this.db
      .prepare(
        `SELECT id, data_source_id, started_at, completed_at, status, document_count, error_message
         FROM index_run_log WHERE data_source_id = ?
         ORDER BY started_at DESC LIMIT 1`,
      )
      .get(dataSourceId) as Record<string, unknown> | undefined;
    return row ? this.rowToRunLog(row) : null;
  }

  getRunHistory(dataSourceId: string, limit = 20): IndexRunLog[] {
    const rows = this.db
      .prepare(
        `SELECT id, data_source_id, started_at, completed_at, status, document_count, error_message
         FROM index_run_log WHERE data_source_id = ?
         ORDER BY started_at DESC LIMIT ?`,
      )
      .all(dataSourceId, limit) as Array<Record<string, unknown>>;
    return rows.map((r) => this.rowToRunLog(r));
  }

  close(): void {
    if (!this.closed) {
      this.db.close();
      this.closed = true;
    }
  }

  // --- helpers ---

  private rowToDoc(row: Record<string, unknown>): IndexedDocument {
    let content = String(row.content);
    if (this.config.encryptionEnabled && this.config.encryptionKey) {
      try {
        content = decryptString(content, this.config.encryptionKey);
      } catch {
        // value may not be encrypted (e.g. older write)
      }
    }
    let extended: Record<string, string> = {};
    try {
      extended = JSON.parse(String(row.extended_data ?? '{}')) as Record<string, string>;
    } catch {
      extended = {};
    }
    return {
      id: String(row.id),
      machine: String(row.machine),
      type: String(row.type),
      provider: String(row.provider),
      location: String(row.location),
      title: String(row.title),
      summary: String(row.summary),
      content,
      extendedData: extended,
      indexedAt: new Date(String(row.indexed_at)),
      contentHash: String(row.content_hash),
    };
  }

  private rowToRunLog(row: Record<string, unknown>): IndexRunLog {
    return {
      id: Number(row.id),
      dataSourceId: String(row.data_source_id),
      startedAt: new Date(String(row.started_at)),
      completedAt: row.completed_at ? new Date(String(row.completed_at)) : null,
      status: String(row.status) as DataSourceStatus,
      documentCount: Number(row.document_count),
      errorMessage: row.error_message ? String(row.error_message) : null,
    };
  }
}

export function newDocumentId(): string {
  return randomUUID().replace(/-/g, '');
}

export function localMachineName(): string {
  return os.hostname();
}
