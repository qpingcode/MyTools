<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, ref, watch} from 'vue';
import {useWorkspaceContext} from '../workspace/context.js';
import IconButton from '../../components/common/IconButton.vue';
import Icon from '../../components/common/Icon.vue';
import RequestTabContextMenu from './RequestTabContextMenu.vue';
import type {Tab} from '../workspace/workspaceTypes.js';

enum DocumentKind { Request = 'request', Runner = 'runner' }
type Document =
    | {kind: DocumentKind.Request; id: string; item: Tab}
    | {kind: DocumentKind.Runner; id: string};

const {
  t, tabs, documentTabIds, RunnerTabId, active, batch, runnerOpen, runnerActive, dirty,
  closeTab, activateRequest, showRunner, closeRunner, createRequest, duplicateRequest,
  closeOtherTabs, closeAllTabs, revealInSidebar, workspace,
} = useWorkspaceContext();
const tabBar = ref<HTMLDivElement>();
const overflowMenu = ref<HTMLDivElement>();
const overflowTrigger = ref<HTMLDivElement>();
const capacity = ref(1);
const firstVisible = ref(0);
const menuOpen = ref(false);
const contextMenu = ref<{item: Tab; x: number; y: number; trigger: HTMLElement}>();
const documents = computed<Document[]>(() => {
  const values: Document[] = [];
  for (const id of documentTabIds.value) {
    if (id === RunnerTabId) {
      if (runnerOpen.value) values.push({kind: DocumentKind.Runner, id});
      continue;
    }
    const item = tabs.value.find(tab => tab.request.id === id);
    if (item) values.push({kind: DocumentKind.Request, id, item});
  }
  return values;
});
const visibleDocuments = computed(() => documents.value.slice(firstVisible.value, firstVisible.value + capacity.value));
const hiddenDocuments = computed(() => documents.value.filter(item => !visibleDocuments.value.includes(item)));
const activeDocumentId = computed(() => runnerActive.value ? RunnerTabId : active.value);
let resizeObserver: ResizeObserver | undefined;

function measure() {
  if (!tabBar.value) return;
  const styles = getComputedStyle(tabBar.value);
  const tabWidth = parseFloat(styles.getPropertyValue('--request-tab-width'));
  const overflowWidth = parseFloat(styles.getPropertyValue('--request-tabs-overflow-width'));
  const width = tabBar.value.clientWidth;
  const reservedWidth = documents.value.length * tabWidth > width ? overflowWidth : 0;
  capacity.value = Math.max(1, Math.floor((width - reservedWidth) / tabWidth));
  revealActive();
}

function revealActive() {
  firstVisible.value = Math.max(0, Math.min(firstVisible.value, documents.value.length - capacity.value));
  const index = documents.value.findIndex(item => item.id === activeDocumentId.value);
  if (index < 0) return;
  if (index < firstVisible.value) firstVisible.value = index;
  else if (index >= firstVisible.value + capacity.value) firstVisible.value = index - capacity.value + 1;
}

watch([() => documents.value.map(item => item.id), activeDocumentId], measure, {flush: 'post'});
watch([activeDocumentId, capacity], () => {
  revealActive();
  menuOpen.value = false;
});

async function toggleMenu() {
  menuOpen.value = !menuOpen.value;
  if (menuOpen.value) {
    await nextTick();
    overflowMenu.value?.querySelector<HTMLButtonElement>('button')?.focus();
  }
}

function closeMenu() {
  menuOpen.value = false;
  overflowTrigger.value?.querySelector('button')?.focus();
}

function navigateMenu(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.preventDefault();
    closeMenu();
    return;
  }
  if (event.key === 'Tab') {
    menuOpen.value = false;
    return;
  }
  const buttons = Array.from(overflowMenu.value?.querySelectorAll<HTMLButtonElement>('button') || []);
  const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
  const next = event.key === 'ArrowDown' ? (index + 1) % buttons.length
      : event.key === 'ArrowUp' ? (index - 1 + buttons.length) % buttons.length
          : event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : -1;
  if (next < 0) return;
  event.preventDefault();
  buttons[next]?.focus();
}

async function selectHidden(document: Document) {
  document.kind === DocumentKind.Runner ? showRunner() : activateRequest(document.id);
  menuOpen.value = false;
  await nextTick();
  tabBar.value?.querySelector<HTMLButtonElement>('.document-tab.selected > button')?.focus();
}

function outsideClick(event: PointerEvent) {
  if (!tabBar.value?.contains(event.target as Node)) menuOpen.value = false;
}

