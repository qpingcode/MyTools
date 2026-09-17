import {randomUUID} from 'node:crypto';
import {CookieJar} from 'tough-cookie';
import {executeRequest, skipped, RequestError, substitute} from './engine.mjs';
import {exportCurl, resolveRequestVariables} from '../../shared/interchange.js';
import {
    Limits,
    TestState,
    ExecutionState,
    ErrorKind,
    CookieSessionExpiry,
    CookieDefaultPath,
    effectiveRequest,
    type Collection,
    type HistoryEntry,
    type ApiRequest,
    type Settings,
    type RunView,
    type Pair,
    type CookieRecord
} from '../../shared/model.js';

export interface RunInput {
    requests: ApiRequest[];
    defaults: Settings;
    variables: Pair[];
    environmentId: string;
    batch: boolean;
    stopOnFailure: boolean;
    stopOnError?: boolean;
    iterations?: number;
    delayMs?: number;
    iterationData?: Record<string, string>[];
    persistResponses?: boolean;
    collections?: Collection[];
    environmentName?: string
}

interface Job {
    view: RunView;
    controller: AbortController;
    started: number;
    bodies: Map<number, Buffer>
}

const wrapScript = (source: string) => `await (async () => {\n${source}\n})();`;
const DefaultRunIterations = 1;
const DefaultRunDelayMs = 0;

function wait(delayMs: number, signal: AbortSignal): Promise<void> {
    return new Promise(resolve => {
        const finish = () => {
            clearTimeout(timer);
            signal.removeEventListener('abort', finish);
            resolve();
        };
        const timer = setTimeout(finish, delayMs);
        signal.addEventListener('abort', finish, {once: true});
    });
}

function cookieExpiry(expires: Date | string | number | undefined): string {
    if (expires == null || expires === CookieSessionExpiry) return '';
    if (expires instanceof Date) return Number.isFinite(expires.getTime()) ? expires.toISOString() : '';
    const parsed = new Date(expires);
    return Number.isFinite(parsed.getTime()) ? parsed.toISOString() : '';
}

function cookieRecord(cookie: {
    key?: string;
    value?: string;
    domain?: string;
    path?: string;
    expires?: Date | string | number;
    httpOnly?: boolean;
    secure?: boolean;
    hostOnly?: boolean
}): CookieRecord {
    return {
        name: cookie.key || '',
        value: cookie.value || '',
        domain: cookie.domain || '',
        path: cookie.path || CookieDefaultPath,
        expires: cookieExpiry(cookie.expires),
        httpOnly: Boolean(cookie.httpOnly),
        secure: Boolean(cookie.secure),
        hostOnly: Boolean(cookie.hostOnly)
    };
}

export class Runner {
    constructor(private recordHistory?: (entry: HistoryEntry) => Promise<void>) {
    }

    private jobs = new Map<string, Job>();
    private environmentId = '';
    private generation = 0;
    private variables: Record<string, string> = Object.create(null);
    private unsetVariables = new Set<string>();
    private jar = new CookieJar();
    private jars = new Map<string, CookieJar>([['', this.jar]]);
    private bodyBytes = 0;
    private previewBytes = 0;

    switchEnvironment(id: string): void {
        if (id !== this.environmentId) {
            this.environmentId = id;
            this.generation++;
            this.variables = Object.create(null);
            this.unsetVariables.clear();
            this.jar = this.jars.get(id) || new CookieJar();
            this.jars.set(id, this.jar);
            if (this.jars.size > Limits.cookieEnvironments) this.jars.delete(this.jars.keys().next().value!);
        }
    }

    clearCookies(): void {
        this.jar = new CookieJar();
        this.jars.set(this.environmentId, this.jar);
    }

    async listCookies(): Promise<CookieRecord[]> {
        const snapshot = await this.jar.serialize();
        return snapshot.cookies
            .map(cookieRecord)
            .sort((left, right) => left.domain.localeCompare(right.domain) || left.name.localeCompare(right.name) || left.path.localeCompare(right.path));
    }

    curl(input: { request: ApiRequest; collection?: Collection; variables: Pair[]; environmentId: string }): string {
        const variables = Object.fromEntries(input.variables.filter(v => v.enabled).map(v => [v.name, v.value]));
        if (input.environmentId === this.environmentId) {
            for (const name of this.unsetVariables) delete variables[name];
            Object.assign(variables, this.variables);
        }
        return exportCurl(resolveRequestVariables(effectiveRequest(input.request, input.collection), text => substitute(text, variables, 'curl')));
    }

