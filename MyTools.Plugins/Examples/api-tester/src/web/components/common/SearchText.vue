<script setup lang="ts">
import {computed} from 'vue';

const props = defineProps<{ text: string; search: string }>();
const HighlightMatchLimit = 1000;
const pieces = computed(() => {
  if (!props.search) return [{text: props.text, match: false}];
  const pieces: { text: string; match: boolean }[] = [];
  const lower = props.text.toLowerCase();
  const needle = props.search.toLowerCase();
  let from = 0;
  let count = 0;
  let found: number;
  while (count++ < HighlightMatchLimit && (found = lower.indexOf(needle, from)) >= 0) {
    if (found > from) pieces.push({text: props.text.slice(from, found), match: false});
    pieces.push({text: props.text.slice(found, found + props.search.length), match: true});
    from = found + props.search.length;
  }
  if (from < props.text.length) pieces.push({text: props.text.slice(from), match: false});
  return pieces;
});
</script>
<template>
  <template v-for="(piece, index) in pieces" :key="index">
    <mark v-if="piece.match">{{ piece.text }}</mark>
    <template v-else>{{ piece.text }}</template>
  </template>
</template>
