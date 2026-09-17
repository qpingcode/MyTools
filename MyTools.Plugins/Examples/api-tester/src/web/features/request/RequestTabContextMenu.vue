<script setup lang="ts">
import {nextTick, onBeforeUnmount, onMounted, ref} from 'vue';
import Icon from '../../components/common/Icon.vue';

const props = defineProps<{
  x: number; y: number; trigger: HTMLElement; newLabel: string; duplicateLabel: string;
  closeLabel: string; closeOthersLabel: string; closeAllLabel: string; revealLabel: string;
}>();
const emit = defineEmits<{
  close: []; create: []; duplicate: []; closeTab: []; closeOthers: []; closeAll: []; reveal: [];
}>();
const menu = ref<HTMLElement>();
const left = ref(props.x);
const top = ref(props.y);
const ContextMenuViewportMargin = 8;
let performingAction = false;

function close() { emit('close'); }
function perform(action: () => void) {
  performingAction = true;
  props.trigger.focus();
  action();
  close();
}
function outside(event: Event) {
  if (!performingAction && event.target instanceof Node && !menu.value?.contains(event.target)) close();
}
function keydown(event: KeyboardEvent) {
  if (event.key === 'Escape') { event.preventDefault(); props.trigger.focus(); close(); return; }
  if (event.key === 'Tab') { close(); return; }
  const buttons = Array.from(menu.value?.querySelectorAll<HTMLButtonElement>('button') || []);
  const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
  const next = event.key === 'ArrowDown' ? (index + 1) % buttons.length
      : event.key === 'ArrowUp' ? (index - 1 + buttons.length) % buttons.length
          : event.key === 'Home' ? 0 : event.key === 'End' ? buttons.length - 1 : -1;
  if (next < 0) return;
  event.preventDefault();
  buttons[next]?.focus();
}
onMounted(async () => {
  await nextTick();
  const bounds = menu.value?.getBoundingClientRect();
  if (bounds) {
    left.value = Math.max(ContextMenuViewportMargin, Math.min(props.x, innerWidth - bounds.width - ContextMenuViewportMargin));
    top.value = Math.max(ContextMenuViewportMargin, Math.min(props.y, innerHeight - bounds.height - ContextMenuViewportMargin));
  }
  menu.value?.querySelector<HTMLButtonElement>('button')?.focus();
  document.addEventListener('pointerdown', outside, true);
  window.addEventListener('resize', close);
  window.addEventListener('blur', close);
});
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', outside, true);
  window.removeEventListener('resize', close);
  window.removeEventListener('blur', close);
});
</script>
<template>
  <Teleport to="body">
    <div ref="menu" class="menu-popover request-tab-context-menu" role="menu"
         :style="{left: left + 'px', top: top + 'px'}" @keydown="keydown" @contextmenu.prevent>
      <button role="menuitem" @click="perform(() => emit('create'))"><Icon name="plus"/>{{ newLabel }}</button>
      <button role="menuitem" @click="perform(() => emit('duplicate'))"><Icon name="copy"/>{{ duplicateLabel }}</button>
      <button role="menuitem" @click="perform(() => emit('closeTab'))"><Icon name="close"/>{{ closeLabel }}</button>
      <button role="menuitem" @click="perform(() => emit('closeOthers'))"><Icon name="close"/>{{ closeOthersLabel }}</button>
      <button role="menuitem" @click="perform(() => emit('closeAll'))"><Icon name="close"/>{{ closeAllLabel }}</button>
      <button role="menuitem" @click="perform(() => emit('reveal'))"><Icon name="folder"/>{{ revealLabel }}</button>
    </div>
  </Teleport>
</template>
