<script setup lang="ts">
import {computed, nextTick, ref, watch} from 'vue';
import SidebarContextMenu from './SidebarContextMenu.vue';
import {SidebarMenuKind} from './sidebarMenuTypes.js';
import {useWorkspaceContext} from '../workspace/context.js';
import IconButton from '../../components/common/IconButton.vue';
import CollectionSettings from './CollectionSettings.vue';
import SidebarCollectionNode from './SidebarCollectionNode.vue';

const settingsCollection = ref<Collection>();
const expanded = ref(new Set<string>());
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
  tabs,
  active,
  collectionId,
  collection,
  search,
  addCollection,
  createRequest,
  renameCollection,
  duplicateCollection,
  deleteCollection,
  open,
  duplicateRequest,
  deleteRequest,
  move,
  openRunner,
  selectCollection,
  revealRequestId,
} = useWorkspaceContext();
import Icon from '../../components/common/Icon.vue';
import type {ApiRequest, Collection} from '../../../shared/model.js';
import {collectionMatchesSearch, rootCollections} from '../../../shared/collectionTree.js';

const visibleRootCollections = computed(() =>
    rootCollections(workspace.value.collections)
        .filter(owner => collectionMatchesSearch(workspace.value.collections, owner, search.value)));
function expandWithAncestors(id: string | undefined) {
  let owner = workspace.value.collections.find(candidate => candidate.id === id);
  while (owner) {
    expanded.value.add(owner.id);
    owner = workspace.value.collections.find(candidate => candidate.id === owner?.parentId);
  }
}

watch(collectionId, id => expandWithAncestors(id), {immediate: true});

function toggleCollection(id: string) {
  if (collectionId.value !== id) {
    selectCollection(id);
    expanded.value.add(id);
    return;
  }
  expanded.value.has(id) ? expanded.value.delete(id) : expanded.value.add(id);
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
</script>
<template>
  <aside class="sidebar">
    <div class="sidebar-heading">
      <h2>{{ t.Collections() }}</h2>
      <div class="icon-actions">
        <IconButton
            icon="folder-plus"
            :label="collection ? t.AddSubcollection() : t.NewCollection()"
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
    <div class="collection-tree">
      <SidebarCollectionNode v-for="owner in visibleRootCollections" :key="owner.id" :owner="owner"
                             :collections="workspace.collections" :expanded="expanded" :search="search"
                             :active-request-id="active" :selected-collection-id="collectionId"
                             @toggle="toggleCollection"
                             @open-request="(request, owner) => open(request, owner.id)"
                             @collection-menu="showCollectionMenu"
                             @request-menu="showRequestMenu"/>
    </div>
  </aside>
  <CollectionSettings v-if="settingsCollection" :collection="settingsCollection"
                      @close="settingsCollection = undefined"/>
  <SidebarContextMenu v-if="requestMenu" :kind="SidebarMenuKind.Request"
                      :key="requestMenu.request.id + ':' + requestMenu.x + ':' + requestMenu.y"
                      :x="requestMenu.x" :y="requestMenu.y" :trigger="requestMenu.trigger"
                      :label="requestMenu.request.name"
                      :can-move-up="requestMenu.owner.requests[0]?.id !== requestMenu.request.id"
                      :can-move-down="requestMenu.owner.requests.at(-1)?.id !== requestMenu.request.id"
                      @close="requestMenu = undefined"
                      @up="move(requestTab(requestMenu.request, requestMenu.owner), -1)"
                      @down="move(requestTab(requestMenu.request, requestMenu.owner), 1)"
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
