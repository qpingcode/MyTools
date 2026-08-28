<script setup lang="ts">
import { computed, reactive, ref } from "vue";
import HighlightText from "./HighlightText.vue";
import SettingField from "./SettingField.vue";
import TableToolbar from "./TableToolbar.vue";
import { t } from "../i18n";
import { markSettingDirty, store } from "../store";
import {
    coercePropertyValue,
    defaultPropertyValue,
    evaluateVisibility,
    formatCellText,
    isTruthyBool,
    parseArrayValue,
} from "../setting-utils";
import type { Setting, SettingSchemaProperty } from "../types";

const props = defineProps<{
    setting: Setting;
}>();

type EditorMode = "add" | "edit" | "clone";

const editorOpen = ref(false);
const editingIndex = ref<number | null>(null);
const editorMode = ref<EditorMode>("add");
const tableQuery = ref("");
const draft = reactive<Record<string, unknown>>({});

const properties = computed((): SettingSchemaProperty[] => props.setting.schema?.properties ?? []);
const editorProperties = computed(() =>
    properties.value.filter((property) => !property.hidden && property.type.toLowerCase() !== "hidden"));
const tableProperties = computed(() =>
    editorProperties.value.filter((property) => property.table !== false));
const visibleEditorProperties = computed(() => {
    const lookup = (name: string) => {
        const match = properties.value.find((property) => property.key.toLowerCase() === name.toLowerCase());
        return match ? draft[match.key] : undefined;
    };
    return editorProperties.value.filter((property) => evaluateVisibility(property.visibility, lookup));
});

const rows = computed(() => {
    const dirty = store.dirtySettings.get(props.setting.fullPath);
    return parseArrayValue(dirty !== undefined ? dirty : props.setting.currentValue);
});

const filteredRows = computed(() => {
    const query = tableQuery.value.trim().toLowerCase();
    if (!query) return rows.value.map((row, index) => ({ row, index }));
    return rows.value
        .map((row, index) => ({ row, index }))
        .filter(({ row }) =>
            editorProperties.value.some((property) =>
                formatCellText(row[property.key]).toLowerCase().includes(query)));
});

const labels = computed(() => ({
    search: t("Plugin.Settings.Table.Search", "Search"),
    add: t("Plugin.Settings.Table.Add", "Add"),
    edit: t("Plugin.Settings.Table.Edit", "Edit"),
    clone: t("Plugin.Settings.Table.Clone", "Clone"),
    delete: t("Plugin.Settings.Table.Delete", "Delete"),
    empty: t("Plugin.Settings.Table.Empty", "No items"),
    apply: t("Plugin.Settings.Table.Apply", "Apply"),
    cancel: t("Plugin.Settings.Cancel", "Cancel"),
}));
const editorTitle = computed(() =>
    editorMode.value === "clone" ? labels.value.clone : editorMode.value === "edit" ? labels.value.edit : labels.value.add);
const editorIcon = computed(() =>
    editorMode.value === "clone" ? "mdi-content-copy" : editorMode.value === "edit" ? "mdi-pencil-outline" : "mdi-plus");

function currentRows(): Record<string, unknown>[] {
    return rows.value.map((row) => ({ ...row }));
}

function persist(next: Record<string, unknown>[]): void {
    markSettingDirty(props.setting.fullPath, JSON.stringify(next));
}

function emptyRow(): Record<string, unknown> {
    const row: Record<string, unknown> = {};
    for (const property of properties.value) {
        row[property.key] = defaultPropertyValue(property.type, property.defaultValue);
    }
    return row;
}

function openEditor(index: number | null): void {
    editingIndex.value = index;
    editorMode.value = index == null ? "add" : "edit";
    const source = index == null ? emptyRow() : { ...rows.value[index] };
    populateDraft(source);
    editorOpen.value = true;
}

function openCloneEditor(index: number): void {
    editingIndex.value = null;
    editorMode.value = "clone";
    populateDraft(structuredClone(rows.value[index]));
    editorOpen.value = true;
}

