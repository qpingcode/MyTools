<script setup lang="ts">
import {computed, ref, watch, onBeforeUnmount, nextTick} from 'vue';
import {useWorkspaceContext} from './context.js';
import {Routes, Limits, type HistoryEntry} from '../../../shared/model.js';
import {
  exportWorkspace,
  exportPostman,
  importWorkspace,
  importCurl,
  resolveRequestVariables,
  ExportFormat
} from '../../../shared/interchange.js';
import {rpc, notification} from '../../services/rpc.js';
import ResponsePanel from '../response/ResponsePanel.vue';
import IconButton from '../../components/common/IconButton.vue';
import Icon from '../../components/common/Icon.vue';
import {WorkspaceTool as ToolTab} from './workspaceToolTypes.js';

const props = defineProps<{ tool: ToolTab }>();
const ToolIcons = {[ToolTab.Import]: 'download', [ToolTab.Export]: 'upload', [ToolTab.History]: 'history'} as const;

enum ImportKind { Curl = 'curl', Json = 'json' }

enum ExportTarget { Workspace = 'workspace', Collection = 'collection', Environment = 'environment' }

const {t, workspace, mutate, open, uid, batch, tabs, confirm} = useWorkspaceContext();
const visible = ref(false);
const dialog = ref<HTMLDialogElement>();
const current = computed(() => props.tool);
const title = computed(() => props.tool === ToolTab.Import ? t.value.Import() : props.tool === ToolTab.Export ? t.value.Export() : t.value.History());
const kind = ref(ImportKind.Curl);
const source = ref('');
const format = ref(ExportFormat.Native);
const target = ref(ExportTarget.Workspace);
const targetId = ref('');
const history = ref<HistoryEntry[]>([]);
const selected = ref<HistoryEntry>();
const search = ref('');
const busy = ref(false);
const error = ref(false);
const historyFailed = ref(false);
const exportCollection = computed(() => workspace.value.collections.find(c => c.id === targetId.value));
const exportEnvironment = computed(() => workspace.value.environments.find(e => e.id === targetId.value));
const filteredHistory = computed(() => history.value.filter(e => (e.request.name + ' ' + e.request.url + ' ' + e.environmentName).toLowerCase().includes(search.value.toLowerCase())));
const running = computed(() => tabs.value.some(tab => tab.running) || batch.value && !batch.value.done);
watch(target, () => {
  targetId.value = target.value === ExportTarget.Collection ? workspace.value.collections[0]?.id || '' : workspace.value.environments[0]?.id || '';
  format.value = ExportFormat.Native;
});
watch(visible, async value => {
  if (value) {
    await nextTick();
    dialog.value?.showModal();
    if (current.value === ToolTab.History) void loadHistory();
  } else dialog.value?.close();
});
watch(running, value => {
  if (!value && visible.value && current.value === ToolTab.History) void loadHistory();
});
onBeforeUnmount(() => dialog.value?.close());

async function loadHistory() {
  historyFailed.value = false;
  try {
    history.value = await rpc<HistoryEntry[]>(Routes.history);
  } catch (failure) {
    historyFailed.value = true;
    console.error(failure);
  }
}

async function importData() {
  if (busy.value) return;
  busy.value = true;
  error.value = false;
  try {
    if (new TextEncoder().encode(source.value).length > Limits.importBytes) throw new Error('Import too large');
    if (kind.value === ImportKind.Curl) open(importCurl(source.value, uid(), t.value.ImportedRequest()));
    else {
      const imported = importWorkspace(source.value, uid);
      await mutate(() => {
        workspace.value.collections.push(...imported.collections);
        workspace.value.environments.push(...imported.environments);
      });
    }
    notification.value = t.value.Imported();
    visible.value = false;
    source.value = '';
  } catch (failure) {
    error.value = true;
    console.error(failure);
  } finally {
    busy.value = false;
  }
}

async function readFile(event: Event) {
  const input = event.target as HTMLInputElement;
  const file = input.files?.[0];
  if (!file) return;
  error.value = false;
  try {
    if (file.size > Limits.importBytes) throw new Error('Import too large');
    source.value = await file.text();
    kind.value = ImportKind.Json;
  } catch (failure) {
    error.value = true;
    console.error(failure);
  }
  input.value = '';
}

function download() {
  const collection = target.value === ExportTarget.Collection ? exportCollection.value : undefined;
  const environment = target.value === ExportTarget.Environment ? exportEnvironment.value : undefined;
  if (target.value !== ExportTarget.Workspace && !collection && !environment) return;
  const contents = format.value === ExportFormat.Postman && collection ? exportPostman(collection) : exportWorkspace(workspace.value, collection, environment);
  const url = URL.createObjectURL(new Blob([contents], {type: 'application/json'}));
  const link = document.createElement('a');
  link.href = url;
  link.download = ExportFilename;
  link.click();
  setTimeout(() => URL.revokeObjectURL(url), Limits.pollMs);
}

const ExportFilename = 'api-tester-export.json';

async function clearHistory() {
  if (!(await confirm(t.value.ClearHistoryConfirm, t.value.ClearHistory))) return;
  try {
    await rpc(Routes.clearHistory);
    selected.value = undefined;
    await loadHistory();
  } catch (failure) {
    console.error(failure);
  }
}

