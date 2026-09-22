<script setup lang="ts">
import {computed, onBeforeUnmount, ref, watch} from 'vue';
import type {LanguageId} from '@qping/content-formatter';
import type {RequestResult} from '../../../shared/model.js';
import {Limits, Routes} from '../../../shared/model.js';
import {rpc} from '../../services/rpc.js';
import {useText} from '../../localization/locale.js';
import JsonTreeNode from './JsonTreeNode.vue';
import SearchText from '../../components/common/SearchText.vue';
import SyntaxHighlightedText from './SyntaxHighlightedText.vue';
import {useWorkspaceContext} from '../workspace/context.js';
import {ResponseBodyFormat} from '../workspace/workspaceTypes.js';
import {
  createHtmlPreviewDocument,
  formatXml,
  inferResponseBodyFormat,
  inferResponseMediaPreview,
  parseJson,
  responseMediaType,
  ResponseMediaPreview,
} from './responseBodyFormat.js';
import {decodeBase64, encodeBase64, formatBase64, formatHex} from './binaryBodyFormat.js';

const MaximumJsonDepth = 64;
const HtmlMarkupPattern = /<\/?[A-Za-z][^>]*>|<!doctype\s+html\b/i;
const ResponseFormatterWorkerPath = 'response-formatter.worker.js';
const FormatGroupSeparator = '────────────';
const props = defineProps<{ result: RequestResult; requestId?: string; runId?: string; resultIndex?: number }>();
const t = useText();
const {responseBodyFormats, responseBodyPreviews, responseBodyFormatting} = useWorkspaceContext();
const fallbackFormat = ref(ResponseBodyFormat.Auto);
const fallbackPreview = ref(false);
const fallbackFormatting = ref(false);
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
const formatRequested = computed({
  get: () => props.requestId
      ? responseBodyFormatting.value[props.requestId] ?? false
      : fallbackFormatting.value,
  set: value => {
    if (props.requestId) responseBodyFormatting.value[props.requestId] = value;
    else fallbackFormatting.value = value;
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
const formattedText = ref(props.result.preview);
const mediaPreviewUrl = ref('');
const mediaPreviewFailed = ref(false);
const formatterWorker = new Worker(ResponseFormatterWorkerPath);
let formatterRequestId = 0;
let mediaPreviewRequestId = 0;
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
const mediaPreview = computed(() => inferResponseMediaPreview(props.result));
const mediaPreviewSupported = computed(() =>
    mediaPreview.value !== ResponseMediaPreview.None
    && props.result.bodyAvailable
    && Boolean(props.runId)
    && props.resultIndex !== undefined,
);
const previewSupported = computed(() =>
    effectiveFormat.value === ResponseBodyFormat.Json
    || effectiveFormat.value === ResponseBodyFormat.Html
    || mediaPreviewSupported.value,
);
const previewActive = computed(() => previewRequested.value && previewSupported.value);
const htmlPreviewDocument = computed(() => createHtmlPreviewDocument(props.result.preview));
const formattedXml = computed(() => formatXml(props.result.preview));
const formatLanguage = computed<LanguageId | undefined>(() => ({
  [ResponseBodyFormat.Json]: 'json' as const,
  [ResponseBodyFormat.Xml]: 'xml' as const,
  [ResponseBodyFormat.Html]: 'html' as const,
  [ResponseBodyFormat.JavaScript]: 'javascript' as const,
  [ResponseBodyFormat.Hex]: undefined,
  [ResponseBodyFormat.Base64]: undefined,
  [ResponseBodyFormat.Auto]: undefined,
  [ResponseBodyFormat.Raw]: undefined,
})[effectiveFormat.value]);
const formatSupported = computed(() =>
    !props.result.binary
    && effectiveFormat.value !== ResponseBodyFormat.Hex
    && effectiveFormat.value !== ResponseBodyFormat.Base64
    && props.result.previewAvailable !== false
    && Boolean(props.result.preview),
);
const formatActive = computed(() => formatRequested.value && formatSupported.value);
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
  const encodedPreview = props.result.binary
      ? props.result.preview
      : encodeBase64(new TextEncoder().encode(props.result.preview));
  if (effectiveFormat.value === ResponseBodyFormat.Hex) return formatHex(encodedPreview);
  if (effectiveFormat.value === ResponseBodyFormat.Base64) return formatBase64(encodedPreview);
  if (props.result.binary) return t.value.BinaryResponse();
  return formatActive.value ? formattedText.value : props.result.preview;
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

function releaseMediaPreviewUrl() {
  if (mediaPreviewUrl.value) URL.revokeObjectURL(mediaPreviewUrl.value);
  mediaPreviewUrl.value = '';
}

watch(
    [previewActive, mediaPreview, () => props.runId, () => props.resultIndex,
      () => props.result.bodyAvailable, () => props.result.completedAt],
    () => {
      const requestId = ++mediaPreviewRequestId;
      releaseMediaPreviewUrl();
      mediaPreviewFailed.value = false;
      if (!previewActive.value || !mediaPreviewSupported.value) return;
      void rpc<string>(Routes.download, {id: props.runId, index: props.resultIndex}).then(base64 => {
        if (requestId !== mediaPreviewRequestId) return;
        const blob = new Blob([decodeBase64(base64)], {type: responseMediaType(props.result)});
        mediaPreviewUrl.value = URL.createObjectURL(blob);
      }).catch(() => {
        if (requestId === mediaPreviewRequestId) mediaPreviewFailed.value = true;
      });
    },
    {immediate: true},
);

formatterWorker.addEventListener('message', (event: MessageEvent<{id: number; formatted: string}>) => {
  if (event.data.id === formatterRequestId) {
    formattedText.value = event.data.formatted.slice(0, Limits.previewBytes);
  }
});
formatterWorker.addEventListener('error', () => {
  formattedText.value = props.result.preview;
});
watch(
    [formatActive, () => props.result.preview, () => props.result.binary,
      () => props.result.previewAvailable, formatLanguage],
    () => {
      formattedText.value = props.result.preview;
      formatterRequestId++;
      if (!formatActive.value) return;
      formatterWorker.postMessage({
        id: formatterRequestId,
        source: props.result.preview,
        language: formatLanguage.value,
      });
    },
    {immediate: true},
);
onBeforeUnmount(() => {
  formatterWorker.terminate();
  mediaPreviewRequestId++;
  releaseMediaPreviewUrl();
});

function formatLabel(value: ResponseBodyFormat): string {
  if (value === ResponseBodyFormat.Auto) return t.value.AutoFormat({format: formatLabel(inferredFormat.value)});
  return {
    [ResponseBodyFormat.Json]: t.value.Json,
    [ResponseBodyFormat.Xml]: t.value.Xml,
    [ResponseBodyFormat.Html]: t.value.Html,
    [ResponseBodyFormat.JavaScript]: t.value.JavaScript,
    [ResponseBodyFormat.Hex]: t.value.Hex,
    [ResponseBodyFormat.Base64]: t.value.Base64,
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
      <template v-for="item in formats" :key="item">
        <option v-if="item === ResponseBodyFormat.Hex" disabled aria-hidden="true">{{ FormatGroupSeparator }}</option>
        <option :value="item">{{ formatLabel(item) }}</option>
      </template>
    </select>
    <button :class="{ selected: previewActive }" :disabled="!previewSupported" :aria-pressed="previewActive"
            @click="previewRequested = !previewRequested">{{ t.Preview() }}</button>
    <button :class="{ selected: formatActive }" :disabled="!formatSupported" :aria-pressed="formatActive"
            @click="formatRequested = !formatRequested">{{ t.FormatResponse() }}</button>
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
  <p v-else-if="previewActive && mediaPreviewFailed" class="muted">{{ t.PreviewLoadFailed() }}</p>
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
    <img v-else-if="previewActive && mediaPreview === ResponseMediaPreview.Image && mediaPreviewUrl"
         class="binary-media-preview binary-image-preview" :src="mediaPreviewUrl" :alt="t.ImagePreview()"/>
    <audio v-else-if="previewActive && mediaPreview === ResponseMediaPreview.Audio && mediaPreviewUrl"
           class="binary-media-preview" :src="mediaPreviewUrl" :aria-label="t.AudioPreview()" controls/>
    <video v-else-if="previewActive && mediaPreview === ResponseMediaPreview.Video && mediaPreviewUrl"
           class="binary-media-preview" :src="mediaPreviewUrl" :aria-label="t.VideoPreview()" controls/>
    <iframe v-else-if="previewActive && mediaPreview === ResponseMediaPreview.Pdf && mediaPreviewUrl"
            class="html-response-preview" sandbox="" referrerpolicy="no-referrer" :src="mediaPreviewUrl"
            :title="t.PdfPreview()"/>
    <pre v-else class="response-code"><SyntaxHighlightedText v-if="syntaxFormat" :text="text" :search="search"
                                                               :format="syntaxFormat"/><SearchText v-else
                                                                                                    :text="text"
                                                                                                    :search="search"/></pre>
  </div>
</template>
