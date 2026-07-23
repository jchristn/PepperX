/**
 * A narrated walkthrough of the PepperX JavaScript SDK against a live node.
 *
 * Build the SDK first (`npm run build`), then:
 *   node examples/console.mjs
 *
 * Point it at a node with PEPPERX_URL and PEPPERX_WS_URL.
 */

import WebSocket from 'ws';

import { PepperXClient, PepperXError, PepperXWebsocketClient } from '../dist/index.js';

const REST_URL = process.env.PEPPERX_URL ?? 'http://127.0.0.1:8000';
const WS_URL = process.env.PEPPERX_WS_URL ?? 'ws://127.0.0.1:8002/';

const encoder = new TextEncoder();
const decoder = new TextDecoder();
const suffix = Math.random().toString(36).slice(2, 12);

const step = (message) => console.log(`> ${message}`);
const detail = (message) => console.log(`    ${message}`);

async function restWalkthrough(container) {
  const client = new PepperXClient(REST_URL);

  step('Checking node health');
  if (!(await client.health())) throw new Error(`No healthy PepperX node at ${REST_URL}`);
  detail('healthy');

  step(`Creating container '${container}'`);
  const created = await client.createContainer(container, { demo: 'javascript' });
  detail(`id ${created.id}`);

  step('Writing an object with labels, tags, and a metadata object');
  const written = await client.writeObject(
    container,
    'reports/2026/summary.txt',
    encoder.encode('PepperX stores this payload verbatim.'),
    {
      contentType: 'text/plain',
      labels: ['report', 'annual'],
      tags: { year: '2026', team: 'platform' },
      metadataObject: { author: 'demo', revision: 3 },
    },
  );
  detail(`extent ${written.extentId}, ${written.sizeBytes} bytes, sha256 ${written.sha256.slice(0, 12)}...`);

  step('Reading the payload back');
  const read = await client.readObject(container, 'reports/2026/summary.txt');
  detail(`"${decoder.decode(read.data)}"`);

  step('Reading full metadata');
  const metadata = await client.readObjectMetadata(container, 'reports/2026/summary.txt');
  detail(
    `labels [${metadata.labels.join(', ')}], ${Object.keys(metadata.tags).length} tags, ` +
      `metadata object present: ${metadata.metadataObject !== null}`,
  );

  step('Searching by label and tag');
  const found = await client.enumerateObjects(container, {
    labels: ['report'],
    tags: { year: '2026' },
  });
  detail(`${found.totalRecords} match(es)`);

  step('Reading statistics');
  const stats = await client.statistics();
  detail(`${stats.containerCount} containers, ${stats.objectCount} objects, ${stats.totalBytes} bytes`);

  step('Deleting the container and its contents');
  await client.deleteContainer(container, true);
  detail('deleted');
}

async function websocketWalkthrough(container) {
  console.log();
  const client = new PepperXWebsocketClient(WS_URL, { WebSocketImpl: WebSocket });

  step('Connecting over WebSockets');
  await client.connect();
  detail('connected');

  try {
    step(`Creating container '${container}'`);
    await client.createContainer(container);
    detail('created');

    step('Writing and reading an object');
    await client.writeObject(container, 'ws-key', encoder.encode('delivered over a persistent connection'), {
      contentType: 'text/plain',
    });
    const payload = await client.readObject(container, 'ws-key');
    detail(`"${decoder.decode(payload)}"`);

    step('Issuing 8 concurrent operations on one connection');
    const results = await Promise.all(Array.from({ length: 8 }, () => client.health()));
    detail(`all ${results.length} correlated correctly`);

    step('Cleaning up');
    await client.deleteObject(container, 'ws-key');
    await client.deleteContainer(container);
    detail('deleted');
  } finally {
    await client.close();
  }
}

async function main() {
  console.log('PepperX JavaScript SDK walkthrough');
  console.log(`  REST      : ${REST_URL}`);
  console.log(`  WebSockets: ${WS_URL}`);
  console.log();

  try {
    await restWalkthrough(`jsdemo${suffix}`);
    await websocketWalkthrough(`jsdemows${suffix}`);
    console.log();
    console.log('Walkthrough complete.');
    return 0;
  } catch (error) {
    console.log();
    if (error instanceof PepperXError) {
      console.log(`PepperX returned an error: ${error.errorType} (${error.statusCode}) ${error.message}`);
    } else {
      console.log(`Failed: ${error.message}`);
      console.log('Is a PepperX node running? Set PEPPERX_URL to point at one.');
    }
    return 1;
  }
}

process.exit(await main());
