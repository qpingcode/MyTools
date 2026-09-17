<script setup lang="ts">
import {computed} from 'vue';
import type {ApiRequest, Collection} from '../../../shared/model.js';
import {HttpMethod} from '../../../shared/model.js';
import {childCollections, collectionMatchesSearch, collectionSubtreeRequests} from '../../../shared/collectionTree.js';
import Icon from '../../components/common/Icon.vue';

defineOptions({name: 'SidebarCollectionNode'});

const props = defineProps<{
  owner: Collection;
  collections: Collection[];
  expanded: Set<string>;
  search: string;
  activeRequestId: string;
  selectedCollectionId: string;
}>();
const emit = defineEmits<{
  toggle: [id: string];
  openRequest: [request: ApiRequest, owner: Collection];
  collectionMenu: [event: MouseEvent | KeyboardEvent, owner: Collection];
  requestMenu: [event: MouseEvent | KeyboardEvent, request: ApiRequest, owner: Collection];
}>();

const children = computed(() => childCollections(props.collections, props.owner.id)
    .filter(child => collectionMatchesSearch(props.collections, child, props.search)));
const requests = computed(() => props.owner.requests.filter(request =>
    (request.name + ' ' + request.url).toLowerCase().includes(props.search.toLowerCase()),
));
const requestCount = computed(() => collectionSubtreeRequests(props.collections, props.owner).length);
const open = computed(() => props.expanded.has(props.owner.id) || Boolean(props.search));
const SidebarMethodAbbreviations: Readonly<Record<string, string | undefined>> = {
  [HttpMethod.Delete]: 'DEL',
  [HttpMethod.Options]: 'OPT',
};
</script>

<template>
  <div class="collection">
    <div class="collection-heading" :class="{selected: owner.id === selectedCollectionId}"
         @contextmenu="emit('collectionMenu', $event, owner)"
         @keydown="emit('collectionMenu', $event, owner)">
      <button class="collection-title" :aria-expanded="open" @click="emit('toggle', owner.id)">
        <Icon :name="open ? 'chevron-down' : 'chevron-right'"/>
        <Icon name="folder"/>
        <span>{{ owner.name }}</span><span class="muted">{{ requestCount }}</span>
      </button>
    </div>
    <div v-if="open" class="request-branches">
      <SidebarCollectionNode v-for="child in children" :key="child.id" :owner="child"
                             :collections="collections" :expanded="expanded" :search="search"
                             :active-request-id="activeRequestId"
                             :selected-collection-id="selectedCollectionId"
                             @toggle="emit('toggle', $event)"
                             @open-request="(request, owner) => emit('openRequest', request, owner)"
                             @collection-menu="(event, owner) => emit('collectionMenu', event, owner)"
                             @request-menu="(event, request, owner) => emit('requestMenu', event, request, owner)"/>
      <div v-for="request in requests" :key="request.id" class="request-row"
           :class="{selected: activeRequestId === request.id}"
           @contextmenu="emit('requestMenu', $event, request, owner)"
           @keydown="emit('requestMenu', $event, request, owner)">
        <button class="request-title" :data-request-id="request.id" @click="emit('openRequest', request, owner)">
          <span class="method-label" :data-method="request.method" :title="request.method"
                :aria-label="request.method">{{ SidebarMethodAbbreviations[request.method] ?? request.method }}</span>
          <span>{{ request.name }}</span>
        </button>
      </div>
    </div>
  </div>
</template>
