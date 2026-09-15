<script setup lang="ts">
import { useWorkspaceContext } from '../workspace/context.js';
import IconButton from '../../components/common/IconButton.vue';
const { t, tab } = useWorkspaceContext();
</script>
<template>
  <div v-if="tab" class="config-content">
    <div
      v-for="(extraction, index) in tab.request.extractions"
      :key="index"
      class="extraction-row"
    >
      <input
        type="checkbox"
        v-model="extraction.enabled"
        :aria-label="t.Enabled()"
      /><label class="field grow"
        >{{ t.Path() }}<input v-model="extraction.path" /></label
      ><label class="field grow"
        >{{ t.VariableName() }}<input v-model="extraction.variable" /></label
      ><IconButton
        icon="trash"
        :label="t.Delete()"
        @click="tab.request.extractions.splice(index, 1)"
      />
    </div>
    <button
      class="add-row"
      @click="
        tab.request.extractions.push({ enabled: true, path: '', variable: '' })
      "
    >
      {{ t.Add() }}
    </button>
  </div>
</template>
