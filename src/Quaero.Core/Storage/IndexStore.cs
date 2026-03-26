using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Quaero.Core.Models;

namespace Quaero.Core.Storage;

/// <summary>
/// SQLite-backed index store with FTS5 full-text search and optional encryption.
/// </summary>
public class IndexStore : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly IndexConfiguration _config;
    private bool _disposed;

    public IndexStore(IndexConfiguration config)
    {
        _config = config;

        var dir = Path.GetDirectoryName(config.DatabasePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var connStr = new SqliteConnectionStringBuilder
        {
            DataSource = config.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString();

        _connection = new SqliteConnection(connStr);
        _connection.Open();
        InitializeSchema();
    }

    private void InitializeSchema()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
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

            CREATE TABLE IF NOT EXISTS index_run_log (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                data_source_id TEXT NOT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT,
                status TEXT NOT NULL DEFAULT 'Indexing',
                document_count INTEGER NOT NULL DEFAULT 0,
                error_message TEXT
            );

            CREATE INDEX IF NOT EXISTS idx_run_log_ds ON index_run_log(data_source_id);
            CREATE INDEX IF NOT EXISTS idx_run_log_status ON index_run_log(data_source_id, status);
            """;
        cmd.ExecuteNonQuery();

        // Create FTS5 virtual table if it doesn't exist
        using var ftsCmd = _connection.CreateCommand();
        ftsCmd.CommandText = """
            CREATE VIRTUAL TABLE IF NOT EXISTS documents_fts USING fts5(
                id UNINDEXED,
                title,
                summary,
                content
            );
            """;
        ftsCmd.ExecuteNonQuery();
    }

    public async Task UpsertDocumentAsync(IndexedDocument doc, CancellationToken cancellationToken = default)
    {
        var extendedJson = JsonSerializer.Serialize(doc.ExtendedData);
        var content = _config.EncryptionEnabled && _config.EncryptionKey != null
            ? Encrypt(doc.Content, _config.EncryptionKey)
            : doc.Content;

        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO documents (id, machine, type, provider, location, title, summary, content, extended_data, indexed_at, content_hash)
            VALUES ($id, $machine, $type, $provider, $location, $title, $summary, $content, $extended, $indexed_at, $hash)
            ON CONFLICT(id) DO UPDATE SET
                title = excluded.title,
                summary = excluded.summary,
                content = excluded.content,
                extended_data = excluded.extended_data,
                indexed_at = excluded.indexed_at,
                content_hash = excluded.content_hash;
            """;
        cmd.Parameters.AddWithValue("$id", doc.Id);
        cmd.Parameters.AddWithValue("$machine", doc.Machine);
        cmd.Parameters.AddWithValue("$type", doc.Type);
        cmd.Parameters.AddWithValue("$provider", doc.Provider);
        cmd.Parameters.AddWithValue("$location", doc.Location);
        cmd.Parameters.AddWithValue("$title", doc.Title);
        cmd.Parameters.AddWithValue("$summary", doc.Summary);
        cmd.Parameters.AddWithValue("$content", content);
        cmd.Parameters.AddWithValue("$extended", extendedJson);
        cmd.Parameters.AddWithValue("$indexed_at", doc.IndexedAt.ToString("O"));
        cmd.Parameters.AddWithValue("$hash", doc.ContentHash);
        await cmd.ExecuteNonQueryAsync(cancellationToken);

        // Update FTS index
        using var ftsCmd = _connection.CreateCommand();
        ftsCmd.CommandText = """
            INSERT OR REPLACE INTO documents_fts (id, title, summary, content)
            VALUES ($id, $title, $summary, $content);
            """;
        ftsCmd.Parameters.AddWithValue("$id", doc.Id);
        ftsCmd.Parameters.AddWithValue("$title", doc.Title);
        ftsCmd.Parameters.AddWithValue("$summary", doc.Summary);
        ftsCmd.Parameters.AddWithValue("$content", doc.Content); // FTS always uses plaintext for search
        await ftsCmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<List<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        var results = new List<SearchResult>();

        using var cmd = _connection.CreateCommand();
        var conditions = new List<string>();
        var hasFullText = !string.IsNullOrWhiteSpace(query.QueryText);

        string sql;
        if (hasFullText)
        {
            sql = """
                SELECT d.*, fts.rank
                FROM documents_fts fts
                JOIN documents d ON d.id = fts.id
                WHERE documents_fts MATCH $query
                """;
            cmd.Parameters.AddWithValue("$query", query.QueryText);
        }
        else
        {
            sql = "SELECT d.*, 0 as rank FROM documents d WHERE 1=1";
        }

        if (query.Provider != null)
        {
            sql += " AND d.provider = $provider";
            cmd.Parameters.AddWithValue("$provider", query.Provider);
        }
        if (query.DataSourceId != null)
        {
            sql += " AND json_extract(d.extended_data, '$.DataSourceId') = $dataSourceId";
            cmd.Parameters.AddWithValue("$dataSourceId", query.DataSourceId);
        }
        if (query.Type != null)
        {
            sql += " AND d.type = $type";
            cmd.Parameters.AddWithValue("$type", query.Type);
        }
        if (query.Machine != null)
        {
            sql += " AND d.machine = $machine";
            cmd.Parameters.AddWithValue("$machine", query.Machine);
        }

        if (hasFullText)
            sql += " ORDER BY rank";
        else
            sql += " ORDER BY d.indexed_at DESC";

        sql += " LIMIT $limit OFFSET $offset";
        cmd.Parameters.AddWithValue("$limit", query.MaxResults);
        cmd.Parameters.AddWithValue("$offset", query.Offset);

        cmd.CommandText = sql;

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var doc = ReadDocument(reader);
            var rank = reader.GetDouble(reader.GetOrdinal("rank"));
            results.Add(new SearchResult { Document = doc, Rank = rank });
        }

        return results;
    }

    public async Task<IndexedDocument?> GetByLocationAsync(string location, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT * FROM documents WHERE location = $location";
        cmd.Parameters.AddWithValue("$location", location);

        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
            return ReadDocument(reader);
        return null;
    }

    public async Task<bool> HasChangedAsync(string location, string contentHash, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT content_hash FROM documents WHERE location = $location";
        cmd.Parameters.AddWithValue("$location", location);

        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        if (result is null) return true;
        return result.ToString() != contentHash;
    }

    public async Task<int> GetDocumentCountAsync(CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM documents";
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<int> GetDocumentCountAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        using var cmd = _connection.CreateCommand();

        var sql = "SELECT COUNT(*) FROM documents d WHERE 1=1";

        if (query.Provider != null)
        {
            sql += " AND d.provider = $provider";
            cmd.Parameters.AddWithValue("$provider", query.Provider);
        }
        if (query.DataSourceId != null)
        {
            sql += " AND json_extract(d.extended_data, '$.DataSourceId') = $dataSourceId";
            cmd.Parameters.AddWithValue("$dataSourceId", query.DataSourceId);
        }
        if (query.Type != null)
        {
            sql += " AND d.type = $type";
            cmd.Parameters.AddWithValue("$type", query.Type);
        }
        if (query.Machine != null)
        {
            sql += " AND d.machine = $machine";
            cmd.Parameters.AddWithValue("$machine", query.Machine);
        }

        cmd.CommandText = sql;
        var result = await cmd.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(result);
    }

    public async Task<List<string>> GetProvidersAsync(CancellationToken cancellationToken = default)
    {
        var providers = new List<string>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "SELECT DISTINCT provider FROM documents ORDER BY provider";
        using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            providers.Add(reader.GetString(0));
        return providers;
    }

    public void Compact()
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = "VACUUM;";
        cmd.ExecuteNonQuery();
    }

    // --- Index Run Log ---

    public async Task<long> StartRunLogAsync(string dataSourceId, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO index_run_log (data_source_id, started_at, status)
            VALUES ($dsId, $started, 'Indexing');
            SELECT last_insert_rowid();
            """;
        cmd.Parameters.AddWithValue("$dsId", dataSourceId);
        cmd.Parameters.AddWithValue("$started", DateTime.UtcNow.ToString("O"));
        var result = await cmd.ExecuteScalarAsync(ct);
        return Convert.ToInt64(result);
    }

    public async Task CompleteRunLogAsync(long logId, DataSourceStatus status, int documentCount, string? error = null, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            UPDATE index_run_log
            SET completed_at = $completed, status = $status, document_count = $docCount, error_message = $error
            WHERE id = $id;
            """;
        cmd.Parameters.AddWithValue("$id", logId);
        cmd.Parameters.AddWithValue("$completed", DateTime.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$status", status.ToString());
        cmd.Parameters.AddWithValue("$docCount", documentCount);
        cmd.Parameters.AddWithValue("$error", (object?)error ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<DateTime?> GetLastSuccessfulRunAsync(string dataSourceId, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT completed_at FROM index_run_log
            WHERE data_source_id = $dsId AND status = 'Success'
            ORDER BY completed_at DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$dsId", dataSourceId);
        var result = await cmd.ExecuteScalarAsync(ct);
        if (result is string s)
            return DateTime.Parse(s);
        return null;
    }

    public async Task<IndexRunLog?> GetLatestRunAsync(string dataSourceId, CancellationToken ct = default)
    {
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, data_source_id, started_at, completed_at, status, document_count, error_message
            FROM index_run_log WHERE data_source_id = $dsId
            ORDER BY started_at DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$dsId", dataSourceId);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
            return ReadRunLog(reader);
        return null;
    }

    public async Task<List<IndexRunLog>> GetRunHistoryAsync(string dataSourceId, int limit = 20, CancellationToken ct = default)
    {
        var logs = new List<IndexRunLog>();
        using var cmd = _connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, data_source_id, started_at, completed_at, status, document_count, error_message
            FROM index_run_log WHERE data_source_id = $dsId
            ORDER BY started_at DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$dsId", dataSourceId);
        cmd.Parameters.AddWithValue("$limit", limit);
        using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            logs.Add(ReadRunLog(reader));
        return logs;
    }

    private static IndexRunLog ReadRunLog(SqliteDataReader reader)
    {
        var completedStr = reader.IsDBNull(reader.GetOrdinal("completed_at"))
            ? null : reader.GetString(reader.GetOrdinal("completed_at"));
        var errorStr = reader.IsDBNull(reader.GetOrdinal("error_message"))
            ? null : reader.GetString(reader.GetOrdinal("error_message"));

        return new IndexRunLog
        {
            Id = reader.GetInt64(reader.GetOrdinal("id")),
            DataSourceId = reader.GetString(reader.GetOrdinal("data_source_id")),
            StartedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("started_at"))),
            CompletedAt = completedStr != null ? DateTime.Parse(completedStr) : null,
            Status = Enum.Parse<DataSourceStatus>(reader.GetString(reader.GetOrdinal("status"))),
            DocumentCount = reader.GetInt32(reader.GetOrdinal("document_count")),
            ErrorMessage = errorStr
        };
    }

    private IndexedDocument ReadDocument(SqliteDataReader reader)
    {
        var content = reader.GetString(reader.GetOrdinal("content"));
        if (_config.EncryptionEnabled && _config.EncryptionKey != null)
        {
            try { content = Decrypt(content, _config.EncryptionKey); }
            catch { /* content may not be encrypted */ }
        }

        return new IndexedDocument
        {
            Id = reader.GetString(reader.GetOrdinal("id")),
            Machine = reader.GetString(reader.GetOrdinal("machine")),
            Type = reader.GetString(reader.GetOrdinal("type")),
            Provider = reader.GetString(reader.GetOrdinal("provider")),
            Location = reader.GetString(reader.GetOrdinal("location")),
            Title = reader.GetString(reader.GetOrdinal("title")),
            Summary = reader.GetString(reader.GetOrdinal("summary")),
            Content = content,
            ExtendedData = JsonSerializer.Deserialize<Dictionary<string, string>>(
                reader.GetString(reader.GetOrdinal("extended_data"))) ?? new(),
            IndexedAt = DateTime.Parse(reader.GetString(reader.GetOrdinal("indexed_at"))),
            ContentHash = reader.GetString(reader.GetOrdinal("content_hash"))
        };
    }

    private static string Encrypt(string plainText, string key)
    {
        using var aes = Aes.Create();
        aes.Key = DeriveKey(key);
        aes.GenerateIV();

        using var encryptor = aes.CreateEncryptor();
        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var encrypted = encryptor.TransformFinalBlock(plainBytes, 0, plainBytes.Length);

        var result = new byte[aes.IV.Length + encrypted.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(encrypted, 0, result, aes.IV.Length, encrypted.Length);

        return Convert.ToBase64String(result);
    }

    private static string Decrypt(string cipherText, string key)
    {
        var fullCipher = Convert.FromBase64String(cipherText);

        using var aes = Aes.Create();
        aes.Key = DeriveKey(key);

        var iv = new byte[aes.BlockSize / 8];
        var cipher = new byte[fullCipher.Length - iv.Length];
        Buffer.BlockCopy(fullCipher, 0, iv, 0, iv.Length);
        Buffer.BlockCopy(fullCipher, iv.Length, cipher, 0, cipher.Length);

        aes.IV = iv;
        using var decryptor = aes.CreateDecryptor();
        var plainBytes = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        return Encoding.UTF8.GetString(plainBytes);
    }

    private static byte[] DeriveKey(string key)
    {
        using var sha256 = SHA256.Create();
        return sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _connection.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
