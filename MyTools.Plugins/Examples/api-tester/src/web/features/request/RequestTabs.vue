<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, ref, watch} from 'vue';
import {useWorkspaceContext} from '../workspace/context.js';
import IconButton from '../../components/common/IconButton.vue';
import Icon from '../../components/common/Icon.vue';
import RequestTabContextMenu from './RequestTabContextMenu.vue';
import type {Tab} from '../workspace/workspaceTypes.js';
import {ListDropKind, listInsertionIndex} from '../../../shared/listReorder.js';
import {
  documentTabRevealDirection,
  DocumentTabRevealIntervalMs,
  DocumentTabRevealNone,
} from './documentTabDragScroll.js';

enum DocumentKind { Request = 'request', Runner = 'runner' }
type Document =
    | {kind: DocumentKind.Request; id: string; item: Tab}
    | {kind: DocumentKind.Runner; id: string};
type DocumentTabDropTarget = {tabId: string; kind: ListDropKind};
type DocumentTabDragSession = {
  tabId: string;
  pointerId: number;
  startX: number;
  startY: number;
  active: boolean;
};

const DocumentTabDragThresholdPx = 12;
const DocumentTabDragButton = 0;

const {
  t, tabs, documentTabIds, RunnerTabId, active, batch, runnerOpen, runnerActive, dirty,
  closeTab, activateRequest, showRunner, closeRunner, createRequest, duplicateRequest,
  closeOtherTabs, closeAllTabs, revealInSidebar, workspace, reorderDocumentTab,
} = useWorkspaceContext();
const tabBar = ref<HTMLDivElement>();
const overflowMenu = ref<HTMLDivElement>();
const overflowTrigger = ref<HTMLDivElement>();
const capacity = ref(1);
const firstVisible = ref(0);
const menuOpen = ref(false);
const contextMenu = ref<{item: Tab; x: number; y: number; trigger: HTMLElement}>();
const dragSession = ref<DocumentTabDragSession>();
const dropTarget = ref<DocumentTabDropTarget>();
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
const dragTabId = computed(() => dragSession.value?.active ? dragSession.value.tabId : '');
let resizeObserver: ResizeObserver | undefined;
let dragPointerX = 0;
let dragPointerY = 0;
let revealFrame = 0;
let lastRevealAt = 0;

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
  if (dragSession.value?.active) return;
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

function tabClass(document: Document) {
  return {
    selected: document.id === activeDocumentId.value,
    dragging: dragTabId.value === document.id,
    'drop-before': dropTarget.value?.tabId === document.id && dropTarget.value.kind === ListDropKind.Before,
    'drop-after': dropTarget.value?.tabId === document.id && dropTarget.value.kind === ListDropKind.After,
  };
}

function suppressNextClick() {
  const timeout = window.setTimeout(() => document.removeEventListener('click', suppress, true));
  function suppress(event: Event) {
    event.preventDefault();
    event.stopPropagation();
    window.clearTimeout(timeout);
    document.removeEventListener('click', suppress, true);
  }
  document.addEventListener('click', suppress, true);
}

function stopDocumentTabReveal() {
  if (!revealFrame) return;
  cancelAnimationFrame(revealFrame);
  revealFrame = 0;
  lastRevealAt = 0;
}

function clampFirstVisible(value: number) {
  return Math.max(0, Math.min(value, Math.max(0, documents.value.length - capacity.value)));
}

function tickDocumentTabReveal(now: number) {
  revealFrame = requestAnimationFrame(tickDocumentTabReveal);
  const session = dragSession.value;
  const bar = tabBar.value;
  if (!session?.active || !bar) return;
  const direction = documentTabRevealDirection(dragPointerX, dragPointerY, bar.getBoundingClientRect());
  if (direction === DocumentTabRevealNone) {
    lastRevealAt = 0;
    return;
  }
  if (lastRevealAt && now - lastRevealAt < DocumentTabRevealIntervalMs) return;
  const next = clampFirstVisible(firstVisible.value + direction);
  if (next === firstVisible.value) return;
  firstVisible.value = next;
  lastRevealAt = now;
  dropTarget.value = dropFromPoint(dragPointerX, dragPointerY, session.tabId);
}

