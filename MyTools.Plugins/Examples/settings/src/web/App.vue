<script setup lang="ts">
import { HostEvents } from "@qping/plugin-bus/web";
import { computed, onMounted, ref, watch } from "vue";
import { useTheme } from "vuetify";
import CommandRunnerPanel from "./panels/CommandRunnerPanel.vue";
import GesturesPanel from "./panels/GesturesPanel.vue";
import KeymapPanel from "./panels/KeymapPanel.vue";
import ScalarSettingsPanel from "./panels/ScalarSettingsPanel.vue";
import HighlightText from "./components/HighlightText.vue";
import { bus } from "./bus";
import { capturingHint } from "./capture-input-action";
import { localeRevision, t } from "./i18n";
import {
    findFirstVisibleCategory,
    loadConfiguration,
    restartApp,
    selectCategory,
    sidebarItems,
    store,
} from "./store";
import { isDarkTheme, readThemeColors } from "./theme";

const vuetifyTheme = useTheme();
const searchText = ref("");
const searchTimer = ref<ReturnType<typeof setTimeout> | null>(null);

const labels = computed(() => ({
    searchPlaceholder: t("Plugin.Settings.Search.Placeholder", "Search settings..."),
    noResults: t("Plugin.Settings.NoResults", "No matching settings found"),
    loading: t("Plugin.Settings.Loading", "Loading..."),
    restartPrompt: t("Plugin.Settings.RestartPrompt", "Some changes require a restart to take effect. Restart now?"),
    cancel: t("Plugin.Settings.Cancel", "Cancel"),
    restart: t("Plugin.Settings.Restart", "Restart"),
    capturing: capturingHint(),
}));

watch(searchText, (value) => {
    if (searchTimer.value) clearTimeout(searchTimer.value);
    searchTimer.value = setTimeout(() => {
        store.searchQuery = (value || "").trim().toLowerCase();
        if (store.searchQuery) {
            const first = findFirstVisibleCategory();
            if (first) selectCategory(first.key);
        }
    }, 150);
});

function applyVuetifyTheme(theme?: string): void {
    const dark = isDarkTheme(theme);
    vuetifyTheme.global.name.value = dark ? "dark" : "light";
    const colors = readThemeColors();
    Object.assign(vuetifyTheme.themes.value[dark ? "dark" : "light"].colors, colors);
}

document.addEventListener("contextmenu", (event) => event.preventDefault());
applyVuetifyTheme(bus.theme.current);

bus.on(HostEvents.Initialize, async () => {
    store.localeTick += 1;
    applyVuetifyTheme(bus.theme.current);
    await loadConfiguration();
});

bus.on(HostEvents.LanguageChanged, async () => {
    store.localeTick += 1;
    await loadConfiguration();
});

bus.on(HostEvents.ThemeChanged, (payload: { theme?: string }) => {
    applyVuetifyTheme(payload.theme);
    store.localeTick += 1;
});

onMounted(() => {
    applyVuetifyTheme(bus.theme.current);
});
</script>

