import { describe, it, expect } from 'vitest';
import type { FetchMessageObject } from 'imapflow';
import type { ParsedMail } from 'mailparser';
import {
  ImapPlugin,
  buildDocument,
  bodyText,
  htmlToText,
  PLUGIN_ID,
} from '../src/index.js';
import type {
  DiscoveredDocument,
  PluginConfiguration,
} from '@quaero/plugin-api';

function makeMsg(overrides: Partial<FetchMessageObject> = {}): FetchMessageObject {
  return {
    seq: 1,
    uid: 42,
    envelope: {
      date: new Date('2024-02-03T04:05:06Z'),
      subject: 'Envelope subject',
      from: [{ name: 'Env From', address: 'env-from@example.com' }],
      to: [{ name: 'Env To', address: 'env-to@example.com' }],
      cc: [],
      bcc: [],
      replyTo: [],
      sender: [],
      inReplyTo: undefined,
      messageId: '<env-msgid@example.com>',
    } as FetchMessageObject['envelope'],
    internalDate: new Date('2024-02-03T04:05:06Z'),
    flags: new Set(['\\Seen']),
    source: Buffer.from('not-used-in-shape-helper'),
    ...overrides,
  } as FetchMessageObject;
}

function makeParsed(overrides: Partial<ParsedMail> = {}): ParsedMail {
  return {
    headers: new Map(),
    headerLines: [],
    attachments: [],
    subject: 'Hello there',
    from: { value: [{ name: 'Alice', address: 'alice@example.com' }], text: 'Alice <alice@example.com>', html: '' },
    to: { value: [{ name: 'Bob', address: 'bob@example.com' }], text: 'Bob <bob@example.com>', html: '' },
    date: new Date('2024-02-03T04:05:06Z'),
    messageId: '<parsed-msgid@example.com>',
    text: 'Hello body text',
    ...overrides,
  } as unknown as ParsedMail;
}

describe('@quaero/plugin-imap helpers', () => {
  it('exports the canonical plugin id', () => {
    expect(PLUGIN_ID).toBe('quaero.plugin.imap');
  });

  it('htmlToText strips tags and decodes basic entities', () => {
    const html =
      '<html><head><style>.x{}</style></head><body><p>Hi&nbsp;there</p><br><div>line2</div>' +
      '<script>evil()</script></body></html>';
    const text = htmlToText(html);
    expect(text).toContain('Hi there');
    expect(text).toContain('line2');
    expect(text).not.toMatch(/<|>/);
    expect(text).not.toContain('evil');
  });

  it('bodyText prefers plain text when present', () => {
    const parsed = makeParsed({ text: 'plain wins', html: '<p>nope</p>' } as Partial<ParsedMail>);
    expect(bodyText(parsed)).toBe('plain wins');
  });

  it('bodyText falls back to HTML→text when only HTML is present', () => {
    const parsed = makeParsed({
      text: '',
      html: '<p>Hello <b>world</b></p>',
    } as Partial<ParsedMail>);
    const txt = bodyText(parsed);
    expect(txt).toContain('Hello');
    expect(txt).toContain('world');
    expect(txt).not.toContain('<');
  });

  it('buildDocument produces a well-shaped DiscoveredDocument', () => {
    const doc: DiscoveredDocument = buildDocument(
      makeParsed(),
      makeMsg(),
      'Gmail',
      'me@example.com',
      'INBOX'
    );
    expect(doc.type).toBe('email');
    expect(doc.provider).toBe('Gmail');
    expect(doc.title).toBe('Hello there');
    expect(doc.location).toBe('imap://me@example.com/INBOX/42');
    expect(doc.content).toBe('Hello body text');
    expect(doc.summary).toContain('Hello body text');
    expect(doc.extendedData?.uid).toBe('42');
    expect(doc.extendedData?.messageId).toBe('<parsed-msgid@example.com>');
    expect(doc.extendedData?.account).toBe('me@example.com');
    expect(doc.extendedData?.mailbox).toBe('INBOX');
    expect(doc.extendedData?.from).toContain('alice@example.com');
    expect(doc.extendedData?.to).toContain('bob@example.com');
    expect(doc.extendedData?.date).toBe('2024-02-03T04:05:06.000Z');
    expect(doc.extendedData?.flags).toBe('\\Seen');
  });

  it('buildDocument falls back to envelope subject and HTML body', () => {
    const parsed = makeParsed({
      subject: '',
      text: '',
      html: '<p>Body via HTML</p>',
    } as Partial<ParsedMail>);
    const doc = buildDocument(parsed, makeMsg(), 'IMAP', 'me@example.com', 'INBOX');
    expect(doc.title).toBe('Envelope subject');
    expect(doc.content).toContain('Body via HTML');
    expect(doc.content).not.toContain('<');
  });

  it('buildDocument exposes attachments in extendedData', () => {
    const parsed = makeParsed({
      attachments: [
        { filename: 'a.pdf', contentType: 'application/pdf' },
        { filename: 'b.png', contentType: 'image/png' },
      ],
    } as Partial<ParsedMail>);
    const doc = buildDocument(parsed, makeMsg(), 'IMAP', 'me@example.com', 'INBOX');
    expect(doc.extendedData?.attachments).toBe('a.pdf, b.png');
  });
});

