import { computed, reactive } from "vue";
import { bus } from "./bus";
import { localeRevision, t } from "./i18n";
import { evaluateVisibility, isTopLevelHeading, settingKey } from "./setting-utils";
import type {
    Category,
    Config,
    GestureConfig,
    KeymapConflict,
    KeymapDirty,
    KeymapPlugin,
    Setting,
    SidebarItem,
} from "./types";

export const SYSTEM_CATEGORY_KEYS = new Set(["General", "Gestures", "Plugins"]);
export const SPECIAL_CATEGORY_KEYS = new Set(["Plugins", "Gestures"]);

const CATEGORY_ICONS: Record<string, string> = {
    General: "mdi-cog-outline",
    Gestures: "mdi-gesture-swipe",
    Plugins: "mdi-puzzle-outline",
};

function categoryIcon(category: Category): string {
    const declared = category.icon?.trim();
    if (declared) {
        return declared.startsWith("mdi-") ? declared : `mdi-${declared}`;
    }
    return CATEGORY_ICONS[category.key] || "mdi-tune-variant";
}

export const store = reactive({
    config: null as Config | null,
    currentCategoryKey: "",
    searchQuery: "",
    loading: false,
    error: "",
    capturing: false,
    localeTick: 0,

    dirtySettings: new Map<string, string>(),
    keymapDirty: new Map<string, KeymapDirty>(),
    keymapConflicts: [] as KeymapConflict[],
    gesturesDirty: false,
    dirty: false,
    saving: false,

    keymapPlugins: null as KeymapPlugin[] | null,
    gestureConfigs: null as GestureConfig[] | null,

    toast: {
        show: false,
        message: "",
        color: "success" as "success" | "error",
    },
    restartModal: false,
});

export function refreshDirty(): void {
    store.dirty = store.dirtySettings.size > 0
        || store.keymapDirty.size > 0
        || store.gesturesDirty;
}

export const isDirty = computed(() => store.dirty);

export function showToast(message: string, color: "success" | "error"): void {
    store.toast.message = message;
    store.toast.color = color;
    store.toast.show = true;
}

export function findCategory(categories: Category[], key: string): Category | null {
    return categories.find((category) => category.key === key) ?? null;
}

export function currentSettingRawValue(setting: Setting): string {
    const dirty = store.dirtySettings.get(setting.key);
    if (dirty !== undefined) return dirty;
    return setting.currentValue ?? "";
}

export function isSettingVisible(setting: Setting, siblings: Setting[]): boolean {
    const needle = (name: string) => {
        const match = siblings.find((item) => settingKey(item).toLowerCase() === name.toLowerCase());
        return match ? currentSettingRawValue(match) : undefined;
    };
    return evaluateVisibility(setting.visibility, needle);
}

export function settingMatchesSearch(setting: Setting): boolean {
    if (!store.searchQuery) return true;
    if (setting.title.toLowerCase().includes(store.searchQuery)) return true;
    if (setting.description && setting.description.toLowerCase().includes(store.searchQuery)) return true;
    if (setting.currentValue && setting.currentValue.toLowerCase().includes(store.searchQuery)) return true;
    return false;
}

export function categorySelfMatches(category: Category): boolean {
    if (!store.searchQuery) return true;
    if (category.name.toLowerCase().includes(store.searchQuery)) return true;
    if (category.description && category.description.toLowerCase().includes(store.searchQuery)) return true;
    for (const setting of category.settings) {
        if (isTopLevelHeading(setting.valueType)) continue;
        if (!isSettingVisible(setting, category.settings)) continue;
        if (settingMatchesSearch(setting)) return true;
    }
    if (category.key === "Plugins" && keymapMatchesSearch()) return true;
    if (category.key === "Gestures" && gesturesMatchesSearch()) return true;
    return false;
}

export function shouldShowCategory(category: Category): boolean {
    if (SPECIAL_CATEGORY_KEYS.has(category.key)) return true;
    return category.settings.some((setting) => !isTopLevelHeading(setting.valueType));
}

function keymapMatchesSearch(): boolean {
    if (!store.searchQuery || !store.keymapPlugins) return false;
    return store.keymapPlugins.some((plugin) => plugin.name.toLowerCase().includes(store.searchQuery));
}

