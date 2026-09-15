import {Worker} from 'node:worker_threads';
import {scriptCrypto} from './scriptCrypto.mjs';
import {
    BodyKind,
    Limits,
    ScriptPhase,
    ScriptLogLevel,
    type ApiRequest,
    type AssertionResult,
    type RequestResult,
    type ScriptLog
} from '../../shared/model.js';

export interface ScriptOutput {
    request: ApiRequest;
    variables: Record<string, string>;
    logs: ScriptLog[];
    tests: AssertionResult[];
    error?: string
}

export class ScriptFailure extends Error {
    constructor(message: string, public output?: ScriptOutput) {
        super(message);
    }
}

// Only a JSON string enters the VM. No host objects or callbacks are exposed.
// A separate worker limits memory and lets cancellation stop even infinite microtasks.
const WorkerSource = String.raw`
const { parentPort, workerData } = require('node:worker_threads');
const vm = require('node:vm');
parentPort.on('message', () => {});
const context = vm.createContext({ __input: JSON.stringify(workerData.input) }, { codeGeneration: { strings: false, wasm: false } });
const source = '(' + workerData.bootstrap + ')(__input, ' + JSON.stringify(workerData.script) + ')';
Promise.resolve(new vm.Script(source).runInContext(context, { timeout: workerData.timeout }))
  .then(value => parentPort.postMessage({ ok: true, value }), error => parentPort.postMessage({ ok: false, error: String(error.message || error) }));
`;

function bootstrap(inputJson: string, source: string): string | Promise<string> {
    // This function is serialized and executes entirely in the isolated VM.
    const input = JSON.parse(inputJson);
    const request = input.request;
    const variables = Object.assign(Object.create(null), input.variables);
    const logs: any[] = [];
    const tests: any[] = [];
    const stringify = (v: any) => typeof v === 'string' ? v : JSON.stringify(v);
    const log = (level: string, values: any[]) => {
        if (logs.length < input.logLimit) logs.push({
            phase: input.phase,
            level,
            text: values.map(stringify).join(' ').slice(0, input.logChars)
        });
    };
    const variableApi = {
        get: (name: string) => variables[name],
        set: (name: string, value: any) => {
            variables[String(name)] = String(value);
        },
        unset: (name: string) => {
            delete variables[name];
        },
        has: (name: string) => Object.hasOwn(variables, name),
        toObject: () => ({...variables}),
        replaceIn: (value: string) => String(value).replace(/\{\{([^{}]+)\}\}/g, (_: string, name: string) => {
            if (name === '$timestamp') return String(Math.floor(Date.now() / input.millisecondsPerSecond));
            if (name === '$randomInt') return String(Math.floor(Math.random() * input.randomIntRange));
            if (!Object.hasOwn(variables, name)) throw new Error('Missing variable: ' + name);
            return variables[name];
        }),
    };
    const list = (pairs: any[]) => ({
        get: (name: string) => pairs.find(p => p.enabled && p.name.toLowerCase() === String(name).toLowerCase())?.value,
        has: (name: string) => pairs.some(p => p.enabled && p.name.toLowerCase() === String(name).toLowerCase()),
        add: (p: any) => pairs.push({name: String(p.key ?? p.name), value: String(p.value), enabled: true}),
        upsert: (p: any) => {
            const name = String(p.key ?? p.name);
            const old = pairs.find(p => p.name.toLowerCase() === name.toLowerCase());
            if (old) {
                old.value = String(p.value);
                old.enabled = true;
            } else pairs.push({name, value: String(p.value), enabled: true});
        },
        remove: (name: string) => {
            for (let i = pairs.length - 1; i >= 0; i--) if (pairs[i].name.toLowerCase() === String(name).toLowerCase()) pairs.splice(i, 1);
        },
        toObject: () => Object.fromEntries(pairs.filter(p => p.enabled).map(p => [p.name, p.value])),
    });

    function expect(actual: any): any {
        let negate = false;
        let deep = false;
        const deepEqual = (a: any, b: any): boolean => a === b || a !== null && b !== null && typeof a === 'object' && typeof b === 'object' && Array.isArray(a) === Array.isArray(b) && Object.keys(a).length === Object.keys(b).length && Object.keys(a).every(key => Object.hasOwn(b, key) && deepEqual(a[key], b[key]));
        const check = (passed: boolean, expected: any) => {
            if (negate ? passed : !passed) throw new Error('Expected ' + stringify(actual) + (negate ? ' not ' : ' ') + stringify(expected));
            negate = false;
            deep = false;
            return chain;
        };
        const chain: any = {
            equal: (expected: any) => check(deep ? deepEqual(actual, expected) : actual === expected, expected),
            eql: (expected: any) => check(deepEqual(actual, expected), expected),
            include: (expected: any) => check(typeof actual === 'string' || Array.isArray(actual) ? actual.includes(expected) : Object.hasOwn(actual, expected), expected),
            above: (expected: number) => check(actual > expected, expected),
            below: (expected: number) => check(actual < expected, expected),
            within: (minimum: number, maximum: number) => check(actual >= minimum && actual <= maximum, [minimum, maximum]),
            a: (expected: string) => check((Array.isArray(actual) ? 'array' : actual === null ? 'null' : typeof actual) === expected, expected),
            property: (name: string, ...value: any[]) => {
                check(actual != null && Object.hasOwn(actual, name) && (!value.length || actual[name] === value[0]), name);
                return expect(actual[name]);
            },
            match: (pattern: RegExp) => check(pattern.test(actual), String(pattern)),
            lengthOf: (length: number) => check(actual?.length === length, length),
        };
        chain.an = chain.a;
        for (const name of ['to', 'be', 'have', 'and', 'that', 'is']) Object.defineProperty(chain, name, {get: () => chain});
        Object.defineProperty(chain, 'deep', {
            get: () => {
                deep = true;
                return chain;
            }
        });
        Object.defineProperty(chain, 'not', {
            get: () => {
                negate = !negate;
                return chain;
            }
        });
        for (const [name, value] of Object.entries({
            true: true,
            false: false,
            null: null,
            undefined: undefined
        })) Object.defineProperty(chain, name, {get: () => check(actual === value, value)});
        Object.defineProperty(chain, 'ok', {get: () => check(Boolean(actual), true)});
        return chain;
    }

    const response = input.response;
    const responseApi = response ? {
        code: response.status, status: response.statusText, responseTime: response.elapsedMs,
        headers: list(response.headers), text: () => input.responseText,
        json: () => JSON.parse(input.responseText),
        to: {have: {status: (code: number) => expect(response.status).equal(code)}},
    } : undefined;
    const pm: any = {
        variables: variableApi, environment: variableApi, collectionVariables: variableApi,
        request: {
            get method() {
                return request.method;
            }, set method(v: string) {
                request.method = String(v);
            },
            get url() {
                return request.url;
            }, set url(v: string) {
                request.url = String(v);
                request.params = [];
            },
            headers: list(request.headers),
            body: {
                get raw() {
                    return request.body.text;
                }, set raw(v: string) {
                    request.body.text = String(v);
                    if (request.body.kind === input.bodyNone) request.body.kind = input.bodyText;
                }, update: (v: any) => {
                    request.body.text = String(typeof v === 'string' ? v : v.raw);
                    if (request.body.kind === input.bodyNone) request.body.kind = input.bodyText;
                }
            },
        },
        response: responseApi, expect,
        test: (name: string, callback: () => any) => {
            if (tests.length >= input.testLimit) throw new Error('Too many tests');
            const test = {name: String(name), passed: true, actual: '', expected: '', detail: ''};
            try {
                if (callback.constructor.name === 'AsyncFunction') throw new Error('Test callbacks must be synchronous');
                const result = callback();
                if (result && typeof result.then === 'function') {
                    result.catch(() => {
                    });
                    throw new Error('Test callbacks must be synchronous');
                }
            } catch (error: any) {
                test.passed = false;
                test.detail = String(error.message || error).slice(0, input.logChars);
            }
            tests.push(test);
        },
    };
    const scriptConsole = Object.fromEntries(input.logLevels.map((level: string) => [level, (...values: any[]) => log(level, values)]));
    Object.assign(globalThis, {pm, console: scriptConsole});
    // The caller inserts the script as source, so eval/Function remain disabled.
    void source;
    const error = (globalThis as any).__scriptError;
    return JSON.stringify({request, variables, logs, tests, error});
}

