<script setup lang="ts">
import IconButton from './IconButton.vue';
import { ref } from 'vue';
import {
  HttpHeader,
  Routes,
  ExecutionState,
  TestState,
  Limits,
  type RequestResult,
} from '../shared/model.js';
import { rpc, errorText, notification } from './rpc.js';
import { useText } from './locale.js';

const props = defineProps<{
  result: RequestResult;
  runId: string;
  index: number;
}>();
const t = useText();
const formatted = ref(false);
const formattedText = ref('');

function execution(state: ExecutionState) {
  return {
    [ExecutionState.Complete]: t.value.Complete,
    [ExecutionState.Failed]: t.value.Failed,
    [ExecutionState.Cancelled]: t.value.Cancelled,
    [ExecutionState.Skipped]: t.value.Skipped,
  }[state]();
}

function test(state: TestState) {
  return {
    [TestState.Passed]: t.value.Passed,
    [TestState.Failed]: t.value.TestFailed,
    [TestState.Untested]: t.value.Untested,
    [TestState.NotApplicable]: t.value.NotApplicable,
  }[state]();
}

async function fullBody(): Promise<Uint8Array<ArrayBuffer>> {
  const base64 = await rpc<string>(Routes.download, {
    id: props.runId,
    index: props.index,
  });
  return Uint8Array.from(atob(base64), (character) => character.charCodeAt(0));
}

async function copy() {
  const bytes = await fullBody();
  const type =
    props.result.headers.find(
      (h) => h.name.toLowerCase() === HttpHeader.ContentType,
    )?.value || '';
  const charset = /charset\s*=\s*["']?([^\s;"']+)/i.exec(type)?.[1] || 'utf-8';
  try {
    await navigator.clipboard.writeText(new TextDecoder(charset).decode(bytes));
  } catch {
    notification.value = t.value.DecodeError();
  }
}

async function save() {
  const bytes = await fullBody();
  const url = URL.createObjectURL(new Blob([bytes]));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = props.result.binary ? 'response.bin' : 'response.txt';
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), Limits.pollMs);
}

function format() {
  try {
    formattedText.value = JSON.stringify(
      JSON.parse(props.result.preview),
      null,
      2,
    ).slice(0, Limits.previewBytes);
    formatted.value = true;
  } catch {
    notification.value = t.value.BadJson();
  }
}

async function copyHeaders() {
  await navigator.clipboard.writeText(
    props.result.headers.map((h) => `${h.name}: ${h.value}`).join('\r\n'),
  );
}

const panel = ref<'Body' | 'ResponseHeaders' | 'RequestHeaders' | 'Assertions'>(
  'Body',
);
const responsePanels = [
  'Body',
  'ResponseHeaders',
  'RequestHeaders',
  'Assertions',
] as const;
</script>
<template>
  <div class="response-panel">
    <div class="response-meta" :title="result.url">
      <span class="status-badge" :data-state="result.execution"
        >{{ result.status ?? execution(result.execution) }}
        {{ result.statusText }}</span
      ><span class="result-badge" :data-state="result.test">{{
        test(result.test)
      }}</span
      ><span class="muted">{{
        t.Summary({
          status: result.status ?? '—',
          elapsed: Math.round(result.elapsedMs),
          size: result.size,
          time: result.completedAt,
        })
      }}</span>
    </div>
    <p v-if="result.error" class="error">{{ errorText(result.error) }}</p>
    <p v-if="result.warnings.length" class="muted">
      {{ t.ContentTypeWarning() }}
    </p>
    <div class="response-toolbar">
      <div class="config-tabs response-tabs">
        <button
          v-for="item in responsePanels"
          :key="item"
          :class="{ selected: panel === item }"
          :aria-pressed="panel === item"
          @click="panel = item"
        >
          {{ item === 'Body' ? t.ResponseBody() : t[item]() }}
        </button>
      </div>
      <div class="icon-actions">
        <IconButton
          icon="copy"
          :label="t.Copy()"
          :disabled="result.binary || !result.bodyAvailable"
          @click="copy"
        /><IconButton
          icon="file"
          :label="t.CopyHeaders()"
          @click="copyHeaders"
        /><IconButton
          icon="download"
          :label="t.SaveBody()"
          :disabled="!result.bodyAvailable"
          @click="save"
        />
      </div>
    </div>
    <template v-if="panel === 'Body'"
      ><div class="body-toolbar">
        <button :class="{ selected: !formatted }" @click="formatted = false">
          {{ t.Raw() }}</button
        ><button
          :class="{ selected: formatted }"
          :disabled="
            result.binary ||
            result.truncated ||
            result.previewAvailable === false
          "
          @click="format"
        >
          {{ t.Formatted() }}
        </button>
      </div>
      <p v-if="result.truncated" class="muted">{{ t.PreviewLimit() }}</p>
      <pre class="response-code">{{
        result.previewAvailable === false
          ? t.CacheError()
          : result.binary
            ? t.BinaryResponse()
            : formatted
              ? formattedText
              : result.preview
      }}</pre>
    </template>
    <table v-if="panel === 'ResponseHeaders' || panel === 'RequestHeaders'">
      <thead>
        <tr>
          <th>{{ t.Key() }}</th>
          <th>{{ t.Value() }}</th>
        </tr>
      </thead>
      <tbody>
        <tr
          v-for="(header, index) in panel === 'ResponseHeaders'
            ? result.headers
            : result.requestHeaders"
          :key="index"
        >
          <td>{{ header.name }}</td>
          <td>{{ header.value }}</td>
        </tr>
      </tbody>
    </table>
    <table v-if="panel === 'Assertions' && result.assertions.length">
      <thead>
        <tr>
          <th>{{ t.Assertions() }}</th>
          <th>{{ t.Actual() }}</th>
          <th>{{ t.Expected() }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="(assertion, index) in result.assertions" :key="index">
          <td :class="assertion.passed ? 'pass' : 'error'">
            {{ assertion.passed ? t.Pass() : t.Fail() }} · {{ assertion.name }}
          </td>
          <td>
            {{ assertion.actualMissing ? t.Missing() : assertion.actual }}
            <pre v-if="assertion.detail">{{ assertion.detail }}</pre>
          </td>
          <td>{{ assertion.expected }}</td>
        </tr>
      </tbody>
    </table>
    <p v-if="panel === 'Assertions' && !result.assertions.length" class="muted">
      {{ t.Untested() }}
    </p>
  </div>
</template>
