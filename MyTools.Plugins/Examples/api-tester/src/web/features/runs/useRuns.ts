import {ref, computed, type Ref, type ComputedRef} from 'vue';
import {
    Routes,
    Limits,
    ExecutionState,
    TestState,
    type Workspace,
    type Collection,
    type Environment,
    type ApiRequest,
    type RunView,
    type RequestResult,
} from '../../../shared/model.js';
import type {Tab} from '../workspace/workspaceTypes.js';
import {rpc} from '../../services/rpc.js';

export function useRuns(
    workspace: Ref<Workspace>,
    tabs: Ref<Tab[]>,
    collection: ComputedRef<Collection | undefined>,
    environment: ComputedRef<Environment | undefined>,
    selected: Ref<Set<string>>,
) {
    const batch = ref<RunView | null>(null);
    const batchId = ref('');
    const expandedResults = ref(new Set<string>());
    const stopOnFailure = ref(false);
    const startingBatch = ref(false);

    function runInput(requests: ApiRequest[], isBatch: boolean) {
        return {
            requests,
            defaults: workspace.value.defaults,
            variables: environment.value?.variables || [],
            environmentId: workspace.value.environmentId,
            batch: isBatch,
            stopOnFailure: stopOnFailure.value,
            collections: workspace.value.collections,
            environmentName: environment.value?.name || '',
        };
    }

    async function monitor(
        id: string,
        update: (view: RunView) => void,
        exists: () => boolean,
    ) {
        let results: RequestResult[] = [];
        while (exists()) {
            const view = await rpc<RunView>(Routes.poll, {
                id,
                from: results.length,
            });
            if (!exists()) return;
            results = [...results, ...view.results].map((result, index) => ({
                ...result,
                bodyAvailable:
                    result.bodyAvailable && !view.evictedBodies?.includes(index),
                preview: view.evictedPreviews?.includes(index) ? '' : result.preview,
                previewAvailable: view.evictedPreviews?.includes(index)
                    ? false
                    : result.previewAvailable,
            }));
            update({...view, results});
            if (view.done) break;
            await new Promise((resolve) => setTimeout(resolve, Limits.pollMs));
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
            item.runId = await rpc<string>(
                Routes.start,
                runInput([item.request], false),
            );
            await monitor(
                item.runId,
                (view) => (item.result = view.results[0]),
                () => tabs.value.some((tab) => tab.request.id === item.request.id),
            );
        } finally {
            item.running = false;
        }
    }

    async function runBatch() {
        if (batch.value && !batch.value.done) {
            await rpc(Routes.cancel, {id: batchId.value});
            return;
        }
        if (startingBatch.value || !collection.value) return;
        const requests = collection.value.requests
            .filter(
                (request) => !selected.value.size || selected.value.has(request.id),
            )
            .map(
                (request) =>
                    tabs.value.find((tab) => tab.request.id === request.id)?.request ||
                    request,
            );
        if (!requests.length) return;
        startingBatch.value = true;
        try {
            if (batchId.value) await rpc(Routes.release, {id: batchId.value});
            batchId.value = await rpc<string>(Routes.start, runInput(requests, true));
            expandedResults.value.clear();
            batch.value = {
                id: batchId.value,
                done: false,
                current: '',
                total: requests.length,
                elapsedMs: 0,
                results: [],
            };
            await monitor(
                batchId.value,
                (value) => (batch.value = value),
                () => !!batchId.value,
            );
        } finally {
            startingBatch.value = false;
        }
    }

    const executionCounts = computed(() =>
        Object.values(ExecutionState).map((state) => ({
            state,
            count:
                batch.value?.results.filter((result) => result.execution === state)
                    .length || 0,
        })),
    );
    const testCounts = computed(() =>
        Object.values(TestState).map((state) => ({
            state,
            count:
                batch.value?.results.filter((result) => result.test === state).length ||
                0,
        })),
    );
    const assertionCounts = computed(() => {
        const assertions =
            batch.value?.results.flatMap((result) => result.assertions) || [];
        return {
            passed: assertions.filter((item) => item.passed).length,
            failed: assertions.filter((item) => !item.passed).length,
        };
    });

    return {
        batch,
        batchId,
        expandedResults,
        stopOnFailure,
        startingBatch,
        runInput,
        monitor,
        send,
        runBatch,
        executionCounts,
        testCounts,
        assertionCounts,
    };
}