function populateDraft(source: Record<string, unknown>): void {
    for (const key of Object.keys(draft)) {
        delete draft[key];
    }
    for (const property of properties.value) {
        draft[property.key] = coercePropertyValue(property.type, source[property.key] ?? defaultPropertyValue(property.type, property.defaultValue));
    }
}

function saveEditor(): void {
    const next = currentRows();
    const item: Record<string, unknown> = {};
    for (const property of properties.value) {
        item[property.key] = coercePropertyValue(
            property.type,
            property.hidden || property.type.toLowerCase() === "hidden"
                ? (editingIndex.value == null
                    ? defaultPropertyValue(property.type, property.defaultValue)
                    : next[editingIndex.value][property.key] ?? defaultPropertyValue(property.type, property.defaultValue))
                : draft[property.key],
        );
    }
    if (editingIndex.value == null) {
        next.push(item);
    } else {
        next.splice(editingIndex.value, 1, item);
    }
    persist(next);
    editorOpen.value = false;
}

function removeRow(index: number): void {
    const next = currentRows();
    next.splice(index, 1);
    persist(next);
}

</script>

<template>
    <div class="array-setting">
        <TableToolbar v-model="tableQuery" :placeholder="labels.search">
            <n-button size="small" secondary @click="openEditor(null)">
                <template #icon>
                    <i class="mdi mdi-plus"></i>
                </template>
                {{ labels.add }}
            </n-button>
        </TableToolbar>

        <div v-if="rows.length === 0" class="empty">{{ labels.empty }}</div>
        <div v-else class="array-table-scroll">
            <div class="array-table">
                <div class="table-head">
                    <div
                        v-for="property in tableProperties"
                        :key="property.key"
                        class="table-cell"
                        :class="property.type.toLowerCase() === 'bool' ? 'col-bool' : 'col-text'"
                        :title="property.title || property.key"
                    >
                        {{ property.title || property.key }}
                    </div>
                    <div class="col-actions"></div>
                </div>
                <div v-for="item in filteredRows" :key="item.index" class="table-row">
                    <div
                        v-for="property in tableProperties"
                        :key="property.key"
                        class="table-cell"
                        :class="property.type.toLowerCase() === 'bool' ? 'col-bool' : 'col-text'"
                    >
                        <i
                            v-if="property.type.toLowerCase() === 'bool' && isTruthyBool(item.row[property.key])"
                            class="mdi mdi-check"
                        ></i>
                        <span v-else-if="property.type.toLowerCase() !== 'bool'" class="cell-text" :title="formatCellText(item.row[property.key])">
                            <HighlightText :text="formatCellText(item.row[property.key])" :query="tableQuery || store.searchQuery" />
                        </span>
                    </div>
                    <div class="col-actions">
                        <button type="button" class="icon-btn" :title="labels.edit" @click="openEditor(item.index)">
                            <i class="mdi mdi-pencil-outline"></i>
                        </button>
                        <button type="button" class="icon-btn" :title="labels.clone" @click="openCloneEditor(item.index)">
                            <i class="mdi mdi-content-copy"></i>
                        </button>
                        <button type="button" class="icon-btn icon-delete" :title="labels.delete" @click="removeRow(item.index)">
                            <i class="mdi mdi-trash-can-outline"></i>
                        </button>
                    </div>
                </div>
            </div>
        </div>

        <n-modal v-model:show="editorOpen" :mask-closable="false">
            <n-card
                class="row-editor"
                :class="`mode-${editorMode}`"
                :bordered="false"
                content-style="padding: 0;"
                role="dialog"
                aria-modal="true"
            >
                <div class="editor-header">
                    <span class="editor-header-icon">
                        <i class="mdi" :class="editorIcon"></i>
                    </span>
                    <div class="editor-heading">
                        <div class="editor-title">{{ editorTitle }}</div>
                        <div v-if="setting.title" class="editor-context">{{ setting.title }}</div>
                    </div>
                    <button
                        type="button"
                        class="editor-close"
                        :title="labels.cancel"
                        :aria-label="labels.cancel"
                        @click="editorOpen = false"
                    >
                        <i class="mdi mdi-close"></i>
                    </button>
                </div>
                <div class="editor-body">
                    <div class="editor-form">
                        <div v-for="property in visibleEditorProperties" :key="property.key" class="editor-field">
                            <div class="form-label">{{ property.title || property.key }}</div>
                            <SettingField
                                :type="property.type"
                                :ui-hint="property.uiHint"
                                :title="property.title || property.key"
                                :model-value="draft[property.key]"
                                @update:model-value="draft[property.key] = $event"
                            />
                        </div>
                    </div>
                </div>
                <div class="editor-toolbar">
                    <div class="toolbar-spacer"></div>
                    <n-button secondary @click="editorOpen = false">{{ labels.cancel }}</n-button>
                    <n-button type="primary" @click="saveEditor">
                        <template #icon>
                            <i class="mdi mdi-check"></i>
                        </template>
                        {{ labels.apply }}
                    </n-button>
                </div>
            </n-card>
        </n-modal>
    </div>
