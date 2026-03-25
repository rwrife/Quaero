# Quaero
Personal, local-only indexing and search tool. Allows you to store and index your data locally, making it easily searchable across your multiple devices.

## Architecture

Quaero is a cross-platform C# solution (.NET 10) with a plugin-based architecture:

| Project | Description |
|---------|-------------|
| **Quaero.Core** | Core library: data models, SQLite storage with FTS5 full-text search, AES-256 encryption, dynamic plugin loading, cron scheduling, run logging |
| **Quaero.Plugins.Abstractions** | Plugin interface (`ISearchPlugin`), setting descriptors, and data contracts |
| **Quaero.Plugins.Markdown** | Indexes `.md` files using Markdig — extracts title from first H1, summary from first paragraph, glob pattern support |
| **Quaero.Plugins.Text** | Indexes text files — uses filename as title, glob pattern support |
| **Quaero.Plugins.Json** | Indexes `.json` files — configurable JSON path mappings for title/summary/content extraction |
| **Quaero.Plugins.Imap** | Indexes emails via IMAP (Gmail, Outlook, etc.) using MailKit, incremental by date |
| **Quaero.Indexer** | Background service with cron-based scheduling, dynamic assembly loading, no hardcoded plugin references |
| **Quaero.UI** | Avalonia desktop app with search, data source management, plugin configuration |

### Key Design Decisions

- **Dynamic plugin loading**: The Indexer scans a `plugins/` folder for assemblies, loads them via `AssemblyLoadContext`, and instantiates `ISearchPlugin` types — no compile-time plugin references needed
- **Data sources**: A "data source" is a named configuration instance of a plugin (e.g., "My Gmail", "Work Notes"). Multiple data sources can use the same plugin type. Configured in `datasources.json`
- **Cron scheduling**: Each data source has a cron expression (5-field). The indexer evaluates schedules every minute and runs data sources that are due
- **Incremental indexing**: Plugins receive `LastSuccessfulRun` datetime. File plugins skip unmodified files; IMAP only fetches emails since the last successful run
- **Run logging**: Every indexing run is logged in SQLite with start time, completion time, status, document count, and error messages

## Getting Started

### Build
```bash
dotnet build
```

### Run the Indexer

1. **Place plugin assemblies** in the plugins folder (`%LOCALAPPDATA%/Quaero/plugins/`). Each plugin DLL and its dependencies go here.

2. **Configure data sources** in `%LOCALAPPDATA%/Quaero/datasources.json`:
```json
[
  {
    "id": "my-notes",
    "name": "My Markdown Notes",
    "pluginAssembly": "Quaero.Plugins.Markdown",
    "pluginType": "Quaero.Plugins.Markdown.MarkdownSearchPlugin",
    "enabled": true,
    "cronSchedule": "0 */6 * * *",
    "settings": {
      "Directory": "C:\\Users\\me\\Notes",
      "FileGlob": "**/*.md"
    }
  },
  {
    "id": "my-gmail",
    "name": "Gmail",
    "pluginAssembly": "Quaero.Plugins.Imap",
    "pluginType": "Quaero.Plugins.Imap.ImapSearchPlugin",
    "enabled": true,
    "cronSchedule": "0 */2 * * *",
    "settings": {
      "Host": "imap.gmail.com",
      "Port": "993",
      "UseSsl": "true",
      "Username": "you@gmail.com",
      "Password": "your-app-password",
      "Provider": "gmail",
      "MaxMessages": "500"
    }
  },
  {
    "id": "my-json-data",
    "name": "JSON API Responses",
    "pluginAssembly": "Quaero.Plugins.Json",
    "pluginType": "Quaero.Plugins.Json.JsonSearchPlugin",
    "enabled": true,
    "cronSchedule": "0 0 * * *",
    "settings": {
      "Directory": "C:\\Users\\me\\Data",
      "FileGlob": "**/*.json",
      "TitlePath": "metadata.title",
      "SummaryPath": "metadata.description",
      "ContentPath": "body.text"
    }
  }
]
```

3. **Run the indexer**:
```bash
dotnet run --project src/Quaero.Indexer
```

The indexer evaluates cron schedules every minute and runs data sources that are due. It performs incremental indexing — only fetching content that has changed since the last successful run.

### Run the UI
```bash
dotnet run --project src/Quaero.UI
```

The UI has two tabs:
- **Search** — full-text search across all indexed content
- **Data Sources** — add, edit, remove, enable/disable data sources; trigger indexing runs; view status and run history

## Features

- **Full-text search** via SQLite FTS5
- **Dynamic plugin loading** — drop assemblies into the plugins folder, no recompilation needed
- **Data source management** — multiple named instances per plugin type with individual settings
- **Cron scheduling** — per-data-source cron expressions for automated indexing
- **Incremental indexing** — only index content changed since last successful run
- **Run logging** — per-data-source run history with status, doc count, and error tracking
- **Glob patterns** — file-based plugins support glob patterns for flexible file matching
- **Configurable JSON paths** — JSON plugin supports dot-notation paths for title/summary/content extraction
- **Optional encryption** — AES-256 content encryption at rest
- **Index compaction** — VACUUM support for minimal storage
- **Cross-platform** — runs on Windows, macOS, and Linux
