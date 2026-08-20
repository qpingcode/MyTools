<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from "vue";
import HighlightText from "../components/HighlightText.vue";
import TableToolbar from "../components/TableToolbar.vue";
import ScalarSettingsPanel from "./ScalarSettingsPanel.vue";
import { bus } from "../bus";
import { captureInputAction } from "../capture-input-action";
import { t } from "../i18n";
import { markGesturesDirty, store } from "../store";
import type { GestureConfig } from "../types";

const DIRECTION_ARROWS: Record<string, string> = {
    Up: "↑",
    Down: "↓",
    Left: "←",
    Right: "→",
};

const GESTURE_DISTANCE_THRESHOLD = 30;
const GESTURE_VISIBLE_DIRS = 4;

const recording = ref(false);
const recordingHint = computed(() => t("Plugin.Settings.Gestures.Recording", "Hold right button to draw..."));
const trail = ref("");
const overlayRef = ref<HTMLElement | null>(null);
let recordingTarget: GestureConfig | null = null;
let directions: string[] = [];
let lastPoint: { x: number; y: number } | null = null;
let isDrawing = false;

const gestures = computed(() => store.gestureConfigs || []);
const tableQuery = ref("");
const highlightQuery = computed(() => tableQuery.value.trim() || store.searchQuery);

const headers = computed(() => ({
    action: t("Plugin.Settings.Gestures.HeaderAction", "Action Name"),
    actionTip: t("Plugin.Settings.Gestures.HeaderActionTip", "The name of this gesture action."),
    gesture: t("Plugin.Settings.Gestures.HeaderGesture", "Trigger Gesture"),
    gestureTip: t("Plugin.Settings.Gestures.HeaderGestureTip", "Click to re-record. Hold the right mouse button and draw."),
    process: t("Plugin.Settings.Gestures.HeaderProcess", "Target Process"),
    processTip: t("Plugin.Settings.Gestures.HeaderProcessTip", "Only trigger this gesture in the specified processes. Leave empty to apply to all processes."),
    trigger: t("Plugin.Settings.Gestures.HeaderTrigger", "Action"),
    triggerTip: t("Plugin.Settings.Gestures.HeaderTriggerTip", "The action to run. Click to choose a keyboard shortcut or mouse button."),
    enabled: t("Plugin.Settings.Gestures.HeaderEnabled", "Enabled"),
    enabledTip: t("Plugin.Settings.Gestures.HeaderEnabledTip", "Enable or disable this gesture."),
}));

watch(recording, async (value) => {
    if (!value) return;
    await nextTick();
    overlayRef.value?.focus();
});

function directionsToArrows(dirs: string[]): string {
    return dirs.map((dir) => DIRECTION_ARROWS[dir] || dir).join(" ");
}

function formatGestureDisplay(dirs: string[]): { visible: string; full: string; truncated: boolean } {
    const full = directionsToArrows(dirs);
    if (dirs.length <= GESTURE_VISIBLE_DIRS) {
        return { visible: full, full, truncated: false };
    }
    return {
        visible: directionsToArrows(dirs.slice(0, GESTURE_VISIBLE_DIRS)) + " …",
        full,
        truncated: true,
    };
}

function formatMouseButtonShort(mouseButton: string): string {
    if (mouseButton === "XButton2") return t("Plugin.Settings.Gestures.MouseForwardShort", "Forward");
    if (mouseButton === "XButton1") return t("Plugin.Settings.Gestures.MouseBackShort", "Back");
    if (mouseButton === "Left") return t("Plugin.Settings.Gestures.MouseLeftShort", "Left");
    if (mouseButton === "Right") return t("Plugin.Settings.Gestures.MouseRightShort", "Right");
    if (mouseButton === "Middle") return t("Plugin.Settings.Gestures.MouseMiddleShort", "Middle");
    return mouseButton;
}

