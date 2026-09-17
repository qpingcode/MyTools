<script setup lang="ts">
import {nextTick, ref, watch} from 'vue';
import SidebarContextMenu from './SidebarContextMenu.vue';
import {SidebarMenuKind} from './sidebarMenuTypes.js';
import {useWorkspaceContext} from '../workspace/context.js';
import IconButton from '../../components/common/IconButton.vue';
import CollectionSettings from './CollectionSettings.vue';

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
  filtered,
  selectCollection,
  revealRequestId,
} = useWorkspaceContext();
import Icon from '../../components/common/Icon.vue';
import {HttpMethod} from '../../../shared/model.js';
import type {ApiRequest, Collection} from '../../../shared/model.js';

const SidebarMethodAbbreviations: Readonly<Record<string, string | undefined>> = {
  [HttpMethod.Delete]: 'DEL',
  [HttpMethod.Options]: 'OPT',
};
watch(
    collectionId,
    (id) => {
      if (id) expanded.value.add(id);
    },
    {immediate: true},
);

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
  if (owner) expanded.value.add(owner.id);
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
            :label="t.NewCollection()"
            @click="addCollection"
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
      <div
          v-for="owner in workspace.collections"
          :key="owner.id"
          class="collection"
      >
        <div
            class="collection-heading"
            :class="{ selected: owner.id === collectionId }"
            @contextmenu="showCollectionMenu($event, owner)"
            @keydown="showCollectionMenu($event, owner)"
        >
          <button
              class="collection-title"
              @click="toggleCollection(owner.id)"
              :aria-expanded="expanded.has(owner.id) || !!search"
          >
            <Icon
                :name="
                expanded.has(owner.id) || search
                  ? 'chevron-down'
                  : 'chevron-right'
              "
            />
            <Icon name="folder"/>
            <span>{{ owner.name }}</span
            ><span class="muted">{{ owner.requests.length }}</span>
          </button>
        </div>
        <template v-if="expanded.has(owner.id) || search"
        >
          <div
              v-for="request in filtered(owner)"
              :key="request.id"
              class="request-row"
              :class="{ selected: active === request.id }"
              @contextmenu="showRequestMenu($event, request, owner)"
              @keydown="showRequestMenu($event, request, owner)"
          >
            <button class="request-title" :data-request-id="request.id" @click="open(request, owner.id)">
              <span class="method-label" :data-method="request.method" :title="request.method"
                    :aria-label="request.method">{{
                  SidebarMethodAbbreviations[request.method] ?? request.method
                }}</span
              ><span>{{ request.name }}</span>
            </button>
          </div
          >
        </template>
      </div>
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
                      @settings="settingsCollection = collectionMenu.owner"
                      @rename="renameCollection(collectionMenu.owner)"
                      @copy="duplicateCollection(collectionMenu.owner)"
                      @delete="deleteCollection(collectionMenu.owner)"/>
</template>
