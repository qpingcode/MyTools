<script setup lang="ts">
import {ref} from 'vue';
import {useText} from '../../localization/locale.js';

const DefaultRequestPercent = 50;
const MinimumRequestPercent = 15;
const MaximumRequestPercent = 85;
const KeyboardResizeStep = 5;
const PercentScale = 100;
const SeparatorHeight = 5;
const root = ref<HTMLElement | null>(null);
const requestPercent = ref(DefaultRequestPercent);
const dragging = ref(false);
const t = useText();
const props = defineProps<{responseMaximized?: boolean}>();
let startY = 0;
let startPercent = DefaultRequestPercent;

function setPercent(value: number) {
  requestPercent.value = Math.min(MaximumRequestPercent, Math.max(MinimumRequestPercent, value));
}

function begin(event: PointerEvent) {
  if (event.button !== 0) return;
  event.preventDefault();
  startY = event.clientY;
  startPercent = requestPercent.value;
  dragging.value = true;
  (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
}

function resize(event: PointerEvent) {
  const height = root.value?.clientHeight;
  if (dragging.value && height) {
    setPercent(startPercent + (event.clientY - startY) / height * PercentScale);
  }
}

function keyboard(event: KeyboardEvent) {
  if (!['ArrowUp', 'ArrowDown', 'Home', 'End'].includes(event.key)) return;
  event.preventDefault();
  setPercent(event.key === 'Home' ? MinimumRequestPercent
      : event.key === 'End' ? MaximumRequestPercent
          : requestPercent.value + (event.key === 'ArrowUp' ? -KeyboardResizeStep : KeyboardResizeStep));
}
</script>
<template>
  <div ref="root" class="request-response-split"
       :class="{ 'resizing-vertical': dragging, 'response-maximized': responseMaximized }"
       :style="{ gridTemplateRows: responseMaximized
       ? 'minmax(0, 1fr)'
       : `${requestPercent}fr ${SeparatorHeight}px ${PercentScale - requestPercent}fr` }">
    <div v-show="!responseMaximized" class="request-pane">
      <slot name="request"/>
    </div>
    <div v-show="!responseMaximized" class="response-splitter" role="separator" aria-orientation="horizontal"
         :aria-label="t.ResizeRequestResponse()" :title="t.ResizeRequestResponse()"
         :aria-valuemin="MinimumRequestPercent" :aria-valuemax="MaximumRequestPercent"
         :aria-valuenow="requestPercent" tabindex="0"
         @pointerdown="begin" @pointermove="resize" @pointerup="dragging = false"
         @pointercancel="dragging = false" @lostpointercapture="dragging = false"
         @keydown="keyboard" @dblclick="requestPercent = DefaultRequestPercent"></div>
    <div class="workspace-scroll">
      <slot/>
    </div>
  </div>
</template>
