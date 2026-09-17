export const PluginId = 'api-tester';

export enum HttpMethod {
    Get = 'GET',
    Post = 'POST',
    Put = 'PUT',
    Patch = 'PATCH',
    Delete = 'DELETE',
    Head = 'HEAD',
    Options = 'OPTIONS'
}

export enum JsonValueType {
    String = 'string',
    Number = 'number',
    Boolean = 'boolean',
    Array = 'array',
    Object = 'object',
    Null = 'null'
}

export enum WarningKind { ContentType = 'contentType' }

export const HttpHeader = {
    Authorization: 'authorization',
    ContentType: 'content-type',
    ContentLength: 'content-length',
    Cookie: 'cookie',
    SetCookie: 'set-cookie',
    Location: 'location',
    Encoding: 'content-encoding',
    Host: 'host',
    Connection: 'connection',
    UserAgent: 'user-agent',
    Referer: 'referer'
} as const;
export const ContentType = {
    Json: 'application/json',
    Text: 'text/plain; charset=utf-8',
    Form: 'application/x-www-form-urlencoded',
    Binary: 'application/octet-stream',
    Multipart: 'multipart/form-data'
} as const;
export const Routes = {
    load: 'loadWorkspace',
    save: 'saveWorkspace',
    start: 'startRun',
    poll: 'pollRun',
    cancel: 'cancelRun',
    release: 'releaseRun',
    environment: 'switchEnvironment',
    cookies: 'clearCookies',
    listCookies: 'listCookies',
    file: 'pickFile',
    download: 'downloadResponse',
    history: 'listHistory',
    clearHistory: 'clearHistory',
    curl: 'exportCurl'
} as const;
export const CookieSessionExpiry = 'Infinity';
export const CookieDefaultPath = '/';
export const Limits = {
    timeoutMs: 30_000,
    redirects: 10,
    previewBytes: 1024 * 1024,
    bodyBytes: 20 * 1024 * 1024,
    cachedBodyBytes: 40 * 1024 * 1024,
    cachedPreviewBytes: 8 * 1024 * 1024,
    diagnosticChars: 2048,
    batchRequests: 1000,
    batchDelayMs: 24 * 60 * 60 * 1000,
    cookieEnvironments: 16,
    retainedRuns: 8,
    pollMs: 200,
    schemaVersion: 1,
    historyEntries: 100,
    historyPreviewBytes: 16 * 1024,
    importBytes: 8 * 1024 * 1024,
    scriptTimeoutMs: 2000,
    scriptMemoryMb: 64,
    scriptOutputBytes: 2 * 1024 * 1024,
    scriptLogEntries: 100,
    scriptLogChars: 2048,
    scriptTests: 100,
    jsonTreeNodes: 2000
} as const;

export enum BodyKind {
    None = 'none',
    Json = 'json',
    Text = 'text',
    Form = 'form',
    Multipart = 'multipart',
    Binary = 'binary'
}

export enum AuthKind { None = 'none', Inherit = 'inherit', Basic = 'basic', Bearer = 'bearer', ApiKey = 'apiKey' }

export enum ScriptPhase { Before = 'before', After = 'after' }

export enum ScriptLogLevel { Log = 'log', Info = 'info', Warn = 'warn', Error = 'error', Debug = 'debug' }

export interface RequestScripts {
    enabled: boolean;
    before: string;
    after: string
}

export interface ScriptLog {
    phase: ScriptPhase;
    level: ScriptLogLevel;
    text: string
}

export enum KeyLocation { Header = 'header', Query = 'query' }

export enum ExecutionState { Complete = 'complete', Failed = 'failed', Cancelled = 'cancelled', Skipped = 'skipped' }

export enum TestState { Passed = 'passed', Failed = 'failed', Untested = 'untested', NotApplicable = 'notApplicable' }

export enum ErrorKind {
    Configuration = 'configuration',
    Variable = 'variable',
    File = 'file',
    Dns = 'dns',
    Connection = 'connection',
    Tls = 'tls',
    Timeout = 'timeout',
    Cancelled = 'cancelled',
    Decode = 'decode',
    BodyLimit = 'bodyLimit',
    RedirectLimit = 'redirectLimit',
    Extraction = 'extraction',
    Storage = 'storage',
    Cache = 'cache',
    Script = 'script'
}

export interface Pair {
    name: string;
    value: string;
    enabled: boolean;
    noEquals?: boolean;
    file?: string;
    contentType?: string;
    size?: number;
    rawQuery?: { encoded: string; name: string; value: string; noEquals: boolean }
}

export interface Settings {
    timeoutMs: number;
    followRedirects: boolean;
    verifyTls: boolean
}

