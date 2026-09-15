<script setup lang="ts">
import { computed, ref, watch } from 'vue';
import { JsonValueType } from '../../../shared/model.js';
import SearchText from '../../components/common/SearchText.vue';
const props = defineProps<{ value: any; name?: string; search: string; expanded: boolean; revision: number }>();
const isObject = computed(() => props.value !== null && typeof props.value === 'object');
const entries = computed(() => isObject.value ? Object.entries(props.value) : []);
const type = computed(() => props.value === null ? JsonValueType.Null : typeof props.value);
const details = ref<HTMLDetailsElement>();
const initialOpen = ref(props.expanded);
watch(() => props.revision, () => { if (details.value) details.value.open = props.expanded; });
</script>
<template>
  <details v-if="isObject" ref="details" class="json-tree-node" :open="initialOpen">
    <summary><span v-if="name !== undefined" class="json-key"><SearchText :text="JSON.stringify(name)" :search="search" />: </span><span class="muted">{{ Array.isArray(value) ? '[' : '{' }} {{ entries.length }} {{ Array.isArray(value) ? ']' : '}' }}</span></summary>
    <div class="json-tree-children"><JsonTreeNode v-for="[key, child] in entries" :key="key" :name="key" :value="child" :search="search" :expanded="expanded" :revision="revision" /></div>
  </details>
  <div v-else class="json-tree-leaf"><span v-if="name !== undefined" class="json-key"><SearchText :text="JSON.stringify(name)" :search="search" />: </span><span :class="'json-' + type"><SearchText :text="JSON.stringify(value)" :search="search" /></span></div>
</template>
