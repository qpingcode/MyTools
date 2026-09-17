import {test} from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';
import {once} from 'node:events';
import {mkdtemp, rm} from 'node:fs/promises';
import {createHash, createHmac} from 'node:crypto';
import os from 'node:os';
import path from 'node:path';
import {CookieJar} from 'tough-cookie';
import {importCurl, exportCurl, importWorkspace, exportWorkspace, exportPostman} from '../src/shared/interchange.js';
import {
    newRequest,
    emptyWorkspace,
    defaultSettings,
    effectiveRequest,
    parseQuery,
    AuthKind,
    KeyLocation,
    HttpMethod,
    HttpHeader,
    BodyKind,
    ScriptPhase,
    TestState,
    ExecutionState,
    ErrorKind,
    Limits,
    type Collection,
    type HistoryEntry,
    type RunView
} from '../src/shared/model.js';
import {executeRequest} from '../src/backend/execution/engine.mjs';
import {runScript} from '../src/backend/scripting/scripts.mjs';
import {HistoryStore, WorkspaceStore} from '../src/backend/persistence/storage.mjs';
import {Runner} from '../src/backend/execution/runner.mjs';
import {validateWorkspace} from '../src/shared/workspaceValidation.js';
import {newCollection} from '../src/shared/collectionTree.js';
import {placeRequest} from '../src/shared/requestPlacement.js';
import {
    requestDragScrollDelta,
    RequestDragScrollEdgePx,
    RequestDragScrollMaxPx,
} from '../src/web/features/sidebar/requestDragScroll.js';

const SuccessStatus = 200;
const PollIntervalMs = 10;
const TestDeadlineMs = 10_000;
const HistoryDirectoryPrefix = 'api-tester-history-';

async function fixture() {
    const received: { url: string; headers: http.IncomingHttpHeaders; text: string }[] = [];
    const server = http.createServer(async (req, res) => {
        let text = '';
        for await (const chunk of req) text += chunk;
        received.push({url: req.url!, headers: req.headers, text});
        res.setHeader('Content-Type', 'application/json');
        res.end(JSON.stringify({token: 'server-token', id: 42, url: req.url}));
    });
    server.listen(0, '127.0.0.1');
    await once(server, 'listening');
    return {
        received, url: `http://127.0.0.1:${(server.address() as any).port}`, close: async () => {
            server.close();
            await once(server, 'close');
        }
    };
}

async function wait(runner: Runner, id: string): Promise<RunView> {
    const deadline = Date.now() + TestDeadlineMs;
    while (!runner.poll(id).done) {
        if (Date.now() > deadline) throw new Error('Run did not finish');
        await new Promise(resolve => setTimeout(resolve, PollIntervalMs));
    }
    return runner.poll(id);
}

test('browser cURL imports quoted JSON, raw query encoding and explicit HTTP methods without invoking a shell', async () => {
    const server = await fixture();
    try {
        const source = `curl '${server.url}/echo?q=%20&q=+&flag' -XPOST -H 'Content-Type: application/json' --data-raw '{"name":"O'\\''Brien","command":"$(echo no)"}' --compressed`;
        const request = importCurl(source, 'imported', 'browser');
        assert.equal(request.method, HttpMethod.Post);
        assert.equal(request.settings!.followRedirects, false);
        const output = await executeRequest(request, request.settings!, {}, new CookieJar(), new AbortController().signal);
        assert.equal(output.result.execution, ExecutionState.Complete);
        assert.equal(server.received[0].url, '/echo?q=%20&q=+&flag');
        assert.deepEqual(JSON.parse(server.received[0].text), {name: "O'Brien", command: '$(echo no)'});
        const restored = importCurl(exportCurl(request), 'restored', 'restored');
        assert.equal(restored.body.text, request.body.text);
        assert.equal(restored.settings!.followRedirects, false);
        assert.throws(() => importCurl('curl https://example.com --proxy http://localhost', 'id', 'bad'));
        assert.throws(() => importCurl('curl https://example.com -d @secret', 'id', 'bad'));
        assert.throws(() => importCurl("curl 'https://example.com", 'id', 'bad'));
        const form = importCurl(`curl '${server.url}' --data-urlencode 'email=user@example.com'`, 'form', 'form');
        assert.equal(form.body.kind, BodyKind.Form);
        assert.equal(form.body.fields[0].value, 'user@example.com');
        const binary = newRequest('file', 'file');
        binary.url = server.url;
        binary.body.kind = BodyKind.Binary;
        binary.body.file = 'C:/files/example.bin';
        const restoredBinary = importCurl(exportCurl(binary), 'file-copy', 'file-copy');
        assert.equal(restoredBinary.body.kind, BodyKind.Binary);
        assert.equal(restoredBinary.body.file, binary.body.file);
        assert.equal(restoredBinary.body.contentType, 'application/octet-stream');
    } finally {
        await server.close();
    }
});

