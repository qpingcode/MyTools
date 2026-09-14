<script setup lang="ts">
import { computed, ref, onMounted, onBeforeUnmount } from 'vue';
import { useWorkspaceContext } from './context.js';
import IconButton from './IconButton.vue';
const {
  t,
  workspace,
  environment,
  addEnvironment,
  switchEnvironment,
  editEnvironment,
  duplicateEnvironment,
  deleteEnvironment,
  defaults,
  clearCookies,
} = useWorkspaceContext();
import Icon from './Icon.vue';
const menu = ref<HTMLDetailsElement | null>(null);
const selector = ref<HTMLDetailsElement | null>(null);
const environmentSearch = ref('');
const filteredEnvironments = computed(() => {
  const query = environmentSearch.value.trim().toLocaleLowerCase();
  return workspace.value.environments.filter(owner => owner.name.toLocaleLowerCase().includes(query));
});
function resetEnvironmentSearch() {
  if (!selector.value?.open) environmentSearch.value = '';
}
function createEnvironment() {
  closeMenu();
  addEnvironment();
}
function closeMenu() {
  if (menu.value) menu.value.open = false;
  if (selector.value) selector.value.open = false;
}
function dismissOutside(event: Event) {
  if (!(event.target instanceof Node)) return;
  if (!menu.value?.contains(event.target) && menu.value) menu.value.open = false;
  if (!selector.value?.contains(event.target) && selector.value) selector.value.open = false;
}
function dismissEscape(event: KeyboardEvent) {
  const opened = selector.value?.open ? selector.value : menu.value?.open ? menu.value : null;
  if (event.key !== 'Escape' || !opened) return;
  event.preventDefault();
  closeMenu();
  opened.querySelector('summary')?.focus();
}
function selectEnvironment(id: string) {
  switchEnvironment(id);
  if (selector.value) selector.value.open = false;
  selector.value?.querySelector('summary')?.focus();
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
    <Icon name="globe" />
    <details ref="selector" class="menu environment-selector" @toggle="resetEnvironmentSearch">
      <summary :aria-label="t.Environments()" :title="environment?.name ?? t.NoEnvironment()">
        <span>{{ environment?.name ?? t.NoEnvironment() }}</span><Icon name="chevron-down" />
      </summary>
      <div class="menu-popover environment-options" :aria-label="t.Environments()">
        <input class="environment-search" v-model="environmentSearch" type="search" :placeholder="t.SearchEnvironments()" :aria-label="t.SearchEnvironments()" />
        <div class="environment-option-list">
        <button :aria-pressed="!workspace.environmentId" @click="selectEnvironment('')">{{ t.NoEnvironment() }}</button>
        <button v-for="owner in filteredEnvironments" :key="owner.id"
          :aria-pressed="workspace.environmentId === owner.id" :title="owner.name"
          @click="selectEnvironment(owner.id)">{{ owner.name }}</button>
        <p v-if="environmentSearch.trim() && !filteredEnvironments.length" class="environment-no-matches" role="status">{{ t.NoMatchingEnvironments() }}</p>
        </div>
        <div class="environment-option-footer">
          <button @click="createEnvironment"><Icon name="plus" />{{ t.NewEnvironment() }}</button>
        </div>
      </div>
    </details>
    <IconButton
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
        <button :disabled="!environment" @click="duplicateEnvironment">
          {{ t.Duplicate() }}</button
        ><button :disabled="!environment" @click="deleteEnvironment">
          {{ t.Delete() }}</button
        ><button @click="clearCookies">{{ t.ClearCookies() }}</button
        ><button @click="defaults">{{ t.Defaults() }}</button>
      </div>
    </details>
  </div>
</template>
