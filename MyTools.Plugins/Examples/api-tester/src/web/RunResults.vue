<script setup lang="ts">
import { useWorkspaceContext } from './context.js';
import ResponsePanel from './ResponsePanel.vue';
const {
  t,
  batch,
  batchId,
  expandedResults,
  executionCounts,
  testCounts,
  assertionCounts,
  execution,
  test,
} = useWorkspaceContext();
</script>
<template>
  <div v-if="batch" class="run-results">
    <h2>{{ t.Results() }}</h2>
    <p>
      {{
        t.Progress({
          done: batch.results.length,
          total: batch.total,
          elapsed: Math.round(batch.elapsedMs),
        })
      }}
    </p>
    <p v-if="batch.current">{{ t.Running({ name: batch.current }) }}</p>
    <p>
      <span v-for="item in executionCounts" :key="item.state"
        >{{ execution(item.state) }}: {{ item.count }} ·
      </span>
    </p>
    <p>
      <span v-for="item in testCounts" :key="item.state"
        >{{ test(item.state) }}: {{ item.count }} ·
      </span>
    </p>
    <p>
      {{ t.Assertions() }}: {{ t.Pass() }} {{ assertionCounts.passed }} ·
      {{ t.Fail() }} {{ assertionCounts.failed }}
    </p>
    <details
      v-for="(result, index) in batch.results"
      :key="result.requestId"
      @toggle="
        ($event.target as HTMLDetailsElement).open
          ? expandedResults.add(result.requestId)
          : expandedResults.delete(result.requestId)
      "
    >
      <summary>
        {{ result.name }} · {{ execution(result.execution) }} ·
        {{ test(result.test) }}
      </summary>
      <ResponsePanel
        v-if="expandedResults.has(result.requestId)"
        :result="result"
        :run-id="batchId"
        :index="index"
      />
    </details>
  </div>
</template>
