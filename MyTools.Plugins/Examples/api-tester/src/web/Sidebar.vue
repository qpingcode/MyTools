<script setup lang="ts">
import { ref, watch } from 'vue';
import { useWorkspaceContext } from './context.js';
import IconButton from './IconButton.vue';
const expanded = ref(new Set<string>());
const {
  t,
  workspace,
  tabs,
  active,
  collectionId,
  search,
  selected,
  collection,
  batch,
  stopOnFailure,
  startingBatch,
  mutate,
  addCollection,
  renameCollection,
  duplicateCollection,
  deleteCollection,
  open,
  deleteRequest,
  move,
  runBatch,
  filtered,
  selectCollection,
  uid,
  clone,
} = useWorkspaceContext();
import Icon from './Icon.vue';
import { HttpMethod, newRequest } from '../shared/model.js';
import type { ApiRequest, Collection } from '../shared/model.js';
const SidebarMethodAbbreviations: Readonly<Record<string, string | undefined>> = {
  [HttpMethod.Delete]: 'DEL',
  [HttpMethod.Options]: 'OPT',
};
watch(
  collectionId,
  (id) => {
    if (id) expanded.value.add(id);
  },
  { immediate: true },
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
async function createRequest() {
  if (!collection.value) await addCollection();
  const owner = collection.value;
  if (!owner) return;
  const request = newRequest(uid(), t.value.NewRequest());
  await mutate(() => owner.requests.push(request));
  expanded.value.add(owner.id);
  open(request, owner.id);
}
async function copyRequest(request: ApiRequest, owner: Collection) {
  const draft =
    tabs.value.find((item) => item.request.id === request.id)?.request ||
    request;
  const copy = { ...clone(draft), id: uid() };
  await mutate(() =>
    owner.requests.splice(
      owner.requests.findIndex((item) => item.id === request.id) + 1,
      0,
      copy,
    ),
  );
  expanded.value.add(owner.id);
  open(copy, owner.id);
}
function toggleSelection(id: string, ownerId: string) {
  if (collectionId.value !== ownerId) selectCollection(ownerId);
  selected.value.has(id) ? selected.value.delete(id) : selected.value.add(id);
}
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
        /><IconButton
          icon="plus"
          :label="t.NewRequest()"
          @click="createRequest"
        />
      </div>
    </div>
    <div class="search-box">
      <Icon name="search" /><input
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
            /><Icon name="folder" /><span>{{ owner.name }}</span
            ><span class="muted">{{ owner.requests.length }}</span>
          </button>
          <div class="tree-actions">
            <IconButton
              icon="edit"
              :label="t.Rename()"
              @click="renameCollection(owner)"
            /><IconButton
              icon="copy"
              :label="t.Duplicate()"
              @click="duplicateCollection(owner)"
            /><IconButton
              icon="trash"
              :label="t.Delete()"
              @click="deleteCollection(owner)"
            />
          </div>
        </div>
        <template v-if="expanded.has(owner.id) || search"
          ><div
            v-for="request in filtered(owner)"
            :key="request.id"
            class="request-row"
            :class="{ selected: active === request.id }"
          >
            <input
              type="checkbox"
              :aria-label="request.name"
              :checked="selected.has(request.id)"
              @change="toggleSelection(request.id, owner.id)"
            /><button class="request-title" @click="open(request, owner.id)">
              <span class="method-label" :data-method="request.method" :title="request.method" :aria-label="request.method">{{
                SidebarMethodAbbreviations[request.method] ?? request.method
              }}</span
              ><span>{{ request.name }}</span>
            </button>
            <div class="tree-actions">
              <IconButton
                icon="copy"
                :label="t.Duplicate()"
                @click="copyRequest(request, owner)"
              /><IconButton
                icon="arrow-up"
                :label="t.Up()"
                :disabled="owner.requests[0]?.id === request.id"
                @click="move(requestTab(request, owner), -1)"
              /><IconButton
                icon="arrow-down"
                :label="t.Down()"
                :disabled="owner.requests.at(-1)?.id === request.id"
                @click="move(requestTab(request, owner), 1)"
              /><IconButton
                icon="trash"
                :label="t.Delete()"
                @click="deleteRequest(requestTab(request, owner))"
              />
            </div></div
        ></template>
      </div>
    </div>
    <div class="sidebar-run">
      <button
        class="run-button"
        :disabled="!collection || (startingBatch && (!batch || batch.done))"
        @click="runBatch"
      >
        <Icon :name="batch && !batch.done ? 'stop' : 'play'" />{{
          batch && !batch.done ? t.Cancel() : t.Run()
        }}</button
      ><label class="check"
        ><input type="checkbox" v-model="stopOnFailure" />{{
          t.StopOnFailure()
        }}</label
      >
    </div>
  </aside>
</template>
