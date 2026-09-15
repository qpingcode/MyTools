<script setup lang="ts">
import {useWorkspaceContext} from '../workspace/context.js';
import IconButton from '../../components/common/IconButton.vue';
import PairTable from '../../components/common/PairTable.vue';
import VariableInput from '../../components/common/VariableInput.vue';

const {
  HttpMethods,
  t,
  tab,
  tabs,
  workspace,
  saveTab,
  dirty,
  changeUrl,
  changeParams,
  send,
  cookies,
  refreshCookies,
} = useWorkspaceContext();
import CookiesDialog from './CookiesDialog.vue';
import {computed, ref, watch} from 'vue';
import Icon from '../../components/common/Icon.vue';
import AuthenticationEditor from '../../components/common/AuthenticationEditor.vue';
import BodyEditor from './BodyEditor.vue';
import ExtractionsEditor from './ExtractionsEditor.vue';
import RequestSettingsEditor from './RequestSettingsEditor.vue';
import AssertionsEditor from './AssertionsEditor.vue';
import ScriptsEditor from '../../components/common/ScriptsEditor.vue';
import {Routes} from '../../../shared/model.js';
import {notification, rpc} from '../../services/rpc.js';

const {environment} = useWorkspaceContext();

async function copyCurl() {
  if (!tab.value) return;
  const curl = await rpc<string>(Routes.curl, {
    request: tab.value.request,
    collection: requestCollection.value,
    variables: environment.value?.variables || [],
    environmentId: workspace.value.environmentId
  }).catch(() => undefined);
  if (!curl) return;
  try {
    await navigator.clipboard.writeText(curl);
    notification.value = t.value.Copied();
  } catch (error) {
    notification.value = t.value.CopyFailed();
    console.error(error);
  }
}

const requestCollection = computed(() =>
    workspace.value.collections.find(
        (owner) => owner.id === tab.value?.collectionId,
    ),
);
const panels = [
  'Params',
  'Headers',
  'Authentication',
  'Body',
  'Extractions',
  'Assertions',
  'Scripts',
  'Settings',
] as const;
type Panel = (typeof panels)[number];
const current = ref<Panel>('Params');
const cookiesOpen = ref(false);

async function openCookies() {
  await refreshCookies();
  cookiesOpen.value = true;
}

const remembered = new Map<string, Panel>();
watch(
    current,
    (value) => {
      if (tab.value) remembered.set(tab.value.request.id, value);
    },
    {flush: 'sync'},
);
watch(
    () => tab.value?.request.id,
    (id, oldId) => {
      if (oldId) remembered.set(oldId, current.value);
      current.value = id ? remembered.get(id) || 'Params' : 'Params';
    },
    {flush: 'sync'},
);
watch(
    () => tabs.value.map((item) => item.request.id),
    (ids) => {
      for (const id of remembered.keys())
        if (!ids.includes(id)) remembered.delete(id);
    },
);

function count(panel: Panel) {
  const request = tab.value?.request;
  if (!request) return 0;
  return panel === 'Params'
      ? request.params.length
      : panel === 'Headers'
          ? request.headers.length
          : panel === 'Extractions'
              ? request.extractions.length
              : 0;
}

function navigate(event: KeyboardEvent, panel: Panel) {
  const directions = {ArrowLeft: -1, ArrowRight: 1} as const;
  if (!(event.key in directions) && event.key !== 'Home' && event.key !== 'End')
    return;
  event.preventDefault();
  const next =
      event.key === 'Home'
          ? 0
          : event.key === 'End'
              ? panels.length - 1
              : (panels.indexOf(panel) +
                  directions[event.key as keyof typeof directions] +
                  panels.length) %
              panels.length;
  current.value = panels[next];
  (event.currentTarget as HTMLElement).parentElement
      ?.querySelectorAll<HTMLButtonElement>('[role=tab]')
      [next].focus();
}
</script>
<template>
  <div v-if="tab" class="request-editor">
    <div class="request-heading">
      <span class="breadcrumb"
      >{{ requestCollection?.name || t.Collections() }} /</span
      ><input
        class="request-name"
        v-model="tab.request.name"
        :aria-label="t.Key()"
    />
      <IconButton
          class="save-button"
          :class="{ 'has-unsaved-changes': dirty(tab) }"
          icon="save"
          :label="t.Save()"
          @click="saveTab(tab)"
      />
      <IconButton icon="copy" :label="t.CopyCurl()" @click="copyCurl"/>
    </div>
    <div class="url-bar">
      <select
          v-model="tab.request.method"
          :aria-label="t.Method()"
          :data-method="tab.request.method"
      >
        <option v-for="method in HttpMethods" :key="method">
          {{ method }}
        </option>
      </select
      >
      <VariableInput
          class="grow"
          :value="tab.request.url"
          :aria-label="t.Url()"
          :placeholder="t.UrlPlaceholder()"
          data-primary-input="true"
          @change="changeUrl($event, tab)"
      />
      <button class="primary send-button" @click="send(tab)">
        <Icon :name="tab.running ? 'stop' : 'play'"/>
        {{
          tab.running ? t.Cancel() : t.Send()
        }}
      </button>
    </div>
    <div class="config-bar">
      <div class="config-tabs" role="tablist" :aria-label="t.RequestSections()">
        <button
            v-for="panel in panels"
            :key="panel"
            role="tab"
            :id="'config-tab-' + panel"
            :aria-controls="'config-panel-' + panel"
            :aria-selected="current === panel"
            :tabindex="current === panel ? 0 : -1"
            :class="{ selected: current === panel }"
            @click="current = panel"
            @keydown="navigate($event, panel)"
        >
          {{
            t[panel]()
          }}<span v-if="count(panel)" class="tab-count" aria-hidden="true">{{
            count(panel)
          }}</span>
        </button>
      </div>
      <button
          type="button"
          class="cookies-button"
          :aria-label="t.Cookies()"
          @click="openCookies"
      >
        <Icon name="cookie"/>
        {{
          t.Cookies()
        }}<span v-if="cookies.length" class="tab-count" aria-hidden="true">{{
          cookies.length
        }}</span>
      </button>
    </div>
    <CookiesDialog v-model:open="cookiesOpen"/>
    <div
        class="config-panel"
        role="tabpanel"
        :id="'config-panel-' + current"
        :aria-labelledby="'config-tab-' + current"
    >
      <PairTable
          v-if="current === 'Params'"
          :values="tab.request.params"
          query
          @changed="changeParams(tab)"
      />
      <PairTable
          v-if="current === 'Headers'"
          :values="tab.request.headers"
          headers
      />
      <AuthenticationEditor v-if="current === 'Authentication'"/>
      <BodyEditor
          v-if="current === 'Body'"
      />
      <ExtractionsEditor
          v-if="current === 'Extractions'"
      />
      <RequestSettingsEditor v-if="current === 'Settings'"/>
      <AssertionsEditor v-if="current === 'Assertions'"/>
      <ScriptsEditor v-if="current === 'Scripts'" v-model="tab.request.scripts"/>
    </div>
  </div>
</template>
