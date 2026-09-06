<script setup lang="ts">
import { computed, ref } from "vue";
import { bus } from "../bus";
import { t } from "../i18n";
import { parseArrayValue } from "../setting-utils";
import { markSettingDirty, store } from "../store";
import type { Setting } from "../types";

const props = defineProps<{ setting: Setting }>();
const busy = ref(false);
const failed = ref(false);
const rows = computed(() => parseArrayValue(store.dirtySettings.get(props.setting.key) ?? props.setting.currentValue));
const labels = computed(() => ({
    add: t("Plugin.Settings.Directories.Add", "Add directory"),
    remove: t("Plugin.Settings.Directories.Remove", "Remove directory"),
    pick: t("Plugin.Settings.Directories.Pick", "Select a directory"),
    failed: t("Plugin.Settings.Directories.Failed", "Could not select a directory. Please try again."),
    empty: t("Plugin.Settings.Table.Empty", "No items"),
}));
function persist(next: Record<string, unknown>[]): void {
    markSettingDirty(props.setting.key, JSON.stringify(next));
}
function remove(index: number): void {
    persist(rows.value.filter((_, i) => i !== index));
}
async function add(): Promise<void> {
    busy.value = true;
    failed.value = false;
    try {
        const result = await bus.call<{ cancelled?: boolean; path?: string }>("pickPath", {
            title: labels.value.pick, kind: "directory",
        });
        if (result?.cancelled || !result?.path) return;
        const path = result.path;
        if (!rows.value.some(row => String(row.Path).replace(/[\\/]+$/, "").toLowerCase() === path.replace(/[\\/]+$/, "").toLowerCase())) {
            persist([...rows.value, { Path: path }]);
        }
    } catch {
        failed.value = true;
    } finally {
        busy.value = false;
    }
}
</script>

<template>
    <div class="directory-list">
        <div v-if="!rows.length" class="empty">{{ labels.empty }}</div>
        <div v-for="(row, index) in rows" :key="index" class="directory-row">
            <i class="mdi mdi-folder-outline folder-icon" aria-hidden="true"></i>
            <span class="directory-path" :title="String(row.Path ?? '')">{{ row.Path }}</span>
            <n-button quaternary size="small" :title="labels.remove" :aria-label="labels.remove" @click="remove(index)">
                <template #icon><i class="mdi mdi-close" aria-hidden="true"></i></template>
            </n-button>
        </div>
        <n-button secondary size="small" :loading="busy" :disabled="busy" @click="add">
            <template #icon><i class="mdi mdi-plus" aria-hidden="true"></i></template>
            {{ labels.add }}
        </n-button>
        <div v-if="failed" role="alert">{{ labels.failed }}</div>
    </div>
</template>

<style scoped>
.directory-list { display: flex; flex-direction: column; gap: 10px; align-items: flex-start; width: 100%; }
.directory-row { display: flex; align-items: center; gap: 10px; width: 100%; padding: 8px 12px; box-sizing: border-box; border: 1px solid var(--border-color, #8884); border-radius: 8px; }
.folder-icon { font-size: 21px; flex-shrink: 0; }
.directory-path { flex: 1; min-width: 0; overflow-wrap: anywhere; }
.empty { opacity: .65; padding: 8px 0; }
</style>