export interface ApiRequest {
    id: string;
    name: string;
    method: string;
    url: string;
    params: Pair[];
    headers: Pair[];
    auth: {
        kind: AuthKind;
        username: string;
        password: string;
        token: string;
        key: string;
        value: string;
        location: KeyLocation
    };
    body: { kind: BodyKind; text: string; contentType: string; fields: Pair[]; file: string; fileSize?: number };
    settings?: Settings;
    scripts?: RequestScripts
}

export interface Collection {
    id: string;
    name: string;
    requests: ApiRequest[];
    auth?: ApiRequest['auth'];
    headers?: Pair[];
    scripts?: RequestScripts
    settings?: Settings
}

export interface Environment {
    id: string;
    name: string;
    variables: Pair[]
}

export interface Workspace {
    version: number;
    collections: Collection[];
    environments: Environment[];
    environmentId: string;
    defaults: Settings
}

export interface Failure {
    kind: ErrorKind;
    field?: string;
    detail?: string
}

export interface AssertionResult {
    name: string;
    passed: boolean;
    actual: string;
    expected: string;
    actualMissing?: boolean;
    detail?: string
}

export interface RequestResult {
    requestId: string;
    name: string;
    execution: ExecutionState;
    test: TestState;
    status?: number;
    statusText?: string;
    url?: string;
    elapsedMs: number;
    size: number;
    completedAt: string;
    headers: Pair[];
    requestHeaders: Pair[];
    preview: string;
    previewAvailable?: boolean;
    binary: boolean;
    truncated: boolean;
    bodyAvailable: boolean;
    verifyTls: boolean;
    assertions: AssertionResult[];
    error?: Failure;
    warnings: WarningKind[];
    scriptLogs?: ScriptLog[];
    sentRequest?: ApiRequest
    iteration?: number
}

export interface HistoryEntry {
    id: string;
    request: ApiRequest;
    result: RequestResult;
    environmentId: string;
    environmentName: string;
    variables: Pair[];
}

export function effectiveRequest(request: ApiRequest, collection?: Collection): ApiRequest {
    const result: ApiRequest = JSON.parse(JSON.stringify(request));
    if (result.auth.kind === AuthKind.Inherit) result.auth = JSON.parse(JSON.stringify(collection?.auth || newRequest('', '').auth));
    const overridden = new Set(result.headers.map(header => header.name.toLowerCase()));
    result.headers = [...(collection?.headers || []).filter(header => !overridden.has(header.name.toLowerCase())), ...result.headers];
    return result;
}

export interface RunView {
    id: string;
    done: boolean;
    current: string;
    total: number;
    elapsedMs: number;
    results: RequestResult[];
    evictedBodies?: number[];
    evictedPreviews?: number[];
}

export interface CookieRecord {
    name: string;
    value: string;
    domain: string;
    path: string;
    expires: string;
    httpOnly: boolean;
    secure: boolean;
    hostOnly: boolean;
}

export const defaultSettings = (): Settings => ({timeoutMs: Limits.timeoutMs, followRedirects: true, verifyTls: true});
export const emptyWorkspace = (): Workspace => ({
    version: Limits.schemaVersion,
    collections: [],
    environments: [],
    environmentId: '',
    defaults: defaultSettings()
});
export const newRequest = (id: string, name: string): ApiRequest => ({
    id,
    name,
    method: HttpMethod.Get,
    url: '',
    params: [],
    headers: [],
    auth: {
        kind: AuthKind.None,
        username: '',
        password: '',
        token: '',
        key: '',
        value: '',
        location: KeyLocation.Header
    },
    body: {kind: BodyKind.None, text: '', contentType: '', fields: [], file: ''}
});

export function parseQuery(url: string): Pair[] {
    const query = url.split('#')[0].split('?').slice(1).join('?');
    return query ? query.split('&').map(part => {
        const separator = part.indexOf('=');
        const name = decodeURIComponent((separator < 0 ? part : part.slice(0, separator)).replace(/\+/g, ' '));
        const value = decodeURIComponent((separator < 0 ? '' : part.slice(separator + 1)).replace(/\+/g, ' '));
        return {
            name,
            value,
            enabled: true,
            noEquals: separator < 0,
            rawQuery: {encoded: part, name, value, noEquals: separator < 0}
        };
    }) : [];
}

export function queryUrl(url: string, params: Pair[]): string {
    const [beforeHash, ...hash] = url.split('#');
    const base = beforeHash.split('?')[0];
    const query = params.filter(p => p.enabled).map(p => {
        const raw = p.rawQuery;
        return raw && raw.name === p.name && raw.value === p.value && raw.noEquals === Boolean(p.noEquals)
            ? raw.encoded
            : encodeURIComponent(p.name) + (p.noEquals ? '' : '=' + encodeURIComponent(p.value));
    }).join('&');
    return base + (query ? '?' + query : '') + (hash.length ? '#' + hash.join('#') : '');
}

