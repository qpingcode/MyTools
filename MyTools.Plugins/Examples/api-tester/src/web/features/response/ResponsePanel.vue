<script setup lang="ts">
import IconButton from '../../components/common/IconButton.vue';
import Icon from '../../components/common/Icon.vue';
import ResponseBodyView from './ResponseBodyView.vue';
import {computed, ref} from 'vue';
import {
  HttpHeader,
  Routes,
  ExecutionState,
  ErrorKind,
  Limits,
  ScriptPhase,
  ScriptLogLevel,
  type RequestResult,
} from '../../../shared/model.js';
import {rpc, errorCaption, errorDetails, notification} from '../../services/rpc.js';
import {useText} from '../../localization/locale.js';
import WorkspaceTools from '../workspace/WorkspaceTools.vue';
import {WorkspaceTool} from '../workspace/workspaceToolTypes.js';
import {useWorkspaceContext} from '../workspace/context.js';
import {ResponsePanelId} from '../workspace/workspaceTypes.js';
import {httpStatusCategory} from './httpStatusCategory.js';

const props = defineProps<{
  result?: RequestResult;
  runId: string;
  index: number;
  requestId?: string;
  requestName?: string;
  maximizable?: boolean;
  maximized?: boolean;
}>();
const emit = defineEmits<{toggleMaximized: []}>();
const t = useText();
const {responsePanels: responsePanelSelections} = useWorkspaceContext();
const hasHttpResponse = computed(() => props.result?.status !== undefined);
const standaloneError = computed(() => !hasHttpResponse.value ? props.result?.error : undefined);
const responseError = computed(() => hasHttpResponse.value ? props.result?.error : undefined);
const errorCancelled = computed(() => standaloneError.value?.kind === ErrorKind.Cancelled);

function currentResult(): RequestResult {
  if (!props.result) throw new Error('Response result is unavailable');
  return props.result;
}

