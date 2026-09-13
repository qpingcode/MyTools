import { createRequire } from 'node:module';
import { run } from 'vue-tsc';
import './i18n-check.mjs';
const require = createRequire(import.meta.url);
// Vue's language tooling needs the JS compiler API, unavailable in TypeScript 7.
// Keep the scaffold compiler version and isolate the Vue-compatible compiler.
run(require.resolve('typescript-vue/lib/tsc'));