function gesturesMatchesSearch(): boolean {
    if (!store.searchQuery || !store.gestureConfigs) return false;
    return store.gestureConfigs.some((gesture) =>
        gesture.actionName.toLowerCase().includes(store.searchQuery)
        || gesture.processNames.some((name) => name.toLowerCase().includes(store.searchQuery)));
}

export const sidebarItems = computed((): SidebarItem[] => {
    if (!store.config) return [];
    const items: SidebarItem[] = [];
    let toolsHeaderInserted = false;
    let appHeaderInserted = false;
    void localeRevision.value;
    store.localeTick;
    for (const category of store.config.categories) {
        if (!shouldShowCategory(category)) continue;
        if (store.searchQuery && !categorySelfMatches(category)) continue;
        if (SYSTEM_CATEGORY_KEYS.has(category.key)) {
            if (!appHeaderInserted) {
                items.push({ type: "group", label: t("Plugin.Settings.Sidebar.App", "App") });
                appHeaderInserted = true;
            }
        } else if (!toolsHeaderInserted) {
            items.push({ type: "group", label: t("Plugin.Settings.Sidebar.Tools", "Tools") });
            toolsHeaderInserted = true;
        }
        items.push({
            type: "category",
            key: category.key,
            name: category.name,
            selectable: category.isSelectable,
            icon: categoryIcon(category),
        });
    }
    return items;
});

export const currentCategory = computed((): Category | null => {
    if (!store.config || !store.currentCategoryKey) return null;
    return findCategory(store.config.categories, store.currentCategoryKey);
});

export function findFirstSelectable(categories: Category[]): Category | null {
    for (const cat of categories) {
        if (shouldShowCategory(cat) && cat.isSelectable) return cat;
    }
    return null;
}

export function findFirstVisibleCategory(): Category | null {
    if (!store.config) return null;
    for (const cat of store.config.categories) {
        if (shouldShowCategory(cat) && cat.isSelectable && categorySelfMatches(cat)) return cat;
    }
    return null;
}

export function selectCategory(key: string): void {
    store.currentCategoryKey = key;
}

export async function loadConfiguration(): Promise<void> {
    store.loading = true;
    store.error = "";
    try {
        store.config = await bus.call<Config>("getConfiguration");
        if (store.currentCategoryKey) {
            selectCategory(store.currentCategoryKey);
        } else {
            const first = findFirstSelectable(store.config.categories);
            if (first) selectCategory(first.key);
        }
        await loadSpecialPanels();
    } catch (error) {
        store.error = error instanceof Error ? error.message : String(error);
    } finally {
        store.loading = false;
    }
}

export async function loadSpecialPanels(): Promise<void> {
    await Promise.allSettled([loadPluginOverrides(), loadGestures()]);
}

export async function loadPluginOverrides(): Promise<void> {
    const data = await bus.call<{ plugins: KeymapPlugin[] }>("getPluginOverrides");
    store.keymapPlugins = data.plugins || [];
    store.keymapDirty.clear();
    store.keymapConflicts = [];
    refreshDirty();
}

export async function loadGestures(): Promise<void> {
    const data = await bus.call<{ gestures: GestureConfig[] }>("getGestures");
    store.gestureConfigs = data.gestures || [];
    store.gesturesDirty = false;
    refreshDirty();
}

export function markSettingDirty(key: string, value: string): void {
    store.dirtySettings.set(key, value);
    refreshDirty();
    scheduleSave();
}

export function markKeymapDirty(overrideKey: string, change: KeymapDirty): void {
    const existing = store.keymapDirty.get(overrideKey) || {};
    store.keymapDirty.set(overrideKey, { ...existing, ...change });
    refreshDirty();
    scheduleSave();
}

export function markGesturesDirty(): void {
    store.gesturesDirty = true;
    refreshDirty();
    scheduleSave();
}

var saveTimer: ReturnType<typeof setTimeout> | null = null;
var saveQueued = false;

function scheduleSave(): void {
    if (saveTimer) clearTimeout(saveTimer);
    saveTimer = setTimeout(() => {
        saveTimer = null;
        void saveSettings();
    }, 300);
}

function applyDirtySettingsToConfig(): void {
    if (!store.config) return;
    for (const [key, value] of store.dirtySettings.entries()) {
        const setting = findSetting(store.config.categories, key);
        if (setting) {
            setting.currentValue = value;
        }
    }
}

function findSetting(categories: Category[], key: string): Setting | null {
    for (const category of categories) {
        const match = category.settings.find((setting) => setting.key === key);
        if (match) return match;
    }
    return null;
}

