import { loadConfiguration, type IndexConfiguration } from '@quaero/core';

export interface ApiClient {
  baseUrl: string;
  config: IndexConfiguration;
  request<T = unknown>(method: string, path: string, body?: unknown): Promise<T>;
  searchPost<T = unknown>(body: unknown): Promise<T>;
}

export function createApiClient(baseUrlOverride?: string): ApiClient {
  const config = loadConfiguration();
  const baseUrl =
    (baseUrlOverride && baseUrlOverride.trim()) ||
    process.env.QUAERO_API_URL ||
    config.serverBaseUrl ||
    'http://localhost:5055';

  async function request<T>(method: string, path: string, body?: unknown): Promise<T> {
    const url = baseUrl.replace(/\/$/, '') + path;
    const init: RequestInit = {
      method,
      headers: body !== undefined ? { 'content-type': 'application/json' } : undefined,
      body: body !== undefined ? JSON.stringify(body) : undefined,
    };
    let res: Response;
    try {
      res = await fetch(url, init);
    } catch (err) {
      const reason = err instanceof Error ? err.message : String(err);
      throw new Error(`request failed (${method} ${url}): ${reason}. Is the daemon running?`);
    }
    if (res.status === 204) return undefined as T;
    const text = await res.text();
    let parsed: unknown = undefined;
    if (text) {
      try {
        parsed = JSON.parse(text);
      } catch {
        parsed = text;
      }
    }
    if (!res.ok) {
      const detail =
        parsed && typeof parsed === 'object' && 'error' in parsed
          ? (parsed as { error: unknown }).error
          : parsed;
      throw new Error(`HTTP ${res.status} ${res.statusText}: ${typeof detail === 'string' ? detail : JSON.stringify(detail)}`);
    }
    return parsed as T;
  }

  return {
    baseUrl,
    config,
    request,
    searchPost: (body) => request('POST', '/api/search', body),
  };
}
