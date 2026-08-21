<script setup lang="ts">
import { computed } from "vue";
import HighlightText from "../components/HighlightText.vue";
import HotKeyRecorder from "../components/HotKeyRecorder.vue";
import ArraySettingTable from "../components/ArraySettingTable.vue";
import SettingField from "../components/SettingField.vue";
import { t } from "../i18n";
import { isHeadingType, isPathType, resolvePathKind } from "../setting-utils";
import { SYSTEM_CATEGORY_KEYS, categorySelfMatches, currentCategory, currentDescription, currentTitle, isSettingVisible, markSettingDirty, store } from "../store";
import type { Option, Setting } from "../types";

const category = computed(() => currentCategory.value);
const visibleSettings = computed(() => {
    const list = category.value?.settings ?? [];
    return list.filter((setting) => isSettingVisible(setting, list));
});
const noMatch = computed(() =>
    !!store.searchQuery && category.value != null && !categorySelfMatches(category.value));

const showCategoryHeader = computed(() =>
    SYSTEM_CATEGORY_KEYS.has(category.value?.key ?? ""));

function isArraySetting(setting: Setting): boolean {
    return setting.valueType === "Array";
}

function isHeading(setting: Setting): boolean {
    return isHeadingType(setting.valueType);
}

function headingLevel(setting: Setting): "h1" | "h2" {
    return setting.valueType.toLowerCase() === "h2" ? "h2" : "h1";
}

function hasSettingCopy(setting: Setting): boolean {
    return !!setting.title || !!setting.description;
}

function usesSchemaControl(setting: Setting): boolean {
    if (setting.valueType === "Language"
        || setting.valueType === "Theme"
        || setting.valueType === "LogLevel"
        || setting.valueType === "HotKey") {
        return false;
    }
    if (isPathType(setting.valueType, setting.fullPath)) return true;
    return !!setting.uiHint;
}

function fieldType(setting: Setting): string {
    if (isPathType(setting.valueType, setting.fullPath)) return "path";
    const valueType = setting.valueType.toLowerCase();
    if (valueType === "integer") return "int";
    if (valueType === "bool") return "bool";
    if (valueType === "double") return "double";
    return "string";
}

function fieldUiHint(setting: Setting): string | undefined {
    if (isPathType(setting.valueType, setting.fullPath)) {
        return resolvePathKind(setting.valueType, setting.uiHint, setting.fullPath);
    }
    return setting.uiHint;
}

function currentRawValue(setting: Setting): unknown {
    const text = currentValue(setting);
    if (setting.valueType === "Bool") return text === "True";
    if (setting.valueType === "Integer") {
        if (!text.trim()) return null;
        const parsed = Number.parseInt(text, 10);
        return Number.isFinite(parsed) ? parsed : null;
    }
    if (setting.valueType === "Double") {
        if (!text.trim()) return null;
        const parsed = Number.parseFloat(text);
        return Number.isFinite(parsed) ? parsed : null;
    }
    return text;
}

function onField(setting: Setting, value: unknown): void {
    if (setting.valueType === "Bool") {
        onBool(setting, !!value);
        return;
    }
    onText(setting, value == null ? "" : String(value));
}

function currentValue(setting: Setting): string {
    const dirty = store.dirtySettings.get(setting.fullPath);
    if (dirty !== undefined) return dirty;
    return setting.currentValue ?? "";
}

function optionsFor(setting: Setting): Option[] {
    if (setting.valueType === "Language") return store.config?.supportedLocales ?? [];
    if (setting.valueType === "Theme") return store.config?.supportedThemes ?? [];
    if (setting.valueType === "LogLevel") return store.config?.supportedLogLevels ?? [];
    return [];
}

function onText(setting: Setting, value: string | number | null): void {
    markSettingDirty(setting.fullPath, value == null ? "" : String(value));
}

function onBool(setting: Setting, value: boolean): void {
    markSettingDirty(setting.fullPath, value ? "True" : "False");
}

function onHotKey(setting: Setting, value: string | null): void {
    markSettingDirty(setting.fullPath, value || "");
}
</script>

