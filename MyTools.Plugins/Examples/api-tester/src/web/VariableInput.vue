<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, onMounted, ref, useAttrs, watch } from 'vue';
import { useWorkspaceContext } from './context.js';
defineOptions({ inheritAttrs: false });
const props = defineProps<{ modelValue?: string; multiline?: boolean }>();
const emit = defineEmits<{ 'update:modelValue': [value: string] }>();
const attrs = useAttrs();
const { environment, t } = useWorkspaceContext();
const VariableOpeningLength = 2;
const TooltipOffset = 12;
const TooltipViewportMargin = 8;
const TooltipMaximumWidth = 320;
const hoveredName = ref<string | undefined>();
const tooltipPosition = ref({ left: '0px', top: '0px' });
const tooltip = ref<HTMLElement | null>(null);
const tooltipValue = computed(() => {
  const variable = environment.value?.variables.findLast(
    pair => pair.enabled && pair.name === hoveredName.value,
  );
  return variable ? variable.value || t.value.VariableEmptyValue() : t.value.VariableNotInEnvironment();
});
const text = ref(props.modelValue ?? String(attrs.value ?? ''));
const control = ref<HTMLInputElement | HTMLTextAreaElement | null>(null);
const mirror = ref<HTMLElement | null>(null);
const mirrorStyle = ref<Record<string, string>>({});
// Match the execution engine's variable syntax, including the braces.
const parts = computed(() => text.value.split(/(\{\{[^{}]+\}\})/g));
let observer: ResizeObserver | undefined;
watch(() => props.modelValue ?? attrs.value, value => {
  text.value = String(value ?? '');
  nextTick(sync);
});
function input(event: Event) {
  hoveredName.value = undefined;
  text.value = (event.target as HTMLInputElement).value;
  emit('update:modelValue', text.value);
  nextTick(sync);
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
onBeforeUnmount(() => observer?.disconnect());
</script>
<template>
  <div class="variable-input" :class="{ grow: !!attrs.class && String(attrs.class).includes('grow') }"
    @pointermove="hover" @pointerleave="hoveredName = undefined" @pointerdown="hoveredName = undefined">
    <component :is="multiline ? 'textarea' : 'input'" ref="control" v-bind="attrs"
      :value="text" @input="input" @scroll="sync(); hoveredName = undefined" @keyup="sync" @click="sync" />
    <div ref="mirror" class="variable-input-mirror" :class="{ multiline }" :style="mirrorStyle" aria-hidden="true">
      <template v-for="(part, index) in parts" :key="index"><mark v-if="index % 2" class="variable-token">{{ part }}</mark><span v-else>{{ part }}</span></template>
    </div>
    <div v-if="hoveredName !== undefined" ref="tooltip" class="variable-tooltip" role="tooltip" :style="tooltipPosition">
      <strong>{{ hoveredName }}</strong>
      <div>{{ tooltipValue }}</div>
    </div>
  </div>
</template>
