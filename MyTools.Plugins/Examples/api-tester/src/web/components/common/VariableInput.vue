<script setup lang="ts">
import {computed, nextTick, onBeforeUnmount, onMounted, ref, useAttrs, watch} from 'vue';
import {useWorkspaceContext} from '../../features/workspace/context.js';

defineOptions({inheritAttrs: false});
const SuggestionListLimit = 12;
const SuggestionListMinWidth = 160;
const props = defineProps<{
  modelValue?: string;
  multiline?: boolean;
  suggestions?: readonly string[];
}>();
const emit = defineEmits<{
  'update:modelValue': [value: string];
  input: [value: string];
}>();
const attrs = useAttrs();
const {environment, t} = useWorkspaceContext();
const VariableOpeningLength = 2;
const TooltipOffset = 12;
const TooltipViewportMargin = 8;
const TooltipMaximumWidth = 320;
const hoveredName = ref<string | undefined>();
const tooltipPosition = ref({left: '0px', top: '0px'});
const tooltip = ref<HTMLElement | null>(null);
const tooltipValue = computed(() => {
  const variable = environment.value?.variables.findLast(
      pair => pair.enabled && pair.name === hoveredName.value,
  );
  return variable ? variable.value || t.value.VariableEmptyValue() : t.value.VariableNotInEnvironment();
});
const text = ref(props.modelValue ?? String(attrs.value ?? ''));
const open = ref(false);
const highlight = ref(0);
const listStyle = ref<Record<string, string>>({});
const control = ref<HTMLInputElement | HTMLTextAreaElement | null>(null);
const filtered = computed(() => {
  const items = props.suggestions || [];
  if (!items.length) return [];
  const query = text.value.trim().toLowerCase();
  const matched = query
      ? items.filter((item) => item.toLowerCase().includes(query))
      : [...items];
  return matched.slice(0, SuggestionListLimit);
});
const showSuggestions = computed(
    () => open.value && !props.multiline && filtered.value.length > 0,
);
watch(showSuggestions, (value) => {
  if (value) {
    window.addEventListener('scroll', placeList, true);
    window.addEventListener('resize', placeList);
    return;
  }
  window.removeEventListener('scroll', placeList, true);
  window.removeEventListener('resize', placeList);
});
const mirror = ref<HTMLElement | null>(null);
const mirrorStyle = ref<Record<string, string>>({});
// Match the execution engine's variable syntax, including the braces.
const parts = computed(() => text.value.split(/(\{\{[^{}]+\}\})/g));
const hasVariables = computed(() => parts.value.length > 1);
let observer: ResizeObserver | undefined;
watch(() => props.modelValue ?? attrs.value, value => {
  text.value = String(value ?? '');
  nextTick(sync);
});

function commit(value: string) {
  text.value = value;
  emit('update:modelValue', value);
  emit('input', value);
}

function input(event: Event) {
  hoveredName.value = undefined;
  commit((event.target as HTMLInputElement).value);
  open.value = !!props.suggestions?.length;
  highlight.value = 0;
  nextTick(() => {
    sync();
    placeList();
  });
}

function placeList() {
  if (!control.value) return;
  const rect = control.value.getBoundingClientRect();
  listStyle.value = {
    left: `${rect.left}px`,
    top: `${rect.bottom}px`,
    width: `${Math.max(rect.width, SuggestionListMinWidth)}px`,
  };
}

function pick(value: string) {
  commit(value);
  open.value = false;
  nextTick(sync);
}

function onFocus() {
  if (!props.suggestions?.length) return;
  open.value = true;
  highlight.value = 0;
  nextTick(placeList);
}

function onBlur() {
  open.value = false;
}

function onKeydown(event: KeyboardEvent) {
  if (!showSuggestions.value) return;
  if (event.key === 'ArrowDown') {
    event.preventDefault();
    highlight.value = (highlight.value + 1) % filtered.value.length;
  } else if (event.key === 'ArrowUp') {
    event.preventDefault();
    highlight.value =
        (highlight.value - 1 + filtered.value.length) % filtered.value.length;
  } else if (event.key === 'Enter') {
    event.preventDefault();
    pick(filtered.value[highlight.value]);
  } else if (event.key === 'Escape') {
    event.preventDefault();
    open.value = false;
  }
}

