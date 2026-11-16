import { createWriteStream, existsSync, renameSync, statSync } from 'node:fs';
import { Readable } from 'node:stream';
import { pipeline } from 'node:stream/promises';
import { basename } from 'node:path';

const [url, destination] = process.argv.slice(2);
if (!url || !destination || !url.startsWith('https://')) throw new Error('An HTTPS URL and destination are required.');
if (existsSync(destination)) process.exit(0);
const partial = `${destination}.download`;
const previous = existsSync(partial) ? statSync(partial).size : 0;
const response = await fetch(url, { headers: previous ? { Range: `bytes=${previous}-` } : {}, signal: AbortSignal.timeout(30 * 60 * 1000) });
if (!response.ok || !response.body) throw new Error(`Download failed: HTTP ${response.status}`);
const append = previous > 0 && response.status === 206;
console.log(`Downloading ${basename(destination)}${append ? ' (resuming)' : ''}`);
await pipeline(Readable.fromWeb(response.body), createWriteStream(partial, { flags: append ? 'a' : 'w' }));
renameSync(partial, destination);
console.log(`Downloaded ${basename(destination)} (${Math.round(statSync(destination).size / 1024 / 1024)} MB)`);
