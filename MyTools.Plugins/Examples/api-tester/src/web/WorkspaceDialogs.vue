<script setup lang="ts">
import { useWorkspaceContext } from './context.js';
import PairTable from './PairTable.vue';
import SettingsEditor from './SettingsEditor.vue';
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
    :aria-label="modal.title()"
    @cancel.prevent="finishModal(false)"
  >
    <h2>
      {{ modal.title() }}
    </h2>
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
    <PairTable v-if="modal.environment" :values="modal.environment.variables" />
    <SettingsEditor v-if="modal.settings" :value="modal.settings" />
    <div class="row">
      <button @click="finishModal(true)">
        {{ modal.kind === DialogKind.Confirm ? t.Continue() : t.Save() }}
      </button>
      <button
        v-if="modal.kind === DialogKind.Unsaved"
        @click="finishModal(false, true)"
      >
        {{ t.Discard() }}
      </button>
      <button @click="finishModal(false)">{{ t.Cancel() }}</button>
    </div>
  </dialog>
</template>