function hover(event: PointerEvent) {
  const token = Array.from(mirror.value?.querySelectorAll<HTMLElement>('.variable-token') ?? [])
      .find(element => Array.from(element.getClientRects()).some(rect =>
          event.clientX >= rect.left && event.clientX <= rect.right &&
          event.clientY >= rect.top && event.clientY <= rect.bottom));
  hoveredName.value = token?.textContent?.slice(VariableOpeningLength, -VariableOpeningLength);
  if (hoveredName.value === undefined) return;
  tooltipPosition.value = {
    left: `${Math.max(TooltipViewportMargin, Math.min(event.clientX + TooltipOffset,
        window.innerWidth - TooltipMaximumWidth - TooltipViewportMargin))}px`,
    top: `${event.clientY + TooltipOffset}px`,
  };
  nextTick(() => {
    if (!tooltip.value) return;
    tooltipPosition.value.top = `${Math.max(TooltipViewportMargin, Math.min(event.clientY + TooltipOffset,
        window.innerHeight - tooltip.value.offsetHeight - TooltipViewportMargin))}px`;
  });
}

function sync() {
  if (!control.value || !mirror.value) return;
  const style = getComputedStyle(control.value);
  mirrorStyle.value = {
    font: style.font,
    lineHeight: props.multiline ? style.lineHeight
        : `${control.value.clientHeight - parseFloat(style.paddingTop) - parseFloat(style.paddingBottom)}px`,
    letterSpacing: style.letterSpacing,
    padding: style.padding,
    borderWidth: style.borderWidth,
    borderRadius: style.borderRadius,
    textAlign: style.textAlign,
    tabSize: style.tabSize,
  };
  mirror.value.scrollLeft = control.value.scrollLeft;
  mirror.value.scrollTop = control.value.scrollTop;
}

onMounted(() => {
  observer = new ResizeObserver(sync);
  if (control.value) observer.observe(control.value);
  sync();
});
onBeforeUnmount(() => {
  observer?.disconnect();
  window.removeEventListener('scroll', placeList, true);
  window.removeEventListener('resize', placeList);
});
</script>
<template>
  <div class="variable-input" :class="{ grow: !!attrs.class && String(attrs.class).includes('grow') }"
       @pointermove="hover" @pointerleave="hoveredName = undefined" @pointerdown="hoveredName = undefined">
    <component :is="multiline ? 'textarea' : 'input'" ref="control" v-bind="attrs"
               :value="text" :role="suggestions?.length ? 'combobox' : undefined"
               :aria-expanded="suggestions?.length ? showSuggestions : undefined"
               :aria-autocomplete="suggestions?.length ? 'list' : undefined"
               @input="input" @focus="onFocus" @blur="onBlur" @keydown="onKeydown"
               @scroll="sync(); hoveredName = undefined" @keyup="sync" @click="sync"/>
    <ul
        v-if="showSuggestions"
        class="suggestion-list"
        role="listbox"
        :style="listStyle"
        @mousedown.prevent
    >
      <li v-for="(item, index) in filtered" :key="item">
        <button
            type="button"
            role="option"
            :aria-selected="index === highlight"
            :class="{ active: index === highlight }"
            @mousedown.prevent="pick(item)"
        >
          {{ item }}
        </button>
      </li>
    </ul>
    <div v-if="hasVariables" ref="mirror" class="variable-input-mirror" :class="{ multiline }" :style="mirrorStyle" aria-hidden="true">
      <template v-for="(part, index) in parts" :key="index">
        <mark v-if="index % 2" class="variable-token">{{ part }}</mark>
        <span v-else>{{ part }}</span></template>
    </div>
    <div v-if="hoveredName !== undefined" ref="tooltip" class="variable-tooltip" role="tooltip"
         :style="tooltipPosition">
      <strong>{{ hoveredName }}</strong>
      <div>{{ tooltipValue }}</div>
    </div>
  </div>
</template>
