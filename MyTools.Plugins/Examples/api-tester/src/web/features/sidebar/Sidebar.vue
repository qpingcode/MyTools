<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, ref, watch} from 'vue';
import SidebarContextMenu from './SidebarContextMenu.vue';
import {SidebarMenuKind} from './sidebarMenuTypes.js';
import {useWorkspaceContext} from '../workspace/context.js';
import IconButton from '../../components/common/IconButton.vue';
import CollectionSettings from './CollectionSettings.vue';
import SidebarCollectionNode from './SidebarCollectionNode.vue';
import {requestInsertionIndex, RequestDropKind} from '../../../shared/requestPlacement.js';
import {requestDragScrollDelta} from './requestDragScroll.js';
import Icon from '../../components/common/Icon.vue';
import type {ApiRequest, Collection} from '../../../shared/model.js';
import {collectionMatchesSearch, rootCollections} from '../../../shared/collectionTree.js';

const RequestDragThresholdPx = 12;
const RequestDragButton = 0;

const settingsCollection = ref<Collection>();
const requestMenu = ref<{ request: ApiRequest; owner: Collection; x: number; y: number; trigger: HTMLElement }>();
const collectionMenu = ref<{ owner: Collection; x: number; y: number; trigger: HTMLElement }>();

function showCollectionMenu(event: MouseEvent | KeyboardEvent, owner: Collection) {
  if (event instanceof KeyboardEvent && event.key !== 'ContextMenu' && !(event.shiftKey && event.key === 'F10')) return;
  event.preventDefault();
  const trigger = (event.currentTarget as HTMLElement).querySelector<HTMLElement>('.collection-title')!;
  const bounds = trigger.getBoundingClientRect();
  requestMenu.value = undefined;
  collectionMenu.value = {
    owner, trigger,
    x: event instanceof MouseEvent ? event.clientX : bounds.left,
    y: event instanceof MouseEvent ? event.clientY : bounds.bottom
  };
}

function showRequestMenu(event: MouseEvent | KeyboardEvent, request: ApiRequest, owner: Collection) {
  if (event instanceof KeyboardEvent && event.key !== 'ContextMenu' && !(event.shiftKey && event.key === 'F10')) return;
  event.preventDefault();
  const row = event.currentTarget as HTMLElement;
  const trigger = row.querySelector<HTMLElement>('.request-title')!;
  const bounds = trigger.getBoundingClientRect();
  collectionMenu.value = undefined;
  requestMenu.value = {
    request, owner, trigger,
    x: event instanceof MouseEvent ? event.clientX : bounds.left,
    y: event instanceof MouseEvent ? event.clientY : bounds.bottom
  };
}

const {
  t,
  workspace,
  expandedCollectionIds,
  tabs,
  active,
  collectionId,
  search,
  addCollection,
  createRequest,
  renameCollection,
  duplicateCollection,
  deleteCollection,
  open,
  duplicateRequest,
  deleteRequest,
  relocateRequest,
  openRunner,
  selectCollection,
  revealRequestId,
} = useWorkspaceContext();
const expanded = expandedCollectionIds;

type RequestDropTarget = {collectionId: string; requestId?: string; kind: RequestDropKind};
type RequestDragSession = {
  requestId: string;
  pointerId: number;
  startX: number;
  startY: number;
  active: boolean;
  source: HTMLElement;
};

const dragSession = ref<RequestDragSession>();
const dropTarget = ref<RequestDropTarget>();
const treeElement = ref<HTMLElement>();
let dragPointerX = 0;
let dragPointerY = 0;
let dragScrollFrame = 0;
const visibleRootCollections = computed(() =>
    rootCollections(workspace.value.collections)
        .filter(owner => collectionMatchesSearch(workspace.value.collections, owner, search.value)));
const dragRequestId = computed(() => dragSession.value?.active ? dragSession.value.requestId : '');

function expandWithAncestors(id: string | undefined) {
  const next = new Set(expanded.value);
  let owner = workspace.value.collections.find(candidate => candidate.id === id);
  let changed = false;
  while (owner) {
    if (!next.has(owner.id)) {
      next.add(owner.id);
      changed = true;
    }
    owner = workspace.value.collections.find(candidate => candidate.id === owner?.parentId);
  }
  if (changed) expanded.value = next;
}

