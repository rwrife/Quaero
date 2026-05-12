import React, { useEffect, useState, useCallback } from 'react';
import { Box, Text, useApp, useInput } from 'ink';
import TextInput from 'ink-text-input';
import { createApiClient, type ApiClient } from '../lib/api-client.js';

type View = 'menu' | 'search' | 'datasources';

interface SearchResult {
  documentId?: string;
  title?: string;
  url?: string;
  filePath?: string;
  provider?: string;
  type?: string;
  dataSourceName?: string;
  score?: number;
}

interface DataSource {
  id: string;
  name: string;
  enabled: boolean;
  cronSchedule: string;
  pluginModule: string;
  latestRun?: { status: string; startedAt: string } | null;
}

interface AppProps {
  apiUrl?: string;
}

export const App: React.FC<AppProps> = ({ apiUrl }) => {
  const [client] = useState<ApiClient>(() => createApiClient(apiUrl));
  const [view, setView] = useState<View>('menu');
  const { exit } = useApp();

  useInput((input, key) => {
    if (view !== 'menu') return;
    if (input === 'q' || (key.ctrl && input === 'c')) exit();
    if (input === '1' || input === 's') setView('search');
    if (input === '2' || input === 'd') setView('datasources');
  });

  return (
    <Box flexDirection="column" padding={1}>
      <Header view={view} baseUrl={client.baseUrl} />
      {view === 'menu' && <Menu />}
      {view === 'search' && <SearchView client={client} onExit={() => setView('menu')} />}
      {view === 'datasources' && (
        <DataSourceView client={client} onExit={() => setView('menu')} />
      )}
      <Footer view={view} />
    </Box>
  );
};

const Header: React.FC<{ view: View; baseUrl: string }> = ({ view, baseUrl }) => (
  <Box flexDirection="column" marginBottom={1}>
    <Text bold color="cyan">
      Quaero — local search
    </Text>
    <Text dimColor>daemon: {baseUrl} | view: {view}</Text>
  </Box>
);

const Menu: React.FC = () => (
  <Box flexDirection="column">
    <Text>Choose a view:</Text>
    <Text>
      <Text color="green">1</Text> Search
    </Text>
    <Text>
      <Text color="green">2</Text> Data sources
    </Text>
    <Text dimColor>Press the number or letter; q quits.</Text>
  </Box>
);

const Footer: React.FC<{ view: View }> = ({ view }) => (
  <Box marginTop={1}>
    <Text dimColor>
      {view === 'menu'
        ? '[1] search  [2] data sources  [q] quit'
        : 'esc/back returns to menu  •  q quits'}
    </Text>
  </Box>
);

interface ViewProps {
  client: ApiClient;
  onExit: () => void;
}

const SearchView: React.FC<ViewProps> = ({ client, onExit }) => {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [submitted, setSubmitted] = useState(false);

  useInput((input, key) => {
    if (key.escape) onExit();
  });

  const onSubmit = useCallback(
    async (value: string) => {
      const text = value.trim();
      if (!text) return;
      setLoading(true);
      setError(null);
      setSubmitted(true);
      try {
        const res = await client.searchPost<{ results: SearchResult[] }>({
          queryText: text,
          maxResults: 20,
        });
        setResults(res.results ?? []);
      } catch (e) {
        setError(e instanceof Error ? e.message : String(e));
        setResults([]);
      } finally {
        setLoading(false);
      }
    },
    [client],
  );

  return (
    <Box flexDirection="column">
      <Box>
        <Text color="yellow">query › </Text>
        <TextInput value={query} onChange={setQuery} onSubmit={onSubmit} />
      </Box>
      {loading && <Text dimColor>searching…</Text>}
      {error && (
        <Text color="red">
          error: {error}
        </Text>
      )}
      {!loading && !error && submitted && results.length === 0 && (
        <Text dimColor>No results.</Text>
      )}
      {!loading && !error && results.length > 0 && (
        <Box flexDirection="column" marginTop={1}>
          {results.slice(0, 15).map((r, idx) => (
            <Box key={r.documentId ?? idx} flexDirection="column" marginBottom={1}>
              <Text>
                <Text color="green">{idx + 1}.</Text>{' '}
                <Text bold>{r.title ?? r.documentId ?? '(untitled)'}</Text>
                {r.score !== undefined && <Text dimColor> ({r.score.toFixed(2)})</Text>}
              </Text>
              <Text dimColor>
                {(r.dataSourceName ?? r.provider ?? '')} {r.type ? `· ${r.type}` : ''}
              </Text>
              {(r.url || r.filePath) && <Text dimColor>{r.url ?? r.filePath}</Text>}
            </Box>
          ))}
        </Box>
      )}
    </Box>
  );
};

const DataSourceView: React.FC<ViewProps> = ({ client, onExit }) => {
  const [items, setItems] = useState<DataSource[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setError(null);
    try {
      const list = await client.request<DataSource[]>('GET', '/api/datasources');
      setItems(list);
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
    }
  }, [client]);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  useInput((input, key) => {
    if (key.escape) onExit();
    if (input === 'r') void refresh();
  });

  if (error) {
    return (
      <Box flexDirection="column">
        <Text color="red">error: {error}</Text>
        <Text dimColor>press r to retry, esc to go back</Text>
      </Box>
    );
  }
  if (items === null) {
    return <Text dimColor>loading…</Text>;
  }
  if (items.length === 0) {
    return <Text dimColor>No data sources configured. Use `quaero ds add` to create one.</Text>;
  }
  return (
    <Box flexDirection="column">
      <Text dimColor>{items.length} data source{items.length === 1 ? '' : 's'} (r=refresh)</Text>
      {items.map((d) => (
        <Box key={d.id} flexDirection="column" marginTop={1}>
          <Text>
            <Text bold>{d.name}</Text>{' '}
            <Text color={d.enabled ? 'green' : 'red'}>
              {d.enabled ? 'enabled' : 'disabled'}
            </Text>
          </Text>
          <Text dimColor>
            id={d.id} · plugin={d.pluginModule} · cron={d.cronSchedule}
          </Text>
          {d.latestRun && (
            <Text dimColor>
              last run: {d.latestRun.status} at {d.latestRun.startedAt}
            </Text>
          )}
        </Box>
      ))}
    </Box>
  );
};
