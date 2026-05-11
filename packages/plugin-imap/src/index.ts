/**
 * @quaero/plugin-imap — indexes messages from an IMAP mailbox (covers Gmail
 * via standard IMAP). Uses `imapflow` for the protocol layer and
 * `mailparser` to parse MIME bodies. Honours `lastSuccessfulRun` for
 * incremental indexing via the IMAP `SINCE` search key.
 */
import { ImapFlow, type ImapFlowOptions, type FetchMessageObject } from 'imapflow';
import { simpleParser, type ParsedMail } from 'mailparser';
import type {
  DiscoveredDocument,
  ISearchPlugin,
  PluginConfiguration,
  PluginMetadata,
  PluginSettingDescriptor,
} from '@quaero/plugin-api';

export const PLUGIN_ID = 'quaero.plugin.imap';

const DEFAULT_MAX_MESSAGES = 500;
const SUMMARY_MAX = 280;

const SETTINGS: readonly PluginSettingDescriptor[] = [
  {
    key: 'host',
    displayName: 'IMAP host',
    description: 'IMAP server hostname (e.g. imap.gmail.com).',
    settingType: 'text',
    isRequired: true,
    defaultValue: 'imap.gmail.com',
  },
  {
    key: 'port',
    displayName: 'IMAP port',
    description: 'IMAP server port (usually 993 for SSL).',
    settingType: 'number',
    defaultValue: '993',
  },
  {
    key: 'useSsl',
    displayName: 'Use SSL/TLS',
    description: 'Connect over TLS (recommended).',
    settingType: 'boolean',
    defaultValue: 'true',
  },
  {
    key: 'username',
    displayName: 'Username',
    description: 'IMAP account username (email address for Gmail).',
    settingType: 'text',
    isRequired: true,
  },
  {
    key: 'password',
    displayName: 'Password',
    description: 'Account password or app-password.',
    settingType: 'password',
    isRequired: true,
  },
  {
    key: 'provider',
    displayName: 'Provider label',
    description: 'Friendly name (e.g. "Gmail", "Fastmail"). Stored on documents.',
    settingType: 'text',
    defaultValue: 'IMAP',
  },
  {
    key: 'mailbox',
    displayName: 'Mailbox',
    description: 'IMAP mailbox to read (default INBOX).',
    settingType: 'text',
    defaultValue: 'INBOX',
  },
  {
    key: 'maxMessages',
    displayName: 'Max messages',
    description: 'Hard cap on messages to fetch per run.',
    settingType: 'number',
    defaultValue: String(DEFAULT_MAX_MESSAGES),
  },
];

const METADATA: PluginMetadata = {
  id: PLUGIN_ID,
  name: 'IMAP mailbox',
  description: 'Indexes email messages from an IMAP server (Gmail-compatible).',
  version: '0.2.0',
};

/**
 * Minimal subset of imapflow's surface that the plugin actually uses. Tests
 * can supply a stub that satisfies this contract without spinning up a real
 * IMAP server.
 */
export interface ImapClientLike {
  connect(): Promise<void>;
  logout(): Promise<void>;
  getMailboxLock(name: string): Promise<{ release(): void }>;
  search(query: Record<string, unknown>, options?: { uid?: boolean }): Promise<number[]>;
  fetch(
    range: string | number[],
    query: Record<string, unknown>,
    options?: { uid?: boolean }
  ): AsyncIterable<FetchMessageObject>;
}

export type ImapClientFactory = (options: ImapFlowOptions) => ImapClientLike;

const defaultFactory: ImapClientFactory = (options) =>
  new ImapFlow(options) as unknown as ImapClientLike;

export interface ImapPluginOptions {
  clientFactory?: ImapClientFactory;
  parseMail?: (source: Buffer | string) => Promise<ParsedMail>;
}

export class ImapPlugin implements ISearchPlugin {
  readonly metadata = METADATA;
  readonly settingDescriptors = SETTINGS;

