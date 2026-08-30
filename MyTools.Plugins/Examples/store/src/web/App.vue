<script setup lang="ts">
import { computed, onMounted, ref, watch } from "vue";
import { HostEvents } from "@qping/plugin-bus/web";
import { createDiscreteApi, darkTheme } from "naive-ui";
import { marked } from "marked";
import DOMPurify from "dompurify";
import { bus } from "./bus";
import { t, currentLocale } from "./i18n";
import { isDarkTheme, readThemeOverrides } from "./theme";

type InstallFilter = "all" | "installed" | "available";
type PluginSummary = {
    id: string;
    name: string;
    currentVersion: string;
    downloadCount: number;
    ownerUsername: string;
    createdAt: string;
    updatedAt: string;
    installed?: boolean;
    installedVersion?: string | null;
    updateAvailable?: boolean;
    canUninstall?: boolean;
};
type PluginVersion = {
    version: string;
    protocolVersion: string;
    fileSize: number;
    createdAt: string;
};
type PluginDetail = PluginSummary & {
    readme?: string | null;
    changelog?: string | null;
    protocolVersion?: string;
    versions?: PluginVersion[];
};

const { message, dialog } = createDiscreteApi(["message", "dialog"]);
const query = ref("");
const items = ref<PluginSummary[]>([]);
const selected = ref<PluginDetail | null>(null);
const loading = ref(false);
const installing = ref(false);
const uninstalling = ref(false);
const filter = ref<InstallFilter>("all");
const tab = ref<"overview" | "changelog">("overview");
const currentTheme = ref(bus.theme.current);
const themeConfig = computed(() => (isDarkTheme(currentTheme.value) ? darkTheme : null));
const themeOverrides = computed(() => {
    void currentTheme.value;
    return readThemeOverrides();
});
const readmeHtml = computed(() => selected.value?.readme
    ? DOMPurify.sanitize(marked.parse(selected.value.readme, { async: false }) as string)
    : "");
const changelogHtml = computed(() => selected.value?.changelog
    ? DOMPurify.sanitize(marked.parse(selected.value.changelog, { async: false }) as string)
    : "");
const selectedInitial = computed(() => selected.value?.name.trim().charAt(0).toUpperCase() || "M");
const versions = computed(() => selected.value?.versions ?? []);
const installedCount = computed(() => items.value.filter((item) => item.installed).length);
const availableCount = computed(() => items.value.filter((item) => !item.installed).length);
const visibleItems = computed(() => {
    if (filter.value === "installed") return items.value.filter((item) => item.installed);
    if (filter.value === "available") return items.value.filter((item) => !item.installed);
    return items.value;
});
const emptyDescription = computed(() => {
    if (items.value.length === 0) return t("Plugin.Store.Empty", "No plugins found");
    if (filter.value === "installed") return t("Plugin.Store.EmptyInstalled", "No installed plugins");
    if (filter.value === "available") return t("Plugin.Store.EmptyAvailable", "All listed plugins are already installed");
    return t("Plugin.Store.Empty", "No plugins found");
});
const isInstalled = computed(() => Boolean(selected.value?.installed));
const canUpdate = computed(() => Boolean(selected.value?.updateAvailable));
const canUninstall = computed(() => Boolean(selected.value?.canUninstall));

async function load(pluginId?: string): Promise<void> {
    loading.value = true;
    try {
        const result = await bus.call<{ items: PluginSummary[] }>("searchPlugins", {
            query: query.value,
            locale: currentLocale.value,
        });
        items.value = result.items || [];
        const visibleIds = new Set(visibleItems.value.map((item) => item.id));
        const id = pluginId
            || (selected.value && visibleIds.has(selected.value.id) ? selected.value.id : undefined)
            || visibleItems.value[0]?.id;
        if (id) await open(id);
        else selected.value = null;
    } catch (error) {
        message.error(error instanceof Error ? error.message : String(error));
    } finally {
        loading.value = false;
    }
}

async function open(pluginId: string): Promise<void> {
    selected.value = await bus.call<PluginDetail>("getPlugin", { pluginId, locale: currentLocale.value });
}

async function install(): Promise<void> {
    if (!selected.value) return;
    const updating = Boolean(selected.value.updateAvailable);
    installing.value = true;
    try {
        await bus.call("installPlugin", { pluginId: selected.value.id }, 180_000);
        message.success(updating
            ? t("Plugin.Store.Updated", "Plugin updated.")
            : t("Plugin.Store.Installed", "Plugin installed."));
        if (!updating && filter.value === "available") filter.value = "installed";
        await load(selected.value.id);
    } catch (error) {
        message.error(error instanceof Error ? error.message : String(error));
    } finally {
        installing.value = false;
    }
}