<template>
    <div v-if="noMatch" class="empty">
        {{ t("Plugin.Settings.NoResults", "No matching settings found") }}
    </div>
    <div v-else-if="!category" class="empty">
        {{ t("Plugin.Settings.NoSettings", "No settings in this category") }}
    </div>
    <div v-else class="scalar-list">
        <div v-if="showCategoryHeader && currentTitle" class="category-header">
            <div class="category-title">
                <HighlightText :text="currentTitle" :query="store.searchQuery" />
            </div>
            <div v-if="currentDescription" class="category-description">
                <HighlightText :text="currentDescription" :query="store.searchQuery" />
            </div>
        </div>
        <div v-if="visibleSettings.length === 0" class="empty">
            {{ t("Plugin.Settings.NoSettings", "No settings in this category") }}
        </div>
        <template v-for="setting in visibleSettings" :key="setting.fullPath">
            <div
                v-if="isHeading(setting)"
                class="setting-heading"
                :class="'setting-heading-' + headingLevel(setting)"
            >
                <div v-if="setting.title" class="heading-title">
                    <HighlightText :text="setting.title" :query="store.searchQuery" />
                </div>
                <div v-if="setting.description" class="heading-description">
                    <HighlightText :text="setting.description" :query="store.searchQuery" />
                </div>
            </div>
            <div
                v-else
                class="setting-item"
                :class="{ 'setting-item-block': isArraySetting(setting) }"
            >
                <div v-if="hasSettingCopy(setting)" class="setting-copy">
                    <div v-if="setting.title" class="setting-title">
                        <HighlightText :text="setting.title" :query="store.searchQuery" />
                    </div>
                    <div v-if="setting.description" class="setting-description">
                        <HighlightText :text="setting.description" :query="store.searchQuery" />
                    </div>
                </div>
                <div v-else-if="!isArraySetting(setting)" class="setting-copy" />
                <div class="setting-control" :class="{ 'setting-control-block': isArraySetting(setting) }">
                <ArraySettingTable v-if="isArraySetting(setting)" :setting="setting" />
                <div v-else-if="setting.valueType === 'Bool' && !setting.uiHint" class="control-bool">
                    <n-switch
                        :value="currentValue(setting) === 'True'"
                        @update:value="onBool(setting, !!$event)"
                    />
                </div>
                <n-select
                    v-else-if="setting.valueType === 'Language' || setting.valueType === 'Theme' || setting.valueType === 'LogLevel'"
                    :value="currentValue(setting)"
                    :options="optionsFor(setting)"
                    value-field="value"
                    label-field="label"
                    size="small"
                    class="control-select"
                    @update:value="onText(setting, $event as string)"
                />
                <HotKeyRecorder
                    v-else-if="setting.valueType === 'HotKey'"
                    class="control-hotkey"
                    :model-value="currentValue(setting)"
                    :default-hot-key="setting.defaultValue ?? ''"
                    exclude-search-hot-key
                    @update:model-value="onHotKey(setting, $event)"
                />
                <SettingField
                    v-else-if="usesSchemaControl(setting)"
                    :type="fieldType(setting)"
                    :ui-hint="fieldUiHint(setting)"
                    :title="setting.title"
                    :model-value="currentRawValue(setting)"
                    @update:model-value="onField(setting, $event)"
                />
                <n-input
                    v-else-if="setting.valueType === 'Integer' || setting.valueType === 'Double'"
                    :value="currentValue(setting)"
                    type="number"
                    size="small"
                    class="control-input"
                    @update:value="onText(setting, $event)"
                />
                <n-input
                    v-else
                    :value="currentValue(setting)"
                    size="small"
                    class="control-input"
                    @update:value="onText(setting, $event)"
                />
            </div>
            </div>
        </template>
    </div>
</template>

<style scoped>
.empty {
    padding: 48px 8px;
    text-align: center;
    color: var(--mt-text-tertiary, #aaaaaa);
    font-size: 13px;
}

.category-header {
    padding: 4px 0 14px;
}

.category-title {
    font-size: 20px;
    font-weight: 650;
    line-height: 1.3;
    color: var(--mt-text, #fff);
}

.category-description {
    margin-top: 6px;
    font-size: 13px;
    line-height: 1.45;
    color: var(--mt-text-tertiary, #aaaaaa);
    white-space: pre-line;
}

.setting-heading {
    min-width: 0;
}

.setting-heading-h1 {
    padding: 4px 0 14px;
}

.setting-heading-h2 {
    padding: 18px 0 8px;
}

.setting-heading-h1 .heading-title {
    font-size: 20px;
    font-weight: 650;
    line-height: 1.3;
    color: var(--mt-text, #fff);
}

.setting-heading-h2 .heading-title {
    font-size: 14px;
    font-weight: 600;
    line-height: 1.35;
    color: var(--mt-text, #fff);
}

.heading-description {
    margin-top: 6px;
    font-size: 13px;
    line-height: 1.45;
    color: var(--mt-text-tertiary, #aaaaaa);
    white-space: pre-line;
}

.setting-heading-h2 .heading-description {
    margin-top: 4px;
}

.setting-item {
    display: flex;
    flex-wrap: wrap;
    align-items: center;
    justify-content: space-between;
    gap: 32px;
    padding: 18px 0;
}

.setting-item-block {
    align-items: stretch;
    flex-direction: column;
    gap: 12px;
    min-width: 0;
    max-width: 100%;
}

.setting-copy {
    min-width: 0;
    flex: 1;
}

.setting-title {
    font-size: 14px;
    font-weight: 600;
    line-height: 1.35;
    color: var(--mt-text, #fff);
}

.setting-description {
    margin-top: 4px;
    font-size: 13px;
    line-height: 1.45;
    color: var(--mt-text-tertiary, #aaaaaa);
    white-space: pre-line;
}

.setting-control {
    flex: 0 0 auto;
    display: flex;
    justify-content: flex-end;
    align-items: center;
    width: 280px;
}

.setting-control-block {
    width: 100%;
    max-width: 100%;
    min-width: 0;
    justify-content: stretch;
}

.scalar-list {
    min-width: 0;
    max-width: 100%;
}

.control-select,
.control-input {
    width: 220px;
}

.control-hotkey {
    width: 220px;
}

.control-bool {
    width: 220px;
    display: flex;
    justify-content: flex-start;
}
</style>
