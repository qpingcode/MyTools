<script setup lang="ts">
import { useWorkspaceContext } from './context.js';
import PairTable from '../../components/common/PairTable.vue';
import SettingsEditor from '../../components/common/SettingsEditor.vue';
import IconButton from '../../components/common/IconButton.vue';
const { t, workspace, modal, finishModal, DialogKind } = useWorkspaceContext();
import { ref, watch, nextTick } from 'vue';
const modalElement = ref<HTMLDialogElement | null>(null);
watch(modal, async (value) => {
  if (value) {
    await nextTick();
    modalElement.value?.showModal();
  }
});
</script>
<template>
  <dialog
    v-if="modal"
    ref="modalElement"
    class="standard-dialog"
    :class="{ 'environment-dialog': !!modal.environment }"
    :aria-label="modal.title()"
    @cancel.prevent="finishModal(false)"
  >
    <div class="dialog-titlebar">
      <h2>{{ modal.title() }}</h2>
      <IconButton icon="close" :label="t.Close()" @click="finishModal(false)" />
    </div>
    <div class="dialog-content">
    <p v-if="modal.message">{{ modal.message() }}</p>
    <input
      v-if="modal.kind === DialogKind.Name"
      v-model="modal.value"
      :aria-label="t.Key()"
      @keydown.enter="finishModal(true)"
    />
    <select
      v-if="modal.kind === DialogKind.Collection"
      v-model="modal.value"
      :aria-label="t.Collections()"
    >
      <option
        v-for="owner in workspace.collections"
        :key="owner.id"
        :value="owner.id"
      >
        {{ owner.name }}
      </option>
    </select>
    <div v-if="modal.environment" class="environment-editor">
      <label class="environment-name-field">
        <span>{{ t.EnvironmentName() }}</span>
        <input v-model="modal.environment.name" :aria-label="t.EnvironmentName()" />
      </label>
      <PairTable :values="modal.environment.variables" />
    </div>
    <SettingsEditor v-if="modal.settings" :value="modal.settings" />
    </div>
    <div class="dialog-actions">
      <button @click="finishModal(false)">{{ t.Cancel() }}</button>
      <button
        v-if="modal.kind === DialogKind.Unsaved"
        @click="finishModal(false, true)"
      >
        {{ t.Discard() }}
      </button>
      <button class="primary" :disabled="!!modal.environment && !modal.environment.name.trim()" @click="finishModal(true)">
        {{ modal.kind === DialogKind.Confirm ? t.Continue() : t.Save() }}
      </button>
    </div>
  </dialog>
</template>