async function uninstall(): Promise<void> {
    if (!selected.value?.canUninstall) return;
    const plugin = selected.value;
    const confirmed = await new Promise<boolean>((resolve) => {
        dialog.warning({
            title: t("Plugin.Store.Uninstall.Title", "Uninstall plugin"),
            content: t(
                "Plugin.Store.Uninstall.Confirm",
                "Uninstall {{name}}? This deletes the plugin files and its local data.",
                { name: plugin.name },
            ),
            positiveText: t("Plugin.Store.Uninstall", "Uninstall"),
            negativeText: t("Plugin.Store.Uninstall.Cancel", "Cancel"),
            onPositiveClick: () => resolve(true),
            onNegativeClick: () => resolve(false),
            onClose: () => resolve(false),
        });
    });
    if (!confirmed) return;
    uninstalling.value = true;
    try {
        await bus.call("uninstallPlugin", { pluginId: plugin.id }, 60_000);
        message.success(t("Plugin.Store.Uninstalled", "Plugin uninstalled."));
        const keepSelected = filter.value !== "installed";
        await load(keepSelected ? plugin.id : undefined);
    } catch (error) {
        message.error(error instanceof Error ? error.message : String(error));
    } finally {
        uninstalling.value = false;
    }
}

function formatDate(value: string): string {
    return new Date(value).toLocaleString(undefined, { dateStyle: "medium", timeStyle: "short" });
}

