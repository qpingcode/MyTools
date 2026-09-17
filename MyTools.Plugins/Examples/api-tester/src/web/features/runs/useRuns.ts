import {ref, computed, type Ref, type ComputedRef} from 'vue';
import {
    Routes, Limits, ExecutionState, TestState,
    type Workspace, type Collection, type Environment, type ApiRequest,
    type RunView, type RequestResult,
} from '../../../shared/model.js';
import type {Tab} from '../workspace/workspaceTypes.js';
import {rpc} from '../../services/rpc.js';

export enum RunnerStage { Configuration = 'configuration', Results = 'results' }
export interface RunnerRequest { request: ApiRequest; enabled: boolean }

const DefaultIterations = 1;
const DefaultDelayMs = 0;

export function useRuns(
    workspace: Ref<Workspace>, tabs: Ref<Tab[]>,
    collection: ComputedRef<Collection | undefined>,
    environment: ComputedRef<Environment | undefined>,
) {
    const batch = ref<RunView | null>(null);
    const batchId = ref('');
    const selectedRunResultIndex = ref(-1);
    const runnerStage = ref(RunnerStage.Configuration);
    const runnerCollectionId = ref('');
    const runnerRequests = ref<RunnerRequest[]>([]);
    const iterations = ref(DefaultIterations);
    const delayMs = ref(DefaultDelayMs);
    const dataFileName = ref('');
    const iterationData = ref<Record<string, string>[]>([]);
    const dataFileError = ref(false);
    const persistResponses = ref(true);
    const stopOnError = ref(false);
    const startingBatch = ref(false);
    const runnerCollection = computed(() => workspace.value.collections.find(owner => owner.id === runnerCollectionId.value));

    function runInput(requests: ApiRequest[], isBatch: boolean) {
        return {
            requests,
            defaults: workspace.value.defaults,
            variables: environment.value?.variables || [],
            environmentId: workspace.value.environmentId,
            batch: isBatch,
            stopOnFailure: false,
            stopOnError: isBatch && stopOnError.value,
            iterations: isBatch ? iterations.value : DefaultIterations,
            delayMs: isBatch ? delayMs.value : DefaultDelayMs,
            iterationData: isBatch ? iterationData.value : [],
            persistResponses: !isBatch || persistResponses.value,
            collections: workspace.value.collections,
            environmentName: environment.value?.name || '',
        };
    }

    async function monitor(id: string, update: (view: RunView) => void, exists: () => boolean) {
        let results: RequestResult[] = [];
        while (exists()) {
            const view = await rpc<RunView>(Routes.poll, {id, from: results.length});
            if (!exists()) return;
            results = [...results, ...view.results].map((result, index) => ({
                ...result,
                bodyAvailable: result.bodyAvailable && !view.evictedBodies?.includes(index),
                preview: view.evictedPreviews?.includes(index) ? '' : result.preview,
                previewAvailable: view.evictedPreviews?.includes(index) ? false : result.previewAvailable,
            }));
            update({...view, results});
            if (view.done) break;
            await new Promise(resolve => setTimeout(resolve, Limits.pollMs));
        }
    }

    async function send(item: Tab) {
        if (item.running) {
            if (item.runId) await rpc(Routes.cancel, {id: item.runId});
            return;
        }
        item.running = true;
        try {
            if (item.runId) await rpc(Routes.release, {id: item.runId});
            item.result = undefined;
            item.runId = await rpc<string>(Routes.start, runInput([item.request], false));
            await monitor(item.runId, view => (item.result = view.results[0]),
                () => tabs.value.some(tab => tab.request.id === item.request.id));
        } finally {
            item.running = false;
        }
    }

    function configureRun(owner: Collection = collection.value!) {
        if (!owner) return false;
        runnerCollectionId.value = owner.id;
        runnerRequests.value = owner.requests.map(request => ({request, enabled: true}));
        iterations.value = DefaultIterations;
        delayMs.value = DefaultDelayMs;
        dataFileName.value = '';
        iterationData.value = [];
        dataFileError.value = false;
        persistResponses.value = true;
        stopOnError.value = false;
        runnerStage.value = RunnerStage.Configuration;
        return true;
    }

    function parseCsv(text: string): Record<string, string>[] {
        const rows = text.trim().split(/\r?\n/).map(row => row.split(',').map(value => value.trim()));
        const headers = rows.shift() || [];
        return rows.filter(row => row.some(Boolean)).map(row => Object.fromEntries(
            headers.map((header, index) => [header, row[index] || '']),
        ));
    }

    async function loadDataFile(file?: File) {
        dataFileError.value = false;
        dataFileName.value = file?.name || '';
        iterationData.value = [];
        if (!file) return;
        try {
            if (file.size > Limits.importBytes) throw new Error('Data file too large');
            const text = await file.text();
            const rows = file.name.toLowerCase().endsWith('.json') ? JSON.parse(text) : parseCsv(text);
            if (!Array.isArray(rows) || !rows.every(row => row && typeof row === 'object' && !Array.isArray(row)))
                throw new Error('Invalid iteration data');
            iterationData.value = rows.map(row => Object.fromEntries(
                Object.entries(row).map(([key, value]) => [key, value == null ? '' : String(value)]),
            ));
            if (iterationData.value.length) iterations.value = iterationData.value.length;
        } catch (error) {
            dataFileError.value = true;
            console.error(error);
        }
    }

    async function runBatch() {
        if (startingBatch.value || batch.value && !batch.value.done) return;
        const owner = runnerCollection.value;
        if (!owner) return;
        const enabledIds = new Set(runnerRequests.value.filter(item => item.enabled).map(item => item.request.id));
        const requests = owner.requests.filter(request => enabledIds.has(request.id)).map(request =>
            tabs.value.find(tab => tab.request.id === request.id)?.request || request,
        );
        const normalizedIterations = Math.max(DefaultIterations, Math.floor(iterations.value));
        if (!requests.length || !Number.isFinite(normalizedIterations) ||
            requests.length * normalizedIterations > Limits.batchRequests || delayMs.value < DefaultDelayMs) return;
        iterations.value = normalizedIterations;
        startingBatch.value = true;
        runnerStage.value = RunnerStage.Results;
        try {
            if (batchId.value) await rpc(Routes.release, {id: batchId.value});
            batchId.value = await rpc<string>(Routes.start, runInput(requests, true));
            selectedRunResultIndex.value = -1;
            batch.value = {id: batchId.value, done: false, current: '', total: requests.length * iterations.value, elapsedMs: 0, results: []};
            await monitor(batchId.value, value => {
                batch.value = value;
                if (selectedRunResultIndex.value < 0 && value.results.length) selectedRunResultIndex.value = 0;
            }, () => !!batchId.value);
        } finally {
            startingBatch.value = false;
        }
    }

    async function cancelBatch() {
        if (batch.value && !batch.value.done && batchId.value) await rpc(Routes.cancel, {id: batchId.value});
    }

    const executionCounts = computed(() => Object.values(ExecutionState).map(state => ({
        state, count: batch.value?.results.filter(result => result.execution === state).length || 0,
    })));
    const testCounts = computed(() => Object.values(TestState).map(state => ({
        state, count: batch.value?.results.filter(result => result.test === state).length || 0,
    })));
    const testResultCounts = computed(() => {
        const assertions = batch.value?.results.flatMap(result => result.assertions) || [];
        return {passed: assertions.filter(item => item.passed).length, failed: assertions.filter(item => !item.passed).length};
    });

    return {
        batch, batchId, selectedRunResultIndex, runnerStage, runnerCollection, runnerRequests,
        iterations, delayMs, dataFileName, iterationData, dataFileError, persistResponses, stopOnError,
        startingBatch, runInput, monitor, send, configureRun, loadDataFile, runBatch, cancelBatch,
        executionCounts, testCounts, testResultCounts, RunnerStage,
    };
}
