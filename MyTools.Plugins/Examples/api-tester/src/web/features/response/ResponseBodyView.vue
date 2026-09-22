<script setup lang="ts">
import {computed, ref, watch} from 'vue';
import type {RequestResult} from '../../../shared/model.js';
import {Limits} from '../../../shared/model.js';
import {useText} from '../../localization/locale.js';
import JsonTreeNode from './JsonTreeNode.vue';
import SearchText from '../../components/common/SearchText.vue';
import SyntaxHighlightedText from './SyntaxHighlightedText.vue';
import {useWorkspaceContext} from '../workspace/context.js';
import {ResponseBodyFormat} from '../workspace/workspaceTypes.js';
import {
  createHtmlPreviewDocument,
  formatHtml,
  formatXml,
  inferResponseBodyFormat,
  parseJson,
} from './responseBodyFormat.js';

const MaximumJsonDepth = 64;
const JsonIndent = 2;
const HtmlMarkupPattern = /<\/?[A-Za-z][^>]*>|<!doctype\s+html\b/i;
const props = defineProps<{ result: RequestResult; requestId?: string }>();
const t = useText();
const {responseBodyFormats, responseBodyPreviews} = useWorkspaceContext();
const fallbackFormat = ref(ResponseBodyFormat.Auto);
const fallbackPreview = ref(false);
const format = computed({
  get: () => props.requestId
      ? responseBodyFormats.value[props.requestId] ?? ResponseBodyFormat.Auto
      : fallbackFormat.value,
  set: value => {
    if (props.requestId) responseBodyFormats.value[props.requestId] = value;
    else fallbackFormat.value = value;
  },
});
const previewRequested = computed({
  get: () => props.requestId
      ? responseBodyPreviews.value[props.requestId] ?? false
      : fallbackPreview.value,
  set: value => {
    if (props.requestId) responseBodyPreviews.value[props.requestId] = value;
    else fallbackPreview.value = value;
  },
});
const inferredFormat = computed(() => inferResponseBodyFormat(props.result));
const effectiveFormat = computed(() => format.value === ResponseBodyFormat.Auto ? inferredFormat.value : format.value);
const formats = Object.values(ResponseBodyFormat);
const search = ref('');
const expanded = ref(true);
const revision = ref(0);
const content = ref<HTMLElement>();
const activeMatch = ref(-1);
const parsedJson = computed(() => {
  if (props.result.binary || props.result.truncated || props.result.previewAvailable === false) return undefined;
  return parseJson(props.result.preview);
});
const treeAllowed = computed(() => {
  if (parsedJson.value === undefined) return false;
  const pending = [{value: parsedJson.value, depth: 0}];
  let count = 0;
  while (pending.length) {
    const node = pending.pop()!;
    if (++count > Limits.jsonTreeNodes || node.depth > MaximumJsonDepth) return false;
    if (node.value && typeof node.value === 'object') for (const value of Object.values(node.value)) pending.push({
      value,
      depth: node.depth + 1,
    });
  }
  return true;
});
const previewSupported = computed(() =>
    effectiveFormat.value === ResponseBodyFormat.Json || effectiveFormat.value === ResponseBodyFormat.Html,
);
const previewActive = computed(() => previewRequested.value && previewSupported.value);
const htmlPreviewDocument = computed(() => createHtmlPreviewDocument(props.result.preview));
const formattedXml = computed(() => formatXml(props.result.preview));
const syntaxFormat = computed(() => {
  if (effectiveFormat.value === ResponseBodyFormat.Json) {
    return parsedJson.value === undefined ? undefined : ResponseBodyFormat.Json;
  }
  if (effectiveFormat.value === ResponseBodyFormat.Xml) {
    return formattedXml.value === undefined ? undefined : ResponseBodyFormat.Xml;
  }
  if (effectiveFormat.value === ResponseBodyFormat.Html) {
    const supported = inferredFormat.value === ResponseBodyFormat.Html || HtmlMarkupPattern.test(props.result.preview);
    return supported ? ResponseBodyFormat.Html : undefined;
  }
  if (effectiveFormat.value === ResponseBodyFormat.JavaScript) {
    return ResponseBodyFormat.JavaScript;
  }
  return undefined;
});
const text = computed(() => {
  if (props.result.previewAvailable === false) return t.value.CacheError();
  if (props.result.binary) return t.value.BinaryResponse();
  if (effectiveFormat.value === ResponseBodyFormat.Json && parsedJson.value !== undefined) {
    return JSON.stringify(parsedJson.value, null, JsonIndent).slice(0, Limits.previewBytes);
  }
  if (effectiveFormat.value === ResponseBodyFormat.Xml) return formattedXml.value ?? props.result.preview;
  if (effectiveFormat.value === ResponseBodyFormat.Html) return formatHtml(props.result.preview);
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
watch([search, previewActive], () => {
  activeMatch.value = -1;
  if (search.value && previewActive.value && effectiveFormat.value === ResponseBodyFormat.Json) {
    expanded.value = true;
    revision.value++;
  }
});
watch(effectiveFormat, () => {
  activeMatch.value = -1;
});
watch(() => props.result, () => {
  activeMatch.value = -1;
});

function formatLabel(value: ResponseBodyFormat): string {
  if (value === ResponseBodyFormat.Auto) return t.value.AutoFormat({format: formatLabel(inferredFormat.value)});
  return {
    [ResponseBodyFormat.Json]: t.value.Json,
    [ResponseBodyFormat.Xml]: t.value.Xml,
    [ResponseBodyFormat.Html]: t.value.Html,
    [ResponseBodyFormat.JavaScript]: t.value.JavaScript,
    [ResponseBodyFormat.Raw]: t.value.Raw,
  }[value]();
}

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
    <select v-model="format" class="response-format-select" :aria-label="t.ResponseFormat()">
      <option v-for="item in formats" :key="item" :value="item">{{ formatLabel(item) }}</option>
    </select>
    <button :class="{ selected: previewActive }" :disabled="!previewSupported" :aria-pressed="previewActive"
            @click="previewRequested = !previewRequested">{{ t.Preview() }}</button>
    <template v-if="previewActive && effectiveFormat === ResponseBodyFormat.Json && treeAllowed">
      <button @click="toggleAll(true)">{{ t.ExpandAll() }}</button>
      <button @click="toggleAll(false)">{{ t.CollapseAll() }}</button>
    </template>
    <div v-if="!previewActive || effectiveFormat === ResponseBodyFormat.Json" class="response-search">
      <input v-model="search" :aria-label="t.SearchResponse()" :placeholder="t.SearchResponse()"
             @keydown.enter.prevent="navigate($event.shiftKey ? -1 : 1)"/><span class="muted">{{
        t.SearchMatches({count: matches})
      }}</span>
      <button :disabled="!matches" @click="navigate(-1)">{{ t.PreviousMatch() }}</button>
      <button :disabled="!matches" @click="navigate(1)">{{ t.NextMatch() }}</button>
    </div>
  </div>
  <p v-if="result.truncated" class="muted">{{ t.PreviewLimit() }}</p>
  <p v-if="previewRequested && !previewSupported" class="muted">{{ t.PreviewUnavailable() }}</p>
  <p v-else-if="previewActive && effectiveFormat === ResponseBodyFormat.Json && parsedJson === undefined" class="muted">
    {{ t.InvalidJsonPreview() }}
  </p>
  <p v-else-if="previewActive && effectiveFormat === ResponseBodyFormat.Json && !treeAllowed" class="muted">
    {{ t.JsonTreeLimit() }}
  </p>
  <div ref="content" class="response-body-content">
    <div v-if="previewActive && effectiveFormat === ResponseBodyFormat.Json && parsedJson !== undefined && treeAllowed"
         class="response-code json-tree">
      <JsonTreeNode :value="parsedJson" :search="search" :expanded="expanded" :revision="revision"/>
    </div>
    <iframe v-else-if="previewActive && effectiveFormat === ResponseBodyFormat.Html" class="html-response-preview"
            sandbox="" referrerpolicy="no-referrer" :srcdoc="htmlPreviewDocument" :title="t.HtmlPreview()"/>
    <pre v-else class="response-code"><SyntaxHighlightedText v-if="syntaxFormat" :text="text" :search="search"
                                                               :format="syntaxFormat"/><SearchText v-else
                                                                                                    :text="text"
                                                                                                    :search="search"/></pre>
  </div>
</template>
