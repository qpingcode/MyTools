<script setup lang="ts">
import {computed, ref, watch} from 'vue';
import {useWorkspaceContext} from '../workspace/context.js';
import ResponsePanel from '../response/ResponsePanel.vue';
import Icon from '../../components/common/Icon.vue';
import {ExecutionState, Limits, ScriptLogLevel, ScriptPhase, TestState} from '../../../shared/model.js';

enum ResultFilter { All = 'all', Passed = 'passed', Failed = 'failed', Skipped = 'skipped', Errors = 'errors', Console = 'console' }

const {
  t, batch, batchId, selectedRunResultIndex, executionCounts, testCounts, testResultCounts,
  execution, test, cancelBatch, runnerStage, runnerCollection, runnerRequests, iterations,
  delayMs, dataFileName, iterationData, dataFileError, persistResponses, stopOnError,
  startingBatch, loadDataFile, runBatch, RunnerStage,
} = useWorkspaceContext();
const filter = ref(ResultFilter.All);
const selectedResult = computed(() => batch.value?.results[selectedRunResultIndex.value]);
const enabledCount = computed(() => runnerRequests.value.filter(item => item.enabled).length);
const runReady = computed(() => enabledCount.value > 0 && Number.isFinite(iterations.value) &&
    iterations.value > 0 && enabledCount.value * Math.floor(iterations.value) <= Limits.batchRequests &&
    Number.isFinite(delayMs.value) && delayMs.value >= 0 && delayMs.value <= Limits.batchDelayMs);
const resultFilters = computed(() => [
  {value: ResultFilter.All, label: t.value.AllTests(), count: batch.value?.results.length || 0},
  {value: ResultFilter.Passed, label: t.value.Passed(), count: batch.value?.results.filter(r => r.test === TestState.Passed).length || 0},
  {value: ResultFilter.Failed, label: t.value.Failed(), count: batch.value?.results.filter(r => r.test === TestState.Failed && !r.error).length || 0},
  {value: ResultFilter.Skipped, label: t.value.Skipped(), count: batch.value?.results.filter(r => r.execution === ExecutionState.Skipped).length || 0},
  {value: ResultFilter.Errors, label: t.value.Errors(), count: batch.value?.results.filter(r => r.error).length || 0},
  {value: ResultFilter.Console, label: t.value.ConsoleLog(), count: batch.value?.results.reduce((sum, r) => sum + (r.scriptLogs?.length || 0), 0) || 0},
]);
const filteredResults = computed(() => (batch.value?.results || []).map((result, index) => ({result, index})).filter(({result}) =>
    filter.value === ResultFilter.All ||
    filter.value === ResultFilter.Passed && result.test === TestState.Passed ||
    filter.value === ResultFilter.Failed && result.test === TestState.Failed && !result.error ||
    filter.value === ResultFilter.Skipped && result.execution === ExecutionState.Skipped ||
    filter.value === ResultFilter.Errors && !!result.error ||
    filter.value === ResultFilter.Console && !!result.scriptLogs?.length));
watch(filter, () => {
  selectedRunResultIndex.value = filteredResults.value[0]?.index ?? -1;
});