test('native backup roundtrip preserves configuration, creates new IDs and disables imported scripts', async () => {
    const workspace = emptyWorkspace();
    const request = newRequest('request', 'saved');
    request.scripts = {enabled: true, before: "pm.variables.set('x','y')", after: "pm.test('id', () => pm.expect(pm.response.json().id).to.equal(42))"};
    workspace.collections.push({
        id: 'collection',
        name: 'saved',
        requests: [request],
        auth: {...request.auth, kind: AuthKind.Bearer, token: '{{token}}'},
        headers: [{name: 'X-Shared', value: 'value', enabled: true}]
    });
    workspace.environments.push({
        id: 'environment',
        name: 'local',
        variables: [{name: 'token', value: 'secret', enabled: true}]
    });
    workspace.environmentId = 'environment';
    let sequence = 0;
    const imported = importWorkspace(exportWorkspace(workspace), () => String(++sequence));
    assert.notEqual(imported.collections[0].requests[0].id, request.id);
    assert.equal(imported.collections[0].requests[0].scripts!.after, request.scripts.after);
    assert.equal(imported.collections[0].requests[0].scripts!.enabled, false);
    assert.equal(imported.environments[0].id, imported.environmentId);
    assert.equal(workspace.collections[0].requests[0].scripts!.enabled, true);
    const invalid = JSON.parse(exportWorkspace(workspace));
    invalid.collections[0].requests[0].headers = [{name: 1, value: null}];
    assert.throws(() => importWorkspace(JSON.stringify(invalid), () => String(++sequence)));
});

test('nested collections persist, remap parents and export as a rooted subtree', () => {
    const workspace = emptyWorkspace();
    const root = newCollection('root', 'Root');
    const child = newCollection('child', 'Child', root.id);
    root.requests.push(newRequest('one', 'one'));
    child.requests.push(newRequest('two', 'two'));
    workspace.collections.push(root, child);
    const childExport = JSON.parse(exportWorkspace(workspace, child));
    assert.equal(childExport.collections.length, 1);
    assert.equal(childExport.collections[0].id, child.id);
    assert.equal(childExport.collections[0].parentId, undefined);
    const rootExport = JSON.parse(exportWorkspace(workspace, root));
    assert.equal(rootExport.collections.length, 2);
    assert.equal(rootExport.collections.find((item: Collection) => item.id === root.id).parentId, undefined);
    assert.equal(rootExport.collections.find((item: Collection) => item.id === child.id).parentId, root.id);
    const postman = JSON.parse(exportPostman(root, workspace.collections));
    assert.equal(postman.item[0].name, 'Child');
    assert.equal(postman.item[0].item[0].name, 'two');
    assert.equal(postman.item[1].name, 'one');
    let sequence = 0;
    const imported = importWorkspace(exportWorkspace(workspace), () => String(++sequence));
    const importedRoot = imported.collections.find(item => !item.parentId)!;
    const importedChild = imported.collections.find(item => item.parentId)!;
    assert.notEqual(importedRoot.id, root.id);
    assert.equal(importedChild.parentId, importedRoot.id);
    const cyclic = emptyWorkspace();
    cyclic.collections.push(newCollection('a', 'A', 'b'), newCollection('b', 'B', 'a'));
    assert.throws(() => validateWorkspace(cyclic));
});