    start(input: RunInput): string {
        if (!input.requests.length) throw new RequestError(ErrorKind.Configuration, 'requests');
        if (input.requests.length > Limits.batchRequests) throw new RequestError(ErrorKind.Configuration, 'requests');
        if (!input.batch && input.requests.length !== 1) throw new RequestError(ErrorKind.Configuration, 'requests');
        this.switchEnvironment(input.environmentId);
        for (const [id, job] of this.jobs) if (this.jobs.size >= Limits.retainedRuns && job.view.done) this.release(id);
        if (this.jobs.size >= Limits.retainedRuns) throw new RequestError(ErrorKind.Cache);
        const snapshot = structuredClone(input);
        const id = randomUUID();
        const requestedIterations = snapshot.iterations ?? DefaultRunIterations;
        const requestedDelayMs = snapshot.delayMs ?? DefaultRunDelayMs;
        if (!Number.isFinite(requestedIterations) || requestedIterations < DefaultRunIterations ||
            !Number.isFinite(requestedDelayMs) || requestedDelayMs < DefaultRunDelayMs || requestedDelayMs > Limits.batchDelayMs)
            throw new RequestError(ErrorKind.Configuration, 'runConfiguration');
        const iterations = Math.floor(requestedIterations);
        if (iterations * snapshot.requests.length > Limits.batchRequests) throw new RequestError(ErrorKind.Configuration, 'iterations');
        const job: Job = {
            view: {
                id,
                done: false,
                current: '',
                total: snapshot.requests.length * iterations,
                elapsedMs: 0,
                results: []
            }, controller: new AbortController(), started: performance.now(), bodies: new Map()
        };
        this.jobs.set(id, job);
        const environment: Record<string, string> = Object.create(null);
        for (const variable of snapshot.variables.filter(v => v.enabled)) environment[variable.name] = variable.value;
        if (!snapshot.batch) for (const name of this.unsetVariables) delete environment[name];
        const generation = this.generation;
        const runtime: Record<string, string> = snapshot.batch ? Object.create(null) : {...this.variables};
        const jar = snapshot.batch ? new CookieJar() : this.jar;
        void this.run(job, snapshot, environment, runtime, jar, generation);
        return id;
    }

