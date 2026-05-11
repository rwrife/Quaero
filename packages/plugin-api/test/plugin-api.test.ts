import { describe, it, expect, expectTypeOf } from 'vitest';
import type {
  ISearchPlugin,
  PluginMetadata,
  PluginConfiguration,
  DiscoveredDocument,
  PluginSettingDescriptor,
  PluginSettingType,
} from '../src/index.js';

describe('@quaero/plugin-api structural contracts', () => {
  it('PluginSettingType accepts the documented union members', () => {
    const values: PluginSettingType[] = [
      'text',
      'folderPath',
      'password',
      'number',
      'boolean',
      'globPattern',
    ];
    expect(values).toHaveLength(6);
  });

  it('PluginSettingDescriptor has the required shape', () => {
    const descriptor: PluginSettingDescriptor = {
      key: 'rootPath',
      displayName: 'Root path',
      description: 'Folder to index',
      settingType: 'folderPath',
      defaultValue: '/tmp',
      isRequired: true,
    };
    expect(descriptor.key).toBe('rootPath');
    expect(descriptor.settingType).toBe('folderPath');
  });

  it('PluginMetadata can omit optional supportedFileExtensions', () => {
    const meta: PluginMetadata = {
      id: 'sample',
      name: 'Sample',
      description: 'A sample plugin',
      version: '0.0.1',
    };
    expect(meta.supportedFileExtensions).toBeUndefined();
  });

  it('PluginConfiguration carries lastSuccessfulRun as Date | null', () => {
    const empty: PluginConfiguration = {
      enabled: true,
      settings: {},
      lastSuccessfulRun: null,
    };
    const seeded: PluginConfiguration = {
      enabled: false,
      settings: { rootPath: '/tmp' },
      lastSuccessfulRun: new Date('2024-01-01T00:00:00Z'),
    };
    expect(empty.lastSuccessfulRun).toBeNull();
    expect(seeded.lastSuccessfulRun).toBeInstanceOf(Date);
  });

  it('DiscoveredDocument requires the core fields and allows optional extras', () => {
    const doc: DiscoveredDocument = {
      type: 'markdown',
      provider: 'filesystem',
      location: '/tmp/readme.md',
      title: 'Readme',
      summary: 'Summary',
      content: '# hi',
      extendedData: { author: 'me' },
      contentHash: 'deadbeef',
    };
    expect(doc.extendedData?.author).toBe('me');
  });

  it('ISearchPlugin can be implemented with an async iterable discovery', async () => {
    const plugin: ISearchPlugin = {
      metadata: {
        id: 'fixture',
        name: 'Fixture',
        description: 'Test fixture plugin',
        version: '0.0.0',
      },
      settingDescriptors: [],
      async initialize(_configuration, _signal) {
        // no-op
      },
      async *discoverDocuments(_signal) {
        yield {
          type: 'text',
          provider: 'fixture',
          location: 'mem://1',
          title: 'one',
          summary: 'one',
          content: 'one',
        };
      },
    };

    const collected: DiscoveredDocument[] = [];
    await plugin.initialize({ enabled: true, settings: {}, lastSuccessfulRun: null });
    for await (const doc of plugin.discoverDocuments()) {
      collected.push(doc);
    }
    expect(collected).toHaveLength(1);
    expect(collected[0]?.title).toBe('one');

    // Type-level assertions
    expectTypeOf(plugin.metadata).toMatchTypeOf<PluginMetadata>();
    expectTypeOf(plugin.settingDescriptors).toMatchTypeOf<readonly PluginSettingDescriptor[]>();
    expectTypeOf(plugin.discoverDocuments).returns.toMatchTypeOf<AsyncIterable<DiscoveredDocument>>();
  });

  it('respects AbortSignal: aborted discovery stops iteration', async () => {
    const ac = new AbortController();
    const plugin: ISearchPlugin = {
      metadata: { id: 'a', name: 'a', description: 'a', version: '0' },
      settingDescriptors: [],
      async initialize() {},
      async *discoverDocuments(signal) {
        for (let i = 0; i < 5; i++) {
          if (signal?.aborted) return;
          yield {
            type: 't',
            provider: 'p',
            location: `mem://${i}`,
            title: `t${i}`,
            summary: '',
            content: '',
          };
          if (i === 1) ac.abort();
        }
      },
    };

    const seen: string[] = [];
    for await (const d of plugin.discoverDocuments(ac.signal)) {
      seen.push(d.location);
    }
    expect(seen).toEqual(['mem://0', 'mem://1']);
  });
});
