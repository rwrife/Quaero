# Quaero

Personal, local-only indexing and search tool. Store and index your data locally, making it easily searchable across your devices.

> **Status:** Active Node.js port. The original .NET implementation is preserved in git history; current development happens on the TypeScript monorepo described below.

## Architecture

Quaero is a Node.js 20+ TypeScript monorepo (npm workspaces) with a plugin-based architecture. Plugins are real npm packages — no dynamic assembly loading.

| Package | Description |
|---------|-------------|
| `@quaero/plugin-api` | Plugin contracts: `SearchPlugin`, setting descriptors, document/run types |
| `@quaero/core` | Core library: SQLite (better-sqlite3) storage with FTS5, encryption, plugin loader, cron evaluator, run logging |
| `@quaero/daemon` | Long-running indexer process: cron-driven scheduler, dynamic plugin loading, run history |
| `@quaero/cli` | CLI for searching the index, managing data sources, and triggering runs |
| `@quaero/plugin-markdown` | Indexes `.md` files — title from first H1, summary from first paragraph |
| `@quaero/plugin-text` | Indexes plain-text files — filename as title |
| `@quaero/plugin-json` | Indexes `.json` files — configurable JSON path mappings for title/summary/content |
| `@quaero/plugin-imap` | Indexes emails via IMAP (Gmail, Outlook, etc.), incremental by date |

### Key Design Decisions

- **Plugin packages**: Each plugin is its own npm package with `package.json` and a TypeScript project reference to `@quaero/plugin-api`. The daemon loads them by package name.
- **Data sources**: A named configuration instance of a plugin (e.g., "My Gmail", "Work Notes"). Multiple data sources can share a plugin. Configured in `datasources.json`.
- **Cron scheduling**: 5-field cron expressions evaluated each minute; due data sources are dispatched.
- **Incremental indexing**: Plugins receive `lastSuccessfulRun`. File plugins skip unmodified files; IMAP only fetches new messages.
- **Run logging**: Every run is recorded in SQLite with start/finish timestamps, status, document count, and error messages.

## Requirements

- Node.js **20.x or 22.x**
- npm 10+
- Works on Linux, macOS, and Windows

## Getting Started

### Install & Build

```bash
npm ci
npm run build
```

This compiles every workspace package via TypeScript project references.

### Run the Daemon

```bash
npm run daemon
```

The daemon evaluates cron schedules every minute and runs data sources that are due. It performs incremental indexing — only fetching content that has changed since the last successful run.

Configuration and database live under the platform-appropriate user data directory (e.g. `~/.local/share/quaero` on Linux, `%LOCALAPPDATA%/Quaero` on Windows).

### Use the CLI

```bash
npm run cli -- --help
```

The CLI lets you search the index and manage data sources.

### Example `datasources.json`

```json
[
  {
    "id": "my-notes",
    "name": "My Markdown Notes",
    "plugin": "@quaero/plugin-markdown",
    "enabled": true,
    "cronSchedule": "0 */6 * * *",
    "settings": {
      "directory": "/home/me/Notes",
      "fileGlob": "**/*.md"
    }
  },
  {
    "id": "my-gmail",
    "name": "Gmail",
    "plugin": "@quaero/plugin-imap",
    "enabled": true,
    "cronSchedule": "0 */2 * * *",
    "settings": {
      "host": "imap.gmail.com",
      "port": 993,
      "useSsl": true,
      "username": "you@gmail.com",
      "password": "your-app-password",
      "maxMessages": 500
    }
  }
]
```

## Development

```bash
npm run build      # tsc -b across the monorepo
npm test           # vitest run (all packages)
npm run test:watch # vitest watch mode
npm run lint       # eslint over packages/**/src
npm run format     # prettier write
npm run clean      # tsc -b --clean + rimraf dist
```

### Repository Layout

```
packages/
  plugin-api/        # contracts
  core/              # storage, scheduler, plugin loader
  daemon/            # long-running indexer
  cli/               # command-line interface
  plugin-markdown/
  plugin-text/
  plugin-json/
  plugin-imap/
```

## Continuous Integration

GitHub Actions runs the full build + test suite on every push and pull request to `main` across a matrix of:

- **OS**: `ubuntu-latest`, `macos-latest`, `windows-latest`
- **Node.js**: `20.x`, `22.x`

See [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

## Features

- **Full-text search** via SQLite FTS5
- **Plugin packages** — add a new npm package, reference it from a data source
- **Data source management** — multiple named instances per plugin type with individual settings
- **Cron scheduling** — per-data-source cron expressions for automated indexing
- **Incremental indexing** — only index content changed since the last successful run
- **Run logging** — per-data-source run history with status, doc count, and error tracking
- **Glob patterns** — file-based plugins support glob patterns for flexible file matching
- **Configurable JSON paths** — JSON plugin supports dot-notation paths for title/summary/content extraction
- **Optional encryption** — AES-256 content encryption at rest
- **Cross-platform** — runs on Linux, macOS, and Windows

## License

See [LICENSE](LICENSE).
