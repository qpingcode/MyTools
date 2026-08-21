<script setup lang="ts">
import { computed, ref } from "vue";
import HighlightText from "../components/HighlightText.vue";
import HotKeyRecorder from "../components/HotKeyRecorder.vue";
import TableToolbar from "../components/TableToolbar.vue";
import { t } from "../i18n";
import { loadPluginOverrides, markKeymapDirty, showToast, store } from "../store";
import { bus } from "../bus";
import type { KeymapPlugin } from "../types";

const plugins = computed(() => store.keymapPlugins || []);
const tableQuery = ref("");

const filteredPlugins = computed(() => {
    const query = tableQuery.value.trim().toLowerCase();
    if (!query) return plugins.value;
    return plugins.value.filter((plugin) => {
        const haystack = [
            plugin.name,
            keywordsOf(plugin),
            hotKeyOf(plugin) || "",
        ].join(" ").toLowerCase();
        return haystack.includes(query);
    });
});

const highlightQuery = computed(() => tableQuery.value.trim() || store.searchQuery);

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

function onPluginHotKeyChange(plugin: KeymapPlugin, value: string | null): void {
    markKeymapDirty(plugin.pluginId, { hotKey: value ?? "" });
}

async function refreshDevelopmentPlugins(): Promise<void> {
    try {
        await bus.call("refreshDevelopmentPlugins");
        await loadPluginOverrides();
        showToast(t("Plugin.Settings.Keymap.DevelopmentRefreshed", "Development plugins refreshed"), "success");
    } catch {
        // Reloading the settings plugin itself can reset the bridge before the response arrives.
    }
}
</script>

<template>
    <div v-if="!store.keymapPlugins" class="empty">
        {{ t("Plugin.Settings.Loading", "Loading...") }}
    </div>
    <div v-else class="keymap-panel">
        <div class="plugin-toolbar">
            <TableToolbar v-model="tableQuery" :placeholder="t('Plugin.Settings.Table.Search', 'Search')" />
            <n-button quaternary circle :title="t('Plugin.Settings.Keymap.RefreshDevelopment', 'Refresh all development plugins')" @click="refreshDevelopmentPlugins">
                <span class="refresh-icon">↻</span>
            </n-button>
        </div>
        <div class="keymap-header">
            <div class="col-name">{{ t("Plugin.Settings.Keymap.HeaderName", "Plugin") }}</div>
            <div class="col-hotkey">{{ t("Plugin.Settings.Keymap.HeaderHotkey", "Hotkey") }}</div>
            <div
                class="col-keywords"
                :title="t('Plugin.Settings.Keymap.HeaderKeywordsTip', 'Aliases used to open this plugin directly')"
            >
                {{ t("Plugin.Settings.Keymap.HeaderKeywords", "Alias") }}
            </div>
            <div
                class="col-global"
                :title="t('Plugin.Settings.Keymap.HeaderGlobalResultsTip', 'Include this plugin in global search when no plugin alias is typed')"
            >
                {{ t("Plugin.Settings.Keymap.HeaderGlobalResults", "Global") }}
            </div>
            <div class="col-enabled">{{ t("Plugin.Settings.Keymap.HeaderEnabled", "Enabled") }}</div>
        </div>
        <div v-for="plugin in filteredPlugins" :key="plugin.pluginId" class="keymap-item">
            <div class="keymap-row">
                <div class="col-name" :title="plugin.name">
                    <HighlightText :text="plugin.name" :query="highlightQuery" />
                    <span v-if="plugin.isDevelopment" class="development-badge">{{ t("Plugin.Settings.Keymap.Development", "Developing") }}</span>
                </div>
                <div class="col-hotkey">
                    <HotKeyRecorder
                        :model-value="hotKeyOf(plugin)"
                        default-hot-key=""
                        :exclude-plugin-id="plugin.pluginId"
                        :current-search-hot-key="searchHotKey()"
                        @update:model-value="onPluginHotKeyChange(plugin, $event)"
                    />
                </div>
                <div class="col-keywords">
                    <n-input
                        :value="keywordsOf(plugin)"
                        :placeholder="t('Plugin.Settings.Keymap.KeywordsPlaceholder', 'e.g. git, repo')"
                        :title="t('Plugin.Settings.Keymap.KeywordsPlaceholder', 'e.g. git, repo')"
                        size="small"
                        @update:value="onKeywords(plugin, String($event || ''))"
                    />
                </div>
                <div class="col-global">
                    <n-checkbox
                        :checked="globalOf(plugin)"
                        :title="t('Plugin.Settings.Keymap.HeaderGlobalResultsTip', 'Include this plugin in global search when no plugin alias is typed')"
                        @update:checked="markKeymapDirty(plugin.pluginId, { includeInGlobalResults: !!$event })"
                    />
                </div>
                <div class="col-enabled">
                    <n-checkbox
                        :checked="enabledOf(plugin)"
                        @update:checked="markKeymapDirty(plugin.pluginId, { isEnabled: !!$event })"
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
.plugin-toolbar { display: flex; align-items: center; gap: 8px; }
.plugin-toolbar > :first-child { flex: 1; }
.refresh-icon { font-size: 20px; line-height: 1; }
.development-badge { margin-left: 6px; padding: 2px 6px; border-radius: 999px; background: #4f7cff22; color: #7ea0ff; font-size: 10px; vertical-align: middle; }

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
    width: 180px;
    flex: 0 0 180px;
    min-width: 0;
    font-weight: 400;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.col-hotkey {
    width: 150px;
    flex: 0 0 150px;
}

.col-keywords {
    width: 130px;
    flex: 1 1 130px;
    min-width: 0;
}

.col-global {
    width: 82px;
    flex: 0 0 82px;
    display: flex;
    justify-content: center;
    text-align: center;
}

.col-enabled {
    width: 68px;
    flex: 0 0 68px;
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