test('requests can be reordered and moved between collections', () => {
    const root = newCollection('root', 'Root');
    const child = newCollection('child', 'Child', root.id);
    root.requests.push(newRequest('one', 'one'), newRequest('two', 'two'), newRequest('three', 'three'));
    const collections = [root, child];
    assert.equal(placeRequest(collections, 'one', root.id, 2), true);
    assert.deepEqual(root.requests.map(request => request.id), ['two', 'one', 'three']);
    assert.equal(placeRequest(collections, 'one', root.id, 2), false);
    assert.equal(placeRequest(collections, 'three', child.id, 0), true);
    assert.deepEqual(root.requests.map(request => request.id), ['two', 'one']);
    assert.equal(child.requests[0].id, 'three');
    assert.equal(placeRequest(collections, 'missing', root.id, 0), false);
});

test('dragging a request near the tree edge produces a scroll delta', () => {
    const bounds = {top: 100, bottom: 400, left: 20, right: 220};
    const centerX = (bounds.left + bounds.right) / 2;
    const centerY = (bounds.top + bounds.bottom) / 2;
    assert.equal(requestDragScrollDelta(centerX, centerY, bounds), 0);
    assert.equal(requestDragScrollDelta(bounds.left - 1, bounds.top, bounds), 0);
    assert.equal(requestDragScrollDelta(centerX, bounds.top, bounds), -RequestDragScrollMaxPx);
    assert.equal(requestDragScrollDelta(centerX, bounds.bottom, bounds), RequestDragScrollMaxPx);
    const halfEdge = RequestDragScrollEdgePx / 2;
    assert.equal(requestDragScrollDelta(centerX, bounds.top + halfEdge, bounds), -RequestDragScrollMaxPx / 2);
    assert.equal(requestDragScrollDelta(centerX, bounds.bottom - halfEdge, bounds), RequestDragScrollMaxPx / 2);
});

test('Postman collections import folder auth and scripts, and exported disabled params survive reimport', () => {
    let sequence = 0;
    const source = JSON.stringify({
        info: {name: 'Postman'},
        auth: {type: 'bearer', bearer: [{key: 'token', value: '{{token}}'}]},
        event: [{listen: 'prerequest', script: {exec: ["console.log('collection')"]}}],
        item: [{
            name: 'folder',
            auth: {type: 'basic', basic: [{key: 'username', value: 'user'}, {key: 'password', value: 'password'}]},
            event: [{listen: 'test', script: {exec: ["pm.test('folder', () => pm.expect(1).equal(1))"]}}],
            item: [{
                name: 'request',
                request: {
                    method: 'POST',
                    url: {
                        raw: 'https://example.com?q=active',
                        query: [{key: 'q', value: 'active'}, {key: 'unused', value: 'off', disabled: true}]
                    },
                    body: {mode: 'raw', raw: '{}', options: {raw: {language: 'json'}}}
                },
                event: [{listen: 'test', script: {exec: ["console.log('request')"]}}]
            }]
        }]
    });
    const imported = importWorkspace(source, () => String(++sequence));
    const collection = imported.collections[0];
    const r = collection.requests[0];
    assert.equal(r.name, 'folder / request');
    assert.equal(r.auth.kind, AuthKind.Basic);
    assert.equal(r.scripts!.enabled, false);
    assert.match(r.scripts!.after, /folder/);
    assert.match(r.scripts!.after, /request/);
    const restored = importWorkspace(exportPostman(collection), () => String(++sequence));
    assert.deepEqual(restored.collections[0].requests[0].params, r.params);
    assert.equal(restored.collections[0].requests[0].body.kind, BodyKind.Json);
    const environment = importWorkspace(JSON.stringify({
        name: 'local',
        values: [{key: 'host', value: 'localhost', enabled: false}]
    }), () => String(++sequence));
    assert.equal(environment.environments[0].variables[0].enabled, false);
});

