import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';
import { MeshoptSimplifier } from 'meshoptimizer';

// Offline only. Keep the original attributes and topology seams. Each level is
// generated independently from the source, so its reported error is cumulative.
await MeshoptSimplifier.ready;
const [input, output] = process.argv.slice(2);
if (!input || !output) throw new Error('Usage: node generate.mjs exported-chain.json output-directory');
const source = JSON.parse(fs.readFileSync(input, 'utf8'));
if (source.indices.length !== 1) throw new Error('Only a single source submesh is supported');
const vertices = new Float32Array(source.vertices), indices = new Uint32Array(source.indices[0]);
const positions = new Float32Array(vertices.length / 4), attributes = new Float32Array(vertices.length / 12 * 5);
for (let i = 0; i < vertices.length / 12; i++) {
  positions.set(vertices.subarray(i*12, i*12+3), i*3);
  attributes.set(vertices.subarray(i*12+3, i*12+8), i*5);
}
const fingerprint = crypto.createHash('sha256').update(Buffer.from(vertices.buffer)).update(Buffer.from(indices.buffer)).digest();
const levels = [];
for (const limit of [0, 0.00001, 0.00003, 0.0001, 0.0003, 0.001, 0.003, 0.01]) {
  const [simplified, error] = MeshoptSimplifier.simplifyWithAttributes(indices, positions, 3, attributes, 5, [1,1,1,1,1], null, 3, limit, ['ErrorAbsolute', 'LockBorder']);
  if (simplified.length >= (levels.at(-1)?.indices.length ?? indices.length)) continue;
  levels.push({ indices: simplified, error });
}
fs.mkdirSync(output, { recursive: true });
const chunks = [Buffer.from('PBCHAIN1'), fingerprint];
function integer(n) { const b = Buffer.alloc(4); b.writeUInt32LE(n); chunks.push(b); }
function single(n) { const b = Buffer.alloc(4); b.writeFloatLE(n); chunks.push(b); }
integer(vertices.length / 12); integer(indices.length); integer(levels.length);
for (const level of levels) { single(level.error); integer(level.indices.length); chunks.push(Buffer.from(level.indices.buffer)); }
const key = fingerprint.toString('hex');
fs.writeFileSync(path.join(output, key + '.bytes'), Buffer.concat(chunks));
fs.writeFileSync(path.join(output, key + '.json'), JSON.stringify({ source: source.Name, sha256: key, originalTriangles: indices.length / 3, levels: levels.map(l => ({ triangles: l.indices.length / 3, error: l.error })) }, null, 2));
console.log(JSON.stringify({ source: source.Name, sha256: key, originalTriangles: indices.length / 3, levels: levels.map(l => ({ triangles: l.indices.length / 3, error: l.error })) }));