function formatSize(bytes: number): string {
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function itemBadge(item: PluginSummary): string | null {
    if (item.updateAvailable) return t("Plugin.Store.Update", "Update");
    if (item.installed) return t("Plugin.Store.InstalledBadge", "Installed");
    return null;
}

function installedVersionLabel(version: string): string {
    return t("Plugin.Store.InstalledVersion", "Installed v{{version}}", { version });
}

watch(() => selected.value?.id, () => {
    tab.value = "overview";
});

watch(filter, async () => {
    const stillVisible = selected.value && visibleItems.value.some((item) => item.id === selected.value?.id);
    if (!stillVisible) {
        const next = visibleItems.value[0];
        if (next) await open(next.id);
        else selected.value = null;
    }
});

bus.on(HostEvents.Initialize, async (payload: { initialState?: { pluginId?: string }; locale?: string }) => {
    currentTheme.value = bus.theme.current;
    await load(payload?.initialState?.pluginId);
});
bus.on(HostEvents.LanguageChanged, async () => {
    await load(selected.value?.id);
});
bus.on(HostEvents.ThemeChanged, (payload: { theme?: string }) => {
    currentTheme.value = payload.theme;
});
onMounted(() => { currentTheme.value = bus.theme.current; });
</script>

<template>
    <n-config-provider :theme="themeConfig" :theme-overrides="themeOverrides">
        <div class="store">
            <aside class="list">
                <header class="store-brand">
                    <div class="store-mark"><i class="mdi mdi-puzzle-outline"></i></div>
                    <div><strong>{{ t("Plugin.Store.Name", "Plugin Store") }}</strong><small>{{ t("Plugin.Store.Tagline", "Discover tools made for MyTools") }}</small></div>
                </header>
                <div class="search-box">
                    <i class="mdi mdi-magnify"></i>
                    <n-input class="search" v-model:value="query" :placeholder="t('Plugin.Store.Search', 'Search plugins')" @keyup.enter="load()" />
                    <n-button size="small" quaternary circle :aria-label="t('Plugin.Store.SearchAction', 'Search')" @click="load()"><i class="mdi mdi-arrow-right"></i></n-button>
                </div>
                <div class="filter-row" role="tablist">
                    <button type="button" class="filter-chip" :class="{ active: filter === 'all' }" @click="filter = 'all'">
                        {{ t("Plugin.Store.Filter.All", "All") }}<span>{{ items.length }}</span>
                    </button>
                    <button type="button" class="filter-chip" :class="{ active: filter === 'installed' }" @click="filter = 'installed'">
                        {{ t("Plugin.Store.Filter.Installed", "Installed") }}<span>{{ installedCount }}</span>
                    </button>
                    <button type="button" class="filter-chip" :class="{ active: filter === 'available' }" @click="filter = 'available'">
                        {{ t("Plugin.Store.Filter.Available", "Not installed") }}<span>{{ availableCount }}</span>
                    </button>
                </div>
                <div class="list-heading"><span>{{ t("Plugin.Store.Explore", "Explore") }}</span><span>{{ visibleItems.length }}</span></div>
                <n-spin class="plugin-results" :show="loading">
                    <n-empty v-if="!loading && visibleItems.length === 0" :description="emptyDescription" />
                    <button
                        v-for="item in visibleItems"
                        :key="item.id"
                        type="button"
                        class="item"
                        :class="{ active: item.id === selected?.id }"
                        @click="open(item.id)"
                    >
                        <span class="item-icon">{{ item.name.trim().charAt(0).toUpperCase() }}</span>
                        <span class="item-copy">
                            <strong>{{ item.name }}</strong>
                            <small>{{ item.ownerUsername }}</small>
                            <span class="item-meta">
                                <span>v{{ item.currentVersion }}</span>
                                <span v-if="itemBadge(item)" class="item-badge" :class="{ update: item.updateAvailable }">{{ itemBadge(item) }}</span>
                                <span><i class="mdi mdi-download-outline"></i>{{ item.downloadCount }}</span>
                            </span>
                        </span>
                        <i class="mdi mdi-chevron-right item-arrow"></i>
                    </button>
                </n-spin>
            </aside>
            <section class="detail" v-if="selected">
                <div class="detail-inner">
                    <header class="plugin-hero">
                        <span class="hero-icon">{{ selectedInitial }}</span>
                        <div class="hero-copy">
                            <div class="verified"><i class="mdi mdi-check-decagram"></i>{{ t("Plugin.Store.VerifiedListing", "MyTools plugin") }}</div>
                            <h1>{{ selected.name }}</h1>
                            <p>{{ selected.id }}</p>
                            <div class="hero-badges">
                                <span>v{{ selected.currentVersion }}</span>
                                <span v-if="selected.installedVersion && selected.installedVersion !== selected.currentVersion">
                                    {{ installedVersionLabel(selected.installedVersion) }}
                                </span>
                                <span><i class="mdi mdi-account-circle-outline"></i>{{ selected.ownerUsername }}</span>
                                <span><i class="mdi mdi-download-outline"></i>{{ selected.downloadCount }}</span>
                                <span><i class="mdi mdi-update"></i>{{ formatDate(selected.updatedAt) }}</span>
                            </div>
                        </div>
                        <div class="hero-actions">
                            <n-button v-if="canUpdate" class="install-button" type="primary" size="large" :loading="installing" :disabled="uninstalling" @click="install">
                                <template #icon><i class="mdi mdi-update"></i></template>{{ t("Plugin.Store.Update", "Update") }}
                            </n-button>
                            <n-button v-else-if="!isInstalled" class="install-button" type="primary" size="large" :loading="installing" @click="install">
                                <template #icon><i class="mdi mdi-download"></i></template>{{ t("Plugin.Store.Install", "Install") }}
                            </n-button>
                            <div v-else class="installed-label"><i class="mdi mdi-check-circle-outline"></i>{{ t("Plugin.Store.InstalledBadge", "Installed") }}</div>
                            <n-button v-if="canUninstall" class="uninstall-button" size="large" :loading="uninstalling" :disabled="installing" @click="uninstall">
                                <template #icon><i class="mdi mdi-delete-outline"></i></template>{{ t("Plugin.Store.Uninstall", "Uninstall") }}
                            </n-button>
                        </div>
                    </header>
                    <n-tabs v-model:value="tab" class="plugin-tabs" type="line" animated>
                        <n-tab-pane name="overview" :tab="t('Plugin.Store.Overview', 'Overview')">
                            <section v-if="selected.readme" class="readme" v-html="readmeHtml"></section>
                            <div v-else class="empty-pane">{{ t("Plugin.Store.NoDescription", "The publisher has not added a description yet.") }}</div>
                        </n-tab-pane>
                        <n-tab-pane name="changelog" :tab="t('Plugin.Store.Changelog', 'Change Log')">
                            <ol v-if="versions.length" class="version-list">
                                <li v-for="item in versions" :key="item.version">
                                    <strong>{{ item.version }}</strong>
                                    <span>{{ formatDate(item.createdAt) }}</span>
                                    <span>{{ formatSize(item.fileSize) }}</span>
                                </li>
                            </ol>
                            <section v-if="selected.changelog" class="readme changelog-md" v-html="changelogHtml"></section>
                            <div v-else-if="versions.length === 0" class="empty-pane">{{ t("Plugin.Store.NoChangelog", "No changelog has been published yet.") }}</div>
                        </n-tab-pane>
                    </n-tabs>
                </div>
            </section>
            <section class="detail detail-placeholder" v-else>
                <i class="mdi mdi-puzzle-outline"></i>
                <h2>{{ t("Plugin.Store.Select.Title", "Choose a plugin") }}</h2>
                <p>{{ t("Plugin.Store.Select.Description", "Select a plugin from the list to view details and install it.") }}</p>
            </section>
        </div>
    </n-config-provider>
</template>