const MillisecondsPerSecond = 1000;
const RandomIntRange = 1001;
const ScriptInsertionPoint = 'void source;';

export async function runScript(source: string, phase: ScriptPhase, request: ApiRequest, variables: Record<string, string>, signal: AbortSignal, response?: RequestResult, responseText = ''): Promise<ScriptOutput> {
    if (!source.trim()) return {request, variables, logs: [], tests: []};
    if (Buffer.byteLength(source) > Limits.scriptOutputBytes) throw new Error('Script too large');
    const bootstrapSource = bootstrap.toString().replace(ScriptInsertionPoint, `globalThis.pm.crypto = (${scriptCrypto.toString()})();\ntry { await (async () => {\n${source}\n})(); } catch (failure) { globalThis.__scriptError = String(failure.message || failure); }`).replace(/^function /, 'async function ');
    const scriptResponse = response ? {...response, sentRequest: undefined} : undefined;
    return new Promise((resolve, reject) => {
        const worker = new Worker(WorkerSource, {
            eval: true, execArgv: [], resourceLimits: {maxOldGenerationSizeMb: Limits.scriptMemoryMb},
            workerData: {
                script: '',
                bootstrap: bootstrapSource,
                timeout: Limits.scriptTimeoutMs,
                input: {
                    request,
                    variables,
                    phase,
                    response: scriptResponse,
                    responseText,
                    logLimit: Limits.scriptLogEntries,
                    logChars: Limits.scriptLogChars,
                    testLimit: Limits.scriptTests,
                    millisecondsPerSecond: MillisecondsPerSecond,
                    randomIntRange: RandomIntRange,
                    bodyNone: BodyKind.None,
                    bodyText: BodyKind.Text,
                    logLevels: Object.values(ScriptLogLevel)
                }
            },
        });
        let settled = false;
        const finish = (error?: Error, value?: ScriptOutput) => {
            if (settled) return;
            settled = true;
            clearTimeout(timer);
            signal.removeEventListener('abort', abort);
            void worker.terminate();
            if (error) reject(error); else resolve(value!);
        };
        const abort = () => finish(new Error('Script cancelled'));
        const timer = setTimeout(() => finish(new Error('Script execution timed out')), Limits.scriptTimeoutMs);
        signal.addEventListener('abort', abort, {once: true});
        if (signal.aborted) abort();
        worker.on('error', error => finish(error as Error));
        worker.on('exit', () => {
            if (!settled) finish(new Error('Script worker exited'));
        });
        worker.on('message', message => {
            if (!message.ok) {
                finish(new Error(message.error));
                return;
            }
            try {
                if (typeof message.value !== 'string' || Buffer.byteLength(message.value) > Limits.scriptOutputBytes) throw new Error('Script output too large');
                const output: ScriptOutput = JSON.parse(message.value);
                if (output.error) finish(new ScriptFailure(output.error, output)); else finish(undefined, output);
            } catch (error) {
                finish(error as Error);
            }
        });
    });
}