describe('ImapPlugin lifecycle', () => {
  function makeConfig(overrides: Partial<PluginConfiguration['settings']> = {}): PluginConfiguration {
    return {
      enabled: true,
      lastSuccessfulRun: null,
      settings: {
        host: 'imap.example.com',
        port: '993',
        useSsl: 'true',
        username: 'me@example.com',
        password: 'secret',
        provider: 'Test',
        mailbox: 'INBOX',
        maxMessages: '100',
        ...overrides,
      },
    };
  }

  it('exposes metadata and settings', () => {
    const p = new ImapPlugin();
    expect(p.metadata.id).toBe(PLUGIN_ID);
    expect(p.settingDescriptors.find((s) => s.key === 'host')?.isRequired).toBe(true);
    expect(p.settingDescriptors.find((s) => s.key === 'username')?.isRequired).toBe(true);
    expect(p.settingDescriptors.find((s) => s.key === 'password')?.settingType).toBe('password');
    expect(p.settingDescriptors.find((s) => s.key === 'maxMessages')?.defaultValue).toBe('500');
  });

  it('initialize rejects missing required settings', async () => {
    const p = new ImapPlugin();
    await expect(
      p.initialize({ enabled: true, lastSuccessfulRun: null, settings: {} })
    ).rejects.toThrow(/host/);
  });

  it('discoverDocuments streams from a stub client and uses SINCE on incremental runs', async () => {
    const calls: { search?: Record<string, unknown>; range?: number[] | string } = {};
    let connected = false;
    let loggedOut = false;
    let lockReleased = false;

    const stubMsg = {
      uid: 7,
      seq: 1,
      envelope: { subject: 'Stub', from: [], to: [], cc: [], bcc: [], replyTo: [], sender: [] },
      internalDate: new Date('2024-05-01T00:00:00Z'),
      flags: new Set(['\\Seen']),
      source: Buffer.from('raw mime ignored by stub parser'),
    } as unknown as FetchMessageObject;

    const plugin = new ImapPlugin({
      clientFactory: () => ({
        async connect() {
          connected = true;
        },
        async logout() {
          loggedOut = true;
        },
        async getMailboxLock() {
          return {
            release() {
              lockReleased = true;
            },
          };
        },
        async search(query) {
          calls.search = query;
          return [1, 2, 7];
        },
        async *fetch(range) {
          calls.range = range;
          yield stubMsg;
        },
      }),
      parseMail: async () =>
        ({
          subject: 'Parsed subject',
          text: 'Parsed body',
          from: { value: [], text: '' },
          to: { value: [], text: '' },
          attachments: [],
          headers: new Map(),
          headerLines: [],
          messageId: '<m@x>',
        }) as unknown as ParsedMail,
    });

    const since = new Date('2024-04-01T00:00:00Z');
    await plugin.initialize({
      enabled: true,
      lastSuccessfulRun: since,
      settings: {
        host: 'imap.example.com',
        username: 'me@example.com',
        password: 'secret',
        mailbox: 'INBOX',
        maxMessages: '50',
        provider: 'Stub',
      },
    });

    const docs: DiscoveredDocument[] = [];
    for await (const d of plugin.discoverDocuments()) docs.push(d);

    expect(connected).toBe(true);
    expect(loggedOut).toBe(true);
    expect(lockReleased).toBe(true);
    expect(calls.search).toEqual({ since });
    expect(calls.range).toEqual([1, 2, 7]);
    expect(docs).toHaveLength(1);
    expect(docs[0]!.provider).toBe('Stub');
    expect(docs[0]!.title).toBe('Parsed subject');
    expect(docs[0]!.content).toBe('Parsed body');
    expect(docs[0]!.extendedData?.account).toBe('me@example.com');
    expect(docs[0]!.location).toBe('imap://me@example.com/INBOX/7');
  });

  it('discoverDocuments uses {all:true} on first run and caps to maxMessages', async () => {
    let observedRange: number[] | string | undefined;
    let observedSearch: Record<string, unknown> | undefined;

    const plugin = new ImapPlugin({
      clientFactory: () => ({
        async connect() {},
        async logout() {},
        async getMailboxLock() {
          return { release() {} };
        },
        async search(q) {
          observedSearch = q;
          return Array.from({ length: 10 }, (_, i) => i + 1);
        },
        async *fetch(range) {
          observedRange = range;
          // yield nothing — we just care about the call shape
        },
      }),
      parseMail: async () => ({ text: '', subject: '' }) as unknown as ParsedMail,
    });

    await plugin.initialize({
      enabled: true,
      lastSuccessfulRun: null,
      settings: {
        host: 'imap.example.com',
        username: 'me@example.com',
        password: 'secret',
        mailbox: 'INBOX',
        maxMessages: '3',
      },
    });

    for await (const _ of plugin.discoverDocuments()) {
      // drain
    }

    expect(observedSearch).toEqual({ all: true });
    expect(observedRange).toEqual([8, 9, 10]);
  });
});