async function reloadConfigQuietly(): Promise<void> {
    const key = store.currentCategoryKey;
    store.config = await bus.call<Config>("getConfiguration");
    store.currentCategoryKey = key;
}

export async function saveSettings(): Promise<void> {
    refreshDirty();
    if (!store.dirty) return;
    if (store.saving) {
        saveQueued = true;
        return;
    }
    store.saving = true;
    try {
        let requiresRestart = false;

        if (store.dirtySettings.size > 0) {
            const changes = Array.from(store.dirtySettings.entries()).map(([key, value]) => ({
                key,
                value,
            }));
            const result = await bus.call<{ requiresRestart: boolean }>("saveConfiguration", { changes });
            applyDirtySettingsToConfig();
            store.dirtySettings.clear();
            requiresRestart = result.requiresRestart;
            await reloadConfigQuietly();
        }

        if (store.keymapDirty.size > 0) {
            const pluginOverridesSaved = await savePluginOverridesInternal();
            if (!pluginOverridesSaved) return;
        }

        if (store.gesturesDirty) {
            await bus.call("saveGestures", { gestures: store.gestureConfigs || [] });
            store.gesturesDirty = false;
        }

        if (requiresRestart) {
            store.restartModal = true;
        }
    } catch (error) {
        showToast(error instanceof Error ? error.message : String(error), "error");
    } finally {
        refreshDirty();
        store.saving = false;
        if (saveQueued) {
            saveQueued = false;
            void saveSettings();
        }
    }
}

async function savePluginOverridesInternal(): Promise<boolean> {
    if (store.keymapDirty.size === 0 || !store.keymapPlugins) return true;

    const keymapOverrides: Record<string, KeymapDirty> = {};
    const hotKeysToValidate: Record<string, string | null> = {};
    const keywordsToValidate: Record<string, string[] | null> = {};
    let hasKeymapChanges = false;

    for (const plugin of store.keymapPlugins) {
        const dirty = store.keymapDirty.get(plugin.overrideKey);
        if (!dirty) continue;
        if (dirty.keywords !== undefined
            || dirty.isEnabled !== undefined
            || dirty.includeInGlobalResults !== undefined) {
            hasKeymapChanges = true;
            keymapOverrides[plugin.overrideKey] = {
                keywords: dirty.keywords !== undefined ? dirty.keywords : plugin.currentKeywords,
                isEnabled: dirty.isEnabled !== undefined ? dirty.isEnabled : plugin.isEnabled,
                includeInGlobalResults: dirty.includeInGlobalResults !== undefined
                    ? dirty.includeInGlobalResults
                    : plugin.includeInGlobalResults,
            };
        }
        if (dirty.hotKey !== undefined) {
            hotKeysToValidate[plugin.overrideKey] = dirty.hotKey;
        }
        if (dirty.keywords !== undefined) keywordsToValidate[plugin.overrideKey] = dirty.keywords;
    }

    const conflicts: KeymapConflict[] = [];
    if (Object.keys(keywordsToValidate).length > 0) {
        const result = await bus.call<{ conflicts: KeymapConflict[] }>("validateKeymap", {
            keywords: keywordsToValidate,
        });
        conflicts.push(...(result.conflicts || []));
    }
    if (Object.keys(hotKeysToValidate).length > 0) {
        const result = await bus.call<{ conflicts: KeymapConflict[] }>("validateHotKeys", {
            hotKeys: hotKeysToValidate,
        });
        conflicts.push(...(result.conflicts || []));
    }

    store.keymapConflicts = conflicts;
    if (store.keymapConflicts.length > 0) {
        showToast(t("Plugin.Settings.Keymap.HasConflicts", "Conflicts detected. Resolve them before saving."), "error");
        return false;
    }

    if (hasKeymapChanges) {
        await bus.call("saveKeymap", { overrides: keymapOverrides });
    }
    if (Object.keys(hotKeysToValidate).length > 0) {
        await bus.call("saveHotKeys", { hotKeys: hotKeysToValidate });
    }
    store.keymapDirty.clear();
    await loadPluginOverrides();
    refreshDirty();
    return true;
}

export async function restartApp(): Promise<void> {
    store.restartModal = false;
    showToast(t("Plugin.Settings.Restarting", "Restarting..."), "success");
    try {
        await bus.call("restart");
    } catch {
        // Host restart disconnects the bridge.
    }
}
