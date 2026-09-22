import {test} from 'node:test';
import './features.test.mjs';
import assert from 'node:assert/strict';
import http from 'node:http';
import {once} from 'node:events';
import {mkdtemp, writeFile, rm} from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {gzipSync} from 'node:zlib';
import {CookieJar} from 'tough-cookie';
import {executeRequest} from '../src/backend/execution/engine.mjs';
import {Runner} from '../src/backend/execution/runner.mjs';
import {WorkspaceStore} from '../src/backend/persistence/storage.mjs';
import {createWorkspaceMutator} from '../src/web/features/workspace/workspacePersistence.js';
import {
    newRequest,
    urlForSending,
    defaultSettings,
    emptyWorkspace,
    parseQuery,
    queryUrl,
    BodyKind,
    HttpMethod,
    ContentType,
    WarningKind,
    AuthKind,
    KeyLocation,
    ExecutionState,
    TestState,
    ErrorKind,
    Limits,
    CookieDefaultPath,
    type ApiRequest,
    type RunView
} from '../src/shared/model.js';

const PollIntervalMs = 5;
const ShortTimeoutMs = 40;
const ServerDelayMs = 150;
const TestDeadlineMs = 5_000;
const VendorJsonType = 'application/vnd.api+json';
const SuccessStatus = 200;
const ErrorStatus = 500;
const RedirectStatus = 302;

async function cleanFixture(directory: string): Promise<void> {
    const resolved = path.resolve(directory);
    assert.ok(resolved.startsWith(path.resolve(os.tmpdir()) + path.sep) && path.basename(resolved).startsWith('api-tester-'));
    await rm(resolved, {recursive: true, force: true});
}

const fixture = async (handler: http.RequestListener) => {
    const server = http.createServer(handler);
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    const url = `http://127.0.0.1:${(server.address() as import('node:net').AddressInfo).port}`;
    return {
        url, close: async () => {
            server.closeAllConnections();
            await new Promise<void>(resolve => server.close(() => resolve()));
        }
    };
};
const requestAt = (url: string) => {
    const request = newRequest('test', 'Test');
    request.url = url;
    return request;
};
const send = (request: ApiRequest, jar = new CookieJar(), signal = new AbortController().signal) => executeRequest(request, request.settings || defaultSettings(), {}, jar, signal);

test('send-time URL normalization trims edges and defaults missing protocols to HTTPS', () => {
    assert.equal(urlForSending(' \r\n example.com/users?active=true \t'), 'https://example.com/users?active=true');
    assert.equal(urlForSending(' http://example.com/users '), 'http://example.com/users');
    assert.equal(urlForSending('\nHTTPS://example.com/users\r'), 'HTTPS://example.com/users');
    assert.equal(urlForSending('localhost:3000/users'), 'https://localhost:3000/users');
    assert.equal(urlForSending('//example.com/users'), 'https://example.com/users');
    assert.equal(urlForSending(' mailto:test@example.com '), 'mailto:test@example.com');
    assert.equal(urlForSending(' \n '), '');
});

test('sending trims the effective URL without changing the request URL', async () => {
    let receivedUrl = '';
    const server = await fixture((request, response) => {
        receivedUrl = request.url || '';
        response.end('ok');
    });
    const request = requestAt(` \r\n${server.url}/trimmed?active=true \t`);
    const originalUrl = request.url;
    request.params = parseQuery(urlForSending(originalUrl));
    try {
        const output = await send(request);
        assert.equal(receivedUrl, '/trimmed?active=true');
        assert.equal(request.url, originalUrl);
        assert.equal(output.result.sentRequest?.url, `${server.url}/trimmed?active=true`);
    } finally {
        await server.close();
    }
});

test('binary responses retain a base64 preview for hex and base64 views', async () => {
    const bytes = Buffer.from([0x89, 0x50, 0x4e, 0x47, 0, 0xff]);
    const server = await fixture((_request, response) => {
        response.setHeader('Content-Type', 'image/png');
        response.end(bytes);
    });
    try {
        const output = await send(requestAt(server.url));
        assert.equal(output.result.binary, true);
        assert.equal(output.result.preview, bytes.toString('base64'));
        assert.equal(output.result.previewAvailable, true);
        assert.equal(output.result.truncated, false);
    } finally {
        await server.close();
    }
});

