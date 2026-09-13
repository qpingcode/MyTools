<script setup lang="ts">
import { useWorkspaceContext } from './context.js';
import IconButton from './IconButton.vue';
const { t, tab, AssertionKind, assertionOptions, DefaultExpectedStatus } =
  useWorkspaceContext();
</script>
<template>
  <div v-if="tab" class="config-content">
    <div
      v-for="(assertion, index) in tab.request.assertions"
      :key="index"
      class="assertion-row"
    >
      <input
        type="checkbox"
        v-model="assertion.enabled"
        :aria-label="t.Enabled()"
      /><input
        v-model="assertion.name"
        :aria-label="t.Key()"
        :placeholder="t.Key()"
      /><select v-model="assertion.kind" :aria-label="t.Assertions()">
        <option
          v-for="[kind, label] in assertionOptions"
          :key="kind"
          :value="kind"
        >
          {{ label }}
        </option></select
      ><input
        v-model="assertion.path"
        :aria-label="t.Path()"
        :placeholder="t.Path()"
      /><input
        v-model="assertion.expected"
        :aria-label="t.Expected()"
        :placeholder="t.Expected()"
      /><IconButton
        icon="trash"
        :label="t.Delete()"
        @click="tab.request.assertions.splice(index, 1)"
      />
    </div>
    <button
      class="add-row"
      @click="
        tab.request.assertions.push({
          name: t.Status(),
          enabled: true,
          kind: AssertionKind.Status,
          path: '',
          expected: DefaultExpectedStatus,
        })
      "
    >
      {{ t.Add() }}
    </button>
  </div>
</template>
