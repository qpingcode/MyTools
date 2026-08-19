<script setup lang="ts">
import { computed, reactive } from "vue";
import HighlightText from "../components/HighlightText.vue";
import HotKeyRecorder from "../components/HotKeyRecorder.vue";
import ArraySettingTable from "../components/ArraySettingTable.vue";
import SettingField from "../components/SettingField.vue";
import { bus } from "../bus";
import { t } from "../i18n";
import { categorySelfMatches, currentCategory, markSettingDirty, store } from "../store";
import type { Option, Setting } from "../types";

const category = computed(() => currentCategory.value);
const noMatch = computed(() =>
    !!store.searchQuery && category.value != null && !categorySelfMatches(category.value));
const pathErrors = reactive<Record<string, string>>({});
const pathDraft = reactive<Record<string, string>>({});

type PathMode = "file" | "fileOrDirectory";
type ValidatePathResult = { valid?: boolean; message?: string };
type PickPathResult = { cancelled?: boolean; path?: string };

function isArraySetting(setting: Setting): boolean {
    return setting.valueType === "Array";
}

function usesSchemaControl(setting: Setting): boolean {
    return !!setting.uiHint && setting.valueType !== "Language"
        && setting.valueType !== "Theme"
        && setting.valueType !== "LogLevel"
        && setting.valueType !== "HotKey"
        && !isPathSetting(setting);
}

function fieldType(setting: Setting): string {
    const valueType = setting.valueType.toLowerCase();
    if (valueType === "integer") return "int";
    if (valueType === "bool") return "bool";
    if (valueType === "double") return "double";
    return "string";
}

function currentRawValue(setting: Setting): unknown {
    const text = currentValue(setting);
    if (setting.valueType === "Bool") return text === "True";
    if (setting.valueType === "Integer") {
        const parsed = Number.parseInt(text || "0", 10);
        return Number.isFinite(parsed) ? parsed : 0;
    }
    if (setting.valueType === "Double") {
        const parsed = Number.parseFloat(text || "0");
        return Number.isFinite(parsed) ? parsed : 0;
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

function isPathSetting(setting: Setting): boolean {
    const fullPath = setting.fullPath.toLowerCase();
    return fullPath === "dllinterfacereader.ilspypathsetting"
        || fullPath === "openpath.riderinstallpath"
        || fullPath === "openpath.vscodeinstallpath"
        || fullPath === "openpath.visualstudioinstallpath"
        || fullPath === "openpath.intellijinstallpath";
}

function pathModeFor(setting: Setting): PathMode {
    return setting.fullPath.toLowerCase() === "dllinterfacereader.ilspypathsetting" ? "file" : "fileOrDirectory";
}

function currentValue(setting: Setting): string {
    if (isPathSetting(setting) && pathDraft[setting.fullPath] !== undefined) {
        return pathDraft[setting.fullPath];
    }
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

function onPathText(setting: Setting, value: string): void {
    pathDraft[setting.fullPath] = value;
    pathErrors[setting.fullPath] = "";
}

async function validatePath(setting: Setting, value: string): Promise<boolean> {
    const result = await bus.call<ValidatePathResult>("validatePath", {
        path: value || "",
        kind: pathModeFor(setting),
    });
    if (!result?.valid) {
        pathErrors[setting.fullPath] = result?.message || t("Plugin.Settings.Path.Invalid", "Invalid path");
        return false;
    }
    pathErrors[setting.fullPath] = "";
    return true;
}

async function commitPath(setting: Setting): Promise<void> {
    const value = pathDraft[setting.fullPath] ?? currentValue(setting);
    if (!(await validatePath(setting, value))) {
        return;
    }
    markSettingDirty(setting.fullPath, value);
    delete pathDraft[setting.fullPath];
}

async function browsePath(setting: Setting): Promise<void> {
    const result = await bus.call<PickPathResult>("pickPath", {
        title: setting.title,
        filter: pathModeFor(setting) === "file"
            ? "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
            : "Executable files (*.exe)|*.exe|All files (*.*)|*.*",
        initialPath: currentValue(setting),
    });
    if (!result || result.cancelled || !result.path) {
        return;
    }
    pathDraft[setting.fullPath] = result.path;
    await commitPath(setting);
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
        <div
            v-for="setting in category.settings"
            :key="setting.fullPath"
            class="setting-item"
            :class="{ 'setting-item-block': isArraySetting(setting) }"
        >
            <div class="setting-copy">
                <div class="setting-title">
                    <HighlightText :text="setting.title" :query="store.searchQuery" />
                </div>
                <div v-if="setting.description" class="setting-description">
                    <HighlightText :text="setting.description" :query="store.searchQuery" />
                </div>
            </div>
            <div class="setting-control" :class="{ 'setting-control-block': isArraySetting(setting) }">
                <ArraySettingTable v-if="isArraySetting(setting)" :setting="setting" />
                <div v-else-if="setting.valueType === 'Bool' && !setting.uiHint" class="control-bool">
                    <n-switch
                        :value="currentValue(setting) === 'True'"
                        @update:value="onBool(setting, !!$event)"
                    />
                </div>
                <div v-else-if="isPathSetting(setting)" class="path-control">
                    <n-input
                        :value="currentValue(setting)"
                        size="small"
                        class="control-input"
                        @update:value="onPathText(setting, String($event || ''))"
                        @blur="commitPath(setting)"
                    />
                    <n-button size="small" secondary class="browse-btn" @click="browsePath(setting)">
                        <template #icon>
                            <i class="mdi mdi-folder-open-outline"></i>
                        </template>
                    </n-button>
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
                    :ui-hint="setting.uiHint"
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
            <div v-if="isPathSetting(setting) && pathErrors[setting.fullPath]" class="path-error">
                {{ pathErrors[setting.fullPath] }}
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

.path-control {
    width: 260px;
    display: flex;
    align-items: center;
    gap: 6px;
}

.path-control .control-input {
    width: 100%;
}

.browse-btn {
    flex: 0 0 auto;
}

.path-error {
    width: 100%;
    margin-top: -8px;
    color: #f44336;
    font-size: 12px;
}
</style>
