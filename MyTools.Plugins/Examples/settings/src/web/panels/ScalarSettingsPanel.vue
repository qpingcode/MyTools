<script setup lang="ts">
import { computed } from "vue";
import HighlightText from "../components/HighlightText.vue";
import HotKeyRecorder from "../components/HotKeyRecorder.vue";
import { t } from "../i18n";
import { categorySelfMatches, currentCategory, markSettingDirty, store } from "../store";
import type { Option, Setting } from "../types";

const category = computed(() => currentCategory.value);
const noMatch = computed(() =>
    !!store.searchQuery && category.value != null && !categorySelfMatches(category.value));

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

function onBool(setting: Setting, value: boolean | null): void {
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
    <div v-else-if="!category || category.settings.length === 0" class="empty">
        {{ t("Plugin.Settings.NoSettings", "No settings in this category") }}
    </div>
    <div v-else class="scalar-list">
        <div v-for="setting in category.settings" :key="setting.fullPath" class="setting-item">
            <div class="setting-copy">
                <div class="setting-title">
                    <HighlightText :text="setting.title" :query="store.searchQuery" />
                </div>
                <div v-if="setting.description" class="setting-description">
                    <HighlightText :text="setting.description" :query="store.searchQuery" />
                </div>
            </div>
            <div class="setting-control">
                <v-switch
                    v-if="setting.valueType === 'Bool'"
                    :model-value="currentValue(setting) === 'True'"
                    color="primary"
                    inset
                    hide-details
                    density="compact"
                    @update:model-value="onBool(setting, $event)"
                />
                <v-select
                    v-else-if="setting.valueType === 'Language' || setting.valueType === 'Theme' || setting.valueType === 'LogLevel'"
                    :model-value="currentValue(setting)"
                    :items="optionsFor(setting)"
                    item-title="label"
                    item-value="value"
                    variant="solo"
                    density="compact"
                    hide-details
                    class="control-select"
                    @update:model-value="onText(setting, $event)"
                />
                <HotKeyRecorder
                    v-else-if="setting.valueType === 'HotKey'"
                    :model-value="currentValue(setting)"
                    :default-hot-key="setting.defaultValue ?? ''"
                    exclude-search-hot-key
                    @update:model-value="onHotKey(setting, $event)"
                />
                <v-text-field
                    v-else-if="setting.valueType === 'Integer' || setting.valueType === 'Double'"
                    :model-value="currentValue(setting)"
                    type="number"
                    :step="setting.valueType === 'Integer' ? '1' : undefined"
                    variant="solo"
                    density="compact"
                    hide-details
                    class="control-input"
                    @update:model-value="onText(setting, $event)"
                />
                <v-text-field
                    v-else
                    :model-value="currentValue(setting)"
                    variant="solo"
                    density="compact"
                    hide-details
                    class="control-input"
                    @update:model-value="onText(setting, $event)"
                />
            </div>
        </div>
    </div>
</template>

<style scoped>
.empty {
    padding: 48px 8px;
    text-align: center;
    color: var(--mt-text-tertiary, #aaaaaa);
    font-size: 13px;
}

.setting-item {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 32px;
    padding: 18px 0;
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
}

.setting-control {
    flex: 0 0 auto;
    display: flex;
    justify-content: flex-end;
    align-items: center;
    max-width: 280px;
}

.control-select,
.control-input {
    width: 220px;
}
</style>
