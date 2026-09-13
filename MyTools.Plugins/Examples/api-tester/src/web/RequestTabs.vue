<script setup lang="ts">
import { useWorkspaceContext } from './context.js';
import IconButton from './IconButton.vue';
const { t, tabs, active, selected, tab, dirty, name, closeTab } =
  useWorkspaceContext();
</script>
<template>
  <div class="document-tabs">
    <div
      v-for="item in tabs"
      :key="item.request.id"
      class="document-tab"
      :class="{ selected: item.request.id === active }"
    >
      <button @click="active = item.request.id">
        <span class="method-label" :data-method="item.request.method">{{
          item.request.method
        }}</span
        ><span class="tab-name">{{ item.request.name }}</span
        ><span
          v-if="dirty(item)"
          class="dirty-dot"
          :title="t.Unsaved({ name: item.request.name })"
        ></span></button
      ><IconButton icon="close" :label="t.Close()" @click="closeTab(item)" />
    </div>
  </div>
</template>
