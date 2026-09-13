import { build } from 'esbuild';
import { pathToFileURL } from 'node:url';
import path from 'node:path';
const output = path.resolve('bin/AgentVerification/engine.test.mjs');
await build({ entryPoints: ['tests/engine.test.mts'], outfile: output, bundle: true, platform: 'node', format: 'esm', packages: 'external', target: 'es2024' });
await import(pathToFileURL(output).href);