test('collection auth inherits explicitly and disabled request headers override common headers', async () => {
    const server = await fixture();
    try {
        const r = newRequest('request', 'inherited');
        r.url = server.url;
        r.auth.kind = AuthKind.Inherit;
        r.headers.push({name: 'x-common', value: 'disabled', enabled: false});
        const collection: Collection = {
            id: 'collection',
            name: 'collection',
            requests: [r],
            auth: {...newRequest('', '').auth, kind: AuthKind.Bearer, token: 'token'},
            headers: [{name: 'X-Common', value: 'common', enabled: true}, {
                name: 'X-Other',
                value: 'other',
                enabled: true
            }]
        };
        const runner = new Runner();
        const id = runner.start({
            requests: [r],
            collections: [collection],
            defaults: defaultSettings(),
            environmentId: '',
            variables: [],
            batch: false,
            stopOnFailure: false
        });
        assert.equal((await wait(runner, id)).results[0].test, TestState.Untested);
        assert.equal(server.received[0].headers.authorization, 'Bearer token');
        assert.equal(server.received[0].headers['x-common'], undefined);
        assert.equal(server.received[0].headers['x-other'], 'other');
        r.auth.kind = AuthKind.None;
        const output = await executeRequest(effectiveRequest(r, collection), defaultSettings(), {}, new CookieJar(), new AbortController().signal);
        assert.equal(output.result.status, SuccessStatus);
        assert.equal(server.received[1].headers.authorization, undefined);
    } finally {
        await server.close();
    }
});

test('scripts update method, URL, body and headers before sending and record response tests and logs', async () => {
    const server = await fixture();
    try {
        const r = newRequest('script', 'script');
        r.url = server.url;
        r.scripts = {
            enabled: true,
            before: `pm.request.method = 'POST'; pm.request.url = '${server.url}/changed?q=script'; pm.variables.set('time', '123'); pm.request.body.update({raw: 'body'}); pm.request.headers.upsert({key:'X-Time',value:pm.variables.get('time')}); console.log('before');`,
            after: "pm.test('status',()=>pm.response.to.have.status(200)); pm.test('id',()=>pm.expect(pm.response.json().id).to.equal(42)); pm.variables.set('token',pm.response.json().token); console.log('after');"
        };
        const output = await executeRequest(r, defaultSettings(), {}, new CookieJar(), new AbortController().signal);
        assert.equal(output.result.execution, ExecutionState.Complete);
        assert.equal(output.result.test, TestState.Passed);
        assert.equal(server.received[0].url, '/changed?q=script');
        assert.equal(server.received[0].text, 'body');
        assert.equal(server.received[0].headers['x-time'], '123');
        assert.equal(output.writes.token, 'server-token');
        assert.equal(output.writes.time, '123');
        assert.deepEqual(output.result.scriptLogs!.map(l => l.phase), [ScriptPhase.Before, ScriptPhase.After]);
        assert.equal(r.method, HttpMethod.Get); // Sending never modifies the saved request.
        assert.equal(output.result.sentRequest!.scripts!.enabled, false);
    } finally {
        await server.close();
    }
});

test('script signing helpers match Node SHA256, HMAC-SHA256 and UTF8 base64', async () => {
    const text = 'hello 中文';
    const secret = 'a'.repeat(Limits.scriptLogChars);
    const source = `pm.variables.set('hash', pm.crypto.sha256(${JSON.stringify(text)})); pm.variables.set('hmac', pm.crypto.hmacSha256(${JSON.stringify(text)}, ${JSON.stringify(secret)})); pm.variables.set('base64', pm.crypto.base64(${JSON.stringify(text)})); pm.test('deep',()=>pm.expect({a:1,b:2}).to.deep.equal({b:2,a:1}));`;
    const output = await runScript(source, ScriptPhase.Before, newRequest('id', ''), {}, new AbortController().signal);
    assert.equal(output.variables.hash, createHash('sha256').update(text).digest('hex'));
    assert.equal(output.variables.hmac, createHmac('sha256', secret).update(text).digest('hex'));
    assert.equal(output.variables.base64, Buffer.from(text).toString('base64'));
    assert.equal(output.tests[0].passed, true);
});

