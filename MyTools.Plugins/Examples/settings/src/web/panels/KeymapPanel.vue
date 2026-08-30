<script setup lang="ts">
import { computed, nextTick, ref } from "vue";
import HighlightText from "../components/HighlightText.vue";
import TableToolbar from "../components/TableToolbar.vue";
import { captureInputAction } from "../capture-input-action";
import { t } from "../i18n";
import { loadPluginOverrides, markKeymapDirty, showToast, store } from "../store";
import { bus } from "../bus";
import type { KeymapPlugin } from "../types";

const plugins = computed(() => store.keymapPlugins || []);
const tableQuery = ref("");
const editingAliasKey = ref<string | null>(null);
const aliasInputRef = ref<{ focus: () => void } | null>(null);

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
    const dirty = store.keymapDirty.get(plugin.overrideKey);
    return dirty?.hotKey !== undefined ? dirty.hotKey : plugin.currentHotKey;
}

function keywordsOf(plugin: KeymapPlugin): string {
    const dirty = store.keymapDirty.get(plugin.overrideKey);
    const value = dirty?.keywords !== undefined ? dirty.keywords : plugin.currentKeywords;
    return (value || []).join(", ");
}

function enabledOf(plugin: KeymapPlugin): boolean {
    const dirty = store.keymapDirty.get(plugin.overrideKey);
    return dirty?.isEnabled !== undefined ? dirty.isEnabled : plugin.isEnabled;
}

function globalOf(plugin: KeymapPlugin): boolean {
    const dirty = store.keymapDirty.get(plugin.overrideKey);
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
    markKeymapDirty(plugin.overrideKey, { keywords });
}

async function startAliasEdit(plugin: KeymapPlugin): Promise<void> {
    editingAliasKey.value = plugin.overrideKey;
    await nextTick();
    aliasInputRef.value?.focus();
}

function stopAliasEdit(): void {
    editingAliasKey.value = null;
}

function searchHotKey(): string | undefined {
    return store.dirtySettings.get("General.SearchHotKey");
}

async function editPluginHotKey(plugin: KeymapPlugin): Promise<void> {
    const result = await captureInputAction({
        showKeyboard: true,
        showMouse: false,
        value: { kind: "hotkey", hotKey: hotKeyOf(plugin) || null },
        defaultHotKey: "",
        excludePluginId: plugin.overrideKey,
        currentSearchHotKey: searchHotKey(),
    });
    if (!result) return;
    markKeymapDirty(plugin.overrideKey, { hotKey: result.hotKey ?? "" });
}

function hasDuplicateId(plugin: KeymapPlugin): boolean {
    return plugins.value.some((item) => item !== plugin && item.pluginId.toLowerCase() === plugin.pluginId.toLowerCase());
}