function formatActionDisplay(gesture: GestureConfig): { text: string; empty: boolean; title: string } {
    if (gesture.actionType === "mouse") {
        const mouseLabel = gesture.mouseButton ? formatMouseButtonShort(gesture.mouseButton) : null;
        if (mouseLabel && gesture.mouseButton) {
            return { text: mouseLabel, empty: false, title: mouseLabel };
        }
    } else if (gesture.hotKey) {
        return { text: gesture.hotKey, empty: false, title: gesture.hotKey };
    }
    const none = t("Plugin.Settings.Gestures.NoAction", "Not set");
    return { text: none, empty: true, title: t("Plugin.Settings.Gestures.ClickToSetAction", "Click to set action") };
}

function gesturesConflict(a: GestureConfig, b: GestureConfig): boolean {
    if (!a.isEnabled || !b.isEnabled) return false;
    if (a.directions.length === 0 || a.directions.length !== b.directions.length) return false;
    if (a.directions.some((dir, index) => dir !== b.directions[index])) return false;
    const aAny = a.processNames.length === 0;
    const bAny = b.processNames.length === 0;
    if (aAny && bAny) return true;
    if (aAny || bAny) return false;
    return a.processNames.some((name) => b.processNames.includes(name));
}

function conflictMap(): Map<string, string> {
    const map = new Map<string, string>();
    const configs = store.gestureConfigs || [];
    for (let i = 0; i < configs.length; i += 1) {
        for (let j = i + 1; j < configs.length; j += 1) {
            const a = configs[i];
            const b = configs[j];
            if (!gesturesConflict(a, b)) continue;
            const aAny = a.processNames.length === 0;
            const bAny = b.processNames.length === 0;
            const aWins = !aAny && bAny;
            appendConflict(map, a, b, aWins);
            appendConflict(map, b, a, !aWins && !(aAny && !bAny));
        }
    }
    return map;
}

function appendConflict(map: Map<string, string>, self: GestureConfig, other: GestureConfig, selfWins: boolean): void {
    const otherName = other.actionName || t("Plugin.Settings.Gestures.Unnamed", "Unnamed");
    const msg = selfWins
        ? t("Plugin.Settings.Gestures.ConflictWins", "Conflicts with \"{{name}}\". This one takes priority.", { name: otherName })
        : t("Plugin.Settings.Gestures.ConflictLose", "Conflicts with \"{{name}}\", which will take priority.", { name: otherName });
    const existing = map.get(self.id);
    map.set(self.id, existing ? existing + "\n" + msg : msg);
}

const conflicts = computed(() => conflictMap());

const filteredGestures = computed(() => {
    const query = tableQuery.value.trim().toLowerCase();
    if (!query) return gestures.value;
    return gestures.value.filter((gesture) => {
        const action = formatActionDisplay(gesture).text;
        const gestureText = formatGestureDisplay(gesture.directions).full;
        const haystack = [
            gesture.actionName,
            action,
            gestureText,
            (gesture.processNames || []).join(" "),
            gesture.hotKey || "",
            gesture.mouseButton || "",
        ].join(" ").toLowerCase();
        return haystack.includes(query);
    });
});

type EditableField = "name" | "process";

const editing = ref<{ gesture: GestureConfig; field: EditableField } | null>(null);
const editInputRef = ref<{ focus: () => void } | null>(null);

function processText(gesture: GestureConfig): string {
    return (gesture.processNames || []).join(", ");
}

function isEditing(gesture: GestureConfig, field: EditableField): boolean {
    return editing.value?.gesture === gesture && editing.value.field === field;
}

async function startEdit(gesture: GestureConfig, field: EditableField): Promise<void> {
    editing.value = { gesture, field };
    await nextTick();
    editInputRef.value?.focus();
}

function stopEdit(): void {
    editing.value = null;
}

function markDirty(): void {
    markGesturesDirty();
}

function addGesture(): void {
    if (!store.gestureConfigs) store.gestureConfigs = [];
    const created: GestureConfig = {
        id: "",
        directions: [],
        actionName: "",
        actionType: "hotkey",
        hotKey: null,
        mouseButton: null,
        processNames: [],
        isEnabled: true,
    };
    store.gestureConfigs.push(created);
    markDirty();
    void startEdit(created, "name");
}

