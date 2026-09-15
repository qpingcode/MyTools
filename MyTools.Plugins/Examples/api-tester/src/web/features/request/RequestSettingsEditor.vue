<script setup lang="ts">
import { useWorkspaceContext } from '../workspace/context.js';
import SettingsEditor from '../../components/common/SettingsEditor.vue';
const { t, tab, override } = useWorkspaceContext();
</script>
<template>
  <div v-if="tab" class="config-content">
    <label class="check"
      ><input
        type="checkbox"
        :checked="!!tab.request.settings"
        @change="override(tab, $event)"
      />{{ t.Override() }}</label
    ><SettingsEditor
      v-if="tab.request.settings"
      :value="tab.request.settings"
    />
  </div>
</template>
