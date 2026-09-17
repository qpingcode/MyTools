<script setup lang="ts">
import {ref, onMounted} from 'vue';
import type {Collection} from '../../../shared/model.js';
import {newRequest} from '../../../shared/model.js';
import {useWorkspaceContext} from '../workspace/context.js';
import AuthenticationEditor from '../../components/common/AuthenticationEditor.vue';
import PairTable from '../../components/common/PairTable.vue';
import ScriptsEditor from '../../components/common/ScriptsEditor.vue';
import IconButton from '../../components/common/IconButton.vue';
import SettingsEditor from '../../components/common/SettingsEditor.vue';

const props = defineProps<{ collection: Collection }>();
const emit = defineEmits<{ close: [] }>();
const {t, clone, mutate, workspace} = useWorkspaceContext();
const draft = ref({
  ...clone(props.collection),
  auth: clone(props.collection.auth || newRequest('', '').auth),
  headers: clone(props.collection.headers || []),
  settings: props.collection.settings ? clone(props.collection.settings) : undefined
});
const dialog = ref<HTMLDialogElement>();
const saving = ref(false);
onMounted(() => dialog.value?.showModal());

async function save() {
  if (saving.value) return;
  saving.value = true;
  try {
    await mutate(() => {
      const owner = workspace.value.collections.find(c => c.id === props.collection.id);
      if (owner) Object.assign(owner, {
        auth: clone(draft.value.auth),
        headers: clone(draft.value.headers),
        scripts: draft.value.scripts ? clone(draft.value.scripts) : undefined,
        settings: draft.value.settings ? clone(draft.value.settings) : undefined
      });
    });
    emit('close');
  } catch (error) {
    console.error(error);
  } finally {
    saving.value = false;
  }
}
</script>
<template>
  <dialog ref="dialog" class="standard-dialog feature-dialog collection-settings-dialog"
          :aria-label="t.CollectionSettings()" @close="emit('close')" @cancel="emit('close')">
    <div class="dialog-titlebar"><h2>{{ t.CollectionSettings() }} · {{ collection.name }}</h2>
      <IconButton icon="close" :label="t.Close()" @click="emit('close')"/>
    </div>
    <div class="dialog-content collection-settings-content">
      <p class="muted">{{ t.CollectionSettingsHint() }}</p>
      <h2>{{ t.Authentication() }}</h2>
      <AuthenticationEditor :auth="draft.auth" collection/>
      <h2>{{ t.CommonHeaders() }}</h2>
      <PairTable :values="draft.headers" headers/>
      <h2>{{ t.Scripts() }}</h2>
      <ScriptsEditor v-model="draft.scripts"/>
      <h2>{{ t.Settings() }}</h2>
      <label class="check"><input type="checkbox" :checked="!!draft.settings"
                                  @change="draft.settings = ($event.target as HTMLInputElement).checked ? clone(workspace.defaults) : undefined"/>
        {{ t.OverrideCollectionSettings() }}
      </label>
      <SettingsEditor v-if="draft.settings" :value="draft.settings"/>
    </div>
    <div class="dialog-actions">
      <button @click="emit('close')">{{ t.Cancel() }}</button>
      <button class="primary" :disabled="saving" @click="save">{{ t.Save() }}</button>
    </div>
  </dialog>
</template>
