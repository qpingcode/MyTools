<script setup lang="ts">
import {computed} from 'vue';
import {ResponseBodyFormat} from '../workspace/workspaceTypes.js';
import {syntaxTokens} from './syntaxHighlight.js';

const HighlightMatchLimit = 1000;
const props = defineProps<{ text: string; search: string; format: ResponseBodyFormat }>();
const chunks = computed(() => {
  if (!props.search) return [{text: props.text, match: false, tokens: syntaxTokens(props.text, props.format)}];
  const result: {text: string; match: boolean; tokens: ReturnType<typeof syntaxTokens>}[] = [];
  const lower = props.text.toLowerCase();
  const needle = props.search.toLowerCase();
  let from = 0;
  let count = 0;
  let found: number;
  while (count++ < HighlightMatchLimit && (found = lower.indexOf(needle, from)) >= 0) {
    if (found > from) {
      const text = props.text.slice(from, found);
      result.push({text, match: false, tokens: syntaxTokens(text, props.format)});
    }
    const text = props.text.slice(found, found + props.search.length);
    result.push({text, match: true, tokens: []});
    from = found + props.search.length;
  }
  if (from < props.text.length) {
    const text = props.text.slice(from);
    result.push({text, match: false, tokens: syntaxTokens(text, props.format)});
  }
  return result;
});
</script>
<template>
  <template v-for="(chunk, chunkIndex) in chunks" :key="chunkIndex">
    <mark v-if="chunk.match">{{ chunk.text }}</mark>
    <template v-else>
      <span v-for="(token, tokenIndex) in chunk.tokens" :key="tokenIndex"
            class="syntax-token" :class="`syntax-${token.kind}`">{{ token.text }}</span>
    </template>
  </template>
</template>