test('script failures prevent sending, response failures preserve response and no variable writes commit', async () => {
    const server = await fixture();
    try {
        const r = newRequest('id', 'failure');
        r.url = server.url;
        r.scripts = {enabled: true, before: "console.log('diagnostic'); throw new Error('before failed')", after: ''};
        let output = await executeRequest(r, defaultSettings(), {}, new CookieJar(), new AbortController().signal);
        assert.equal(output.result.error!.kind, ErrorKind.Script);
        assert.equal(server.received.length, 0);
        assert.equal(output.result.scriptLogs![0].text, 'diagnostic');
        r.scripts = {
            enabled: true,
            before: '',
            after: "pm.variables.set('token','bad'); console.log('after diagnostic'); throw new Error('after failed')"
        };
        output = await executeRequest(r, defaultSettings(), {}, new CookieJar(), new AbortController().signal);
        assert.equal(output.result.execution, ExecutionState.Complete);
        assert.equal(output.result.test, TestState.Failed);
        assert.equal(output.result.status, SuccessStatus);
        assert.deepEqual({...output.writes}, {});
        assert.equal(output.result.scriptLogs![0].text, 'after diagnostic');
        r.scripts.after = "pm.test('fail',()=>pm.expect(42).to.equal(43))";
        output = await executeRequest(r, defaultSettings(), {}, new CookieJar(), new AbortController().signal);
        assert.equal(output.result.test, TestState.Failed);
        assert.equal(output.result.assertions.at(-1)!.passed, false);
    } finally {
        await server.close();
    }
});

test('script workers block host access and dynamic code, time out loops and stop on cancellation', async () => {
    const r = newRequest('id', 'sandbox');
    const signal = new AbortController().signal;
    for (const source of ["process.exit()", "require('node:fs')", "pm.variables.get.constructor('return process')()", "console.log.constructor('return process')()"])
        await assert.rejects(runScript(source, ScriptPhase.Before, r, {}, signal));
    await assert.rejects(runScript('while(true) {}', ScriptPhase.Before, r, {}, signal), /timed out/);
    await assert.rejects(runScript('await new Promise(()=>{})', ScriptPhase.Before, r, {}, signal), /timed out/);
    const controller = new AbortController();
    const pending = runScript('while(true) {}', ScriptPhase.Before, r, {}, controller.signal);
    controller.abort();
    await assert.rejects(pending, /cancelled/);
});

test('collection and request scripts have separate lexical scopes and carry variables between requests', async () => {
    const server = await fixture();
    try {
        const first = newRequest('first', 'first');
        first.url = server.url;
        first.scripts = {
            enabled: true,
            before: "const x=2; pm.variables.set('value',String(Number(pm.variables.get('value'))+x))",
            after: "pm.variables.set('token',pm.response.json().token)"
        };
        const second = newRequest('second', 'second');
        second.url = server.url;
        second.auth.kind = AuthKind.Bearer;
        second.auth.token = '{{token}}';
        second.headers.push({name: 'X-Value', value: '{{value}}', enabled: true});
        const collection: Collection = {
            id: 'collection',
            name: 'collection',
            requests: [first, second],
            scripts: {
                enabled: true,
                before: "const x=1; if(!pm.variables.has('value')) pm.variables.set('value',String(x))",
                after: ''
            }
        };
        const runner = new Runner();
        const id = runner.start({
            requests: [first, second],
            collections: [collection],
            defaults: defaultSettings(),
            variables: [],
            environmentId: '',
            batch: true,
            stopOnFailure: false
        });
        const view = await wait(runner, id);
        assert.equal(view.results[1].test, TestState.Untested);
        assert.equal(server.received[1].headers.authorization, 'Bearer server-token');
        assert.equal(server.received[1].headers['x-value'], '3');
    } finally {
        await server.close();
    }
});

