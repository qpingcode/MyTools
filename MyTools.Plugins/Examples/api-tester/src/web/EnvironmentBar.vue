<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue';
import { useWorkspaceContext } from './context.js';
import IconButton from './IconButton.vue';
const {
  t,
  workspace,
  environment,
  addEnvironment,
  switchEnvironment,
  editEnvironment,
  renameEnvironment,
  duplicateEnvironment,
  deleteEnvironment,
  defaults,
  clearCookies,
} = useWorkspaceContext();
import Icon from './Icon.vue';
const menu = ref<HTMLDetailsElement | null>(null);
function closeMenu() {
  if (menu.value) menu.value.open = false;
}
function dismissOutside(event: Event) {
  if (event.target instanceof Node && !menu.value?.contains(event.target))
    closeMenu();
}
function dismissEscape(event: KeyboardEvent) {
  if (event.key !== 'Escape' || !menu.value?.open) return;
  event.preventDefault();
  closeMenu();
  menu.value.querySelector('summary')?.focus();
}
function dismissAction(event: MouseEvent) {
  const button =
    event.target instanceof Element ? event.target.closest('button') : null;
  if (button && !button.disabled) closeMenu();
}
onMounted(() => {
  document.addEventListener('pointerdown', dismissOutside, true);
  document.addEventListener('focusin', dismissOutside, true);
  document.addEventListener('keydown', dismissEscape);
});
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', dismissOutside, true);
  document.removeEventListener('focusin', dismissOutside, true);
  document.removeEventListener('keydown', dismissEscape);
});
</script>
<template>
  <div class="environment-bar">
    <Icon name="globe" /><select
      :value="workspace.environmentId"
      :aria-label="t.Environments()"
      @change="switchEnvironment(($event.target as HTMLSelectElement).value)"
    >
      <option value="">{{ t.NoEnvironment() }}</option>
      <option
        v-for="owner in workspace.environments"
        :key="owner.id"
        :value="owner.id"
      >
        {{ owner.name }}
      </option></select
    ><IconButton
      icon="plus"
      :label="t.NewEnvironment()"
      @click="addEnvironment"
    /><IconButton
      icon="edit"
      :label="t.EditEnvironment()"
      :disabled="!environment"
      @click="editEnvironment"
    />
    <details ref="menu" class="menu">
      <summary :aria-label="t.Settings()" :title="t.Settings()">
        <Icon name="more" />
      </summary>
      <div class="menu-popover" @click="dismissAction">
        <button :disabled="!environment" @click="renameEnvironment">
          {{ t.Rename() }}</button
        ><button :disabled="!environment" @click="duplicateEnvironment">
          {{ t.Duplicate() }}</button
        ><button :disabled="!environment" @click="deleteEnvironment">
          {{ t.Delete() }}</button
        ><button @click="clearCookies">{{ t.ClearCookies() }}</button
        ><button @click="defaults">{{ t.Defaults() }}</button>
      </div>
    </details>
  </div>
</template>