    private async run(job: Job, input: RunInput, environment: Record<string, string>, runtime: Record<string, string>, jar: CookieJar, generation: number): Promise<void> {
        let stopped = false;
        const iterations = Math.floor(input.iterations ?? DefaultRunIterations);
        const delayMs = Math.floor(input.delayMs ?? DefaultRunDelayMs);
        const scheduled = Array.from({length: iterations}, (_, iteration) =>
            input.requests.map(request => ({request, iteration}))).flat();
        try {
            for (const {request, iteration} of scheduled) {
                if (stopped || job.controller.signal.aborted) {
                    const result = skipped(request);
                    result.iteration = iteration + 1;
                    job.view.results.push(result);
                    continue;
                }
                if (delayMs) await wait(delayMs, job.controller.signal);
                if (job.controller.signal.aborted) {
                    const result = skipped(request);
                    result.iteration = iteration + 1;
                    job.view.results.push(result);
                    continue;
                }
                job.view.current = request.name;
                const collection = input.collections?.find(c => c.requests.some(r => r.id === request.id));
                const effective = effectiveRequest(request, collection);
                if (collection?.scripts?.enabled) {
                    effective.scripts = {
                        enabled: true,
                        before: [collection.scripts.before, request.scripts?.enabled ? request.scripts.before : ''].filter(Boolean).map(wrapScript).join('\n'),
                        after: [collection.scripts.after, request.scripts?.enabled ? request.scripts.after : ''].filter(Boolean).map(wrapScript).join('\n'),
                    };
                }
                const variables = {...environment, ...(input.iterationData?.[iteration] || {}), ...runtime};
                const settings = effective.settings || collection?.settings || input.defaults;
                const output = await executeRequest(effective, settings, variables, jar, job.controller.signal);
                output.result.iteration = iteration + 1;
                if (!this.jobs.has(job.view.id)) return;
                if (input.persistResponses !== false && output.body) this.cache(job, job.view.results.length, output.body);
                if (input.persistResponses === false) {
                    output.result.bodyAvailable = false;
                    output.result.preview = '';
                    output.result.previewAvailable = false;
                }
                job.view.results.push(output.result);
                if (input.persistResponses !== false) this.cachePreview(output.result);
                Object.assign(runtime, output.writes);
                for (const name of output.unsets || []) {
                    delete runtime[name];
                    delete environment[name];
                }
                if (!input.batch && this.generation === generation && !job.controller.signal.aborted) {
                    Object.assign(this.variables, output.writes);
                    for (const name of Object.keys(output.writes)) this.unsetVariables.delete(name);
                    for (const name of output.unsets || []) {
                        delete this.variables[name];
                        this.unsetVariables.add(name);
                    }
                }
                if (this.recordHistory) {
                    try {
                        await this.recordHistory({
                            id: randomUUID(),
                            request: output.result.sentRequest || effective,
                            result: output.result,
                            environmentId: input.environmentId,
                            environmentName: input.environmentName || '',
                            variables: Object.entries(variables).map(([name, value]) => ({name, value, enabled: true}))
                        });
                    } catch (error) {
                        console.error('Could not save request history', error);
                    }
                }
                delete output.result.sentRequest;
                stopped = input.stopOnFailure && output.result.test === TestState.Failed ||
                    Boolean(input.stopOnError && output.result.error);
            }
        } catch (error) {
            const current = scheduled[job.view.results.length];
            if (current) {
                const result = skipped(current.request);
                result.iteration = current.iteration + 1;
                result.error = {kind: ErrorKind.Configuration, detail: (error as Error).message};
                result.execution = ExecutionState.Failed;
                result.test = TestState.Failed;
                job.view.results.push(result);
            }
            for (const remaining of scheduled.slice(job.view.results.length)) {
                const result = skipped(remaining.request);
                result.iteration = remaining.iteration + 1;
                job.view.results.push(result);
            }
        } finally {
            job.view.done = true;
            job.view.current = '';
            job.view.elapsedMs = performance.now() - job.started;
        }
    }

    private cache(job: Job, index: number, body: Buffer): void {
        if (!this.jobs.has(job.view.id)) return;
        for (const entry of this.jobs.values()) {
            if (this.bodyBytes + body.length <= Limits.cachedBodyBytes) break;
            for (const [cachedIndex, cachedBody] of entry.bodies) {
                entry.bodies.delete(cachedIndex);
                this.bodyBytes -= cachedBody.length;
                if (entry.view.results[cachedIndex]) entry.view.results[cachedIndex].bodyAvailable = false;
                if (this.bodyBytes + body.length <= Limits.cachedBodyBytes) break;
            }
        }
        job.bodies.set(index, body);
        this.bodyBytes += body.length;
    }

    private cachePreview(result: import('../../shared/model.js').RequestResult): void {
        const size = Buffer.byteLength(result.preview);
        for (const entry of this.jobs.values()) {
            if (this.previewBytes + size <= Limits.cachedPreviewBytes) break;
            for (const cached of entry.view.results) {
                if (cached === result) continue;
                this.previewBytes -= Buffer.byteLength(cached.preview);
                cached.preview = '';
                cached.previewAvailable = false;
                if (this.previewBytes + size <= Limits.cachedPreviewBytes) break;
            }
        }
        this.previewBytes += size;
    }

    poll(id: string): RunView {
        const job = this.jobs.get(id);
        if (!job) throw new RequestError(ErrorKind.Cache);
        return {...job.view, elapsedMs: job.view.done ? job.view.elapsedMs : performance.now() - job.started};
    }

    cancel(id: string): void {
        this.jobs.get(id)?.controller.abort();
    }

    release(id: string): void {
        const job = this.jobs.get(id);
        if (!job) return;
        job.controller.abort();
        for (const bytes of job.bodies.values()) this.bodyBytes -= bytes.length;
        for (const result of job.view.results) this.previewBytes -= Buffer.byteLength(result.preview);
        this.jobs.delete(id);
    }

    download(id: string, index: number): string {
        const body = this.jobs.get(id)?.bodies.get(index);
        if (!body) throw new RequestError(ErrorKind.Cache);
        return body.toString('base64');
    }
}