</template>

<style scoped>
.array-setting {
    width: 100%;
    max-width: 100%;
    min-width: 0;
    box-sizing: border-box;
}

.empty {
    padding: 28px 8px;
    text-align: center;
    color: var(--mt-text-tertiary, #aaaaaa);
    font-size: 13px;
}

.array-table-scroll {
    width: 100%;
    max-width: 100%;
    min-width: 0;
    box-sizing: border-box;
    overflow: auto;
}

.array-table-scroll::-webkit-scrollbar {
    width: 6px;
    height: 6px;
}

.array-table-scroll::-webkit-scrollbar-thumb {
    background: var(--mt-border, #404040);
    border-radius: 8px;
}

.array-table-scroll::-webkit-scrollbar-track {
    background: transparent;
}

.array-table {
    min-width: 100%;
    width: 100%;
}

.table-head,
.table-row {
    display: flex;
    align-items: center;
    gap: 8px;
    min-width: 100%;
    box-sizing: border-box;
}

.table-head {
    padding: 8px 0 10px;
    border-bottom: 1px solid var(--mt-border, #404040);
    font-size: 12px;
    font-weight: 600;
    color: var(--mt-text-tertiary, #aaaaaa);
}

.table-row {
    padding: 10px 0;
    border-bottom: 1px solid var(--mt-border, #404040);
}

.col-text {
    flex: 1 1 180px;
    min-width: 160px;
    max-width: 280px;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.col-bool {
    flex: 0 0 120px;
    width: 120px;
    display: flex;
    justify-content: center;
    white-space: nowrap;
}

.col-actions {
    flex: 0 0 100px;
    position: sticky;
    right: 0;
    z-index: 2;
    display: flex;
    justify-content: flex-end;
    gap: 2px;
    align-self: stretch;
    align-items: center;
    padding-left: 10px;
    background: var(--mt-surface-bg, #1e1e1e);
    box-shadow: -12px 0 8px -6px var(--mt-surface-bg, #1e1e1e);
}

.cell-text {
    display: block;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.icon-btn {
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
}

.icon-btn:hover {
    background: var(--mt-surface-hover, #3a3a3a);
    color: var(--mt-text, #fff);
}

.icon-delete:hover {
    background: rgba(239, 68, 68, 0.14);
    color: #ef4444;
}

.row-editor {
    --editor-accent: var(--settings-accent, #22c55e);
    width: min(620px, calc(100vw - 32px));
    max-height: min(760px, calc(100vh - 32px));
    overflow: hidden;
    background: var(--mt-surface, #292929);
    border: 1px solid color-mix(in srgb, var(--mt-border, #404040) 88%, var(--editor-accent));
    border-radius: 16px;
    box-shadow:
        0 24px 64px rgba(0, 0, 0, 0.3),
        0 4px 16px rgba(0, 0, 0, 0.16);
}

.row-editor.mode-edit {
    --editor-accent: #60a5fa;
}

.row-editor.mode-clone {
    --editor-accent: #a78bfa;
}

.editor-header {
    position: relative;
    display: flex;
    align-items: center;
    gap: 12px;
    min-height: 46px;
    box-sizing: border-box;
    padding: 7px 12px 7px 14px;
    border-bottom: 1px solid color-mix(in srgb, var(--mt-border, #404040) 76%, var(--editor-accent));
    background:
        linear-gradient(90deg, color-mix(in srgb, var(--editor-accent) 13%, transparent), transparent 58%),
        color-mix(in srgb, var(--mt-surface-alt, #333333) 56%, var(--mt-surface, #292929));
    box-shadow: 0 1px 0 color-mix(in srgb, var(--mt-text, #fff) 3%, transparent);
}

.editor-header-icon {
    width: 32px;
    height: 32px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    flex: 0 0 32px;
    border: 1px solid color-mix(in srgb, var(--editor-accent) 28%, transparent);
    border-radius: 9px;
    background: color-mix(in srgb, var(--editor-accent) 16%, transparent);
    color: var(--editor-accent);
    font-size: 17px;
    box-shadow: inset 0 1px 0 color-mix(in srgb, white 8%, transparent);
}

.editor-heading {
    flex: 1 1 auto;
    min-width: 0;
}

.editor-title {
    font-size: 17px;
    font-weight: 650;
    line-height: 1.25;
    color: var(--mt-text, #fff);
    letter-spacing: -0.01em;
}

.editor-context {
    margin-top: 3px;
    overflow: hidden;
    color: var(--mt-text-tertiary, #aaaaaa);
    font-size: 12px;
    line-height: 1.25;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.editor-close {
    width: 30px;
    height: 30px;
    display: inline-flex;
    align-items: center;
    justify-content: center;
    flex: 0 0 30px;
    border: 1px solid transparent;
    border-radius: 9px;
    background: transparent;
    color: var(--mt-text-tertiary, #aaaaaa);
    cursor: pointer;
    font-size: 18px;
    transition: background-color 150ms ease, border-color 150ms ease, color 150ms ease;
}

.editor-close:hover {
    border-color: var(--mt-border, #404040);
    background: var(--mt-surface-hover, #3a3a3a);
    color: var(--mt-text, #fff);
}

.editor-close:focus-visible {
    outline: 2px solid var(--editor-accent);
    outline-offset: 2px;
}

.editor-body {
    max-height: calc(min(760px, 100vh - 32px) - 93px);
    overflow-x: hidden;
    overflow-y: auto;
    padding: 20px;
    background: var(--mt-surface, #292929);
}

.editor-body::-webkit-scrollbar {
    width: 6px;
}

.editor-body::-webkit-scrollbar-thumb {
    border-radius: 8px;
    background: var(--mt-border, #404040);
}

.editor-body::-webkit-scrollbar-track {
    background: transparent;
}

.editor-form {
    display: grid;
    gap: 12px;
}

.editor-field {
    display: grid;
    grid-template-columns: minmax(120px, 160px) minmax(0, 1fr);
    align-items: start;
    gap: 16px;
    min-width: 0;
    padding: 2px 0;
}

.form-label {
    padding-top: 7px;
    font-size: 13px;
    font-weight: 600;
    line-height: 1.35;
    color: var(--mt-text-secondary, var(--mt-text, #fff));
    overflow-wrap: anywhere;
}

.editor-toolbar {
    display: flex;
    align-items: center;
    justify-content: flex-end;
    gap: 10px;
    min-height: 46px;
    box-sizing: border-box;
    padding: 6px 14px;
    border-top: 1px solid var(--mt-border, #404040);
    background: color-mix(in srgb, var(--mt-surface-alt, #333333) 64%, var(--mt-surface, #292929));
    box-shadow: 0 -8px 24px rgba(0, 0, 0, 0.06);
}

.toolbar-spacer {
    flex: 1 1 auto;
}

@media (max-width: 520px) {
    .row-editor {
        width: calc(100vw - 20px);
        max-height: calc(100vh - 20px);
        border-radius: 14px;
    }

    .editor-header {
        padding-inline: 14px 12px;
    }

    .editor-body {
        max-height: calc(100vh - 113px);
        padding: 16px;
    }

    .editor-field {
        grid-template-columns: 1fr;
        gap: 6px;
    }

    .form-label {
        padding-top: 0;
    }

    .editor-toolbar {
        padding-inline: 14px;
    }
}
</style>
