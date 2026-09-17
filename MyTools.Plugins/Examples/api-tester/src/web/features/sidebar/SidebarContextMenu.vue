<script setup lang="ts">
import {nextTick, onBeforeUnmount, onMounted, ref} from 'vue';
import {useText} from '../../localization/locale.js';
import Icon from '../../components/common/Icon.vue';
import {SidebarMenuKind} from './sidebarMenuTypes.js';

const props = defineProps<{
  x: number; y: number; trigger: HTMLElement; label: string;
  kind: SidebarMenuKind;
  canMoveUp?: boolean; canMoveDown?: boolean;
}>();
const emit = defineEmits<{ close: []; up: []; down: []; copy: []; delete: []; settings: []; rename: []; run: []; add: []; addCollection: [] }>();
const t = useText();
const menu = ref<HTMLElement>();
const left = ref(props.x);
const top = ref(props.y);
const ContextMenuViewportMargin = 8;
let performingAction = false;

function close() {
  emit('close');
}

function outside(event: Event) {
  if (performingAction) return;
  if (event.target instanceof Node && menu.value?.contains(event.target)) return;
  close();
}

function perform(action: () => void) {
  performingAction = true;
  props.trigger.focus();
  action();
  close();
}

function keydown(event: KeyboardEvent) {
  if (event.key === 'Escape') {
    event.preventDefault();
    props.trigger.focus();
    close();
    return;
  }
  if (event.key === 'Tab') {
    close();
    return;
  }
  const buttons = Array.from(menu.value?.querySelectorAll<HTMLButtonElement>('button:not(:disabled)') ?? []);
  const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
  let target: HTMLButtonElement | undefined;
  switch (event.key) {
    case 'ArrowDown':
      target = buttons[(index + 1) % buttons.length];
      break;
    case 'ArrowUp':
      target = buttons[(index - 1 + buttons.length) % buttons.length];
      break;
    case 'Home':
      target = buttons[0];
      break;
    case 'End':
      target = buttons.at(-1);
      break;
    default:
      return;
  }
  event.preventDefault();
  target?.focus();
}

onMounted(async () => {
  await nextTick();
  const bounds = menu.value?.getBoundingClientRect();
  if (!bounds) return;
  left.value = Math.max(ContextMenuViewportMargin, Math.min(props.x, window.innerWidth - bounds.width - ContextMenuViewportMargin));
  top.value = Math.max(ContextMenuViewportMargin, Math.min(props.y, window.innerHeight - bounds.height - ContextMenuViewportMargin));
  menu.value?.querySelector<HTMLButtonElement>('button:not(:disabled)')?.focus();
  document.addEventListener('pointerdown', outside, true);
  document.addEventListener('focusin', outside, true);
  document.addEventListener('scroll', outside, true);
  window.addEventListener('resize', close);
  window.addEventListener('blur', close);
});
onBeforeUnmount(() => {
  document.removeEventListener('pointerdown', outside, true);
  document.removeEventListener('focusin', outside, true);
  document.removeEventListener('scroll', outside, true);
  window.removeEventListener('resize', close);
  window.removeEventListener('blur', close);
});
</script>
<template>
  <Teleport to="body">
    <div ref="menu" class="menu-popover sidebar-context-menu" role="menu" :aria-label="label"
         :style="{ left: left + 'px', top: top + 'px' }" @keydown="keydown" @contextmenu.prevent>
      <template v-if="kind === SidebarMenuKind.Request">
        <button role="menuitem" :disabled="!canMoveUp" @click="perform(() => emit('up'))">
          <Icon name="arrow-up"/>
          {{ t.Up() }}
        </button>
        <button role="menuitem" :disabled="!canMoveDown" @click="perform(() => emit('down'))">
          <Icon name="arrow-down"/>
          {{ t.Down() }}
        </button>
      </template>
      <template v-else>
        <button role="menuitem" @click="perform(() => emit('run'))">
          <Icon name="play"/>
          {{ t.RunCollection() }}
        </button>
        <button role="menuitem" @click="perform(() => emit('add'))">
          <Icon name="plus"/>
          {{ t.AddRequest() }}
        </button>
        <button role="menuitem" @click="perform(() => emit('addCollection'))">
          <Icon name="folder-plus"/>
          {{ t.AddSubcollection() }}
        </button>
        <button role="menuitem" @click="perform(() => emit('settings'))">
          <Icon name="more"/>
          {{ t.CollectionSettings() }}
        </button>
        <button role="menuitem" @click="perform(() => emit('rename'))">
          <Icon name="edit"/>
          {{ t.Rename() }}
        </button>
      </template>
      <button role="menuitem" @click="perform(() => emit('copy'))">
        <Icon name="copy"/>
        {{ t.Duplicate() }}
      </button>
      <button role="menuitem" class="sidebar-context-delete" @click="perform(() => emit('delete'))">
        <Icon name="trash"/>
        {{ t.Delete() }}
      </button>
    </div>
  </Teleport>
</template>
