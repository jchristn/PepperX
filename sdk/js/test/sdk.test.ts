/**
 * Tests for the PepperX JavaScript SDK.
 *
 * These exercise a live node. Point them at one with `PEPPERX_URL` and `PEPPERX_WS_URL`; when no
 * node is reachable the tests skip rather than fail.
 */

import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import WebSocket from 'ws';

import { PepperXClient, PepperXError, PepperXWebsocketClient } from '../src/index.js';
import type { WebSocketLike } from '../src/index.js';

const REST_URL = process.env.PEPPERX_URL ?? 'http://127.0.0.1:8000';
const WS_URL = process.env.PEPPERX_WS_URL ?? 'ws://127.0.0.1:8002/';

const client = new PepperXClient(REST_URL, { timeoutMs: 30000 });

let available = false;
const containers: string[] = [];

function newContainer(): string {
  const name = 'js' + Math.random().toString(36).slice(2, 18).replace(/[^a-z0-9]/g, 'x');
  containers.push(name);
  return name;
}

const encoder = new TextEncoder();
const decoder = new TextDecoder();

beforeAll(async () => {
  available = await client.health();
});

afterAll(async () => {
  if (!available) return;
  for (const name of containers) {
    try {
      await client.deleteContainer(name, true);
    } catch {
      // Already removed by the test itself.
    }
  }
});

describe('PepperXClient', () => {
  it('reports health', async () => {
    if (!available) return;
    expect(await client.health()).toBe(true);
  });

  it('creates, reads, tags, and deletes a container', async () => {
    if (!available) return;

    const name = newContainer();
    const created = await client.createContainer(name, { team: 'js' });
    expect(created.name).toBe(name);
    expect(created.tags.team).toBe('js');

    expect(await client.containerExists(name)).toBe(true);
    const read = await client.readContainer(name);
    expect(read?.id).toBe(created.id);

    const updated = await client.updateContainerTags(name, { tier: 'gold' });
    expect(updated.tags).toEqual({ tier: 'gold' });

    const page = await client.enumerateContainers({ maxResults: 1000 });
    expect(page.totalRecords).toBeGreaterThanOrEqual(1);

    await client.deleteContainer(name);
    expect(await client.containerExists(name)).toBe(false);
    expect(await client.readContainer(name)).toBeNull();
  });

  it('round-trips an object with metadata', async () => {
    if (!available) return;

    const name = newContainer();
    await client.createContainer(name);

    const written = await client.writeObject(name, 'a/b/c.txt', encoder.encode('js-payload'), {
      contentType: 'text/plain',
      labels: ['sdk', 'js'],
      tags: { lang: 'typescript' },
      metadataObject: { nested: { value: 1 } },
    });
    expect(written.extentId).toBeTruthy();
    expect(written.sizeBytes).toBe(10);

    const read = await client.readObject(name, 'a/b/c.txt');
    expect(read).not.toBeNull();
    expect(decoder.decode(read!.data)).toBe('js-payload');
    expect(read!.sha256).toBe(written.sha256);

    const metadata = await client.readObjectMetadata(name, 'a/b/c.txt');
    expect(metadata!.labels.sort()).toEqual(['js', 'sdk']);
    expect(metadata!.tags.lang).toBe('typescript');
    expect(metadata!.metadataObject).toEqual({ nested: { value: 1 } });

    expect(await client.objectExists(name, 'a/b/c.txt')).toBe(true);
    expect(await client.deleteObject(name, 'a/b/c.txt')).toBe(true);
    expect(await client.readObject(name, 'a/b/c.txt')).toBeNull();
  });

  it('preserves binary payloads', async () => {
    if (!available) return;

    const name = newContainer();
    await client.createContainer(name);

    const payload = new Uint8Array(1024);
    for (let i = 0; i < payload.length; i++) payload[i] = i % 256;

    await client.writeObject(name, 'binary.bin', payload);
    const read = await client.readObject(name, 'binary.bin');
    expect(read!.data).toEqual(payload);
  });

  it('streams an object out without buffering', async () => {
    if (!available) return;

    const name = newContainer();
    await client.createContainer(name);

    const payload = new Uint8Array(64 * 1024).fill(7);
    await client.writeObject(name, 'streamed.bin', payload);

    const stream = await client.openObject(name, 'streamed.bin');
    expect(stream).not.toBeNull();

    const chunks: Uint8Array[] = [];
    const reader = stream!.getReader();
    for (;;) {
      const { done, value } = await reader.read();
      if (done) break;
      if (value) chunks.push(value);
    }

    const total = chunks.reduce((sum, chunk) => sum + chunk.length, 0);
    expect(total).toBe(payload.length);
  });

  it('updates metadata while preserving the payload', async () => {
    if (!available) return;

    const name = newContainer();
    await client.createContainer(name);

    await client.writeObject(name, 'k', encoder.encode('keep-me'), { labels: ['before'] });
    await client.updateObjectMetadata(name, 'k', { labels: ['after'], tags: { t: 'v' } });

    const metadata = await client.readObjectMetadata(name, 'k');
    expect(metadata!.labels).toEqual(['after']);
    expect(metadata!.tags).toEqual({ t: 'v' });

    const read = await client.readObject(name, 'k');
    expect(decoder.decode(read!.data)).toBe('keep-me');
  });

  it('filters by labels and tags with AND semantics', async () => {
    if (!available) return;

    const name = newContainer();
    await client.createContainer(name);

    await client.writeObject(name, 'match', encoder.encode('a'), {
      labels: ['red', 'blue'],
      tags: { tier: 'gold' },
    });
    await client.writeObject(name, 'partial', encoder.encode('b'), { labels: ['red'] });
    await client.writeObject(name, 'other', encoder.encode('c'), {
      labels: ['red', 'blue'],
      tags: { tier: 'silver' },
    });

    const found = await client.enumerateObjects(name, {
      labels: ['red', 'blue'],
      tags: { tier: 'gold' },
    });
    expect(found.totalRecords).toBe(1);
    expect(found.objects[0].key).toBe('match');
  });

  it('surfaces typed errors', async () => {
    if (!available) return;

    const name = newContainer();
    await client.createContainer(name);

    await expect(client.createContainer(name)).rejects.toBeInstanceOf(PepperXError);

    try {
      await client.createContainer(name);
    } catch (error) {
      const typed = error as PepperXError;
      expect(typed.errorType).toBe('Conflict');
      expect(typed.statusCode).toBe(409);
    }

    await client.writeObject(name, 'once', encoder.encode('first'));
    try {
      await client.writeObject(name, 'once', encoder.encode('second'), { noOverwrite: true });
      throw new Error('expected a conflict');
    } catch (error) {
      expect((error as PepperXError).errorType).toBe('Conflict');
    }
  });

  it('reads statistics and nodes', async () => {
    if (!available) return;

    const stats = await client.statistics();
    expect(stats.containerCount).toBeGreaterThanOrEqual(0);
    expect((await client.nodes()).length).toBeGreaterThanOrEqual(1);
  });
});

