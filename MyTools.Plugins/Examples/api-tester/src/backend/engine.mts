import http from 'node:http';
import https from 'node:https';
import {createReadStream} from 'node:fs';
import {stat, access} from 'node:fs/promises';
import {constants} from 'node:fs';
import {randomUUID} from 'node:crypto';
import {Readable, PassThrough} from 'node:stream';
import {pipeline} from 'node:stream/promises';
import {createGunzip, createInflate, createBrotliDecompress} from 'node:zlib';
import {isDeepStrictEqual} from 'node:util';
import path from 'node:path';
import {CookieJar} from 'tough-cookie';
import { runScript, ScriptFailure } from './scripts.mjs';
import {
    HttpMethod,
    WarningKind,
    JsonValueType,
    HttpHeader,
    ContentType,
    BodyKind,
    AuthKind,
    KeyLocation,
    AssertionKind,
    ExecutionState,
    TestState,
    ErrorKind,
    Limits,
    ScriptPhase,
    parseQuery,
    DefaultSuccessStatus,
    queryUrl,
    type ApiRequest,
    type Assertion,
    type Pair,
    type Settings,
    type RequestResult,
    type Failure,
    type AssertionResult
} from '../shared/model.js';

const Header = HttpHeader;
const Mime = ContentType;
const RedirectStatus = new Set([301, 302, 303, 307, 308]);
const SupportedMethods = new Set<string>(Object.values(HttpMethod));
const StatusSeeOther = 303;
const LegacyPostRedirects = new Set([301, 302]);
const ResponseCharsetPattern = /charset\s*=\s*["']?([^\s;"']+)/i;
const NoValue = Symbol('missing');
const MultipartBoundaryPrefix = 'mytools-api-';
const MinimumHttpStatus = 100;
const MaximumHttpStatus = 599;
const MaximumTimerMs = 2_147_483_647;
const JsonTypes = new Set<string>(Object.values(JsonValueType));

function validateTests(request: ApiRequest): void {
    for (const assertion of request.assertions.filter(item => item.enabled)) {
        if (!Object.values(AssertionKind).includes(assertion.kind)) throw new RequestError(ErrorKind.Configuration, 'assertions');
        if (assertion.kind === AssertionKind.Status && (!assertion.expected.trim() || !Number.isInteger(Number(assertion.expected)) || Number(assertion.expected) < MinimumHttpStatus || Number(assertion.expected) > MaximumHttpStatus)) throw new RequestError(ErrorKind.Configuration, 'assertions.expected');
        if (assertion.kind === AssertionKind.Time && (!assertion.expected.trim() || !Number.isFinite(Number(assertion.expected)) || Number(assertion.expected) < 0)) throw new RequestError(ErrorKind.Configuration, 'assertions.expected');
        if (assertion.kind === AssertionKind.Header && !assertion.path.trim()) throw new RequestError(ErrorKind.Configuration, 'assertions.path');
        if ([AssertionKind.Exists, AssertionKind.Value, AssertionKind.Type].includes(assertion.kind)) pointer({}, assertion.path);
        if (assertion.kind === AssertionKind.Value) { try { JSON.parse(assertion.expected); } catch { throw new RequestError(ErrorKind.Configuration, 'assertions.expected'); } }
        if (assertion.kind === AssertionKind.Type && !JsonTypes.has(assertion.expected)) throw new RequestError(ErrorKind.Configuration, 'assertions.expected');
    }
    for (const extraction of request.extractions.filter(item => item.enabled)) { pointer({}, extraction.path); if (!extraction.variable.trim()) throw new RequestError(ErrorKind.Configuration, 'extractions.variable'); }
}

export class RequestError extends Error {
    constructor(public kind: ErrorKind, public field = '', detail = '') {
        super(detail || kind);
    }
}

export function substitute(value: string, variables: Record<string, string>, field: string): string {
    return value.replace(/\{\{([^{}]+)\}\}/g, (_all, name: string) => {
        if (!Object.hasOwn(variables, name)) throw new RequestError(ErrorKind.Variable, field, name);
        return variables[name];
    });
}

export function pointer(value: unknown, location: string): unknown {
    if (!location) return value;
    if (!location.startsWith('/') || /~(?![01])/g.test(location)) throw new RequestError(ErrorKind.Configuration, 'path', location);
    for (const encoded of location.slice(1).split('/')) {
        const key = encoded.replace(/~1/g, '/').replace(/~0/g, '~');
        if (value === null || typeof value !== 'object' || !Object.hasOwn(value, key) || (Array.isArray(value) && !/^(0|[1-9]\d*)$/.test(key))) return NoValue;
        value = (value as Record<string, unknown>)[key];
    }
    return value;
}

function display(value: unknown): string {
    return value === NoValue ? '' : typeof value === 'string' ? value : JSON.stringify(value) ?? String(value);
}

export function evaluateAssertions(request: ApiRequest, response: {
    status: number;
    elapsedMs: number;
    headers: Pair[];
    text: string
}): AssertionResult[] {
    let json: unknown;
    let parsed = false;
    const enabled = request.assertions.filter(a => a.enabled);
    const assertions: Assertion[] = enabled.length
        ? enabled
        : [{
            name: '',
            enabled: true,
            kind: AssertionKind.Status,
            path: '',
            expected: String(DefaultSuccessStatus)
        }];
    return assertions.map(assertion => {
        let actual: unknown;
        let expected: unknown = assertion.expected;
        let passed = false;
        let detail: string | undefined;
        try {
            switch (assertion.kind) {
                case AssertionKind.Status:
                    actual = response.status;
                    expected = Number(assertion.expected);
                    passed = actual === expected;
                    break;
                case AssertionKind.Time:
                    actual = response.elapsedMs;
                    expected = Number(assertion.expected);
                    passed = response.elapsedMs < (expected as number);
                    break;
                case AssertionKind.Header:
                    actual = response.headers.filter(h => h.name.toLowerCase() === assertion.path.toLowerCase()).map(h => h.value);
                    passed = (actual as string[]).length > 0;
                    break;
                case AssertionKind.Text:
                    actual = response.text;
                    passed = response.text.includes(assertion.expected);
                    break;
                case AssertionKind.Exists:
                case AssertionKind.Value:
                case AssertionKind.Type:
                    if (!parsed) {
                        json = JSON.parse(response.text);
                        parsed = true;
                    }
                    actual = pointer(json, assertion.path);
                    if (assertion.kind === AssertionKind.Exists) passed = actual !== NoValue;
                    else if (assertion.kind === AssertionKind.Value) {
                        expected = JSON.parse(assertion.expected);
                        passed = actual !== NoValue && isDeepStrictEqual(actual, expected);
                    } else {
                        actual = actual === NoValue ? NoValue : actual === null ? JsonValueType.Null : Array.isArray(actual) ? JsonValueType.Array : typeof actual;
                        passed = actual === expected;
                    }
                    break;
                default:
                    throw new RequestError(ErrorKind.Configuration, 'assertions');
            }
        } catch (error) {
            detail = (error as Error).message;
        }
        return {
            name: assertion.name,
            passed,
            actual: display(actual).slice(0, Limits.diagnosticChars),
            expected: display(expected).slice(0, Limits.diagnosticChars),
            actualMissing: actual === NoValue,
            detail: detail?.slice(0, Limits.diagnosticChars)
        };
    });
}

export function extractVariables(request: ApiRequest, text: string): Record<string, string> {
    const active = request.extractions.filter(e => e.enabled);
    const writes: Record<string, string> = Object.create(null);
    if (!active.length) return writes;
    let json: unknown;
    try {
        json = JSON.parse(text);
    } catch {
        throw new RequestError(ErrorKind.Extraction, 'extractions', 'Invalid JSON');
    }
    for (const extraction of active) {
        const value = pointer(json, extraction.path);
        if (value === NoValue || !extraction.variable.trim()) throw new RequestError(ErrorKind.Extraction, extraction.path, extraction.variable);
        writes[extraction.variable] = typeof value === 'string' ? value : JSON.stringify(value);
    }
    return writes;
}

interface PreparedBody {
    length: number;
    contentType: string;
    stream: () => AsyncIterable<Buffer>
}

async function checkedFile(file: string): Promise<number> {
    try {
        await access(file, constants.R_OK);
        const info = await stat(file);
        if (!info.isFile()) throw new Error('Not a regular file');
        return info.size;
    } catch (error) {
        throw new RequestError(ErrorKind.File, file, (error as Error).message);
    }
}

function literalBody(text: string, contentType: string): PreparedBody {
    const bytes = Buffer.from(text);
    return {
        length: bytes.length, contentType, stream: async function* () {
            yield bytes;
        }
    };
}

function disposition(value: string): string {
    return value.replace(/[\r\n]/g, '').replace(/\\/g, '\\\\').replace(/"/g, '\\"');
}

async function prepareBody(request: ApiRequest, replace: (value: string, field: string) => string, signal: AbortSignal): Promise<PreparedBody | undefined> {
    const body = request.body;
    switch (body.kind) {
        case BodyKind.None:
            return undefined;
        case BodyKind.Json: {
            const text = replace(body.text, 'body');
            try {
                JSON.parse(text);
            } catch (error) {
                throw new RequestError(ErrorKind.Configuration, 'body', (error as Error).message);
            }
            return literalBody(text, body.contentType || Mime.Json);
        }
        case BodyKind.Text:
            return literalBody(replace(body.text, 'body'), body.contentType || Mime.Text);
        case BodyKind.Form:
            return literalBody(body.fields.filter(f => f.enabled).map(f => encodeURIComponent(replace(f.name, 'body')) + '=' + encodeURIComponent(replace(f.value, 'body'))).join('&'), Mime.Form);
        case BodyKind.Binary: {
            const length = await checkedFile(body.file);
            return {
                length,
                contentType: body.contentType || Mime.Binary,
                stream: () => createReadStream(body.file, {signal})
            };
        }
        case BodyKind.Multipart: {
            const boundary = MultipartBoundaryPrefix + randomUUID();
            const parts: { head: Buffer; value?: Buffer; file?: string; length: number }[] = [];
            for (const field of body.fields.filter(f => f.enabled)) {
                const file = field.file;
                const value = file ? undefined : Buffer.from(replace(field.value, 'body'));
                const length = file ? await checkedFile(file) : value!.length;
                const type = field.contentType || (file ? Mime.Binary : Mime.Text);
                if (/[\r\n]/.test(type)) throw new RequestError(ErrorKind.Configuration, 'body.contentType');
                parts.push({
                    head: Buffer.from(`--${boundary}\r\nContent-Disposition: form-data; name="${disposition(replace(field.name, 'body'))}"${file ? `; filename="${disposition(path.basename(file))}"` : ''}\r\nContent-Type: ${type}\r\n\r\n`),
                    value,
                    file,
                    length
                });
            }
            const separator = Buffer.from('\r\n');
            const end = Buffer.from(`--${boundary}--\r\n`);
            return {
                contentType: `${Mime.Multipart}; boundary=${boundary}`,
                length: parts.reduce((sum, p) => sum + p.head.length + p.length + separator.length, end.length),
                stream: async function* () {
                    for (const part of parts) {
                        signal.throwIfAborted();
                        yield part.head;
                        if (part.file) {
                            for await (const chunk of createReadStream(part.file, {signal})) yield chunk as Buffer;
                        } else yield part.value!;
                        yield separator;
                    }
                    yield end;
                }
            };
        }
        default:
            throw new RequestError(ErrorKind.Configuration, 'body');
    }
}

function failure(error: unknown, signal: AbortSignal, timedOut: boolean): Failure {
    if (timedOut) return {kind: ErrorKind.Timeout};
    if (signal.aborted) return {kind: ErrorKind.Cancelled};
    if (error instanceof RequestError) return {kind: error.kind, field: error.field, detail: error.message};
    const code = (error as NodeJS.ErrnoException).code || '';
    return {
        kind: ['ENOTFOUND', 'EAI_AGAIN'].includes(code) ? ErrorKind.Dns : /CERT|TLS|SSL|SELF_SIGNED/.test(code) ? ErrorKind.Tls : ErrorKind.Connection,
        detail: (error as Error).message
    };
}

function send(url: URL, method: string, headers: Pair[], body: PreparedBody | undefined, settings: Settings, signal: AbortSignal): Promise<http.IncomingMessage> {
    return new Promise((resolve, reject) => {
        const client = url.protocol === 'https:' ? https : http;
        const outgoing = client.request(url, {
            method,
            headers: headers.flatMap(h => [h.name, h.value]),
            rejectUnauthorized: settings.verifyTls,
            signal,
            agent: false
        }, resolve);
        outgoing.on('error', reject);
        if (body) pipeline(Readable.from(body.stream()), outgoing, {signal}).catch(reject); else outgoing.end();
    });
}

export function skipped(request: ApiRequest): RequestResult {
    return {
        requestId: request.id,
        name: request.name,
        execution: ExecutionState.Skipped,
        test: TestState.NotApplicable,
        elapsedMs: 0,
        size: 0,
        completedAt: '',
        headers: [],
        requestHeaders: [],
        preview: '',
        binary: false,
        truncated: false,
        bodyAvailable: false,
        verifyTls: true,
        assertions: [],
        warnings: []
    };
}

export async function executeRequest(request: ApiRequest, settings: Settings, variables: Record<string, string>, jar: CookieJar, outerSignal: AbortSignal): Promise<{
    result: RequestResult;
    body?: Buffer;
    writes: Record<string, string>; unsets?: string[]
}> {
    const result = skipped(request);
    result.execution = ExecutionState.Failed;
    result.test = TestState.Failed;
    result.verifyTls = settings.verifyTls;
    const controller = new AbortController();
    const signal = controller.signal;
    const cancel = () => controller.abort();
    outerSignal.addEventListener('abort', cancel, {once: true});
    if (outerSignal.aborted) cancel();
    let timedOut = false;
    let timer: ReturnType<typeof setTimeout> | undefined;
    let started = 0;
    const writes: Record<string, string> = Object.create(null);
    const initialVariables = { ...variables };
    variables = { ...variables };
    request = structuredClone(request);
    result.scriptLogs = [];
    try {
        signal.throwIfAborted();
        if (request.scripts?.enabled && request.scripts.before.trim()) {
            try {
                const output = await runScript(request.scripts.before, ScriptPhase.Before, request, variables, signal);
                if (output.request.url !== request.url) output.request.params = parseQuery(output.request.url);
                request = output.request; variables = output.variables;
                result.scriptLogs.push(...output.logs); result.assertions.push(...output.tests);
            } catch (error) {
                if (error instanceof ScriptFailure && error.output) { result.scriptLogs.push(...error.output.logs); result.assertions.push(...error.output.tests); }
                throw new RequestError(ErrorKind.Script, 'before', (error as Error).message);
            }
        }
        result.sentRequest = structuredClone(request);
        if (!SupportedMethods.has(request.method) || !Number.isFinite(settings.timeoutMs) || settings.timeoutMs <= 0 || settings.timeoutMs > MaximumTimerMs) throw new RequestError(ErrorKind.Configuration, 'settings');
        validateTests(request);
        const replace = (value: string, field: string) => substitute(value, variables, field);
        const params = request.params.map(p => p.enabled ? {
            ...p,
            name: replace(p.name, 'params'),
            value: replace(p.value, 'params')
        } : p);
        let address: URL;
        try {
            address = new URL(queryUrl(replace(request.url, 'url'), params));
        } catch (error) {
            if (error instanceof RequestError) throw error;
            throw new RequestError(ErrorKind.Configuration, 'url');
        }
        if (!['http:', 'https:'].includes(address.protocol)) throw new RequestError(ErrorKind.Configuration, 'url');
        address.hash = '';
        let headers = request.headers.filter(h => h.enabled).map(h => ({
            ...h,
            name: replace(h.name, 'headers'),
            value: replace(h.value, 'headers')
        }));
        const auth = request.auth;
        const authenticationHeaders = new Set<string>();
        const addAuth = (name: string, value: string, location: KeyLocation) => {
            if (!name || (location === KeyLocation.Header ? headers.some(h => h.name.toLowerCase() === name.toLowerCase()) : params.some(p => p.enabled && p.name === name))) throw new RequestError(ErrorKind.Configuration, 'auth', 'Conflicting authentication');
            if (location === KeyLocation.Header) {
                authenticationHeaders.add(name.toLowerCase());
                headers.push({name, value, enabled: true});
            } else {
                params.push({name, value, enabled: true});
                address = new URL(queryUrl(address.href, params));
            }
        };
        switch (auth.kind) {
            case AuthKind.None:
                break;
            case AuthKind.Basic:
                addAuth(Header.Authorization, 'Basic ' + Buffer.from(replace(auth.username, 'auth') + ':' + replace(auth.password, 'auth')).toString('base64'), KeyLocation.Header);
                break;
            case AuthKind.Bearer:
                addAuth(Header.Authorization, 'Bearer ' + replace(auth.token, 'auth'), KeyLocation.Header);
                break;
            case AuthKind.ApiKey:
                addAuth(replace(auth.key, 'auth'), replace(auth.value, 'auth'), auth.location);
                break;
            default:
                throw new RequestError(ErrorKind.Configuration, 'auth');
        }
        if (headers.some(h => h.name.toLowerCase() === Header.ContentLength || (request.body.kind === BodyKind.Multipart && h.name.toLowerCase() === Header.ContentType))) throw new RequestError(ErrorKind.Configuration, 'headers', 'Managed header');
        let body = await prepareBody(request, replace, signal);
        let method = request.method;
        if (body) {
            const configured = headers.find(h => h.name.toLowerCase() === Header.ContentType);
            if (!configured) headers.push({name: Header.ContentType, value: body.contentType, enabled: true});
            else if (configured.value.split(';')[0] !== body.contentType.split(';')[0]) result.warnings.push(WarningKind.ContentType);
            headers.push({name: Header.ContentLength, value: String(body.length), enabled: true});
        }
        started = performance.now();
        const sentRequest = structuredClone(request);
        sentRequest.url = address.href; sentRequest.params = params;
        sentRequest.headers = headers.filter(h => h.name.toLowerCase() !== Header.ContentLength);
        sentRequest.auth.kind = AuthKind.None;
        if ([BodyKind.Json, BodyKind.Text].includes(sentRequest.body.kind)) sentRequest.body.text = replace(sentRequest.body.text, 'body');
        if (sentRequest.body.kind === BodyKind.Binary) sentRequest.body.file = replace(sentRequest.body.file, 'body');
        if ([BodyKind.Form, BodyKind.Multipart].includes(sentRequest.body.kind)) sentRequest.body.fields = sentRequest.body.fields.map(p => p.enabled ? { ...p, name: replace(p.name, 'body'), value: p.file ? p.value : replace(p.value, 'body'), file: p.file ? replace(p.file, 'body') : undefined } : p);
        if (sentRequest.scripts) sentRequest.scripts.enabled = false;
        result.sentRequest = sentRequest;
        try { for (const header of headers) { http.validateHeaderName(header.name); http.validateHeaderValue(header.name, header.value); } } catch (error) { throw new RequestError(ErrorKind.Configuration, 'headers', (error as Error).message); }
        timer = setTimeout(() => {
            timedOut = true;
            controller.abort();
        }, settings.timeoutMs);
        let response: http.IncomingMessage;
        let redirectCount = 0;
        while (true) {
            signal.throwIfAborted();
            const currentHeaders = [...headers];
            if (!currentHeaders.some(h => h.name.toLowerCase() === Header.Host)) currentHeaders.push({name: Header.Host, value: address.host, enabled: true});
            if (!currentHeaders.some(h => h.name.toLowerCase() === Header.Connection)) currentHeaders.push({name: Header.Connection, value: 'close', enabled: true});
            if (!body && [HttpMethod.Post, HttpMethod.Put, HttpMethod.Patch].includes(method as HttpMethod)) currentHeaders.push({ name: Header.ContentLength, value: '0', enabled: true });
            const cookie = await jar.getCookieString(address.href);
            if (cookie && !currentHeaders.some(h => h.name.toLowerCase() === Header.Cookie)) currentHeaders.push({
                name: Header.Cookie,
                value: cookie,
                enabled: true
            });
            result.requestHeaders = currentHeaders;
            result.url = address.href;
            response = await send(address, method, currentHeaders, body, settings, signal);
            for (const value of response.headers[Header.SetCookie] || []) await jar.setCookie(value, address.href, {ignoreError: true});
            if (!settings.followRedirects || !RedirectStatus.has(response.statusCode!) || !response.headers.location) break;
            response.destroy();
            if (redirectCount++ >= Limits.redirects) throw new RequestError(ErrorKind.RedirectLimit);
            const next = new URL(response.headers[Header.Location]!, address);
            if (!['http:', 'https:'].includes(next.protocol)) throw new RequestError(ErrorKind.Configuration, 'url');
            next.hash = '';
            if (next.origin !== address.origin) headers = headers.filter(h =>
                !authenticationHeaders.has(h.name.toLowerCase()) &&
                ![Header.Authorization, Header.Cookie, Header.Host].includes(h.name.toLowerCase() as typeof Header.Authorization));
            if (response.statusCode === StatusSeeOther && method !== HttpMethod.Head || LegacyPostRedirects.has(response.statusCode!) && method === HttpMethod.Post) {
                method = HttpMethod.Get;
                body = undefined;
                headers = headers.filter(h => ![Header.ContentType, Header.ContentLength].includes(h.name.toLowerCase() as typeof Header.ContentType));
            }
            address = next;
        }
        result.status = response.statusCode;
        result.statusText = response.statusMessage;
        for (let index = 0; index < response.rawHeaders.length; index += 2) result.headers.push({
            name: response.rawHeaders[index],
            value: response.rawHeaders[index + 1],
            enabled: true
        });
        let input: Readable = response;
        const encoding = response.headers[Header.Encoding];
        const decoder = method === HttpMethod.Head ? undefined : encoding === 'gzip' ? createGunzip() : encoding === 'deflate' ? createInflate() : encoding === 'br' ? createBrotliDecompress() : undefined;
        if (decoder) {
            const output = new PassThrough();
            pipeline(response, decoder, output, {signal}).catch(error => output.destroy(error));
            input = output;
        }
        const chunks: Buffer[] = [];
        try {
            for await (const chunk of input) {
                signal.throwIfAborted();
                const bytes = Buffer.from(chunk);
                result.size += bytes.length;
                if (result.size > Limits.bodyBytes) {
                    response.destroy();
                    input.destroy();
                    throw new RequestError(ErrorKind.BodyLimit);
                }
                chunks.push(bytes);
            }
        } catch (error) {
            response.destroy();
            throw error;
        }
        result.elapsedMs = performance.now() - started;
        clearTimeout(timer);
        signal.throwIfAborted();
        const bytes = Buffer.concat(chunks);
        const contentType = response.headers[Header.ContentType] || '';
        result.binary = !/^(text\/|application\/(json|.*\+json|xml|.*\+xml|javascript|x-www-form-urlencoded))/i.test(contentType) && bytes.includes(0);
        if (/^(image|audio|video)\//i.test(contentType) || contentType.startsWith(Mime.Binary)) result.binary = true;
        let text = '';
        if (!result.binary) {
            try {
                const charset = ResponseCharsetPattern.exec(contentType)?.[1] || 'utf-8';
                text = new TextDecoder(charset, {fatal: true}).decode(bytes);
            } catch (error) {
                result.error = {kind: ErrorKind.Decode, detail: (error as Error).message};
                text = bytes.toString('utf8');
            }
        }
        result.execution = ExecutionState.Complete;
        result.bodyAvailable = true;
        result.preview = Buffer.from(text).subarray(0, Limits.previewBytes).toString('utf8');
        result.previewAvailable = true;
        result.truncated = bytes.length > Limits.previewBytes;
        result.assertions.push(...evaluateAssertions(request, {
            status: result.status!,
            elapsedMs: result.elapsedMs,
            headers: result.headers,
            text
        }));
        try {
            Object.assign(writes, extractVariables(request, text));
        } catch (error) {
            result.error = failure(error, signal, false);
        }
        if (request.scripts?.enabled && request.scripts.after.trim()) {
            try {
                const output = await runScript(request.scripts.after, ScriptPhase.After, request, { ...variables, ...writes }, signal, result, text);
                variables = output.variables; result.scriptLogs.push(...output.logs); result.assertions.push(...output.tests);
                // Post-response unset takes precedence over extraction writes.
                for (const key of Object.keys(writes)) if (!Object.hasOwn(variables, key)) delete writes[key];
            } catch (error) {
                if (error instanceof ScriptFailure && error.output) { result.scriptLogs.push(...error.output.logs); result.assertions.push(...error.output.tests); }
                result.error = failure(new RequestError(ErrorKind.Script, 'after', (error as Error).message), signal, false);
            }
        }
        for (const [key, value] of Object.entries(variables)) if (initialVariables[key] !== value) writes[key] = value;
        result.test = result.error || result.assertions.some(a => !a.passed) ? TestState.Failed : result.assertions.length ? TestState.Passed : TestState.Untested;
        result.completedAt = new Date().toISOString();
        return {result, body: bytes, writes: result.error ? Object.create(null) : writes, unsets: result.error ? [] : Object.keys(initialVariables).filter(key => !Object.hasOwn(variables, key))};
    } catch (error) {
        result.error = failure(error, signal, timedOut);
        result.execution = result.error.kind === ErrorKind.Cancelled ? ExecutionState.Cancelled : ExecutionState.Failed;
        result.test = result.execution === ExecutionState.Cancelled ? TestState.NotApplicable : TestState.Failed;
        result.elapsedMs = started ? performance.now() - started : 0;
        result.completedAt = new Date().toISOString();
        return {result, writes};
    } finally {
        clearTimeout(timer);
        outerSignal.removeEventListener('abort', cancel);
    }
}