function showContextMenu(event: MouseEvent | KeyboardEvent, item: Tab) {
  if (event instanceof KeyboardEvent && event.key !== 'ContextMenu' && !(event.shiftKey && event.key === 'F10')) return;
  event.preventDefault();
  const trigger = (event.currentTarget as HTMLElement).querySelector<HTMLButtonElement>('button')!;
  const bounds = trigger.getBoundingClientRect();
  contextMenu.value = {item, trigger, x: event instanceof MouseEvent ? event.clientX : bounds.left,
    y: event instanceof MouseEvent ? event.clientY : bounds.bottom};
}

function owner(item: Tab) {
  return workspace.value.collections.find(collection => collection.id === item.collectionId);
}

onMounted(() => {
  measure();
  resizeObserver = new ResizeObserver(measure);
  if (tabBar.value) resizeObserver.observe(tabBar.value);
  document.addEventListener('pointerdown', outsideClick);
});
onBeforeUnmount(() => {
  resizeObserver?.disconnect();
  document.removeEventListener('pointerdown', outsideClick);
});
</script>
<template>
  <div ref="tabBar" class="document-tabs-bar">
    <div class="document-tabs">
      <div v-for="document in visibleDocuments" :key="document.id" class="document-tab"
           :class="{selected: document.id === activeDocumentId}"
           @contextmenu="document.kind === DocumentKind.Request && showContextMenu($event, document.item)"
           @keydown="document.kind === DocumentKind.Request && showContextMenu($event, document.item)">
        <template v-if="document.kind === DocumentKind.Request">
          <button :title="document.item.request.name" @click="activateRequest(document.id)">
            <span class="method-label" :data-method="document.item.request.method">{{ document.item.request.method }}</span>
            <span class="tab-name">{{ document.item.request.name }}</span>
            <span v-if="dirty(document.item)" class="dirty-dot" :title="t.Unsaved({name: document.item.request.name})"/>
          </button>
          <IconButton icon="close" :label="t.Close()" @click="closeTab(document.item)"/>
        </template>
        <template v-else>
          <button :title="t.Results()" @click="showRunner">
            <Icon name="play"/>
            <span class="tab-name">{{ t.Results() }}</span>
            <span v-if="batch" class="runner-tab-progress">{{ batch.results.length }}/{{ batch.total }}</span>
          </button>
          <IconButton icon="close" :label="t.Close()" @click="closeRunner"/>
        </template>
      </div>
    </div>
    <div v-if="hiddenDocuments.length" ref="overflowTrigger" class="request-tabs-overflow">
      <IconButton icon="chevron-down" :label="t.HiddenRequests()" aria-haspopup="menu" :aria-expanded="menuOpen"
                  @click="toggleMenu" @keydown.down.prevent="toggleMenu"/>
    </div>
    <div v-if="menuOpen && hiddenDocuments.length" ref="overflowMenu" class="request-tabs-menu" role="menu"
         :aria-label="t.HiddenRequests()" @keydown="navigateMenu">
      <button v-for="document in hiddenDocuments" :key="document.id" role="menuitem" @click="selectHidden(document)">
        <template v-if="document.kind === DocumentKind.Request">
          <span class="method-label" :data-method="document.item.request.method">{{ document.item.request.method }}</span>
          <span class="tab-name">{{ document.item.request.name }}</span>
          <span v-if="dirty(document.item)" class="dirty-dot" :title="t.Unsaved({name: document.item.request.name})"/>
        </template>
        <template v-else>
          <Icon name="play"/>
          <span class="tab-name">{{ t.Results() }}</span>
          <span v-if="batch" class="runner-tab-progress">{{ batch.results.length }}/{{ batch.total }}</span>
        </template>
      </button>
    </div>
    <RequestTabContextMenu v-if="contextMenu" :x="contextMenu.x" :y="contextMenu.y"
                           :trigger="contextMenu.trigger" :new-label="t.NewRequest()"
                           :duplicate-label="t.DuplicateTab()" :close-label="t.CloseTab()"
                           :close-others-label="t.CloseOtherTabs()" :close-all-label="t.CloseAllTabs()"
                           :reveal-label="t.RevealInSidebar()" @close="contextMenu = undefined"
                           @create="createRequest(owner(contextMenu.item))"
                           @duplicate="owner(contextMenu.item) && duplicateRequest(contextMenu.item.request, owner(contextMenu.item)!)"
                           @close-tab="closeTab(contextMenu.item)" @close-others="closeOtherTabs(contextMenu.item)"
                           @close-all="closeAllTabs" @reveal="revealInSidebar(contextMenu.item)"/>
  </div>
</template>
