import { AuthKind, BodyKind, ContentType, HttpHeader, HttpMethod, KeyLocation, Limits, emptyWorkspace, newRequest, parseQuery, queryUrl, type ApiRequest, type Collection, type Environment, type Pair, type RequestScripts, type Workspace } from './model.js';
import { validateWorkspace } from './workspaceValidation.js';

const PostmanCollectionSchema = 'https://schema.getpostman.com/json/collection/v2.1.0/collection.json';
const JsonIndent = 2;
export enum ExportFormat { Native = 'native', Postman = 'postman' }
const pair = (name: string, value: string, enabled = true): Pair => ({ name, value, enabled });
const clone = <T>(value: T): T => JSON.parse(JSON.stringify(value));

// Parse shell quoting without evaluating substitutions or invoking a shell.
export function curlTokens(source: string): string[] {
  const tokens: string[] = []; let token = ''; let quote = ''; let started = false;
  source = source.replace(/\\\r?\n|\^\r?\n|`\r?\n/g, '');
  for (let index = 0; index < source.length; index++) {
    const char = source[index];
    if (quote) {
      if (char === quote) quote = '';
      else if (char === '\\' && quote === '"' && ['"', '\\', '$', '`'].includes(source[index + 1])) token += source[++index];
      else token += char;
    } else if (char === "'" || char === '"') { quote = char; started = true; }
    else if (/\s/.test(char)) { if (started) { tokens.push(token); token = ''; started = false; } }
    else if (char === '\\') { if (++index >= source.length) throw new Error('Incomplete escape'); token += source[index]; started = true; }
    else { token += char; started = true; }
  }
  if (quote) throw new Error('Unclosed quote');
  if (started) tokens.push(token);
  return tokens;
}