<template>
    <v-app class="settings-app">
        <div class="settings-shell">
            <nav class="sidebar">
                <div class="sidebar-search">
                    <v-text-field
                        :key="localeRevision"
                        v-model="searchText"
                        :placeholder="labels.searchPlaceholder"
                        prepend-inner-icon="mdi-magnify"
                        variant="solo"
                        density="compact"
                        hide-details
                        clearable
                    />
                </div>
                <div class="sidebar-nav">
                    <template v-for="(item, index) in sidebarItems" :key="item.type === 'group' ? `g-${index}` : item.key">
                        <div v-if="item.type === 'group'" class="nav-group">{{ item.label }}</div>
                        <button
                            v-else
                            type="button"
                            class="nav-item"
                            :class="{ active: item.key === store.currentCategoryKey, disabled: !item.selectable }"
                            :disabled="!item.selectable"
                            @click="item.selectable && selectCategory(item.key)"
                        >
                            <v-icon :icon="item.icon" size="18" class="nav-icon" />
                            <span class="nav-label">
                                <HighlightText :text="item.name" :query="store.searchQuery" />
                            </span>
                        </button>
                    </template>
                    <div v-if="sidebarItems.length === 0" class="empty">
                        {{ labels.noResults }}
                    </div>
                </div>
            </nav>
            <section class="content-panel">
                <div class="settings-scroll">
                    <div v-if="store.loading" class="empty">
                        {{ labels.loading }}
                    </div>
                    <div v-else-if="store.error" class="empty">{{ store.error }}</div>
                    <KeymapPanel v-else-if="store.currentCategoryKey === 'Plugins'" />
                    <GesturesPanel v-else-if="store.currentCategoryKey === 'Gestures'" />
                    <CommandRunnerPanel v-else-if="store.currentCategoryKey === 'CommandRunner'" />
                    <ScalarSettingsPanel v-else />
                </div>
            </section>
        </div>

        <v-snackbar v-model="store.toast.show" :color="store.toast.color" timeout="3000">
            {{ store.toast.message }}
        </v-snackbar>

        <v-dialog v-model="store.restartModal" max-width="420">
            <v-card rounded="lg">
                <v-card-text>
                    {{ labels.restartPrompt }}
                </v-card-text>
                <v-card-actions>
                    <v-spacer />
                    <v-btn variant="text" rounded="lg" @click="store.restartModal = false">
                        {{ labels.cancel }}
                    </v-btn>
                    <v-btn color="primary" rounded="lg" @click="restartApp">
                        {{ labels.restart }}
                    </v-btn>
                </v-card-actions>
            </v-card>
        </v-dialog>

        <v-overlay v-model="store.capturing" class="align-center justify-center" persistent>
            <v-card width="280" rounded="lg" class="pa-4 text-center">
                <div class="mb-3">{{ labels.capturing }}</div>
                <v-progress-linear color="primary" indeterminate rounded />
            </v-card>
        </v-overlay>
    </v-app>
</template>

<style scoped>
.settings-shell {
    display: flex;
    flex-direction: row;
    flex: 1 1 auto;
    min-height: 0;
    height: 100%;
    overflow: hidden;
}

.sidebar {
    width: 248px;
    flex: 0 0 248px;
    min-height: 0;
    display: flex;
    flex-direction: column;
    border-right: 1px solid var(--mt-border, #404040);
    background: var(--mt-surface-bg, #1e1e1e);
}

.sidebar-search {
    flex: 0 0 auto;
    padding: 16px 14px 8px;
}

.sidebar-nav {
    flex: 1 1 auto;
    min-height: 0;
    overflow-y: auto;
    padding: 4px 10px 16px;
}

.nav-group {
    margin: 16px 8px 6px;
    font-size: 11px;
    font-weight: 600;
    letter-spacing: 0.06em;
    text-transform: uppercase;
    color: var(--mt-text-tertiary, #aaaaaa);
}

.nav-item {
    display: flex;
    align-items: center;
    gap: 10px;
    width: 100%;
    border: none;
    background: transparent;
    color: var(--mt-text, #fff);
    text-align: left;
    border-radius: 10px;
    padding: 8px 10px;
    margin-bottom: 2px;
    cursor: pointer;
    font: inherit;
}

.nav-item:hover:not(.disabled) {
    background: var(--mt-surface-hover, #3a3a3a);
}

.nav-item.active {
    background: var(--mt-surface-hover, #3a3a3a);
}

.nav-item.disabled {
    opacity: 0.45;
    cursor: default;
}

.nav-item:focus {
    outline: none;
}

.nav-item:focus-visible {
    outline: 2px solid var(--mt-accent, #3F51B5);
    outline-offset: 1px;
}

.nav-icon {
    opacity: 0.85;
    flex-shrink: 0;
}

.nav-label {
    font-size: 13.5px;
    line-height: 1.3;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
}

.content-panel {
    flex: 1 1 auto;
    min-width: 0;
    min-height: 0;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    background: var(--mt-surface-bg, #1e1e1e);
}

.settings-scroll {
    flex: 1 1 auto;
    min-height: 0;
    overflow-x: hidden;
    overflow-y: auto;
    padding: 16px 20px 20px;
}

.empty {
    padding: 32px 8px;
    text-align: center;
    font-size: 13px;
    color: var(--mt-text-tertiary, #aaaaaa);
}

.mb-3 {
    margin-bottom: 12px;
}
</style>
