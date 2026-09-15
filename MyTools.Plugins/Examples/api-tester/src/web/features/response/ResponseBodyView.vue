<script setup lang="ts">
import {computed, ref, watch} from 'vue';
import type {RequestResult} from '../../../shared/model.js';
import {Limits} from '../../../shared/model.js';
import {useText} from '../../localization/locale.js';
import JsonTreeNode from './JsonTreeNode.vue';
import SearchText from '../../components/common/SearchText.vue';

enum ViewKind { Raw = 'raw', Formatted = 'formatted', Tree = 'tree' }

const MaximumJsonDepth = 64;
const JsonIndent = 2;
const props = defineProps<{ result: RequestResult }>();
const t = useText();
const mode = ref(ViewKind.Raw);
const search = ref('');
const expanded = ref(true);
const revision = ref(0);
const content = ref<HTMLElement>();
const activeMatch = ref(-1);
const parsed = computed(() => {
  if (props.result.binary || props.result.truncated || props.result.previewAvailable === false) return undefined;
  try {
    return {value: JSON.parse(props.result.preview)};
  } catch {
    return undefined;
  }
});
const treeAllowed = computed(() => {
  if (!parsed.value) return false;
  const pending = [{value: parsed.value.value, depth: 0}];
  let count = 0;
  while (pending.length) {
    const node = pending.pop()!;
    if (++count > Limits.jsonTreeNodes || node.depth > MaximumJsonDepth) return false;
    if (node.value && typeof node.value === 'object') for (const value of Object.values(node.value)) pending.push({
      value,
      depth: node.depth + 1
    });
  }
  return true;
});
const text = computed(() => {
  if (props.result.previewAvailable === false) return t.value.CacheError();
  if (props.result.binary) return t.value.BinaryResponse();
  if (mode.value === ViewKind.Formatted && parsed.value) {
    try {
      return JSON.stringify(parsed.value.value, null, JsonIndent).slice(0, Limits.previewBytes);
    } catch {
      return props.result.preview;
    }
  }
  return props.result.preview;
});
const matches = computed(() => {
  if (!search.value) return 0;
  let count = 0;
  let from = 0;
  const lower = text.value.toLowerCase();
  const needle = search.value.toLowerCase();
  while ((from = lower.indexOf(needle, from)) >= 0) {
    count++;
    from += needle.length;
  }
  return count;
});
watch([search, mode], () => {
  activeMatch.value = -1;
  if (search.value && mode.value === ViewKind.Tree) {
    expanded.value = true;
    revision.value++;
  }
});
watch(() => props.result, () => {
  activeMatch.value = -1;
  mode.value = ViewKind.Raw;
});

function toggleAll(value: boolean) {
  expanded.value = value;
  revision.value++;
}

function navigate(direction: number) {
  const marks = content.value?.querySelectorAll('mark');
  if (!marks?.length) return;
  marks.forEach(mark => mark.classList.remove('active-match'));
  activeMatch.value = activeMatch.value < 0 ? direction > 0 ? 0 : marks.length - 1 : (activeMatch.value + direction + marks.length) % marks.length;
  marks[activeMatch.value].classList.add('active-match');
  marks[activeMatch.value].scrollIntoView({block: 'nearest'});
}
</script>
<template>
  <div class="body-toolbar">
    <button :class="{ selected: mode === ViewKind.Raw }" @click="mode = ViewKind.Raw">{{ t.Raw() }}</button>
    <button :class="{ selected: mode === ViewKind.Formatted }" :disabled="!parsed" @click="mode = ViewKind.Formatted">
      {{ t.Formatted() }}
    </button>
    <button :class="{ selected: mode === ViewKind.Tree }" :disabled="!treeAllowed" @click="mode = ViewKind.Tree">
      {{ t.JsonTree() }}
    </button>
    <template v-if="mode === ViewKind.Tree">
      <button @click="toggleAll(true)">{{ t.ExpandAll() }}</button>
      <button @click="toggleAll(false)">{{ t.CollapseAll() }}</button>
    </template>
  </div>
  <div class="response-search"><input v-model="search" :aria-label="t.SearchResponse()"
                                      :placeholder="t.SearchResponse()"
                                      @keydown.enter.prevent="navigate($event.shiftKey ? -1 : 1)"/><span class="muted">{{
      t.SearchMatches({count: matches})
    }}</span>
    <button :disabled="!matches" @click="navigate(-1)">{{ t.PreviousMatch() }}</button>
    <button :disabled="!matches" @click="navigate(1)">{{ t.NextMatch() }}</button>
  </div>
  <p v-if="result.truncated" class="muted">{{ t.PreviewLimit() }}</p>
  <p v-if="parsed && !treeAllowed" class="muted">{{ t.JsonTreeLimit() }}</p>
  <div ref="content" class="response-body-content">
    <div v-if="mode === ViewKind.Tree && parsed" class="response-code json-tree">
      <JsonTreeNode :value="parsed.value" :search="search" :expanded="expanded" :revision="revision"/>
    </div>
    <pre v-else class="response-code"><SearchText :text="text" :search="search"/></pre>
  </div>
</template>
