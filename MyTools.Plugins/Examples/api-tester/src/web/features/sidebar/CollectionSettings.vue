<script setup lang="ts">
import { ref, onMounted } from 'vue';
import type { Collection } from '../../../shared/model.js';
import { newRequest } from '../../../shared/model.js';
import { useWorkspaceContext } from '../workspace/context.js';
import AuthenticationEditor from '../../components/common/AuthenticationEditor.vue';
import PairTable from '../../components/common/PairTable.vue';
import ScriptsEditor from '../../components/common/ScriptsEditor.vue';
import IconButton from '../../components/common/IconButton.vue';
const props = defineProps<{ collection: Collection }>();
const emit = defineEmits<{ close: [] }>();
const { t, clone, mutate, workspace } = useWorkspaceContext();
const draft = ref({ ...clone(props.collection), auth: clone(props.collection.auth || newRequest('', '').auth), headers: clone(props.collection.headers || []) });
const dialog = ref<HTMLDialogElement>();
const saving = ref(false);
onMounted(() => dialog.value?.showModal());
async function save() {
  if (saving.value) return;
  saving.value = true;
  try { await mutate(() => { const owner = workspace.value.collections.find(c => c.id === props.collection.id); if (owner) Object.assign(owner, { auth: clone(draft.value.auth), headers: clone(draft.value.headers), scripts: draft.value.scripts ? clone(draft.value.scripts) : undefined }); }); emit('close'); }
  catch (error) { console.error(error); }
  finally { saving.value = false; }
}
</script>
<template>
  <dialog ref="dialog" class="standard-dialog feature-dialog collection-settings-dialog" :aria-label="t.CollectionSettings()" @close="emit('close')" @cancel="emit('close')">
    <div class="dialog-titlebar"><h2>{{ t.CollectionSettings() }} · {{ collection.name }}</h2><IconButton icon="close" :label="t.Close()" @click="emit('close')" /></div>
    <div class="dialog-content collection-settings-content">
      <p class="muted">{{ t.CollectionSettingsHint() }}</p>
      <h2>{{ t.Authentication() }}</h2><AuthenticationEditor :auth="draft.auth" collection />
      <h2>{{ t.CommonHeaders() }}</h2><PairTable :values="draft.headers" headers />
      <h2>{{ t.Scripts() }}</h2><ScriptsEditor v-model="draft.scripts" />
    </div>
    <div class="dialog-actions"><button @click="emit('close')">{{ t.Cancel() }}</button><button class="primary" :disabled="saving" @click="save">{{ t.Save() }}</button></div>
  </dialog>
</template>