test('manual script variables and unsets affect later sends and copied cURL until the environment changes', async () => {
    const server = await fixture();
    try {
        const first = newRequest('first', 'first');
        first.url = server.url;
        first.scripts = {
            enabled: true,
            before: "pm.variables.unset('drop')",
            after: "pm.variables.set('token',pm.response.json().token)"
        };
        const variables = [{name: 'drop', value: 'saved', enabled: true}];
        const runner = new Runner();
        await wait(runner, runner.start({
            requests: [first],
            defaults: defaultSettings(),
            variables,
            environmentId: 'local',
            batch: false,
            stopOnFailure: false
        }));
        const second = newRequest('second', 'second');
        second.url = server.url;
        second.auth.kind = AuthKind.Bearer;
        second.auth.token = '{{token}}';
        assert.match(runner.curl({
            request: second,
            variables,
            environmentId: 'local'
        }), /authorization: Bearer server-token/i);
        second.headers.push({name: 'X-Drop', value: '{{drop}}', enabled: true});
        const failure = await wait(runner, runner.start({
            requests: [second],
            defaults: defaultSettings(),
            variables,
            environmentId: 'local',
            batch: false,
            stopOnFailure: false
        }));
        assert.equal(failure.results[0].error!.kind, ErrorKind.Variable);
        assert.equal(server.received.length, 1);
        assert.throws(() => runner.curl({request: second, variables, environmentId: 'local'}));
        runner.switchEnvironment('other');
        second.auth.kind = AuthKind.None;
        await wait(runner, runner.start({
            requests: [second],
            defaults: defaultSettings(),
            variables,
            environmentId: 'other',
            batch: false,
            stopOnFailure: false
        }));
        assert.equal(server.received[1].headers['x-drop'], 'saved');
    } finally {
        await server.close();
    }
});

test('history persists actual request snapshots across restarts, bounds previews and serializes clears', async () => {
    const directory = await mkdtemp(path.join(os.tmpdir(), HistoryDirectoryPrefix));
    const server = await fixture();
    try {
        const store = new HistoryStore(directory);
        const runner = new Runner(entry => store.append(entry));
        const r = newRequest('id', 'history');
        r.url = server.url + '/{{path}}?q=%20&q=+&flag';
        r.params = parseQuery(r.url);
        r.headers.push({name: 'X-Token', value: '{{token}}', enabled: true});
        const id = runner.start({
            requests: [r],
            defaults: defaultSettings(),
            variables: [{name: 'path', value: 'actual', enabled: true}, {name: 'token', value: 'token', enabled: true}],
            environmentId: '',
            environmentName: 'local',
            batch: false,
            stopOnFailure: false
        });
        await wait(runner, id);
        r.url = 'https://changed.example.com';
        const entries = await new HistoryStore(directory).list();
        assert.equal(entries.length, 1);
        assert.equal(entries[0].request.url, server.url + '/actual?q=%20&q=+&flag');
        assert.equal(entries[0].request.headers.find(h => h.name === 'X-Token')!.value, 'token');
        assert.equal(entries[0].result.bodyAvailable, false);
        assert.equal(entries[0].environmentName, 'local');
        await executeRequest(entries[0].request, defaultSettings(), {}, new CookieJar(), new AbortController().signal);
        assert.equal(server.received[1].url, '/actual?q=%20&q=+&flag');
        const entry: HistoryEntry = entries[0];
        entry.result.preview = 'x'.repeat(Limits.historyPreviewBytes + 1);
        entry.result.size = entry.result.preview.length;
        const appends = Array.from({length: Limits.historyEntries + 1}, (_, i) => store.append({
            ...entry,
            id: String(i)
        }));
        await Promise.all(appends);
        const bounded = await store.list();
        assert.equal(bounded.length, Limits.historyEntries);
        assert.equal(Buffer.byteLength(bounded[0].result.preview), Limits.historyPreviewBytes);
        assert.equal(bounded[0].result.truncated, true);
        const otherRequestEntry = structuredClone(entry);
        otherRequestEntry.id = 'other-entry';
        otherRequestEntry.request.id = 'other-request';
        await store.append(otherRequestEntry);
        await store.clear('id');
        assert.deepEqual((await store.list()).map(item => item.request.id), ['other-request']);
        await Promise.all([store.append(entry), store.clear()]);
        assert.deepEqual(await store.list(), []);
        const workspaceStore = new WorkspaceStore(directory);
        const workspace = emptyWorkspace();
        workspace.collections.push({
            id: 'collection',
            name: 'scripts',
            requests: [newRequest('saved', 'saved')],
            scripts: {enabled: true, before: '', after: 'console.log(1)'}
        });
        await workspaceStore.save(workspace);
        assert.deepEqual(await new WorkspaceStore(directory).load(), workspace);
    } finally {
        await server.close();
        await rm(directory, {recursive: true, force: true});
    }
});
