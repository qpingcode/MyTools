<script setup lang="ts">
import { useWorkspaceContext } from './context.js';
import IconButton from './IconButton.vue';
const { t, tab, assertionOptions, AssertionKind, DefaultExpectedStatus } = useWorkspaceContext();
function add() {
  tab.value?.request.assertions.push({ name: '', enabled: true, kind: AssertionKind.Status, path: '', expected: DefaultExpectedStatus });
}
</script>
<template>
  <div v-if="tab" class="config-content">
    <p class="muted">{{ t.AssertionsHint() }}</p>
    <div v-for="(assertion, index) in tab.request.assertions" :key="index" class="assertion-editor-row">
      <input type="checkbox" v-model="assertion.enabled" :aria-label="t.Enabled()" />
      <label class="field">{{ t.TestName() }}<input v-model="assertion.name" /></label>
      <label class="field">{{ t.AssertionKind() }}<select v-model="assertion.kind"><option v-for="[kind, label] in assertionOptions" :key="kind" :value="kind">{{ label }}</option></select></label>
      <label v-if="assertion.kind !== AssertionKind.Status && assertion.kind !== AssertionKind.Time && assertion.kind !== AssertionKind.Text" class="field">{{ t.Path() }}<input v-model="assertion.path" /></label>
      <label v-if="assertion.kind !== AssertionKind.Exists && assertion.kind !== AssertionKind.Header" class="field">{{ t.Expected() }}<input v-model="assertion.expected" /></label>
      <IconButton icon="trash" :label="t.Delete()" @click="tab.request.assertions.splice(index, 1)" />
    </div>
    <button class="add-row" @click="add">{{ t.Add() }}</button>
  </div>
</template>
