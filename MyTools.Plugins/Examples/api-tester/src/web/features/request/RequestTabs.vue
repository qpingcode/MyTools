<script setup lang="ts">
import {computed, nextTick, onMounted, onBeforeUnmount, ref, watch} from 'vue';
import {useWorkspaceContext} from '../workspace/context.js';
import IconButton from '../../components/common/IconButton.vue';

const {t, tabs, active, dirty, closeTab} = useWorkspaceContext();
const tabBar = ref<HTMLDivElement>();
const overflowMenu = ref<HTMLDivElement>();
const overflowTrigger = ref<HTMLDivElement>();
const capacity = ref(1);
const firstVisible = ref(0);
const menuOpen = ref(false);
const visibleTabs = computed(() => tabs.value.slice(firstVisible.value, firstVisible.value + capacity.value));
const hiddenTabs = computed(() => tabs.value.filter(item => !visibleTabs.value.includes(item)));
let resizeObserver: ResizeObserver | undefined;

function measure() {
  if (!tabBar.value) return;
  const styles = getComputedStyle(tabBar.value);
  const tabWidth = parseFloat(styles.getPropertyValue('--request-tab-width'));
  const overflowWidth = parseFloat(styles.getPropertyValue('--request-tabs-overflow-width'));
  const width = tabBar.value.clientWidth;
  const reservedWidth = tabs.value.length * tabWidth > width ? overflowWidth : 0;
  capacity.value = Math.max(1, Math.floor((width - reservedWidth) / tabWidth));
}

function revealActive() {
  firstVisible.value = Math.max(0, Math.min(firstVisible.value, tabs.value.length - capacity.value));
  const index = tabs.value.findIndex(item => item.request.id === active.value);
  if (index < 0) return;
  if (index < firstVisible.value) firstVisible.value = index;
  else if (index >= firstVisible.value + capacity.value) firstVisible.value = index - capacity.value + 1;
}

watch(() => tabs.value.length, measure, {flush: 'post'});
watch([active, capacity, () => tabs.value.map(item => item.request.id)], () => {
  revealActive();
  menuOpen.value = false;
});

async function toggleMenu() {
  menuOpen.value = !menuOpen.value;
  if (menuOpen.value) {
    await nextTick();
    overflowMenu.value?.querySelector<HTMLButtonElement>('button')?.focus();
  }
}

function closeMenu() {
  menuOpen.value = false;
  overflowTrigger.value?.querySelector('button')?.focus();
}

function navigateMenu(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.preventDefault();
    closeMenu();
    return;
  }
  if (event.key === 'Tab') {
    menuOpen.value = false;
    return;
  }
  const buttons = Array.from(overflowMenu.value?.querySelectorAll<HTMLButtonElement>('button') || []);
  const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
  let next: number;
  switch (event.key) {
    case 'ArrowDown':
      next = (index + 1) % buttons.length;
      break;
    case 'ArrowUp':
      next = (index - 1 + buttons.length) % buttons.length;
      break;
    case 'Home':
      next = 0;
      break;
    case 'End':
      next = buttons.length - 1;
      break;
    default:
      return;
  }
  event.preventDefault();
  buttons[next]?.focus();
}

async function selectHidden(id: string) {
  active.value = id;
  menuOpen.value = false;
  await nextTick();
  tabBar.value?.querySelector<HTMLButtonElement>('.document-tab.selected > button')?.focus();
}

function outsideClick(event: PointerEvent) {
  if (!tabBar.value?.contains(event.target as Node)) menuOpen.value = false;
}

onMounted(() => {
  measure();
  revealActive();
  resizeObserver = new ResizeObserver(measure);
  if (tabBar.value) resizeObserver.observe(tabBar.value);
  document.addEventListener('pointerdown', outsideClick);
});
onBeforeUnmount(() => {
  resizeObserver?.disconnect();
  document.removeEventListener('pointerdown', outsideClick);
});
</script>
<template>
  <div ref="tabBar" class="document-tabs-bar">
    <div class="document-tabs">
      <div v-for="item in visibleTabs" :key="item.request.id" class="document-tab"
           :class="{ selected: item.request.id === active }">
        <button :title="item.request.name" @click="active = item.request.id">
          <span class="method-label" :data-method="item.request.method">{{ item.request.method }}</span>
          <span class="tab-name">{{ item.request.name }}</span>
          <span v-if="dirty(item)" class="dirty-dot" :title="t.Unsaved({ name: item.request.name })"></span>
        </button>
        <IconButton icon="close" :label="t.Close()" @click="closeTab(item)"/>
      </div>
    </div>
    <div v-if="hiddenTabs.length" ref="overflowTrigger" class="request-tabs-overflow">
      <IconButton icon="chevron-down" :label="t.HiddenRequests()" aria-haspopup="menu" :aria-expanded="menuOpen"
                  @click="toggleMenu" @keydown.down.prevent="toggleMenu"/>
    </div>
    <div v-if="menuOpen && hiddenTabs.length" ref="overflowMenu" class="request-tabs-menu" role="menu"
         :aria-label="t.HiddenRequests()" @keydown="navigateMenu">
      <button v-for="item in hiddenTabs" :key="item.request.id" role="menuitem" @click="selectHidden(item.request.id)">
        <span class="method-label" :data-method="item.request.method">{{ item.request.method }}</span>
        <span class="tab-name">{{ item.request.name }}</span>
        <span v-if="dirty(item)" class="dirty-dot" :title="t.Unsaved({ name: item.request.name })"></span>
      </button>
    </div>
  </div>
</template>