let selectingCollectionNode = false;
watch(collectionId, id => {
  if (!selectingCollectionNode) expandWithAncestors(id);
}, {immediate: true, flush: 'sync'});

function selectCollectionNode(id: string) {
  selectingCollectionNode = true;
  try {
    selectCollection(id);
  } finally {
    selectingCollectionNode = false;
  }
  toggleCollection(id);
}

function toggleCollection(id: string) {
  const next = new Set(expanded.value);
  next.has(id) ? next.delete(id) : next.add(id);
  expanded.value = next;
}

function requestTab(request: ApiRequest, owner: Collection) {
  return (
      tabs.value.find((item) => item.request.id === request.id) || {
        request,
        collectionId: owner.id,
        baseline: JSON.stringify(request),
        runId: '',
        running: false,
      }
  );
}

watch(revealRequestId, async id => {
  if (!id) return;
  const owner = workspace.value.collections.find(item => item.requests.some(request => request.id === id));
  expandWithAncestors(owner?.id);
  await nextTick();
  document.querySelector<HTMLElement>(`[data-request-id="${CSS.escape(id)}"]`)?.scrollIntoView({block: 'nearest'});
  revealRequestId.value = '';
});

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

function refreshDropTarget(x: number, y: number, requestId: string) {
  const target = dropFromPoint(x, y, requestId);
  dropTarget.value = target;
  if (target) expandWithAncestors(target.collectionId);
}

function stopRequestDragScroll() {
  if (!dragScrollFrame) return;
  cancelAnimationFrame(dragScrollFrame);
  dragScrollFrame = 0;
}

function tickRequestDragScroll() {
  dragScrollFrame = requestAnimationFrame(tickRequestDragScroll);
  const session = dragSession.value;
  const tree = treeElement.value;
  if (!session?.active || !tree) return;
  const delta = requestDragScrollDelta(dragPointerX, dragPointerY, tree.getBoundingClientRect());
  if (!delta) return;
  const previous = tree.scrollTop;
  tree.scrollTop += delta;
  if (tree.scrollTop !== previous) refreshDropTarget(dragPointerX, dragPointerY, session.requestId);
}

function startRequestDragScroll() {
  if (dragScrollFrame) return;
  dragScrollFrame = requestAnimationFrame(tickRequestDragScroll);
}

function endRequestDrag() {
  stopRequestDragScroll();
  const session = dragSession.value;
  if (session?.source.hasPointerCapture(session.pointerId)) session.source.releasePointerCapture(session.pointerId);
  window.removeEventListener('pointermove', onRequestDragMove);
  window.removeEventListener('pointerup', onRequestDragEnd);
  window.removeEventListener('pointercancel', onRequestDragEnd);
  dragSession.value = undefined;
  dropTarget.value = undefined;
}

function dropFromPoint(x: number, y: number, requestId: string): RequestDropTarget | undefined {
  const element = document.elementFromPoint(x, y);
  const row = element?.closest<HTMLElement>('.request-row');
  if (row?.dataset.requestId && row.dataset.collectionId) {
    if (row.dataset.requestId === requestId) return;
    const bounds = row.getBoundingClientRect();
    return {
      collectionId: row.dataset.collectionId,
      requestId: row.dataset.requestId,
      kind: y < bounds.top + bounds.height / 2 ? RequestDropKind.Before : RequestDropKind.After,
    };
  }
  const heading = element?.closest<HTMLElement>('.collection-heading');
  if (heading?.dataset.collectionId) {
    return {collectionId: heading.dataset.collectionId, kind: RequestDropKind.Into};
  }
}

function onRequestDragMove(event: PointerEvent) {
  const session = dragSession.value;
  if (!session || event.pointerId !== session.pointerId) return;
  const deltaX = event.clientX - session.startX;
  const deltaY = event.clientY - session.startY;
  if (!session.active) {
    if (deltaX * deltaX + deltaY * deltaY < RequestDragThresholdPx * RequestDragThresholdPx) return;
    session.source.setPointerCapture(session.pointerId);
    dragSession.value = {...session, active: true};
    dragPointerX = event.clientX;
    dragPointerY = event.clientY;
    startRequestDragScroll();
  }
  event.preventDefault();
  dragPointerX = event.clientX;
  dragPointerY = event.clientY;
  refreshDropTarget(event.clientX, event.clientY, session.requestId);
}