  private host = '';
  private port = 993;
  private useSsl = true;
  private username = '';
  private password = '';
  private provider = 'IMAP';
  private mailbox = 'INBOX';
  private maxMessages = DEFAULT_MAX_MESSAGES;
  private lastSuccessfulRun: Date | null = null;

  private readonly clientFactory: ImapClientFactory;
  private readonly parseMail: (source: Buffer | string) => Promise<ParsedMail>;

  constructor(options: ImapPluginOptions = {}) {
    this.clientFactory = options.clientFactory ?? defaultFactory;
    this.parseMail = options.parseMail ?? ((src) => simpleParser(src));
  }

  async initialize(configuration: PluginConfiguration): Promise<void> {
    const s = configuration.settings;
    const host = s.host?.trim();
    const username = s.username?.trim();
    const password = s.password;
    if (!host) throw new Error('plugin-imap: host setting is required');
    if (!username) throw new Error('plugin-imap: username setting is required');
    if (!password) throw new Error('plugin-imap: password setting is required');

    this.host = host;
    this.port = parsePositiveInt(s.port, 993);
    this.useSsl = parseBool(s.useSsl, true);
    this.username = username;
    this.password = password;
    this.provider = s.provider?.trim() || 'IMAP';
    this.mailbox = s.mailbox?.trim() || 'INBOX';
    this.maxMessages = parsePositiveInt(s.maxMessages, DEFAULT_MAX_MESSAGES);
    this.lastSuccessfulRun = configuration.lastSuccessfulRun;
  }

  async *discoverDocuments(signal?: AbortSignal): AsyncIterable<DiscoveredDocument> {
    if (!this.host || !this.username) {
      throw new Error('plugin-imap: not initialized');
    }

    const client = this.clientFactory({
      host: this.host,
      port: this.port,
      secure: this.useSsl,
      auth: { user: this.username, pass: this.password },
      logger: false,
    });

    await client.connect();
    let lock: { release(): void } | null = null;
    try {
      lock = await client.getMailboxLock(this.mailbox);

      const searchQuery: Record<string, unknown> = {};
      if (this.lastSuccessfulRun) {
        searchQuery.since = this.lastSuccessfulRun;
      } else {
        searchQuery.all = true;
      }
      const uids = (await client.search(searchQuery, { uid: true })) ?? [];

      const limited = uids.slice(-this.maxMessages);
      if (limited.length === 0) return;

      const stream = client.fetch(
        limited,
        { uid: true, envelope: true, internalDate: true, source: true, flags: true },
        { uid: true }
      );

      for await (const msg of stream) {
        if (signal?.aborted) return;
        const doc = await this.shapeDocument(msg);
        if (doc) yield doc;
      }
    } finally {
      try {
        lock?.release();
      } catch {
        /* ignore */
      }
      try {
        await client.logout();
      } catch {
        /* ignore */
      }
    }
  }

  /** Visible for testing. */
  async shapeDocument(msg: FetchMessageObject): Promise<DiscoveredDocument | null> {
    const source = msg.source;
    if (!source) return null;
    const parsed = await this.parseMail(source);
    return buildDocument(parsed, msg, this.provider, this.username, this.mailbox);
  }
}