export function importCurl(source: string, id: string, name: string): ApiRequest {
  const args = curlTokens(source); const r = newRequest(id, name);
  r.settings = { ...emptyWorkspace().defaults, followRedirects: false };
  if (!['curl', 'curl.exe'].includes(args.shift()?.toLowerCase() || '')) throw new Error('Expected cURL');
  let explicitMethod = false; const data: string[] = []; let form = false; let json = false;
  for (let index = 0; index < args.length; index++) {
    let flag = args[index]; let inline: string | undefined;
    if (flag.startsWith('--') && flag.includes('=')) { const split = flag.indexOf('='); inline = flag.slice(split + 1); flag = flag.slice(0, split); }
    else if (/^-[XHubdFAe]/.test(flag) && flag.length > 2) { inline = flag.slice(2); flag = flag.slice(0, 2); }
    const value = () => { const v = inline ?? args[++index]; if (v === undefined) throw new Error(`Missing ${flag}`); return v; };
    if (/^https?:\/\//i.test(flag)) { if (r.url) throw new Error('Multiple URLs'); r.url = flag; }
    else switch (flag) {
      case '--url': r.url = value(); break;
      case '-X': case '--request': r.method = value().toUpperCase(); explicitMethod = true; break;
      case '-I': case '--head': r.method = HttpMethod.Head; explicitMethod = true; break;
      case '-H': case '--header': {
        const header = value(); const split = header.indexOf(':'); if (split <= 0) throw new Error('Invalid header');
        const name = header.slice(0, split).trim();
        // Browser cURL includes transport headers that this client manages itself.
        if (![HttpHeader.ContentLength, HttpHeader.Host, HttpHeader.Connection].includes(name.toLowerCase() as typeof HttpHeader.Host)) r.headers.push(pair(name, header.slice(split + 1).trim()));
        break;
      }
      case '-b': case '--cookie': { const cookie = value(); if (!cookie.includes('=')) throw new Error('Cookie files are unsupported'); r.headers.push(pair(HttpHeader.Cookie, cookie)); break; }
      case '-A': case '--user-agent': r.headers.push(pair(HttpHeader.UserAgent, value())); break;
      case '-e': case '--referer': r.headers.push(pair(HttpHeader.Referer, value())); break;
      case '-u': case '--user': { const credentials = value(); const split = credentials.indexOf(':'); r.auth.kind = AuthKind.Basic; r.auth.username = split < 0 ? credentials : credentials.slice(0, split); r.auth.password = split < 0 ? '' : credentials.slice(split + 1); break; }
      case '-d': case '--data': case '--data-raw': case '--data-binary': case '--json': {
        const body = value();
        if (body.startsWith('@') && flag === '--data-binary') { if (body === '@-' || r.body.kind !== BodyKind.None) throw new Error('Standard input or mixed files are unsupported'); r.body.kind = BodyKind.Binary; r.body.file = body.slice(1); r.body.contentType = ContentType.Form; break; }
        if (body.startsWith('@') && flag !== '--data-raw') throw new Error('File-backed data is unsupported'); data.push(body); json ||= flag === '--json'; break;
      }
      case '--data-urlencode': { const field = value(); const split = field.indexOf('='); if (field.includes('@') && (split < 0 || field.indexOf('@') < split)) throw new Error('File-backed data is unsupported'); data.push(split < 0 ? encodeURIComponent(field) : field.slice(0, split) + '=' + encodeURIComponent(field.slice(split + 1))); form = true; break; }
      case '-F': case '--form': case '--form-string': {
        if (r.body.kind === BodyKind.Binary) throw new Error('Mixed request bodies');
        const field = value(); const split = field.indexOf('='); if (split < 0) throw new Error('Invalid form field');
        r.body.kind = BodyKind.Multipart; const p = pair(field.slice(0, split), field.slice(split + 1));
        if (p.value.startsWith('@') && flag !== '--form-string') { const [file, type] = p.value.slice(1).split(';type='); p.file = file; p.value = ''; p.contentType = type || ContentType.Binary; }
        r.body.fields.push(p); break;
      }
      case '-k': case '--insecure': r.settings = { ...emptyWorkspace().defaults, ...r.settings, verifyTls: false }; break;
      case '-L': case '--location': r.settings = { ...emptyWorkspace().defaults, ...r.settings, followRedirects: true }; break;
      case '--max-time': { const timeoutMs = Number(value()) * MillisecondsPerSecond; if (!Number.isFinite(timeoutMs) || timeoutMs <= 0) throw new Error('Invalid timeout'); r.settings = { ...emptyWorkspace().defaults, ...r.settings, timeoutMs }; break; }
      case '--compressed': case '-s': case '--silent': case '-S': case '--show-error': case '-v': case '--verbose': break;
      default: throw new Error(`Unsupported cURL option: ${flag}`);
    }
  }
  if (!r.url || !Object.values(HttpMethod).includes(r.method as HttpMethod)) throw new Error('Invalid URL or method');
  const address = new URL(r.url); if (!['http:', 'https:'].includes(address.protocol)) throw new Error('Invalid URL');
  r.params = parseQuery(r.url);
  if (data.length) {
    if ([BodyKind.Multipart, BodyKind.Binary].includes(r.body.kind)) throw new Error('Mixed request bodies');
    const type = r.headers.find(h => h.name.toLowerCase() === HttpHeader.ContentType)?.value || (json ? ContentType.Json : ContentType.Form);
    form ||= type.startsWith(ContentType.Form);
    r.body.contentType = type; r.body.text = data.join('&'); r.body.kind = form ? BodyKind.Form : type.includes('json') ? BodyKind.Json : BodyKind.Text;
    if (form) r.body.fields = parseQuery('https://localhost/?' + r.body.text);
  }
  if (r.body.kind === BodyKind.Binary) r.body.contentType = r.headers.find(h => h.name.toLowerCase() === HttpHeader.ContentType)?.value || r.body.contentType;
  if (!explicitMethod && r.body.kind !== BodyKind.None) r.method = HttpMethod.Post;
  return r;
}
const MillisecondsPerSecond = 1000;
const shellQuote = (value: string) => "'" + value.replace(/'/g, "'\\''") + "'";
export function resolveRequestVariables(request: ApiRequest, replace: (text: string) => string): ApiRequest {
  const r = clone(request);
  r.url = replace(r.url);
  const resolvePairs = (pairs: Pair[]) => pairs.map(p => !p.enabled ? p : { ...p, name: replace(p.name), value: p.file ? p.value : replace(p.value), file: p.file ? replace(p.file) : undefined });
  r.params = resolvePairs(r.params); r.headers = resolvePairs(r.headers);
  if ([BodyKind.Json, BodyKind.Text].includes(r.body.kind)) r.body.text = replace(r.body.text);
  if (r.body.kind === BodyKind.Binary) r.body.file = replace(r.body.file);
  if ([BodyKind.Form, BodyKind.Multipart].includes(r.body.kind)) r.body.fields = resolvePairs(r.body.fields);
  r.body.contentType = replace(r.body.contentType);
  const fields = r.auth.kind === AuthKind.Basic ? ['username', 'password'] as const : r.auth.kind === AuthKind.Bearer ? ['token'] as const : r.auth.kind === AuthKind.ApiKey ? ['key', 'value'] as const : [];
  for (const key of fields) r.auth[key] = replace(r.auth[key]);
  return r;
}
export function exportCurl(request: ApiRequest): string {
  const r = clone(request); const args = ['curl', '--request', shellQuote(r.method), shellQuote(queryUrl(r.url, r.params))];
  for (const h of r.headers.filter(h => h.enabled)) args.push('--header', shellQuote(h.name + ': ' + h.value));
  switch (r.auth.kind) {
    case AuthKind.Basic: args.push('--user', shellQuote(r.auth.username + ':' + r.auth.password)); break;
    case AuthKind.Bearer: args.push('--header', shellQuote('Authorization: Bearer ' + r.auth.token)); break;
    case AuthKind.ApiKey:
      if (r.auth.location === KeyLocation.Header) args.push('--header', shellQuote(r.auth.key + ': ' + r.auth.value));
      else args[CurlUrlArgumentIndex] = shellQuote(queryUrl(r.url, [...r.params, pair(r.auth.key, r.auth.value)]));
      break;
  }
  const contentHeader = () => { if (!r.headers.some(h => h.enabled && h.name.toLowerCase() === HttpHeader.ContentType)) args.push('--header', shellQuote('Content-Type: ' + (r.body.contentType || (r.body.kind === BodyKind.Json ? ContentType.Json : r.body.kind === BodyKind.Form ? ContentType.Form : r.body.kind === BodyKind.Binary ? ContentType.Binary : ContentType.Text)))); };
  if ([BodyKind.Json, BodyKind.Text].includes(r.body.kind)) { contentHeader(); args.push('--data-raw', shellQuote(r.body.text)); }
  if (r.body.kind === BodyKind.Form) { contentHeader(); args.push('--data-raw', shellQuote(queryUrl('', r.body.fields).slice(1))); }
  if (r.body.kind === BodyKind.Multipart) for (const field of r.body.fields.filter(p => p.enabled)) args.push(field.file ? '--form' : '--form-string', shellQuote(field.name + '=' + (field.file ? '@' + field.file + ';type=' + (field.contentType || ContentType.Binary) : field.value)));
  if (r.body.kind === BodyKind.Binary) { contentHeader(); args.push('--data-binary', shellQuote('@' + r.body.file)); }
  if (r.settings?.verifyTls === false) args.push('--insecure');
  if (r.settings?.followRedirects !== false) args.push('--location');
  if (r.settings) args.push('--max-time', shellQuote(String(r.settings.timeoutMs / MillisecondsPerSecond)));
  return args.join(' ');
}
const CurlUrlArgumentIndex = 3;

function postmanAuth(source: any): ApiRequest['auth'] {
  const auth = newRequest('', '').auth;
  if (!source) { auth.kind = AuthKind.Inherit; return auth; }
  if (source.type === 'noauth') return auth;
  const fields = Object.fromEntries((source[source.type] || []).map((field: any) => [field.key, String(field.value ?? '')]));
  switch (source.type) {
    case 'basic': auth.kind = AuthKind.Basic; auth.username = fields.username || ''; auth.password = fields.password || ''; break;
    case 'bearer': auth.kind = AuthKind.Bearer; auth.token = fields.token || ''; break;
    case 'apikey': auth.kind = AuthKind.ApiKey; auth.key = fields.key || ''; auth.value = fields.value || ''; auth.location = fields.in === 'query' ? KeyLocation.Query : KeyLocation.Header; break;
    default: throw new Error(`Unsupported Postman auth: ${source.type}`);
  }
  return auth;
}
function postmanScripts(events: any[]): RequestScripts {
  const text = (phase: string) => (events || []).filter(e => e.listen === phase).map(e => Array.isArray(e.script?.exec) ? e.script.exec.join('\n') : String(e.script?.exec || '')).filter(Boolean).map(source => `await (async () => {\n${source}\n})();`).join('\n');
  return { enabled: false, before: text('prerequest'), after: text('test') };
}
export function importWorkspace(source: string, uid: () => string): Workspace {
  if (new TextEncoder().encode(source).length > Limits.importBytes) throw new Error('Import too large');
  const parsed = JSON.parse(source); const workspace = emptyWorkspace();
  if (parsed.version !== undefined) {
    validateWorkspace(parsed); Object.assign(workspace, clone(parsed));
  } else if (Array.isArray(parsed.item) && parsed.info) {
    const collection: Collection = { id: uid(), name: String(parsed.info.name || ''), requests: [], headers: [], auth: postmanAuth(parsed.auth), scripts: postmanScripts(parsed.event) };
    if (collection.auth!.kind === AuthKind.Inherit) collection.auth!.kind = AuthKind.None;
    const visit = (items: any[], prefix: string, inheritedAuth: any, inheritedEvents: any[]) => {
      for (const item of items) {
        if (item.item) { visit(item.item, prefix + String(item.name || '') + ' / ', item.auth || inheritedAuth, [...inheritedEvents, ...(item.event || [])]); continue; }
        const input = typeof item.request === 'string' ? { url: item.request } : item.request;
        if (!input) throw new Error('Invalid Postman request');
        const r = newRequest(uid(), prefix + String(item.name || ''));
        r.method = input.method || HttpMethod.Get;
        r.url = typeof input.url === 'string' ? input.url : input.url?.raw || '';
        r.params = parseQuery(r.url);
        if (typeof input.url === 'object' && Array.isArray(input.url.query)) r.params = input.url.query.map((p: any) => pair(String(p.key || ''), String(p.value || ''), !p.disabled));
        r.headers = (input.header || []).map((h: any) => pair(String(h.key || ''), String(h.value || ''), !h.disabled));
        r.auth = postmanAuth(input.auth || inheritedAuth);
        r.scripts = postmanScripts([...inheritedEvents, ...(item.event || [])]);
        const body = input.body;
        if (body?.mode === 'raw') { r.body.kind = body.options?.raw?.language === 'json' ? BodyKind.Json : BodyKind.Text; r.body.text = String(body.raw || ''); }
        else if (body?.mode === 'urlencoded' || body?.mode === 'formdata') {
          r.body.kind = body.mode === 'urlencoded' ? BodyKind.Form : BodyKind.Multipart;
          r.body.fields = (body[body.mode] || []).map((p: any) => ({ ...pair(String(p.key || ''), String(p.value || ''), !p.disabled), ...(p.type === 'file' ? { file: String(Array.isArray(p.src) ? p.src[0] || '' : p.src || ''), contentType: p.contentType || ContentType.Binary } : {}) }));
        } else if (body?.mode === 'file') { r.body.kind = BodyKind.Binary; r.body.file = String(body.file?.src || ''); }
        else if (body?.mode && body.mode !== 'raw') throw new Error(`Unsupported Postman body: ${body.mode}`);
        collection.requests.push(r);
      }
    };
    visit(parsed.item, '', undefined, []); workspace.collections.push(collection);
    if (Array.isArray(parsed.variable) && parsed.variable.length) workspace.environments.push({ id: uid(), name: collection.name, variables: parsed.variable.map((v: any) => pair(String(v.key || ''), String(v.value || ''), !v.disabled)) });
  } else if (Array.isArray(parsed.values)) {
    workspace.environments.push({ id: uid(), name: String(parsed.name || ''), variables: parsed.values.map((v: any) => pair(String(v.key || ''), String(v.value ?? ''), v.enabled !== false)) });
  } else throw new Error('Unknown import format');
  // Reassign identities so importing a backup never replaces existing requests.
  const activeEnvironment = workspace.environmentId;
  for (const c of workspace.collections) { c.id = uid(); if (c.scripts) c.scripts.enabled = false; for (const r of c.requests) { r.id = uid(); if (r.scripts) r.scripts.enabled = false; } }
  for (const e of workspace.environments) { const old = e.id; e.id = uid(); if (old === activeEnvironment) workspace.environmentId = e.id; }
  if (!workspace.environments.some(e => e.id === workspace.environmentId)) workspace.environmentId = '';
  validateWorkspace(workspace); return workspace;
}

export function exportWorkspace(workspace: Workspace, collection?: Collection, environment?: Environment): string {
  const result = clone(workspace);
  if (collection) { result.collections = [clone(collection)]; result.environments = []; result.environmentId = ''; }
  if (environment) { result.collections = []; result.environments = [clone(environment)]; result.environmentId = environment.id; }
  return JSON.stringify(result, null, JsonIndent);
}
export function exportPostman(collection: Collection): string {
  const auth = (a?: ApiRequest['auth']): any => {
    if (!a || a.kind === AuthKind.Inherit) return undefined;
    const fields = (values: Record<string, string>) => Object.entries(values).map(([key, value]) => ({ key, value, type: 'string' }));
    switch (a.kind) {
      case AuthKind.None: return { type: 'noauth' };
      case AuthKind.Basic: return { type: 'basic', basic: fields({ username: a.username, password: a.password }) };
      case AuthKind.Bearer: return { type: 'bearer', bearer: fields({ token: a.token }) };
      case AuthKind.ApiKey: return { type: 'apikey', apikey: fields({ key: a.key, value: a.value, in: a.location }) };
    }
  };
  const events = (s?: RequestScripts) => !s?.enabled ? [] : [{ listen: 'prerequest', script: { type: 'text/javascript', exec: s.before.split('\n') } }, { listen: 'test', script: { type: 'text/javascript', exec: s.after.split('\n') } }];
  const item = collection.requests.map(r => {
    const body: any = r.body.kind === BodyKind.None ? undefined : r.body.kind === BodyKind.Binary ? { mode: 'file', file: { src: r.body.file } } : [BodyKind.Form, BodyKind.Multipart].includes(r.body.kind) ? (() => { const mode = r.body.kind === BodyKind.Form ? 'urlencoded' : 'formdata'; return { mode, [mode]: r.body.fields.map(p => ({ key: p.name, value: p.value, disabled: !p.enabled, type: p.file ? 'file' : 'text', src: p.file, contentType: p.contentType })) }; })() : { mode: 'raw', raw: r.body.text, options: { raw: { language: r.body.kind === BodyKind.Json ? 'json' : 'text' } } };
    const ownHeaders = new Set(r.headers.map(h => h.name.toLowerCase()));
    const headers = [...(collection.headers || []).filter(h => !ownHeaders.has(h.name.toLowerCase())), ...r.headers];
    if (r.body.contentType && ![BodyKind.None, BodyKind.Multipart].includes(r.body.kind) && !headers.some(h => h.enabled && h.name.toLowerCase() === HttpHeader.ContentType)) headers.push(pair(HttpHeader.ContentType, r.body.contentType));
    return { name: r.name, event: events(r.scripts), request: { method: r.method, url: { raw: queryUrl(r.url, r.params), query: r.params.map(p => ({ key: p.name, value: p.value, disabled: !p.enabled })) }, auth: auth(r.auth), header: headers.map(h => ({ key: h.name, value: h.value, disabled: !h.enabled })), body } };
  });
  return JSON.stringify({ info: { name: collection.name, schema: PostmanCollectionSchema }, auth: auth(collection.auth), event: events(collection.scripts), item }, null, JsonIndent);
}
