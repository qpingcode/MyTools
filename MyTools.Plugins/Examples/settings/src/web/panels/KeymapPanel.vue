<script setup lang="ts">
import { computed } from "vue";
import HighlightText from "../components/HighlightText.vue";
import HotKeyRecorder from "../components/HotKeyRecorder.vue";
import { t } from "../i18n";
import { markKeymapDirty, store } from "../store";
import type { KeymapPlugin } from "../types";

const plugins = computed(() => store.keymapPlugins || []);

function hotKeyOf(plugin: KeymapPlugin): string | null {
    const dirty = store.keymapDirty.get(plugin.pluginId);
    return dirty?.hotKey !== undefined ? dirty.hotKey : plugin.currentHotKey;
}

function keywordsOf(plugin: KeymapPlugin): string {
    const dirty = store.keymapDirty.get(plugin.pluginId);
    const value = dirty?.keywords !== undefined ? dirty.keywords : plugin.currentKeywords;
    return (value || []).join(", ");
}

function enabledOf(plugin: KeymapPlugin): boolean {
    const dirty = store.keymapDirty.get(plugin.pluginId);
    return dirty?.isEnabled !== undefined ? dirty.isEnabled : plugin.isEnabled;
}

function globalOf(plugin: KeymapPlugin): boolean {
    const dirty = store.keymapDirty.get(plugin.pluginId);
    return dirty?.includeInGlobalResults !== undefined
        ? dirty.includeInGlobalResults
        : plugin.includeInGlobalResults;
}

function conflictOf(pluginId: string): string {
    const conflicts = store.keymapConflicts.filter((item) => item.pluginId === pluginId);
    if (conflicts.length === 0) return "";
    return conflicts
        .map((item) => `${item.field} '${item.value}' ${t("Plugin.Settings.Keymap.ConflictsWith", "conflicts with")} ${item.conflictsWith}`)
        .join("\n");
}

function onKeywords(plugin: KeymapPlugin, value: string): void {
    const keywords = value.split(",").map((item) => item.trim()).filter(Boolean).slice(0, 3);
    markKeymapDirty(plugin.pluginId, { keywords });
}

function searchHotKey(): string | undefined {
    return store.dirtySettings.get("General.SearchHotKey");
}
</script>

<template>
    <div v-if="!store.keymapPlugins" class="empty">
        {{ t("Plugin.Settings.Loading", "Loading...") }}
    </div>
    <div v-else class="keymap-panel">
        <div class="keymap-header">
            <div class="col-name">{{ t("Plugin.Settings.Keymap.HeaderName", "Plugin Name") }}</div>
            <div class="col-hotkey">{{ t("Plugin.Settings.Keymap.HeaderHotkey", "Hotkey") }}</div>
            <div class="col-keywords">{{ t("Plugin.Settings.Keymap.HeaderKeywords", "Keyword") }}</div>
            <div
                class="col-global"
                :title="t('Plugin.Settings.Keymap.HeaderGlobalResultsTip', 'Include this plugin when searching without a keyword')"
            >
                {{ t("Plugin.Settings.Keymap.HeaderGlobalResults", "Global results") }}
            </div>
            <div class="col-enabled">{{ t("Plugin.Settings.Keymap.HeaderEnabled", "Enabled") }}</div>
        </div>
        <div v-for="plugin in plugins" :key="plugin.pluginId" class="keymap-item">
            <div class="keymap-row">
                <div class="col-name" :title="plugin.name">
                    <HighlightText :text="plugin.name" :query="store.searchQuery" />
                </div>
                <div class="col-hotkey">
                    <HotKeyRecorder
                        :model-value="hotKeyOf(plugin)"
                        :default-hot-key="plugin.defaultHotKey"
                        :exclude-plugin-id="plugin.pluginId"
                        :current-search-hot-key="searchHotKey()"
                        @update:model-value="markKeymapDirty(plugin.pluginId, { hotKey: $event })"
                    />
                </div>
                <div class="col-keywords">
                    <v-text-field
                        :model-value="keywordsOf(plugin)"
                        :placeholder="t('Plugin.Settings.Keymap.KeywordsPlaceholder', 'None')"
                        density="compact"
                        variant="outlined"
                        hide-details
                        @update:model-value="onKeywords(plugin, String($event || ''))"
                    />
                </div>
                <div class="col-global">
                    <v-checkbox
                        :model-value="globalOf(plugin)"
                        :title="t('Plugin.Settings.Keymap.HeaderGlobalResultsTip', 'Include this plugin when searching without a keyword')"
                        hide-details
                        density="compact"
                        color="primary"
                        @update:model-value="markKeymapDirty(plugin.pluginId, { includeInGlobalResults: !!$event })"
                    />
                </div>
                <div class="col-enabled">
                    <v-checkbox
                        :model-value="enabledOf(plugin)"
                        hide-details
                        density="compact"
                        color="primary"
                        @update:model-value="markKeymapDirty(plugin.pluginId, { isEnabled: !!$event })"
                    />
                </div>
            </div>
            <div v-if="conflictOf(plugin.pluginId)" class="keymap-conflict">
                ⚠ {{ conflictOf(plugin.pluginId) }}
            </div>
        </div>
    </div>
</template>

<style scoped>
.empty {
    padding: 40px;
    text-align: center;
    opacity: 0.6;
}

.keymap-panel {
    min-width: 0;
}

.keymap-header,
.keymap-row {
    display: flex;
    align-items: center;
    gap: 6px;
}

.keymap-header {
    padding: 8px 0 10px;
    border-bottom: 1px solid var(--mt-border, #404040);
    font-size: 12px;
    font-weight: 600;
    color: var(--mt-text-tertiary, #aaaaaa);
}

.keymap-header > * {
    white-space: nowrap;
}

.keymap-row {
    padding: 10px 0;
    border-bottom: 1px solid var(--mt-border, #404040);
}

.col-name {
    width: 140px;
    flex: 1 1 140px;
    min-width: 0;
    font-weight: 500;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.col-hotkey {
    width: 120px;
    flex: 0 0 120px;
}

.col-keywords {
    width: 88px;
    flex: 0 0 88px;
    min-width: 0;
}

.col-global {
    width: 72px;
    flex: 0 0 72px;
    display: flex;
    justify-content: center;
    text-align: center;
}

.col-enabled {
    width: 48px;
    flex: 0 0 48px;
    display: flex;
    justify-content: center;
    text-align: center;
}

.keymap-conflict {
    color: #f44336;
    font-size: 12px;
    padding: 0 4px 10px;
}
</style>