function removeGesture(gesture: GestureConfig): void {
    if (editing.value?.gesture === gesture) {
        stopEdit();
    }
    if (!store.gestureConfigs) return;
    const index = store.gestureConfigs.indexOf(gesture);
    if (index >= 0) {
        store.gestureConfigs.splice(index, 1);
        markDirty();
    }
}

function onProcessChange(gesture: GestureConfig, value: string): void {
    gesture.processNames = value.split(",").map((item) => item.trim()).filter(Boolean);
    markDirty();
}

async function setAction(gesture: GestureConfig): Promise<void> {
    const result = await captureInputAction({
        showKeyboard: true,
        showMouse: true,
        value: {
            kind: gesture.actionType === "mouse" ? "mouse" : "hotkey",
            hotKey: gesture.hotKey ?? null,
            mouseButton: gesture.mouseButton ?? null,
        },
        excludeReservedHotKey: true,
    });
    if (!result) return;
    gesture.actionType = result.kind;
    gesture.hotKey = result.kind === "hotkey" ? (result.hotKey ?? null) : null;
    gesture.mouseButton = result.kind === "mouse" ? (result.mouseButton ?? null) : null;
    markDirty();
}

function startRecording(gesture: GestureConfig): void {
    recordingTarget = gesture;
    directions = [];
    lastPoint = null;
    isDrawing = false;
    trail.value = "";
    recording.value = true;
    void bus.call("suspendGestures");
}

function stopRecording(commit: boolean): void {
    recording.value = false;
    if (commit && recordingTarget) {
        recordingTarget.directions = [...directions];
        markDirty();
    }
    recordingTarget = null;
    void bus.call("resumeGestures");
}

function detectDirection(currentX: number, currentY: number): void {
    if (!lastPoint) return;
    const deltaX = currentX - lastPoint.x;
    const deltaY = currentY - lastPoint.y;
    if (Math.abs(deltaX) < GESTURE_DISTANCE_THRESHOLD && Math.abs(deltaY) < GESTURE_DISTANCE_THRESHOLD) {
        return;
    }
    const direction = Math.abs(deltaX) > Math.abs(deltaY)
        ? (deltaX > 0 ? "Right" : "Left")
        : (deltaY > 0 ? "Down" : "Up");
    if (directions.length > 0 && directions[directions.length - 1] === direction) {
        lastPoint = { x: currentX, y: currentY };
        return;
    }
    directions.push(direction);
    lastPoint = { x: currentX, y: currentY };
    trail.value = directionsToArrows(directions);
}

function onMouseDown(event: MouseEvent): void {
    if (event.button !== 2) return;
    event.preventDefault();
    isDrawing = true;
    directions = [];
    lastPoint = { x: event.clientX, y: event.clientY };
    trail.value = "";
}

function onMouseMove(event: MouseEvent): void {
    if (!isDrawing) return;
    event.preventDefault();
    detectDirection(event.clientX, event.clientY);
}

function onMouseUp(event: MouseEvent): void {
    if (event.button !== 2 || !isDrawing) return;
    event.preventDefault();
    stopRecording(true);
}

function onKeyDown(event: KeyboardEvent): void {
    if (event.key === "Escape") stopRecording(false);
}

onBeforeUnmount(() => {
    if (recording.value) stopRecording(false);
});
</script>

