<script setup lang="ts">
import { computed } from "vue";
import { highlight } from "../i18n";

const props = defineProps<{
    text: string;
    query?: string;
}>();

const html = computed(() => highlight(props.text, props.query || ""));
</script>

<template>
    <span v-html="html"></span>
</template>