function onRequestDragEnd(event: PointerEvent) {
  const session = dragSession.value;
  if (!session || event.pointerId !== session.pointerId) return;
  const target = session.active ? dropTarget.value : undefined;
  const requestId = session.requestId;
  const dragged = session.active;
  endRequestDrag();
  if (!dragged || !target) return;
  suppressNextClick();
  const owner = workspace.value.collections.find(item => item.id === target.collectionId);
  if (!owner) return;
  void relocateRequest(requestId, target.collectionId, requestInsertionIndex(owner, target.requestId, target.kind));
}

function startRequestDrag(event: PointerEvent, request: ApiRequest) {
  if (event.button !== RequestDragButton || event.pointerType === 'touch') return;
  endRequestDrag();
  dragSession.value = {
    requestId: request.id,
    pointerId: event.pointerId,
    startX: event.clientX,
    startY: event.clientY,
    active: false,
    source: event.currentTarget as HTMLElement,
  };
  window.addEventListener('pointermove', onRequestDragMove, {passive: false});
  window.addEventListener('pointerup', onRequestDragEnd);
  window.addEventListener('pointercancel', onRequestDragEnd);
}

onBeforeUnmount(endRequestDrag);
</script>
<template>
  <aside class="sidebar">
    <div class="sidebar-heading">
      <h2>{{ t.Collections() }}</h2>
      <div class="icon-actions">
        <IconButton
            icon="folder-plus"
            :label="t.NewCollection()"
            @click="addCollection()"
        />
        <IconButton
            icon="plus"
            :label="t.NewRequest()"
            @click="createRequest()"
        />
      </div>
    </div>
    <div class="search-box">
      <Icon name="search"/>
      <input
          v-model="search"
          :aria-label="t.Search()"
          :placeholder="t.Search()"
      />
    </div>
    <div ref="treeElement" class="collection-tree" :class="{'request-dragging': Boolean(dragRequestId)}">
      <SidebarCollectionNode v-for="owner in visibleRootCollections" :key="owner.id" :owner="owner"
                             :collections="workspace.collections" :expanded="expanded" :search="search"
                             :active-request-id="active" :selected-collection-id="collectionId"
                             :drag-request-id="dragRequestId"
                             :drop-collection-id="dropTarget?.collectionId ?? ''"
                             :drop-request-id="dropTarget?.requestId ?? ''"
                             :drop-kind="dropTarget?.kind ?? ''"
                             @toggle="toggleCollection"
                             @select="selectCollectionNode"
                             @open-request="(request, owner) => open(request, owner.id)"
                             @collection-menu="showCollectionMenu"
                             @request-menu="showRequestMenu"
                             @request-drag-start="startRequestDrag"/>
    </div>
  </aside>
  <CollectionSettings v-if="settingsCollection" :collection="settingsCollection"
                      @close="settingsCollection = undefined"/>
  <SidebarContextMenu v-if="requestMenu" :kind="SidebarMenuKind.Request"
                      :key="requestMenu.request.id + ':' + requestMenu.x + ':' + requestMenu.y"
                      :x="requestMenu.x" :y="requestMenu.y" :trigger="requestMenu.trigger"
                      :label="requestMenu.request.name"
                      @close="requestMenu = undefined"
                      @copy="duplicateRequest(requestMenu.request, requestMenu.owner)"
                      @delete="deleteRequest(requestTab(requestMenu.request, requestMenu.owner))"/>
  <SidebarContextMenu v-if="collectionMenu" :kind="SidebarMenuKind.Collection"
                      :key="collectionMenu.owner.id + ':' + collectionMenu.x + ':' + collectionMenu.y"
                      :x="collectionMenu.x" :y="collectionMenu.y" :trigger="collectionMenu.trigger"
                      :label="collectionMenu.owner.name"
                      @close="collectionMenu = undefined"
                      @run="openRunner(collectionMenu.owner)"
                      @add="createRequest(collectionMenu.owner)"
                      @add-collection="addCollection(collectionMenu.owner)"
                      @settings="settingsCollection = collectionMenu.owner"
                      @rename="renameCollection(collectionMenu.owner)"
                      @copy="duplicateCollection(collectionMenu.owner)"
                      @delete="deleteCollection(collectionMenu.owner)"/>
</template>