function execution(state: ExecutionState) {
  return {
    [ExecutionState.Complete]: t.value.Complete,
    [ExecutionState.Failed]: t.value.Failed,
    [ExecutionState.Cancelled]: t.value.Cancelled,
    [ExecutionState.Skipped]: t.value.Skipped,
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
  const result = currentResult();
  const type =
      result.headers.find(
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
  const result = currentResult();
  const url = URL.createObjectURL(new Blob([bytes]));
  const anchor = document.createElement('a');
  anchor.href = url;
  anchor.download = result.binary ? 'response.bin' : 'response.txt';
  anchor.click();
  setTimeout(() => URL.revokeObjectURL(url), Limits.pollMs);
}

async function copyHeaders() {
  await navigator.clipboard.writeText(
      currentResult().headers.map((h) => `${h.name}: ${h.value}`).join('\r\n'),
  );
}

const fallbackPanel = ref(ResponsePanelId.Body);
const viewStateRequestId = computed(() => props.requestId || props.result?.requestId || '');
const panel = computed({
  get: () => viewStateRequestId.value
      ? responsePanelSelections.value[viewStateRequestId.value] ?? ResponsePanelId.Body
      : fallbackPanel.value,
  set: value => {
    if (viewStateRequestId.value) responsePanelSelections.value[viewStateRequestId.value] = value;
    else fallbackPanel.value = value;
  },
});
const responsePanels = [
  ResponsePanelId.Body,
  ResponsePanelId.ResponseHeaders,
  ResponsePanelId.RequestHeaders,
  ResponsePanelId.TestResults,
  ResponsePanelId.ScriptConsole,
] as const;

function panelLabel(item: ResponsePanelId) {
  return item === ResponsePanelId.Body ? t.value.ResponseBody() : t.value[item]();
}

function toggleMaximizedFromToolbar(event: MouseEvent) {
  const target = event.target;
  if (!props.maximizable || target instanceof Element && target.closest('button, select, input, textarea, a')) return;
  event.preventDefault();
  emit('toggleMaximized');
}
</script>
<template>
  <div class="response-panel">
    <div class="response-toolbar" @dblclick="toggleMaximizedFromToolbar">
      <IconButton
          v-if="maximizable"
          class="response-maximize-button"
          :icon="maximized ? 'chevron-down' : 'chevron-up'"
          :label="maximized ? t.RestoreRequestResponse() : t.MaximizeResponse()"
          :aria-pressed="maximized"
          @click="emit('toggleMaximized')"
      />
      <div v-if="result && hasHttpResponse" class="config-tabs response-tabs">
        <button
            v-for="item in responsePanels"
            :key="item"
            :class="{ selected: panel === item }"
            :aria-pressed="panel === item"
            @click="panel = item"
        >
          {{ panelLabel(item) }}
        </button>
      </div>
      <select v-if="result && hasHttpResponse" v-model="panel" class="panel-select response-panel-select" :aria-label="t.Response()">
        <option v-for="item in responsePanels" :key="item" :value="item">
          {{ panelLabel(item) }}
        </option>
      </select>
      <div v-if="result && hasHttpResponse" class="response-toolbar-trailing">
        <div class="response-meta" :title="result.url">
          <span class="status-badge" :data-state="result.execution"
                :data-status-category="httpStatusCategory(result.status)"
          >{{ result.status ?? execution(result.execution) }}
            {{ result.statusText }}</span
          ><span class="muted">{{
              t.Summary({
                elapsed: Math.round(result.elapsedMs),
                size: result.size,
              })
            }}</span>
        </div>
        <div class="icon-actions">
          <WorkspaceTools v-if="requestId" :tool="WorkspaceTool.History" :request-id="requestId"
                          :request-name="requestName" icon-only/>
          <IconButton
              icon="copy"
              :label="t.Copy()"
              :disabled="result.binary || !result.bodyAvailable"
              @click="copy"
          />
          <IconButton
              icon="file"
              :label="t.CopyHeaders()"
              @click="copyHeaders"
          />
          <IconButton
              icon="download"
              :label="t.SaveBody()"
              :disabled="!result.bodyAvailable"
              @click="save"
          />
        </div>
      </div>
      <div v-else class="response-toolbar-trailing">
        <div class="icon-actions">
          <WorkspaceTools v-if="requestId" :tool="WorkspaceTool.History" :request-id="requestId"
                          :request-name="requestName" icon-only/>
        </div>
      </div>
    </div>
    <slot v-if="!result" name="empty"/>
    <div v-else-if="standaloneError" class="response-error-state" role="alert">
      <div class="response-error-card" :data-cancelled="errorCancelled">
        <Icon name="alert-circle"/>
        <h2>{{ errorCaption(standaloneError) }}</h2>
        <p>{{ t.NoHttpResponse() }}</p>
        <pre v-if="errorDetails(standaloneError)">{{ errorDetails(standaloneError) }}</pre>
      </div>
    </div>
    <template v-else>
    <div v-if="responseError" class="response-error-banner" role="alert">
      <Icon name="alert-circle"/>
      <div><strong>{{ errorCaption(responseError) }}</strong>
        <pre v-if="errorDetails(responseError)">{{ errorDetails(responseError) }}</pre>
      </div>
    </div>
    <p v-if="result.warnings.length" class="muted response-warning">
      {{ t.ContentTypeWarning() }}
    </p>
    <ResponseBodyView v-if="panel === ResponsePanelId.Body" :result="result" :request-id="viewStateRequestId"/>
    <div v-if="panel === ResponsePanelId.ScriptConsole" class="response-code script-console">
      <p v-if="!result.scriptLogs?.length" class="muted">{{ t.NoScriptLogs() }}</p>
      <div v-for="(log, index) in result.scriptLogs" :key="index"
           :class="log.level === ScriptLogLevel.Error ? 'error' : ''"><span
          class="muted">{{ log.phase === ScriptPhase.Before ? t.BeforeScript() : t.AfterScript() }} · {{
          log.level
        }}</span> {{ log.text }}
      </div>
    </div>
    <div v-if="panel === ResponsePanelId.ResponseHeaders || panel === ResponsePanelId.RequestHeaders" class="response-table-scroll">
      <table class="response-headers-table">
        <colgroup>
          <col class="response-header-name-column"/>
          <col class="response-header-value-column"/>
        </colgroup>
        <thead>
        <tr>
          <th>{{ t.Key() }}</th>
          <th>{{ t.Value() }}</th>
        </tr>
        </thead>
        <tbody>
        <tr
            v-for="(header, index) in panel === ResponsePanelId.ResponseHeaders
            ? result.headers
            : result.requestHeaders"
            :key="index"
        >
          <td>{{ header.name }}</td>
          <td>{{ header.value }}</td>
        </tr>
        </tbody>
      </table>
    </div>
    <div v-if="panel === ResponsePanelId.TestResults && result.assertions.length" class="response-table-scroll">
      <table class="response-assertions-table">
        <colgroup>
          <col class="response-assertion-name-column"/>
          <col class="response-assertion-value-column"/>
          <col class="response-assertion-value-column"/>
        </colgroup>
        <thead>
        <tr>
          <th>{{ t.TestResults() }}</th>
          <th>{{ t.Actual() }}</th>
          <th>{{ t.Expected() }}</th>
        </tr>
        </thead>
        <tbody>
        <tr v-for="(assertion, index) in result.assertions" :key="index">
          <td :class="assertion.passed ? 'pass' : 'error'">
            {{ assertion.passed ? t.Pass() : t.Fail() }} ·
            {{ assertion.name || t.Status() }}
          </td>
          <td>
            {{ assertion.actualMissing ? t.Missing() : assertion.actual }}
            <pre v-if="assertion.detail">{{ assertion.detail }}</pre>
          </td>
          <td>{{ assertion.expected }}</td>
        </tr>
        </tbody>
      </table>
    </div>
    <p v-if="panel === ResponsePanelId.TestResults && !result.assertions.length" class="muted">
      {{ t.Untested() }}
    </p>
    </template>
  </div>
</template>