async function dataFileChanged(event: Event) {
  const input = event.target as HTMLInputElement;
  await loadDataFile(input.files?.[0]);
}
</script>
<template>
  <div class="run-results runner-workspace">
    <template v-if="runnerStage === RunnerStage.Configuration">
      <header class="runner-header"><div><h2>{{ t.CollectionRunner() }}</h2><p>{{ runnerCollection?.name }}</p></div></header>
      <div class="runner-configuration">
        <section class="runner-request-selection">
          <h3>{{ t.Requests() }} <span class="muted">{{ enabledCount }}/{{ runnerRequests.length }}</span></h3>
          <label v-for="item in runnerRequests" :key="item.request.id" class="runner-request-option">
            <input type="checkbox" v-model="item.enabled"/>
            <span class="method-label" :data-method="item.request.method">{{ item.request.method }}</span>
            <span>{{ item.request.name }}</span>
          </label>
          <p v-if="!runnerRequests.length" class="muted">{{ t.NoRequestsInCollection() }}</p>
        </section>
        <section class="runner-options">
          <h3>{{ t.RunConfiguration() }}</h3>
          <label class="field">{{ t.Iterations() }}<input type="number" min="1" v-model.number="iterations"/></label>
          <label class="field">{{ t.Delay() }}<input type="number" min="0" :max="Limits.batchDelayMs" v-model.number="delayMs"/></label>
          <label class="field">{{ t.TestDataFile() }}<input type="file" accept=".json,.csv,application/json,text/csv" @change="dataFileChanged"/></label>
          <p v-if="dataFileName" class="muted">{{ t.DataFileSummary({name: dataFileName, rows: iterationData.length}) }}</p>
          <p v-if="dataFileError" class="error">{{ t.InvalidDataFile() }}</p>
          <h3>{{ t.AdvancedSettings() }}</h3>
          <label class="check"><input type="checkbox" v-model="persistResponses"/>{{ t.PersistResponses() }}</label>
          <label class="check"><input type="checkbox" v-model="stopOnError"/>{{ t.StopOnError() }}</label>
          <button class="primary runner-start" :disabled="!runReady || startingBatch" @click="runBatch">
            <Icon name="play"/>{{ t.RunCollection() }}
          </button>
        </section>
      </div>
    </template>
    <template v-else-if="batch">
      <header class="runner-header">
        <div><h2>{{ t.Results() }}</h2>
          <p>{{ t.Progress({done: batch.results.length, total: batch.total, elapsed: Math.round(batch.elapsedMs)}) }}</p>
          <p v-if="batch.current">{{ t.Running({name: batch.current}) }}</p></div>
        <button v-if="!batch.done" class="danger" @click="cancelBatch"><Icon name="stop"/>{{ t.StopRun() }}</button>
      </header>
      <div class="runner-summary">
        <span v-for="item in executionCounts" :key="item.state">{{ execution(item.state) }}: {{ item.count }}</span>
        <span v-for="item in testCounts" :key="item.state">{{ test(item.state) }}: {{ item.count }}</span>
        <span>{{ t.TestResults() }}: {{ t.Pass() }} {{ testResultCounts.passed }} · {{ t.Fail() }} {{ testResultCounts.failed }}</span>
      </div>
      <div class="runner-filter-tabs" role="tablist" :aria-label="t.ResultFilters()">
        <button v-for="item in resultFilters" :key="item.value" role="tab" :aria-selected="filter === item.value"
                :class="{selected: filter === item.value}" @click="filter = item.value">{{ item.label }} <span>{{ item.count }}</span></button>
      </div>
      <div class="runner-content">
        <nav class="runner-result-list" :aria-label="t.Results()">
          <button v-for="item in filteredResults" :key="item.index" :class="{selected: item.index === selectedRunResultIndex}"
                  :aria-pressed="item.index === selectedRunResultIndex" @click="selectedRunResultIndex = item.index">
            <span class="runner-result-name">{{ item.result.name }}</span>
            <span class="muted">{{ item.result.iteration && item.result.iteration > 1 ? t.Iteration({number: item.result.iteration}) + ' · ' : '' }}{{ execution(item.result.execution) }} · {{ test(item.result.test) }}</span>
          </button>
          <div v-if="!filteredResults.length" class="empty-response"><Icon name="inbox"/><p>{{ batch.current ? t.Running({name: batch.current}) : t.NoMatchingResults() }}</p></div>
        </nav>
        <section class="runner-result-detail">
          <div v-if="filter === ResultFilter.Console && selectedResult" class="runner-console">
            <p v-if="!selectedResult.scriptLogs?.length" class="muted">{{ t.NoScriptLogs() }}</p>
            <div v-for="(log, index) in selectedResult.scriptLogs" :key="index" :class="log.level === ScriptLogLevel.Error ? 'error' : ''">
              <span class="muted">{{ log.phase === ScriptPhase.Before ? t.BeforeScript() : t.AfterScript() }} · {{ log.level }}</span> {{ log.text }}
            </div>
          </div>
          <ResponsePanel v-else-if="selectedResult" :result="selectedResult" :run-id="batchId" :index="selectedRunResultIndex"/>
          <div v-else class="empty-response"><Icon name="inbox"/><p>{{ t.NoResults() }}</p></div>
        </section>
      </div>
    </template>
  </div>
</template>
