export const PluginId = 'api-tester';
export enum HttpMethod { Get = 'GET', Post = 'POST', Put = 'PUT', Patch = 'PATCH', Delete = 'DELETE', Head = 'HEAD', Options = 'OPTIONS' }
export enum JsonValueType { String = 'string', Number = 'number', Boolean = 'boolean', Array = 'array', Object = 'object', Null = 'null' }
export enum WarningKind { ContentType = 'contentType' }
export const HttpHeader = { Authorization: 'authorization', ContentType: 'content-type', ContentLength: 'content-length', Cookie: 'cookie', SetCookie: 'set-cookie', Location: 'location', Encoding: 'content-encoding', Host: 'host', Connection: 'connection' } as const;
export const ContentType = { Json: 'application/json', Text: 'text/plain; charset=utf-8', Form: 'application/x-www-form-urlencoded', Binary: 'application/octet-stream', Multipart: 'multipart/form-data' } as const;
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
    download: 'downloadResponse'
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
    cookieEnvironments: 16,
    retainedRuns: 8,
    openTabs: 12,
    pollMs: 200,
    schemaVersion: 1
} as const;

export enum BodyKind {
    None = 'none',
    Json = 'json',
    Text = 'text',
    Form = 'form',
    Multipart = 'multipart',
    Binary = 'binary'
}

export enum AuthKind { None = 'none', Basic = 'basic', Bearer = 'bearer', ApiKey = 'apiKey' }

export enum KeyLocation { Header = 'header', Query = 'query' }

export enum AssertionKind {
    Status = 'status',
    Time = 'time',
    Header = 'header',
    Text = 'text',
    Exists = 'exists',
    Value = 'value',
    Type = 'type'
}

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
    Cache = 'cache'
}

export interface Pair {
    name: string;
    value: string;
    enabled: boolean;
    noEquals?: boolean;
    file?: string;
    contentType?: string;
    size?: number
}

export interface Settings {
    timeoutMs: number;
    followRedirects: boolean;
    verifyTls: boolean
}

export interface Assertion {
    name: string;
    enabled: boolean;
    kind: AssertionKind;
    path: string;
    expected: string
}

export interface Extraction {
    enabled: boolean;
    path: string;
    variable: string
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
    assertions: Assertion[];
    extractions: Extraction[]
}

export interface Collection {
    id: string;
    name: string;
    requests: ApiRequest[]
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
    warnings: WarningKind[]
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
    body: {kind: BodyKind.None, text: '', contentType: '', fields: [], file: ''},
    assertions: [],
    extractions: []
});

export function parseQuery(url: string): Pair[] {
    const query = url.split('#')[0].split('?').slice(1).join('?');
    return query ? query.split('&').map(part => {
        const separator = part.indexOf('=');
        return {
            name: decodeURIComponent(separator < 0 ? part : part.slice(0, separator)),
            value: decodeURIComponent(separator < 0 ? '' : part.slice(separator + 1)),
            enabled: true,
            noEquals: separator < 0
        };
    }) : [];
}

export function queryUrl(url: string, params: Pair[]): string {
    const [beforeHash, ...hash] = url.split('#');
    const base = beforeHash.split('?')[0];
    const query = params.filter(p => p.enabled).map(p => encodeURIComponent(p.name) + (p.noEquals ? '' : '=' + encodeURIComponent(p.value))).join('&');
    return base + (query ? '?' + query : '') + (hash.length ? '#' + hash.join('#') : '');
}