function startDocumentTabReveal() {
  if (revealFrame) return;
  revealFrame = requestAnimationFrame(tickDocumentTabReveal);
}

function endDocumentTabDrag() {
  stopDocumentTabReveal();
  const bar = tabBar.value;
  const session = dragSession.value;
  if (bar && session?.pointerId !== undefined && bar.hasPointerCapture(session.pointerId)) {
    bar.releasePointerCapture(session.pointerId);
  }
  window.removeEventListener('pointermove', onDocumentTabDragMove);
  window.removeEventListener('pointerup', onDocumentTabDragEnd);
  window.removeEventListener('pointercancel', onDocumentTabDragEnd);
  dragSession.value = undefined;
  dropTarget.value = undefined;
}

function dropFromPoint(x: number, y: number, tabId: string): DocumentTabDropTarget | undefined {
  const element = document.elementFromPoint(x, y);
  const tab = element?.closest<HTMLElement>('.document-tab');
  if (tab?.dataset.documentId && tab.dataset.documentId !== tabId) {
    const bounds = tab.getBoundingClientRect();
    return {
      tabId: tab.dataset.documentId,
      kind: x < bounds.left + bounds.width / 2 ? ListDropKind.Before : ListDropKind.After,
    };
  }
  if (element?.closest('.request-tabs-overflow, .request-tabs-menu')) {
    const last = visibleDocuments.value.findLast(item => item.id !== tabId);
    if (last) return {tabId: last.id, kind: ListDropKind.After};
  }
}

function onDocumentTabDragMove(event: PointerEvent) {
  const session = dragSession.value;
  const bar = tabBar.value;
  if (!session || event.pointerId !== session.pointerId) return;
  const deltaX = event.clientX - session.startX;
  const deltaY = event.clientY - session.startY;
  if (!session.active) {
    if (deltaX * deltaX + deltaY * deltaY < DocumentTabDragThresholdPx * DocumentTabDragThresholdPx) return;
    bar?.setPointerCapture(session.pointerId);
    dragSession.value = {...session, active: true};
    dragPointerX = event.clientX;
    dragPointerY = event.clientY;
    menuOpen.value = false;
    startDocumentTabReveal();
  }
  event.preventDefault();
  dragPointerX = event.clientX;
  dragPointerY = event.clientY;
  dropTarget.value = dropFromPoint(event.clientX, event.clientY, session.tabId);
}

function onDocumentTabDragEnd(event: PointerEvent) {
  const session = dragSession.value;
  if (!session || event.pointerId !== session.pointerId) return;
  const target = session.active ? dropTarget.value : undefined;
  const tabId = session.tabId;
  const dragged = session.active;
  endDocumentTabDrag();
  if (!dragged || !target) return;
  suppressNextClick();
  reorderDocumentTab(tabId, listInsertionIndex(documentTabIds.value, target.tabId, target.kind));
}

function startDocumentTabDrag(event: PointerEvent, tabId: string) {
  if (event.button !== DocumentTabDragButton || event.pointerType === 'touch') return;
  if ((event.target as HTMLElement).closest('.icon-button')) return;
  endDocumentTabDrag();
  dragSession.value = {
    tabId,
    pointerId: event.pointerId,
    startX: event.clientX,
    startY: event.clientY,
    active: false,
  };
  window.addEventListener('pointermove', onDocumentTabDragMove, {passive: false});
  window.addEventListener('pointerup', onDocumentTabDragEnd);
  window.addEventListener('pointercancel', onDocumentTabDragEnd);
}

onMounted(() => {
  measure();
  resizeObserver = new ResizeObserver(measure);
  if (tabBar.value) resizeObserver.observe(tabBar.value);
  document.addEventListener('pointerdown', outsideClick);
});
onBeforeUnmount(() => {
  endDocumentTabDrag();
  resizeObserver?.disconnect();
  document.removeEventListener('pointerdown', outsideClick);
});
</script>
<template>
  <div ref="tabBar" class="document-tabs-bar" :class="{'document-tab-dragging': Boolean(dragTabId)}">
    <div class="document-tabs">
      <div v-for="document in visibleDocuments" :key="document.id" class="document-tab"
           :class="tabClass(document)" :data-document-id="document.id" :title="t.DragDocumentTab()"
           @pointerdown="startDocumentTabDrag($event, document.id)"
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