async function completed(runner: Runner, id: string): Promise<RunView> {
    const deadline = Date.now() + TestDeadlineMs;
    while (Date.now() < deadline) {
        const view = runner.poll(id);
        if (view.done) return view;
        await new Promise(resolve => setTimeout(resolve, PollIntervalMs));
    }
    throw new Error('Run did not complete');
}

test('query editing preserves raw encoding, duplicate order and absent equals signs', () => {
    const raw = 'http://localhost/path?x=%2F&x=%252F&plus=+&flag&empty=';
    const params = parseQuery(raw);
    assert.equal(params[0].value, '/');
    assert.equal(params[1].value, '%2F');
    assert.equal(params[2].value, ' ');
    assert.equal(queryUrl(raw, params), raw);
    params[2].value = '+';
    assert.equal(queryUrl(raw, params), 'http://localhost/path?x=%2F&x=%252F&plus=%2B&flag&empty=');
});

test('query encoding survives sending and only edited or substituted values are re-encoded', async () => {
    const server = await fixture((req, res) => res.end(req.url));
    try {
        const request = requestAt(server.url + '/?q=a+b&encoded=%2f&literal=%2B&token={{token}}&flag');
        request.params = parseQuery(request.url);
        const output = await executeRequest(request, defaultSettings(), {token: 'a+b'}, new CookieJar(), new AbortController().signal);
        assert.equal(output.result.preview, '/?q=a+b&encoded=%2f&literal=%2B&token=a%2Bb&flag');
    } finally {
        await server.close();
    }
});

test('API-key authentication stays on same-origin redirects and is removed across origins', async () => {
    const ApiKeyHeader = 'x-api-key';
    const destination = await fixture((req, res) => res.end(JSON.stringify(req.headers)));
    const source = await fixture((req, res) => {
        if (req.url === '/same') {
            res.writeHead(RedirectStatus, {Location: '/final'});
            res.end();
        } else if (req.url === '/cross') {
            res.writeHead(RedirectStatus, {Location: destination.url});
            res.end();
        } else res.end(JSON.stringify(req.headers));
    });
    try {
        const request = requestAt(source.url + '/same');
        request.auth.kind = AuthKind.ApiKey;
        request.auth.key = ApiKeyHeader;
        request.auth.value = 'secret';
        request.headers = [{name: 'Host', value: 'original.example', enabled: true}];
        const same = JSON.parse((await send(request)).result.preview);
        assert.equal(same[ApiKeyHeader], 'secret');
        assert.equal(same.host, 'original.example');
        request.url = source.url + '/cross';
        const cross = JSON.parse((await send(request)).result.preview);
        assert.equal(cross[ApiKeyHeader], undefined);
        assert.equal(cross.host, new URL(destination.url).host);
    } finally {
        await source.close();
        await destination.close();
    }
});