function onEnabled(plugin: KeymapPlugin, enabled: boolean): void {
    if (enabled) {
        for (const sibling of plugins.value) {
            if (sibling.overrideKey !== plugin.overrideKey
                && sibling.pluginId.toLowerCase() === plugin.pluginId.toLowerCase()) {
                markKeymapDirty(sibling.overrideKey, { isEnabled: false });
            }
        }
    }
    markKeymapDirty(plugin.overrideKey, { isEnabled: enabled });
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
        <div v-for="plugin in filteredPlugins" :key="plugin.overrideKey" class="keymap-item">
            <div class="keymap-row">
                <div class="col-name" :title="plugin.name">
                    <HighlightText :text="plugin.name" :query="highlightQuery" />
                    <span v-if="plugin.isDevelopment" class="development-badge">{{ t("Plugin.Settings.Keymap.Development", "Developing") }}</span>
                    <div v-if="hasDuplicateId(plugin)" class="duplicate-id" :title="plugin.location">
                        ID: {{ plugin.pluginId }} · {{ plugin.location }}
                    </div>
                </div>
                <div class="col-hotkey">
                    <button
                        type="button"
                        class="flat-display"
                        :class="{ empty: !hotKeyOf(plugin) }"
                        :title="hotKeyOf(plugin) || t('Plugin.Settings.Keymap.NoHotkey', 'None')"
                        @click="editPluginHotKey(plugin)"
                    >
                        <HighlightText
                            :text="hotKeyOf(plugin) || t('Plugin.Settings.Keymap.NoHotkey', 'None')"
                            :query="highlightQuery"
                        />
                    </button>
                </div>
                <div class="col-keywords">
                    <n-input
                        v-if="editingAliasKey === plugin.overrideKey"
                        ref="aliasInputRef"
                        :value="keywordsOf(plugin)"
                        :placeholder="t('Plugin.Settings.Keymap.KeywordsPlaceholder', 'e.g. git, repo')"
                        :title="t('Plugin.Settings.Keymap.KeywordsPlaceholder', 'e.g. git, repo')"
                        size="small"
                        @update:value="onKeywords(plugin, String($event || ''))"
                        @blur="stopAliasEdit"
                        @keydown.enter.prevent="stopAliasEdit"
                        @keydown.esc.prevent="stopAliasEdit"
                    />
                    <button
                        v-else
                        type="button"
                        class="flat-display"
                        :class="{ empty: !keywordsOf(plugin) }"
                        :title="keywordsOf(plugin) || t('Plugin.Settings.Keymap.KeywordsPlaceholder', 'e.g. git, repo')"
                        @click="startAliasEdit(plugin)"
                    >
                        <HighlightText
                            v-if="keywordsOf(plugin)"
                            :text="keywordsOf(plugin)"
                            :query="highlightQuery"
                        />
                        <span v-else>{{ t("Plugin.Settings.Keymap.KeywordsPlaceholder", "e.g. git, repo") }}</span>
                    </button>
                </div>
                <div class="col-global">
                    <n-checkbox
                        :checked="globalOf(plugin)"
                        :title="t('Plugin.Settings.Keymap.HeaderGlobalResultsTip', 'Include this plugin in global search when no plugin alias is typed')"
                        @update:checked="markKeymapDirty(plugin.overrideKey, { includeInGlobalResults: !!$event })"
                    />
                </div>
                <div class="col-enabled">
                    <n-checkbox
                        :checked="enabledOf(plugin)"
                        @update:checked="onEnabled(plugin, !!$event)"
                    />
                </div>
            </div>
            <div v-if="conflictOf(plugin.overrideKey)" class="keymap-conflict">
                ⚠ {{ conflictOf(plugin.overrideKey) }}
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
    width: max-content;
    max-width: 100%;
}
.plugin-toolbar { display: flex; align-items: center; gap: 8px; }
.plugin-toolbar > :first-child { flex: 1; }
.refresh-icon { font-size: 20px; line-height: 1; }
.development-badge { margin-left: 6px; padding: 2px 6px; border-radius: 999px; background: #4f7cff22; color: #7ea0ff; font-size: 10px; vertical-align: middle; }
.duplicate-id { margin-top: 3px; overflow: hidden; color: #f0a020; font-size: 10px; font-weight: 400; text-overflow: ellipsis; white-space: nowrap; }

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
    width: 120px;
    flex: 0 0 120px;
    min-width: 0;
}

.col-keywords {
    width: 120px;
    flex: 0 0 120px;
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

.flat-display {
    width: 100%;
    min-width: 0;
    padding: 6px 8px;
    overflow: hidden;
    border: none;
    border-radius: 8px;
    background: transparent;
    color: var(--mt-text, #fff);
    font: inherit;
    text-align: left;
    text-overflow: ellipsis;
    white-space: nowrap;
    cursor: pointer;
}

.flat-display:hover {
    background: var(--mt-surface-hover, #3a3a3a);
}

.flat-display.empty {
    font-style: italic;
    opacity: 0.6;
}
</style>