describe('PepperXWebsocketClient', () => {
  it('round-trips an object over one connection', async () => {
    if (!available) return;

    const ws = new PepperXWebsocketClient(WS_URL, {
      WebSocketImpl: WebSocket as unknown as new (url: string) => WebSocketLike,
    });
    await ws.connect();

    try {
      expect(await ws.health()).toBe(true);

      const name = newContainer();
      await ws.createContainer(name);

      await ws.writeObject(name, 'ws-key', encoder.encode('ws-payload'), {
        contentType: 'text/plain',
        labels: ['ws'],
      });

      const payload = await ws.readObject(name, 'ws-key');
      expect(decoder.decode(payload!)).toBe('ws-payload');

      const metadata = await ws.readObjectMetadata(name, 'ws-key');
      expect(metadata!.labels).toEqual(['ws']);

      expect(await ws.deleteObject(name, 'ws-key')).toBe(true);
      expect(await ws.readObject(name, 'ws-key')).toBeNull();
    } finally {
      await ws.close();
    }
  });

  it('correlates concurrent operations', async () => {
    if (!available) return;

    const ws = new PepperXWebsocketClient(WS_URL, {
      WebSocketImpl: WebSocket as unknown as new (url: string) => WebSocketLike,
    });
    await ws.connect();

    try {
      const results = await Promise.all(Array.from({ length: 16 }, () => ws.health()));
      expect(results.every(Boolean)).toBe(true);
    } finally {
      await ws.close();
    }
  });
});
