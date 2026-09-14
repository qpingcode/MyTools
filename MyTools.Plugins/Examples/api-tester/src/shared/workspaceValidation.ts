import { AuthKind, BodyKind, AssertionKind, KeyLocation, Limits, type Workspace } from './model.js';

const MaximumFieldChars = Limits.importBytes;
export function validateWorkspace(value: unknown): asserts value is Workspace {
  const object = (v: any) => v && typeof v === 'object' && !Array.isArray(v);
  const text = (v: any) => typeof v === 'string' && v.length <= MaximumFieldChars;
  const pairs = (v: any) => Array.isArray(v) && v.every(p => object(p) && text(p.name) && text(p.value) && typeof p.enabled === 'boolean' && (p.file === undefined || text(p.file)));
  const settings = (v: any) => object(v) && Number.isFinite(v.timeoutMs) && v.timeoutMs > 0 && typeof v.followRedirects === 'boolean' && typeof v.verifyTls === 'boolean';
  const scripts = (v: any) => v === undefined || object(v) && typeof v.enabled === 'boolean' && text(v.before) && text(v.after);
  const auth = (v: any, inherited: boolean) => object(v) && Object.values(AuthKind).includes(v.kind) && (inherited || v.kind !== AuthKind.Inherit) && ['username', 'password', 'token', 'key', 'value'].every(k => text(v[k])) && Object.values(KeyLocation).includes(v.location);
  const ids = new Set<string>();
  const identifier = (v: any) => text(v) && v.length > 0 && !ids.has(v) && Boolean(ids.add(v));
  const v: any = value;
  if (!object(v) || v.version !== Limits.schemaVersion || !Array.isArray(v.collections) || !Array.isArray(v.environments) || !text(v.environmentId) || !settings(v.defaults)) throw new Error('Unsupported workspace format');
  for (const c of v.collections) {
    if (!object(c) || !identifier(c.id) || !text(c.name) || !Array.isArray(c.requests) || c.headers !== undefined && !pairs(c.headers) || c.auth !== undefined && !auth(c.auth, false) || !scripts(c.scripts)) throw new Error('Invalid collection');
    for (const r of c.requests) {
      if (!object(r) || !identifier(r.id) || !text(r.name) || !text(r.method) || !text(r.url) || !pairs(r.params) || !pairs(r.headers) || !auth(r.auth, true) || !object(r.body) || !Object.values(BodyKind).includes(r.body.kind) || !text(r.body.text) || !text(r.body.contentType) || !text(r.body.file) || !pairs(r.body.fields) || !scripts(r.scripts) || r.settings !== undefined && !settings(r.settings)) throw new Error('Invalid request');
      if (!Array.isArray(r.assertions) || !r.assertions.every((a: any) => object(a) && text(a.name) && typeof a.enabled === 'boolean' && Object.values(AssertionKind).includes(a.kind) && text(a.path) && text(a.expected)) || !Array.isArray(r.extractions) || !r.extractions.every((e: any) => object(e) && typeof e.enabled === 'boolean' && text(e.path) && text(e.variable))) throw new Error('Invalid tests');
    }
  }
  for (const e of v.environments) if (!object(e) || !identifier(e.id) || !text(e.name) || !pairs(e.variables)) throw new Error('Invalid environment');
}
