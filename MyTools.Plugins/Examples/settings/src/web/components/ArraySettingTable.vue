<script setup lang="ts">
import { computed, reactive, ref } from "vue";
import HighlightText from "./HighlightText.vue";
import SettingField from "./SettingField.vue";
import { t } from "../i18n";
import { markSettingDirty, store } from "../store";
import {
    coercePropertyValue,
    defaultPropertyValue,
    formatCellText,
    isTruthyBool,
    parseArrayValue,
} from "../setting-utils";
import type { Setting, SettingSchemaProperty } from "../types";

const props = defineProps<{
    setting: Setting;
}>();

const editorOpen = ref(false);
const editingIndex = ref<number | null>(null);
const tableQuery = ref("");
const draft = reactive<Record<string, unknown>>({});

const properties = computed((): SettingSchemaProperty[] => props.setting.schema?.properties ?? []);
const visibleProperties = computed(() =>
    properties.value.filter((property) => !property.hidden && property.type.toLowerCase() !== "hidden"));

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
            visibleProperties.value.some((property) =>
                formatCellText(row[property.key]).toLowerCase().includes(query)));
});

const labels = computed(() => ({
    search: t("Plugin.Settings.Table.Search", "Search"),
    add: t("Plugin.Settings.Table.Add", "Add"),
    edit: t("Plugin.Settings.Table.Edit", "Edit"),
    delete: t("Plugin.Settings.Table.Delete", "Delete"),
    empty: t("Plugin.Settings.Table.Empty", "No items"),
    apply: t("Plugin.Settings.Table.Apply", "Apply"),
    cancel: t("Plugin.Settings.Cancel", "Cancel"),
}));

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
    const source = index == null ? emptyRow() : { ...rows.value[index] };
    for (const key of Object.keys(draft)) {
        delete draft[key];
    }
    for (const property of properties.value) {
        draft[property.key] = coercePropertyValue(property.type, source[property.key] ?? defaultPropertyValue(property.type, property.defaultValue));
    }
    editorOpen.value = true;
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

function editorFields(): SettingSchemaProperty[] {
    return visibleProperties.value;
}
</script>

<template>
    <div class="array-setting">
        <div class="table-toolbar">
            <n-input
                v-model:value="tableQuery"
                size="small"
                clearable
                class="table-search"
                :placeholder="labels.search"
            >
                <template #prefix>
                    <i class="mdi mdi-magnify"></i>
                </template>
            </n-input>
            <n-button size="small" type="primary" secondary @click="openEditor(null)">
                <template #icon>
                    <i class="mdi mdi-plus"></i>
                </template>
                {{ labels.add }}
            </n-button>
        </div>

        <div v-if="rows.length === 0" class="empty">{{ labels.empty }}</div>
        <div v-else class="array-table-scroll">
            <div class="array-table">
                <div class="table-head">
                    <div
                        v-for="property in visibleProperties"
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
                        v-for="property in visibleProperties"
                        :key="property.key"
                        class="table-cell"
                        :class="property.type.toLowerCase() === 'bool' ? 'col-bool' : 'col-text'"
                    >
                        <i
                            v-if="property.type.toLowerCase() === 'bool'"
                            class="mdi"
                            :class="isTruthyBool(item.row[property.key]) ? 'mdi-check' : 'mdi-minus'"
                        ></i>
                        <span v-else class="cell-text" :title="formatCellText(item.row[property.key])">
                            <HighlightText :text="formatCellText(item.row[property.key])" :query="tableQuery || store.searchQuery" />
                        </span>
                    </div>
                    <div class="col-actions">
                        <button type="button" class="icon-btn" :title="labels.edit" @click="openEditor(item.index)">
                            <i class="mdi mdi-pencil-outline"></i>
                        </button>
                        <button type="button" class="icon-btn icon-delete" :title="labels.delete" @click="removeRow(item.index)">
                            <i class="mdi mdi-trash-can-outline"></i>
                        </button>
                    </div>
                </div>
            </div>
        </div>

        <n-modal v-model:show="editorOpen" :mask-closable="false">
            <n-card class="row-editor" role="dialog" aria-modal="true">
                <div class="editor-title">
                    {{ editingIndex == null ? labels.add : labels.edit }}
                </div>
                <div class="editor-form">
                    <template v-for="property in editorFields()" :key="property.key">
                        <div class="form-label">{{ property.title || property.key }}</div>
                        <SettingField
                            :type="property.type"
                            :ui-hint="property.uiHint"
                            :model-value="draft[property.key]"
                            @update:model-value="draft[property.key] = $event"
                        />
                    </template>
                </div>
                <div class="editor-actions">
                    <n-button size="small" @click="editorOpen = false">{{ labels.cancel }}</n-button>
                    <n-button size="small" type="primary" @click="saveEditor">{{ labels.apply }}</n-button>
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

.table-toolbar {
    display: flex;
    align-items: center;
    gap: 8px;
    margin-bottom: 10px;
    min-width: 0;
}

.table-search {
    flex: 1 1 auto;
    min-width: 0;
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
    flex: 0 0 64px;
    display: flex;
    justify-content: center;
}

.col-actions {
    flex: 0 0 72px;
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
    width: min(560px, calc(100vw - 32px));
    background: var(--mt-surface, #292929);
}

.editor-title {
    font-size: 16px;
    font-weight: 600;
    margin-bottom: 12px;
}

.editor-form {
    display: grid;
    grid-template-columns: max-content minmax(0, 1fr);
    column-gap: 12px;
    row-gap: 10px;
    align-items: start;
}

.form-label {
    padding-top: 6px;
    font-size: 13px;
    font-weight: 500;
    color: var(--mt-text, #fff);
    white-space: nowrap;
}

.editor-actions {
    margin-top: 14px;
    display: flex;
    justify-content: flex-end;
    gap: 8px;
}
</style>
