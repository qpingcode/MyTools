<script setup lang="ts">
import { ref, onMounted, onBeforeUnmount } from 'vue';
import { useText } from '../../localization/locale.js';
const SidebarDefaultWidth = 300;
const SidebarMinimumWidth = 240;
const SidebarMaximumWidth = 560;
const WorkspaceMinimumWidth = 420;
const KeyboardResizeStep = 20;
const SeparatorWidth = 5;
const width = ref(SidebarDefaultWidth);
const root = ref<HTMLElement | null>(null);
const dragging = ref(false);
const maximum = ref(SidebarMaximumWidth);
const t = useText();
let startX = 0;
let startWidth = SidebarDefaultWidth;
let observer: ResizeObserver | undefined;
function clamp(value: number) {
  return Math.min(maximum.value, Math.max(SidebarMinimumWidth, value));
}
function begin(event: PointerEvent) {
  if (event.button !== 0) return;
  event.preventDefault();
  startX = event.clientX;
  startWidth = width.value;
  dragging.value = true;
  (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
}
function resize(event: PointerEvent) {
  if (dragging.value) width.value = clamp(startWidth + event.clientX - startX);
}
function keyboard(event: KeyboardEvent) {
  if (!['ArrowLeft', 'ArrowRight', 'Home', 'End'].includes(event.key)) return;
  event.preventDefault();
  width.value = clamp(
    event.key === 'Home'
      ? SidebarMinimumWidth
      : event.key === 'End'
        ? maximum.value
        : width.value +
          (event.key === 'ArrowLeft'
            ? -KeyboardResizeStep
            : KeyboardResizeStep),
  );
}
onMounted(() => {
  observer = new ResizeObserver((entries) => {
    maximum.value = Math.max(
      SidebarMinimumWidth,
      Math.min(
        SidebarMaximumWidth,
        entries[0].contentRect.width - WorkspaceMinimumWidth - SeparatorWidth,
      ),
    );
    width.value = clamp(width.value);
  });
  if (root.value) observer.observe(root.value);
});
onBeforeUnmount(() => observer?.disconnect());
</script>
<template>
  <div
    ref="root"
    class="split-pane"
    :class="{ resizing: dragging }"
    :style="{ '--sidebar-width': width + 'px' }"
  >
    <slot name="sidebar" />
    <div
      class="splitter"
      role="separator"
      aria-orientation="vertical"
      :aria-label="t.ResizeSidebar()"
      :title="t.ResizeSidebar()"
      :aria-valuemin="SidebarMinimumWidth"
      :aria-valuemax="maximum"
      :aria-valuenow="width"
      tabindex="0"
      @pointerdown="begin"
      @pointermove="resize"
      @pointerup="dragging = false"
      @pointercancel="dragging = false"
      @lostpointercapture="dragging = false"
      @keydown="keyboard"
      @dblclick="width = clamp(SidebarDefaultWidth)"
    ></div>
    <slot />
  </div>
</template>