export function buildDocument(
  parsed: ParsedMail,
  msg: FetchMessageObject,
  provider: string,
  account: string,
  mailbox: string
): DiscoveredDocument {
  const uid = msg.uid ?? 0;
  const messageId = parsed.messageId || msg.envelope?.messageId || `uid:${uid}`;
  const subject = parsed.subject || msg.envelope?.subject || '(no subject)';
  const from = formatAddress(parsed.from) || formatAddressList(msg.envelope?.from);
  const to = formatAddress(parsed.to) || formatAddressList(msg.envelope?.to);
  const cc = formatAddress(parsed.cc) || formatAddressList(msg.envelope?.cc);
  const dateObj =
    parsed.date ?? (msg.envelope?.date ? new Date(msg.envelope.date) : msg.internalDate);
  const date = dateObj instanceof Date && !Number.isNaN(dateObj.getTime()) ? dateObj : null;

  const content = bodyText(parsed);
  const summary = truncate(content || subject, SUMMARY_MAX);

  const extendedData: Record<string, string> = {
    uid: String(uid),
    messageId,
    mailbox,
    account,
    from,
    to,
  };
  if (cc) extendedData.cc = cc;
  if (date) extendedData.date = date.toISOString();
  if (Array.isArray(msg.flags)) {
    const flags = [...msg.flags].filter((f): f is string => typeof f === 'string');
    if (flags.length) extendedData.flags = flags.join(',');
  } else if (msg.flags && typeof (msg.flags as Set<string>).size === 'number') {
    const flags = [...(msg.flags as Set<string>)];
    if (flags.length) extendedData.flags = flags.join(',');
  }
  if (parsed.attachments?.length) {
    extendedData.attachments = parsed.attachments
      .map((a) => a.filename || a.contentType || 'attachment')
      .join(', ');
  }

  return {
    type: 'email',
    provider,
    location: `imap://${account}/${encodeURIComponent(mailbox)}/${uid}`,
    title: subject,
    summary,
    content,
    extendedData,
  };
}

/**
 * Extract a plain-text body, falling back to a crude HTML→text conversion
 * when only an HTML body is available.
 */
export function bodyText(parsed: ParsedMail): string {
  if (parsed.text && parsed.text.trim().length > 0) return parsed.text;
  if (parsed.html && typeof parsed.html === 'string') return htmlToText(parsed.html);
  if (typeof parsed.textAsHtml === 'string') return htmlToText(parsed.textAsHtml);
  return '';
}

export function htmlToText(html: string): string {
  return html
    .replace(/<style[\s\S]*?<\/style>/gi, ' ')
    .replace(/<script[\s\S]*?<\/script>/gi, ' ')
    .replace(/<\/(p|div|h[1-6]|li|tr|br)>/gi, '\n')
    .replace(/<br\s*\/?>/gi, '\n')
    .replace(/<[^>]+>/g, ' ')
    .replace(/&nbsp;/gi, ' ')
    .replace(/&amp;/gi, '&')
    .replace(/&lt;/gi, '<')
    .replace(/&gt;/gi, '>')
    .replace(/&quot;/gi, '"')
    .replace(/&#39;/gi, "'")
    .replace(/[ \t]+/g, ' ')
    .replace(/\n{3,}/g, '\n\n')
    .trim();
}

function formatAddress(field: ParsedMail['from'] | ParsedMail['to']): string {
  if (!field) return '';
  if (Array.isArray(field)) {
    return field.map((f) => f.text).filter(Boolean).join(', ');
  }
  return field.text || '';
}

function formatAddressList(list: unknown): string {
  if (!Array.isArray(list)) return '';
  return list
    .map((a: { name?: string; address?: string }) => {
      if (a.name && a.address) return `${a.name} <${a.address}>`;
      return a.address || a.name || '';
    })
    .filter(Boolean)
    .join(', ');
}

function truncate(s: string, max: number): string {
  const collapsed = s.replace(/\s+/g, ' ').trim();
  if (collapsed.length <= max) return collapsed;
  return collapsed.slice(0, max - 1).trimEnd() + '…';
}

function parseBool(v: string | undefined, fallback: boolean): boolean {
  if (v === undefined || v === null || v === '') return fallback;
  return /^(1|true|yes|on)$/i.test(v.trim());
}

function parsePositiveInt(v: string | undefined, fallback: number): number {
  if (!v) return fallback;
  const n = Number.parseInt(v, 10);
  return Number.isFinite(n) && n > 0 ? n : fallback;
}

const plugin = new ImapPlugin();
export { plugin };
export default plugin;
