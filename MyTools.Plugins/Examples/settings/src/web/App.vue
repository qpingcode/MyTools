<script setup lang="ts">
import { HostEvents } from "@qping/plugin-bus/web";
import { computed, onMounted, ref, watch } from "vue";
import { createDiscreteApi, darkTheme } from "naive-ui";
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
import { isDarkTheme, readThemeOverrides } from "./theme";

const { message } = createDiscreteApi(["message"]);
const searchText = ref("");
const searchTimer = ref<ReturnType<typeof setTimeout> | null>(null);
const currentTheme = ref(bus.theme.current);

const labels = computed(() => ({
    searchPlaceholder: t("Plugin.Settings.Search.Placeholder", "Search settings..."),
    noResults: t("Plugin.Settings.NoResults", "No matching settings found"),
    loading: t("Plugin.Settings.Loading", "Loading..."),
    restartPrompt: t("Plugin.Settings.RestartPrompt", "Some changes require a restart to take effect. Restart now?"),
    cancel: t("Plugin.Settings.Cancel", "Cancel"),
    restart: t("Plugin.Settings.Restart", "Restart"),
    capturing: capturingHint(),
}));

const themeOverrides = computed(() => {
    void currentTheme.value;
    return readThemeOverrides();
});
const themeConfig = computed(() => (isDarkTheme(currentTheme.value) ? darkTheme : null));

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

watch(
    () => store.toast.show,
    (show) => {
        if (!show) return;
        if (store.toast.color === "error") {
            message.error(store.toast.message, { duration: 3000 });
        } else {
            message.success(store.toast.message, { duration: 3000 });
        }
        store.toast.show = false;
    },
);

function iconClass(icon: string): string[] {
    return ["mdi", icon];
}

document.addEventListener("contextmenu", (event) => event.preventDefault());

bus.on(HostEvents.Initialize, async () => {
    store.localeTick += 1;
    currentTheme.value = bus.theme.current;
    await loadConfiguration();
});

bus.on(HostEvents.LanguageChanged, async () => {
    store.localeTick += 1;
    await loadConfiguration();
});

bus.on(HostEvents.ThemeChanged, (payload: { theme?: string }) => {
    currentTheme.value = payload.theme;
    store.localeTick += 1;
});

onMounted(() => {
    currentTheme.value = bus.theme.current;
});
</script>

<template>
    <n-config-provider :theme="themeConfig" :theme-overrides="themeOverrides">
        <div class="settings-app">
            <div class="settings-shell">
                <nav class="sidebar">
                    <div class="sidebar-search">
                        <n-input
                            :key="localeRevision"
                            v-model:value="searchText"
                            :placeholder="labels.searchPlaceholder"
                            clearable
                            size="small"
                        >
                            <template #prefix>
                                <i class="mdi mdi-magnify nav-input-icon"></i>
                            </template>
                        </n-input>
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
                                <i :class="iconClass(item.icon)" class="nav-icon"></i>
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
                        <ScalarSettingsPanel v-else />
                    </div>
                </section>
            </div>

            <n-modal v-model:show="store.restartModal" :mask-closable="false">
                <n-card class="dialog-card" role="dialog" aria-modal="true">
                    <p class="dialog-text">{{ labels.restartPrompt }}</p>
                    <div class="dialog-actions">
                        <n-button size="small" @click="store.restartModal = false">
                            {{ labels.cancel }}
                        </n-button>
                        <n-button size="small" type="primary" @click="restartApp">
                            {{ labels.restart }}
                        </n-button>
                    </div>
                </n-card>
            </n-modal>

            <n-modal :show="store.capturing" :mask-closable="false">
                <n-card class="capture-card" role="dialog" aria-modal="true">
                    <div class="capture-text">{{ labels.capturing }}</div>
                    <n-spin size="small" />
                </n-card>
            </n-modal>
        </div>
    </n-config-provider>
</template>

<style scoped>
.settings-app {
    position: relative;
    width: 100%;
    height: 100vh;
    min-height: 0;
    max-height: 100vh;
    overflow: hidden;
    font-family: inherit;
    font-size: 14px;
    background: var(--mt-surface-bg, #1e1e1e);
    color: var(--mt-text, #e0e0e0);
}

.settings-shell {
    position: absolute;
    inset: 0;
    display: flex;
    flex-direction: row;
    min-height: 0;
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
    overflow-y: scroll;
    scrollbar-gutter: stable;
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

.nav-item:hover:not(.disabled),
.nav-item.active {
    background: var(--mt-surface-hover, #3a3a3a);
}

.nav-item.disabled {
    opacity: 0.45;
    cursor: default;
}

.nav-item:focus-visible {
    outline: 2px solid var(--mt-accent, #3f51b5);
    outline-offset: 1px;
}

.nav-icon {
    opacity: 0.85;
    flex-shrink: 0;
    font-size: 18px;
}

.nav-input-icon {
    opacity: 0.7;
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
    position: relative;
    display: flex;
    flex-direction: column;
    overflow: hidden;
    background: var(--mt-surface-bg, #1e1e1e);
}

.settings-scroll {
    position: absolute;
    inset: 0;
    box-sizing: border-box;
    overflow-x: hidden;
    overflow-y: scroll;
    scrollbar-gutter: stable;
    padding: 16px 20px 20px;
}

.empty {
    padding: 32px 8px;
    text-align: center;
    font-size: 13px;
    color: var(--mt-text-tertiary, #aaaaaa);
}

.dialog-card,
.capture-card {
    width: min(420px, calc(100vw - 32px));
    background: var(--mt-surface, #292929);
}

.capture-card {
    width: 280px;
    text-align: center;
}

.capture-text {
    margin-bottom: 12px;
}

.dialog-text {
    margin: 0 0 12px;
}

.dialog-actions {
    display: flex;
    justify-content: flex-end;
    gap: 8px;
}
</style>
