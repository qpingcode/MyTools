<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import { HostEvents } from "@qping/plugin-bus/web";
import { createDiscreteApi, darkTheme } from "naive-ui";
import { marked } from "marked";
import DOMPurify from "dompurify";
import { bus } from "./bus";
import { t } from "./i18n";
import { isDarkTheme, readThemeOverrides } from "./theme";

type PluginSummary = {
    id: string;
    name: string;
    currentVersion: string;
    downloadCount: number;
    ownerUsername: string;
    createdAt: string;
    updatedAt: string;
};
type PluginDetail = PluginSummary & { readme?: string | null; protocolVersion?: string };

const { message } = createDiscreteApi(["message"]);
const query = ref("");
const items = ref<PluginSummary[]>([]);
const selected = ref<PluginDetail | null>(null);
const loading = ref(false);
const installing = ref(false);
const currentTheme = ref(bus.theme.current);
const themeConfig = computed(() => (isDarkTheme(currentTheme.value) ? darkTheme : null));
const themeOverrides = computed(() => {
    void currentTheme.value;
    return readThemeOverrides();
});
const readmeHtml = computed(() => selected.value?.readme
    ? DOMPurify.sanitize(marked.parse(selected.value.readme, { async: false }) as string)
    : "");
const selectedInitial = computed(() => selected.value?.name.trim().charAt(0).toUpperCase() || "M");

async function load(pluginId?: string): Promise<void> {
    loading.value = true;
    try {
        const result = await bus.call<{ items: PluginSummary[] }>("searchPlugins", { query: query.value });
        items.value = result.items || [];
        const id = pluginId || selected.value?.id || items.value[0]?.id;
        if (id) await open(id);
        else selected.value = null;
    } catch (error) {
        message.error(error instanceof Error ? error.message : String(error));
    } finally {
        loading.value = false;
    }
}

async function open(pluginId: string): Promise<void> {
    selected.value = await bus.call<PluginDetail>("getPlugin", { pluginId });
}

async function install(): Promise<void> {
    if (!selected.value) return;
    installing.value = true;
    try {
        await bus.call("installPlugin", { pluginId: selected.value.id }, 180_000);
        message.success(t("Plugin.Store.Installed", "Plugin installed."));
    } catch (error) {
        message.error(error instanceof Error ? error.message : String(error));
    } finally {
        installing.value = false;
    }
}

function formatDate(value: string): string {
    return new Date(value).toLocaleString();
}

bus.on(HostEvents.Initialize, async (payload: { initialState?: { pluginId?: string } }) => {
    currentTheme.value = bus.theme.current;
    await load(payload?.initialState?.pluginId);
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
                <div class="list-heading"><span>{{ t("Plugin.Store.Explore", "Explore") }}</span><span>{{ items.length }}</span></div>
                <n-spin class="plugin-results" :show="loading">
                    <n-empty v-if="!loading && items.length === 0" :description="t('Plugin.Store.Empty', 'No plugins found')" />
                    <button
                        v-for="item in items"
                        :key="item.id"
                        type="button"
                        class="item"
                        :class="{ active: item.id === selected?.id }"
                        @click="open(item.id)"
                    >
                        <span class="item-icon">{{ item.name.trim().charAt(0).toUpperCase() }}</span>
                        <span class="item-copy"><strong>{{ item.name }}</strong><small>{{ item.ownerUsername }}</small><span class="item-meta"><span>v{{ item.currentVersion }}</span><span><i class="mdi mdi-download-outline"></i>{{ item.downloadCount }}</span></span></span>
                        <i class="mdi mdi-chevron-right item-arrow"></i>
                    </button>
                </n-spin>
            </aside>
            <section class="detail" v-if="selected">
                <div class="detail-inner">
                    <header class="plugin-hero">
                        <span class="hero-icon">{{ selectedInitial }}</span>
                        <div class="hero-copy"><div class="verified"><i class="mdi mdi-check-decagram"></i>{{ t("Plugin.Store.VerifiedListing", "MyTools plugin") }}</div><h1>{{ selected.name }}</h1><p>{{ selected.id }}</p><div class="hero-badges"><span>v{{ selected.currentVersion }}</span><span><i class="mdi mdi-account-circle-outline"></i>{{ selected.ownerUsername }}</span><span><i class="mdi mdi-download-outline"></i>{{ selected.downloadCount }}</span></div></div>
                        <n-button class="install-button" type="primary" size="large" :loading="installing" @click="install"><template #icon><i class="mdi mdi-download"></i></template>{{ t("Plugin.Store.Install", "Install") }}</n-button>
                    </header>
                    <div class="facts">
                        <div><i class="mdi mdi-tag-outline"></i><span>{{ t("Plugin.Store.Version", "Version") }}</span><strong>{{ selected.currentVersion }}</strong></div>
                        <div><i class="mdi mdi-update"></i><span>{{ t("Plugin.Store.Updated", "Updated") }}</span><strong>{{ formatDate(selected.updatedAt) }}</strong></div>
                        <div><i class="mdi mdi-shield-check-outline"></i><span>{{ t("Plugin.Store.Protocol", "Protocol") }}</span><strong>{{ selected.protocolVersion || "—" }}</strong></div>
                    </div>
                    <article class="readme" v-if="selected.readme"><div class="section-title"><i class="mdi mdi-text-box-outline"></i>{{ t("Plugin.Store.About", "About this plugin") }}</div><div class="readme-content" v-html="readmeHtml"></div></article>
                    <article class="readme readme-empty" v-else><i class="mdi mdi-text-box-remove-outline"></i><p>{{ t("Plugin.Store.NoDescription", "The publisher has not added a description yet.") }}</p></article>
                </div>
            </section>
            <section class="detail detail-placeholder" v-else><i class="mdi mdi-puzzle-outline"></i><h2>{{ t("Plugin.Store.Select.Title", "Choose a plugin") }}</h2><p>{{ t("Plugin.Store.Select.Description", "Select a plugin from the list to view details and install it.") }}</p></section>
        </div>
    </n-config-provider>
</template>