<template>
    <div>
        <ScalarSettingsPanel />
        <TableToolbar
            v-model="tableQuery"
            :placeholder="t('Plugin.Settings.Table.Search', 'Search')"
        >
            <n-button size="small" secondary @click="addGesture">
                <template #icon>
                    <i class="mdi mdi-plus"></i>
                </template>
                {{ t("Plugin.Settings.Table.Add", "Add") }}
            </n-button>
        </TableToolbar>
        <div v-if="gestures.length === 0" class="empty">
            {{ t("Plugin.Settings.Gestures.Empty", "No gestures configured") }}
        </div>
        <div v-else class="gesture-panel">
            <div class="gesture-header">
                <div class="col-name" :title="headers.actionTip">{{ headers.action }}</div>
                <div class="col-gesture" :title="headers.gestureTip">{{ headers.gesture }}</div>
                <div class="col-process" :title="headers.processTip">{{ headers.process }}</div>
                <div class="col-trigger" :title="headers.triggerTip">{{ headers.trigger }}</div>
                <div class="col-enabled" :title="headers.enabledTip">{{ headers.enabled }}</div>
                <div class="col-actions"></div>
            </div>
            <div v-for="(gesture, index) in filteredGestures" :key="gesture.id || index" class="gesture-row">
                <div class="col-name">
                    <i
                        v-if="conflicts.get(gesture.id)"
                        class="mdi mdi-alert conflict-icon"
                        :title="conflicts.get(gesture.id)"
                    ></i>
                    <n-input
                        v-if="isEditing(gesture, 'name')"
                        ref="editInputRef"
                        :value="gesture.actionName"
                        :placeholder="t('Plugin.Settings.Gestures.NamePlaceholder', 'e.g. Close Tab')"
                        size="small"
                        @update:value="
                            gesture.actionName = String($event || '');
                            markDirty();
                        "
                        @blur="stopEdit"
                        @keydown.enter.prevent="stopEdit"
                        @keydown.esc.prevent="stopEdit"
                    />
                    <button
                        v-else
                        type="button"
                        class="flat-display"
                        :class="{ empty: !gesture.actionName }"
                        :title="gesture.actionName || t('Plugin.Settings.Gestures.NamePlaceholder', 'e.g. Close Tab')"
                        @click="startEdit(gesture, 'name')"
                    >
                        <HighlightText
                            v-if="gesture.actionName"
                            :text="gesture.actionName"
                            :query="highlightQuery"
                        />
                        <span v-else>{{ t("Plugin.Settings.Gestures.NamePlaceholder", "e.g. Close Tab") }}</span>
                    </button>
                </div>
                <div class="col-gesture">
                    <button
                        type="button"
                        class="flat-display"
                        :class="{ empty: gesture.directions.length === 0 }"
                        :title="
                            gesture.directions.length === 0
                                ? t('Plugin.Settings.Gestures.ClickToRecord', 'Click to record')
                                : formatGestureDisplay(gesture.directions).full
                        "
                        @click="startRecording(gesture)"
                    >
                        <span v-if="gesture.directions.length === 0">
                            {{ t("Plugin.Settings.Gestures.NoGesture", "Not set") }}
                        </span>
                        <HighlightText
                            v-else
                            :text="formatGestureDisplay(gesture.directions).visible"
                            :query="highlightQuery"
                        />
                    </button>
                </div>
                <div class="col-process">
                    <n-input
                        v-if="isEditing(gesture, 'process')"
                        ref="editInputRef"
                        :value="processText(gesture)"
                        :placeholder="t('Plugin.Settings.Gestures.ProcessPlaceholder', 'Any')"
                        :title="t('Plugin.Settings.Gestures.ProcessHint', 'Comma-separated process names')"
                        size="small"
                        @update:value="onProcessChange(gesture, String($event || ''))"
                        @blur="stopEdit"
                        @keydown.enter.prevent="stopEdit"
                        @keydown.esc.prevent="stopEdit"
                    />
                    <button
                        v-else
                        type="button"
                        class="flat-display"
                        :class="{ empty: !processText(gesture) }"
                        :title="processText(gesture) || t('Plugin.Settings.Gestures.ProcessHint', 'Comma-separated process names')"
                        @click="startEdit(gesture, 'process')"
                    >
                        <HighlightText
                            v-if="processText(gesture)"
                            :text="processText(gesture)"
                            :query="highlightQuery"
                        />
                        <span v-else>{{ t("Plugin.Settings.Gestures.ProcessPlaceholder", "Any") }}</span>
                    </button>
                </div>
                <div class="col-trigger">
                    <button
                        type="button"
                        class="flat-display"
                        :class="{ empty: formatActionDisplay(gesture).empty }"
                        :title="formatActionDisplay(gesture).title"
                        @click="setAction(gesture)"
                    >
                        {{ formatActionDisplay(gesture).text }}
                    </button>
                </div>
                <div class="col-enabled">
                    <n-checkbox
                        :checked="gesture.isEnabled"
                        @update:checked="
                            gesture.isEnabled = !!$event;
                            markDirty();
                        "
                    />
                </div>
                <div class="col-actions">
                    <button
                        type="button"
                        class="icon-delete-btn"
                        :title="t('Plugin.Settings.Gestures.Delete', 'Delete')"
                        @click="removeGesture(gesture)"
                    >
                        <i class="mdi mdi-trash-can-outline delete-icon"></i>
                    </button>
                </div>
            </div>
        </div>
        <div
            v-if="recording"
            ref="overlayRef"
            class="gesture-record-overlay"
            tabindex="0"
            @mousedown="onMouseDown"
            @mousemove="onMouseMove"
            @mouseup="onMouseUp"
            @contextmenu.prevent
            @keydown="onKeyDown"
        >
            <div class="gesture-record-hint">{{ recordingHint }}</div>
            <div class="gesture-record-trail">{{ trail }}</div>
        </div>
    </div>