function reopen(entry: HistoryEntry) {
  // Materialize recorded variable values, so a changed active environment cannot alter replay.
  const variables = Object.fromEntries(entry.variables.filter(v => v.enabled).map(v => [v.name, v.value]));
  const replace = (text: string) => text.replace(/\{\{([^{}]+)\}\}/g, (match, name) => Object.hasOwn(variables, name) ? variables[name] : match);
  const request = resolveRequestVariables(entry.request, replace);
  request.id = uid();
  open(request);
  visible.value = false;
}

function reopenSelected() {
  if (selected.value) reopen(selected.value);
}
</script>
<template>
  <button class="workspace-tools-button" @click="visible = true">
    <Icon :name="ToolIcons[tool]"/>
    {{ title }}
  </button>
  <dialog v-if="visible" ref="dialog" class="standard-dialog feature-dialog workspace-tools-dialog" :aria-label="title"
          @close.self="visible = false" @cancel.self="visible = false">
    <div class="dialog-titlebar"><h2>{{ title }}</h2>
      <IconButton icon="close" :label="t.Close()" @click="visible = false"/>
    </div>
    <div v-if="current === ToolTab.Import" class="dialog-content tools-content">
      <label class="field">{{ t.ImportFormat() }}<select v-model="kind">
        <option :value="ImportKind.Curl">cURL</option>
        <option :value="ImportKind.Json">{{ t.JsonImportFormat() }}</option>
      </select></label>
      <label class="field">{{ t.ImportSource() }}<textarea class="script-code" v-model="source"
                                                           spellcheck="false"/></label>
      <label class="field">{{ t.ImportFile() }}<input type="file" accept=".json,application/json"
                                                      @change="readFile"/></label>
      <p class="muted">{{ t.ImportHint() }}</p>
      <p v-if="error" role="alert" class="error">{{ t.ImportFailed() }}</p>
    </div>
    <div v-if="current === ToolTab.Export" class="dialog-content tools-content">
      <label class="field">{{ t.ExportTarget() }}<select v-model="target">
        <option :value="ExportTarget.Workspace">{{ t.WorkspaceBackup() }}</option>
        <option :value="ExportTarget.Collection">{{ t.Collections() }}</option>
        <option :value="ExportTarget.Environment">{{ t.Environments() }}</option>
      </select></label>
      <label v-if="target === ExportTarget.Collection" class="field">{{ t.Collections() }}<select v-model="targetId">
        <option v-for="c in workspace.collections" :key="c.id" :value="c.id">{{ c.name }}</option>
      </select></label>
      <label v-if="target === ExportTarget.Environment" class="field">{{ t.Environments() }}<select v-model="targetId">
        <option v-for="e in workspace.environments" :key="e.id" :value="e.id">{{ e.name }}</option>
      </select></label>
      <label class="field">{{ t.ExportFormat() }}<select v-model="format">
        <option :value="ExportFormat.Native">{{ t.NativeFormat() }}</option>
        <option v-if="target === ExportTarget.Collection" :value="ExportFormat.Postman">{{ t.PostmanFormat() }}</option>
      </select></label>
      <p class="muted">{{ t.ExportHint() }}</p>
    </div>
    <div v-if="current === ToolTab.History" class="tools-history">
      <div class="body-toolbar"><input v-model="search" :aria-label="t.SearchHistory()"
                                       :placeholder="t.SearchHistory()"/></div>
      <p v-if="historyFailed" role="alert" class="error">{{ t.HistoryFailed() }}</p>
      <p v-else-if="!filteredHistory.length" class="muted">{{ t.NoHistory() }}</p>
      <div class="history-list">
        <button v-for="entry in filteredHistory" :key="entry.id" class="history-row"
                :class="{ selected: selected?.id === entry.id }" @click="selected = entry"><span class="method-label"
                                                                                                 :data-method="entry.request.method">{{
            entry.request.method
          }}</span><span>{{ entry.request.name }} · {{ entry.request.url }}</span><span
            class="muted">{{ entry.result.status ?? '—' }} · {{
            entry.environmentName || t.NoEnvironment()
          }} · {{ entry.result.completedAt }}</span></button>
      </div>
      <section v-if="selected" class="history-detail">
        <div class="section-heading"><h2>{{ selected.request.name }}</h2></div>
        <p class="muted">{{ t.HistoryHint() }}</p>
        <ResponsePanel :key="selected.id" :result="selected.result" run-id="" :index="0"/>
      </section>
    </div>
    <div class="dialog-actions">
      <button @click="visible = false">{{ t.Cancel() }}</button>
      <template v-if="current === ToolTab.History">
        <button @click="loadHistory">{{ t.Refresh() }}</button>
        <button :disabled="!history.length" @click="clearHistory">{{ t.ClearHistory() }}</button>
        <button class="primary" :disabled="!selected" @click="reopenSelected">{{ t.ReopenRequest() }}</button>
      </template>
      <button v-if="current === ToolTab.Import" class="primary" :disabled="busy || !source.trim()" @click="importData">
        {{ t.Import() }}
      </button>
      <button v-if="current === ToolTab.Export" class="primary"
              :disabled="target !== ExportTarget.Workspace && !targetId" @click="download">{{ t.Export() }}
      </button>
    </div>
  </dialog>
</template>
