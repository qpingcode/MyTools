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
                <n-input class="search" v-model:value="query" :placeholder="t('Plugin.Store.Search', 'Search plugins')" @keyup.enter="load()" />
                <n-button size="small" @click="load()">{{ t("Plugin.Store.SearchAction", "Search") }}</n-button>
                <n-spin :show="loading">
                    <n-empty v-if="!loading && items.length === 0" :description="t('Plugin.Store.Empty', 'No plugins found')" />
                    <button
                        v-for="item in items"
                        :key="item.id"
                        type="button"
                        class="item"
                        :class="{ active: item.id === selected?.id }"
                        @click="open(item.id)"
                    >
                        <div>{{ item.name }}</div>
                        <div class="muted">v{{ item.currentVersion }} · {{ item.downloadCount }}</div>
                    </button>
                </n-spin>
            </aside>
            <section class="detail" v-if="selected">
                <h1>{{ selected.name }}</h1>
                <p class="muted">{{ selected.id }}</p>
                <div class="meta muted">
                    <div>{{ t("Plugin.Store.Version", "Version") }}: {{ selected.currentVersion }}</div>
                    <div>{{ t("Plugin.Store.Downloads", "Downloads") }}: {{ selected.downloadCount }}</div>
                    <div>{{ t("Plugin.Store.Owner", "Publisher") }}: {{ selected.ownerUsername }}</div>
                    <div>{{ t("Plugin.Store.Created", "Created") }}: {{ formatDate(selected.createdAt) }}</div>
                    <div>{{ t("Plugin.Store.Updated", "Updated") }}: {{ formatDate(selected.updatedAt) }}</div>
                </div>
                <n-button type="primary" size="small" :loading="installing" @click="install">
                    {{ t("Plugin.Store.Install", "Install") }}
                </n-button>
                <div class="readme" v-if="selected.readme" v-html="readmeHtml"></div>
            </section>
        </div>
    </n-config-provider>
</template>