</template>

<style scoped>
.empty {
    padding: 24px 0;
    text-align: center;
    opacity: 0.6;
}

.gesture-header,
.gesture-row {
    display: flex;
    align-items: center;
    gap: 6px;
}

.gesture-header {
    padding: 8px 0 10px;
    border-bottom: 1px solid var(--mt-border, #404040);
    font-size: 12px;
    font-weight: 600;
    color: var(--mt-text-tertiary, #aaaaaa);
}

.gesture-row {
    padding: 10px 0;
    border-bottom: 1px solid var(--mt-border, #404040);
}

.col-name {
    width: 140px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    gap: 4px;
    min-width: 0;
}

.col-name > .flat-display,
.col-name :deep(.n-input),
.col-process > .flat-display,
.col-process :deep(.n-input) {
    flex: 1 1 auto;
    width: 100%;
    min-width: 0;
}

.col-gesture {
    width: 110px;
    flex-shrink: 0;
}

.col-process {
    width: 140px;
    flex-shrink: 0;
    display: flex;
    align-items: center;
    min-width: 0;
}

.col-trigger {
    width: 110px;
    flex-shrink: 0;
}

.col-enabled,
.col-actions {
    width: 48px;
    flex-shrink: 0;
    display: flex;
    justify-content: center;
}

.icon-delete-btn {
    width: 28px;
    height: 28px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border: none;
    border-radius: 8px;
    background: transparent;
    color: var(--mt-text-tertiary, #aaaaaa);
    cursor: pointer;
    transition: background-color 140ms ease, color 140ms ease, transform 120ms ease;
}

.icon-delete-btn:hover {
    background: rgba(239, 68, 68, 0.14);
    color: #ef4444;
}

.icon-delete-btn:active {
    transform: scale(0.96);
}

.delete-icon {
    font-size: 16px;
    line-height: 1;
}

.flat-display {
    width: 100%;
    border: none;
    background: transparent;
    color: var(--mt-text, #fff);
    text-align: left;
    padding: 6px 8px;
    border-radius: 8px;
    cursor: pointer;
    font: inherit;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.flat-display:hover {
    background: var(--mt-surface-hover, #3a3a3a);
}

.flat-display.empty {
    font-style: italic;
    opacity: 0.6;
}

.gesture-record-overlay {
    position: fixed;
    inset: 0;
    z-index: 300;
    background: rgba(0, 0, 0, 0.45);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    cursor: crosshair;
    user-select: none;
}

.gesture-record-hint {
    color: var(--mt-text, #1e1e1e);
    font-size: 18px;
    font-weight: 500;
    margin-bottom: 20px;
    padding: 12px 28px;
    background: var(--mt-surface, #ffffff);
    border: 1px solid var(--mt-accent, #3f51b5);
    border-radius: 10px;
    box-shadow: 0 8px 24px rgba(0, 0, 0, 0.18);
}

.gesture-record-trail {
    color: #ffffff;
    font-size: 48px;
    font-family: "Cascadia Code", Consolas, monospace;
    letter-spacing: 8px;
    min-height: 64px;
    text-shadow: 0 1px 8px rgba(0, 0, 0, 0.55);
}

.conflict-icon {
    color: #f44336;
}
</style>