test('workspace mutations serialize and a failed save cannot roll back the following save', async () => {
    const state = {value: emptyWorkspace()};
    const owner = {id: 'collection', name: 'Original', requests: []};
    state.value.collections.push(owner);
    let rejectFirst!: (error: Error) => void;
    const firstSave = new Promise<void>((_resolve, reject) => {
        rejectFirst = reject;
    });
    const snapshots: import('../src/shared/model.js').Workspace[] = [];
    const mutate = createWorkspaceMutator(state, async workspace => {
        snapshots.push(structuredClone(workspace));
        if (snapshots.length === 1) await firstSave;
    });
    const first = mutate(() => {
        owner.name = 'Failed';
        state.value.collections = [];
    });
    const rejected = assert.rejects(first, /save failed/);
    const second = mutate(() => {
        owner.name = 'Succeeded';
    });
    await Promise.resolve();
    await Promise.resolve();
    assert.equal(snapshots.length, 1);
    assert.equal(owner.name, 'Failed');
    rejectFirst(new Error('save failed'));
    await rejected;
    await second;
    assert.equal(state.value.collections[0], owner);
    assert.equal(owner.name, 'Succeeded');
    assert.deepEqual(state.value, snapshots[1]);
});
test('HTTP errors remain complete; script tests and variable writes are transactional', async () => {
    const server = await fixture((_req, res) => {
        res.writeHead(ErrorStatus, {'Content-Type': 'application/json', 'X-Repeat': ['a', 'b']});
        res.end('{"token":"abc","empty":null,"value":200,"a/b":{"~":1}}');
    });
    try {
        const request = requestAt(server.url);
        const output = await send(request);
        assert.equal(output.result.execution, ExecutionState.Complete);
        assert.equal(output.result.test, TestState.Untested);
        assert.deepEqual(output.result.assertions, []);
        assert.equal(output.result.headers.filter(h => h.name === 'X-Repeat').length, 2);
        request.scripts = {
            enabled: true,
            before: '',
            after: "const body = pm.response.json(); pm.test('Strict', () => pm.expect(body.value).to.equal('200')); pm.test('Null exists', () => pm.expect(body).to.have.property('empty')); pm.variables.set('token', body.token);"
        };
        const tested = await send(request);
        assert.equal(tested.result.test, TestState.Failed);
        assert.equal(tested.result.assertions[1].passed, true);
        assert.equal(tested.writes.token, 'abc');
        request.scripts.after = "pm.variables.set('token', pm.response.json().token); throw new Error('after failed');";
        const failedScript = await send(request);
        assert.equal(failedScript.result.error?.kind, ErrorKind.Script);
        assert.deepEqual(Object.keys(failedScript.writes), []);
    } finally {
        await server.close();
    }
});
test('post-response tests determine pass and fail states', async () => {
    let status = SuccessStatus;
    const server = await fixture((_req, res) => {
        res.writeHead(status, {'Content-Type': 'text/plain'});
        res.end('ok');
    });
    try {
        const request = requestAt(server.url);
        request.scripts = {enabled: true, before: '', after: "pm.test('status', () => pm.response.to.have.status(200));"};
        assert.equal((await send(request)).result.test, TestState.Passed);
        status = ErrorStatus;
        assert.equal((await send(request)).result.test, TestState.Failed);
    } finally {
        await server.close();
    }
});
test('JSON uses the default MIME unless a Content-Type header overrides it', async () => {
    const types: string[] = [];
    const server = await fixture((req, res) => {
        types.push(req.headers['content-type'] || '');
        res.end('ok');
    });
    try {
        const request = requestAt(server.url);
        request.method = HttpMethod.Post;
        request.body.kind = BodyKind.Json;
        request.body.text = '{}';
        assert.equal((await send(request)).result.execution, ExecutionState.Complete);
        assert.equal(types[0], ContentType.Json);
        request.headers = [{name: 'Content-Type', value: VendorJsonType, enabled: true}];
        const overridden = await send(request);
        assert.equal(overridden.result.execution, ExecutionState.Complete);
        assert.equal(types[1], VendorJsonType);
        assert.deepEqual(overridden.result.warnings, [WarningKind.ContentType]);
    } finally {
        await server.close();
    }
});
test('authentication conflicts and disabled undefined variables are handled before sending', async () => {
    let count = 0;
    const server = await fixture((_req, res) => {
        count++;
        res.end('ok');
    });
    try {
        const request = requestAt(server.url);
        request.auth.kind = AuthKind.Bearer;
        request.auth.token = 'token';
        request.headers = [{name: 'Authorization', value: 'manual', enabled: true}];
        assert.equal((await send(request)).result.error?.kind, ErrorKind.Configuration);
        assert.equal(count, 0);
        request.headers = [];
        request.params.push({name: 'disabled', value: '{{missing}}', enabled: false});
        assert.equal((await send(request)).result.execution, ExecutionState.Complete);
        request.auth.kind = AuthKind.ApiKey;
        request.auth.key = 'key';
        request.auth.value = 'value';
        request.auth.location = KeyLocation.Query;
        request.params.push({name: 'key', value: 'manual', enabled: true});
        assert.equal((await send(request)).result.error?.kind, ErrorKind.Configuration);
    } finally {
        await server.close();
    }
});
test('streamed binary and multipart uploads deliver actual file bytes', async () => {
    const directory = await mkdtemp(path.join(os.tmpdir(), 'api-tester-'));
    const file = path.join(directory, 'sample.bin');
    const bytes = Buffer.from([0, 1, 2, 255]);
    await writeFile(file, bytes);
    const received: Buffer[] = [];
    const types: string[] = [];
    const server = await fixture(async (req, res) => {
        const parts: Buffer[] = [];
        for await (const chunk of req) parts.push(Buffer.from(chunk));
        received.push(Buffer.concat(parts));
        types.push(req.headers['content-type'] || '');
        res.end('ok');
    });
    try {
        const request = requestAt(server.url);
        request.method = 'POST';
        request.body.kind = BodyKind.Binary;
        request.body.file = file;
        assert.equal((await send(request)).result.execution, ExecutionState.Complete);
        assert.deepEqual(received[0], bytes);
        request.body.kind = BodyKind.Multipart;
        request.body.fields = [{name: 'upload', value: '', enabled: true, file}, {
            name: 'label',
            value: 'hello',
            enabled: true
        }];
        assert.equal((await send(request)).result.execution, ExecutionState.Complete);
        assert.ok(types[1].startsWith('multipart/form-data; boundary='));
        assert.ok(received[1].includes(bytes));
        assert.ok(received[1].includes(Buffer.from('name="label"')));
        request.body.kind = BodyKind.Binary;
        request.body.file = path.join(directory, 'missing');
        assert.equal((await send(request)).result.error?.kind, ErrorKind.File);
    } finally {
        await server.close();
        await cleanFixture(directory);
    }
});
test('timeout and explicit cancellation are distinct', async () => {
    const server = await fixture((_req, res) => {
        setTimeout(() => res.end('slow'), ServerDelayMs);
    });
    try {
        const request = requestAt(server.url);
        request.settings = {...defaultSettings(), timeoutMs: ShortTimeoutMs};
        assert.equal((await send(request)).result.error?.kind, ErrorKind.Timeout);
        request.settings = defaultSettings();
        const controller = new AbortController();
        const operation = send(request, new CookieJar(), controller.signal);
        setTimeout(() => controller.abort(), ShortTimeoutMs);
        const output = await operation;
        assert.equal(output.result.error?.kind, ErrorKind.Cancelled);
        assert.equal(output.result.execution, ExecutionState.Cancelled);
    } finally {
        await server.close();
    }
});
test('redirects receive cookies and respect redirect and decompressed body limits', async () => {
    const server = await fixture((req, res) => {
        if (req.url === '/redirect') {
            res.writeHead(RedirectStatus, {Location: '/cookie', 'Set-Cookie': 'session=abc; Path=/'});
            res.end();
        } else if (req.url === '/cookie') res.end(req.headers.cookie || '');
        else if (req.url === '/loop') {
            res.writeHead(RedirectStatus, {Location: '/loop'});
            res.end();
        } else {
            res.writeHead(SuccessStatus, {'Content-Type': 'text/plain', 'Content-Encoding': 'gzip'});
            res.end(gzipSync(Buffer.alloc(Limits.bodyBytes + 1, 'a')));
        }
    });
    try {
        assert.equal((await send(requestAt(server.url + '/redirect'))).result.preview, 'session=abc');
        assert.equal((await send(requestAt(server.url + '/loop'))).result.error?.kind, ErrorKind.RedirectLimit);
        const output = await send(requestAt(server.url + '/large'));
        assert.equal(output.result.error?.kind, ErrorKind.BodyLimit);
        assert.equal(output.result.bodyAvailable, false);
        assert.deepEqual(output.result.assertions, []);
    } finally {
        await server.close();
    }
});
test('manual tokens share a session; batches isolate tokens and apply whole-run stop', async () => {
    const server = await fixture((req, res) => {
        res.setHeader('Content-Type', 'application/json');
        res.end(req.url === '/login' ? '{"token":"abc"}' : JSON.stringify({auth: req.headers.authorization || ''}));
    });
    try {
        const runner = new Runner();
        const login = requestAt(server.url + '/login');
        login.id = 'login';
        login.scripts = {enabled: true, before: '', after: "pm.variables.set('token', pm.response.json().token);"};
        const dependent = requestAt(server.url + '/user');
        dependent.id = 'user';
        dependent.auth.kind = AuthKind.Bearer;
        dependent.auth.token = '{{token}}';
        const base = {
            defaults: defaultSettings(),
            variables: [],
            environmentId: 'dev',
            batch: false,
            stopOnFailure: false
        };
        await completed(runner, runner.start({...base, requests: [login]}));
        const manual = await completed(runner, runner.start({...base, requests: [dependent]}));
        assert.equal(JSON.parse(manual.results[0].preview).auth, 'Bearer abc');
        const isolated = await completed(runner, runner.start({...base, batch: true, requests: [dependent]}));
        assert.equal(isolated.results[0].error?.kind, ErrorKind.Variable);
        const linked = await completed(runner, runner.start({...base, batch: true, requests: [login, dependent]}));
        assert.equal(linked.results[1].execution, ExecutionState.Complete);
        runner.switchEnvironment('test');
        const switched = await completed(runner, runner.start({...base, environmentId: 'test', requests: [dependent]}));
        assert.equal(switched.results[0].error?.kind, ErrorKind.Variable);
        const stopped = await completed(runner, runner.start({
            ...base,
            batch: true,
            stopOnFailure: true,
            requests: [dependent, login]
        }));
        assert.equal(stopped.results[1].execution, ExecutionState.Skipped);
    } finally {
        await server.close();
    }
});
test('collection runs apply collection settings, iterations, data, response persistence and stop-on-error', async () => {
    const RunnerDelayMs = 10;
    const server = await fixture((req, res) => {
        res.setHeader('Content-Type', 'text/plain');
        if (req.url === '/slow') setTimeout(() => res.end('slow'), ServerDelayMs);
        else res.end(req.url);
    });
    try {
        const runner = new Runner();
        const dataRequest = requestAt(server.url + '/{{item}}');
        const dataCollection = {id: 'data', name: 'Data', requests: [dataRequest]};
        const iterated = await completed(runner, runner.start({
            requests: [dataRequest], defaults: defaultSettings(), variables: [], environmentId: '',
            batch: true, stopOnFailure: false, iterations: 2, delayMs: RunnerDelayMs,
            iterationData: [{item: 'one'}, {item: 'two'}], persistResponses: false,
            collections: [dataCollection]
        }));
        assert.equal(iterated.total, 2);
        assert.deepEqual(iterated.results.map(result => result.iteration), [1, 2]);
        assert.deepEqual(iterated.results.map(result => new URL(result.url!).pathname), ['/one', '/two']);
        assert.ok(iterated.results.every(result => !result.bodyAvailable && result.preview === ''));

        const slow = {...requestAt(server.url + '/slow'), id: 'slow'};
        const after = {...requestAt(server.url + '/after'), id: 'after'};
        const settings = {...defaultSettings(), timeoutMs: ShortTimeoutMs, verifyTls: false};
        const stopped = await completed(runner, runner.start({
            requests: [slow, after], defaults: defaultSettings(), variables: [], environmentId: '',
            batch: true, stopOnFailure: false, stopOnError: true,
            collections: [{id: 'settings', name: 'Settings', requests: [slow, after], settings}]
        }));
        assert.equal(stopped.results[0].error?.kind, ErrorKind.Timeout);
        assert.equal(stopped.results[0].verifyTls, false);
        assert.equal(stopped.results[1].execution, ExecutionState.Skipped);
    } finally {
        await server.close();
    }
});
test('manual cookie jar can be listed and stays isolated from batch runs', async () => {
    const server = await fixture((_req, res) => {
        res.writeHead(SuccessStatus, {'Set-Cookie': 'session=abc; Path=/', 'Content-Type': 'text/plain'});
        res.end('ok');
    });
    try {
        const runner = new Runner();
        const request = requestAt(server.url);
        const base = {
            defaults: defaultSettings(),
            variables: [],
            environmentId: 'dev',
            batch: false,
            stopOnFailure: false
        };
        await completed(runner, runner.start({...base, requests: [request]}));
        const listed = await runner.listCookies();
        assert.equal(listed.length, 1);
        assert.equal(listed[0].name, 'session');
        assert.equal(listed[0].value, 'abc');
        assert.equal(listed[0].path, CookieDefaultPath);
        await completed(runner, runner.start({...base, batch: true, requests: [request]}));
        assert.equal((await runner.listCookies()).length, 1);
        runner.switchEnvironment('other');
        assert.equal((await runner.listCookies()).length, 0);
        runner.switchEnvironment('dev');
        assert.equal((await runner.listCookies()).length, 1);
        runner.clearCookies();
        assert.equal((await runner.listCookies()).length, 0);
    } finally {
        await server.close();
    }
});
test('workspace writes serialize and restore complete saved configuration', async () => {
    const directory = await mkdtemp(path.join(os.tmpdir(), 'api-tester-store-'));
    try {
        const store = new WorkspaceStore(directory);
        assert.deepEqual(await store.load(), emptyWorkspace());
        const workspace = emptyWorkspace();
        workspace.collections.push({id: 'collection', name: 'Collection', requests: [requestAt('http://localhost')]});
        await Promise.all([store.save(workspace), store.save({...workspace, environmentId: 'last'})]);
        assert.equal((await store.load()).environmentId, 'last');
        assert.deepEqual((await store.load()).collections, workspace.collections);
    } finally {
        await cleanFixture(directory);
    }
});
test('HEAD on compressed endpoints and encoded form bodies work', async () => {
    let received = '';
    const server = await fixture(async (req, res) => {
        for await (const chunk of req) received += chunk.toString();
        res.writeHead(SuccessStatus, {'Content-Type': 'text/plain', 'Content-Encoding': 'gzip'});
        if (req.method === 'HEAD') res.end(); else res.end(gzipSync(Buffer.from('ok')));
    });
    try {
        const request = requestAt(server.url);
        request.method = 'HEAD';
        const head = await send(request);
        assert.equal(head.result.execution, ExecutionState.Complete);
        assert.equal(head.result.size, 0);
        request.method = 'POST';
        request.body.kind = BodyKind.Form;
        request.body.fields = [{name: 'x', value: '%2F', enabled: true}, {name: 'x', value: '+ ', enabled: true}];
        assert.equal((await send(request)).result.execution, ExecutionState.Complete);
        assert.equal(received, 'x=%252F&x=%2B%20');
    } finally {
        await server.close();
    }
});
test('cancelling a batch cancels the active request and skips the remaining requests', async () => {
    const server = await fixture((_req, res) => {
        setTimeout(() => res.end('slow'), ServerDelayMs);
    });
    try {
        const runner = new Runner();
        const requests = [requestAt(server.url), {...requestAt(server.url), id: 'next'}];
        const id = runner.start({
            requests,
            defaults: defaultSettings(),
            variables: [],
            environmentId: '',
            batch: true,
            stopOnFailure: false
        });
        setTimeout(() => runner.cancel(id), ShortTimeoutMs);
        const view = await completed(runner, id);
        assert.equal(view.results[0].execution, ExecutionState.Cancelled);
        assert.equal(view.results[1].execution, ExecutionState.Skipped);
        assert.equal(view.results.length, view.total);
    } finally {
        await server.close();
    }
});
test('response preview and complete-body caches evict old data within their budgets', async () => {
    const body = Buffer.alloc(Limits.bodyBytes - 1, 'a');
    const server = await fixture((_req, res) => {
        res.setHeader('Content-Type', 'text/plain');
        res.end(body);
    });
    try {
        const runner = new Runner();
        const BodyEvictionRequestCount = 3;
        const requests = Array.from({length: BodyEvictionRequestCount}, (_, index) => ({
            ...requestAt(server.url),
            id: String(index)
        }));
        const id = runner.start({
            requests,
            defaults: defaultSettings(),
            variables: [],
            environmentId: '',
            batch: true,
            stopOnFailure: false
        });
        const view = await completed(runner, id);
        assert.equal(view.results[0].bodyAvailable, false);
        assert.equal(view.results.at(-1)!.bodyAvailable, true);
        assert.throws(() => runner.download(id, 0));
        runner.release(id);
    } finally {
        await server.close();
    }
    const previewBody = Buffer.alloc(Limits.previewBytes + 1, 'a');
    const small = await fixture((_req, res) => {
        res.setHeader('Content-Type', 'text/plain');
        res.end(previewBody);
    });
    try {
        const runner = new Runner();
        const count = Limits.cachedPreviewBytes / Limits.previewBytes + 1;
        const requests = Array.from({length: count}, (_, index) => ({...requestAt(small.url), id: String(index)}));
        const id = runner.start({
            requests,
            defaults: defaultSettings(),
            variables: [],
            environmentId: '',
            batch: true,
            stopOnFailure: false
        });
        const view = await completed(runner, id);
        assert.equal(view.results[0].previewAvailable, false);
        assert.ok(view.results.reduce((sum, result) => sum + Buffer.byteLength(result.preview), 0) <= Limits.cachedPreviewBytes);
        runner.release(id);
    } finally {
        await small.close();
    }
});
