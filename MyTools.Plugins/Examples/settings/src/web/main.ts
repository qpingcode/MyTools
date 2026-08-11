import { tool } from "@qping/plugin-common/client";
import type { Config } from "./types";
import { t } from "./utils";
import * as common from "./common";
import {
    findCategory,
    findFirstSelectable,
    findFirstVisibleCategory,
    renderCategoryTree,
    renderSettings,
    setGesturesSearchChecker,
    setPluginsSearchChecker,
    setSelectCategoryCallback
} from "./config-panel";
import { renderKeymap, saveKeymapInternal, keymapMatchesSearch } from "./keymap-panel";
import { renderGestures, saveGesturesInternal, gesturesMatchesSearch } from "./gestures-panel";

var hostEvents = tool.events.host;

// Suppress the browser/WebView2 default context menu so it doesn't interfere
// with right-button gesture recording on the Mouse Gestures page.
document.addEventListener("contextmenu", (e) => e.preventDefault());

// Allow config-panel to search-match special categories and report selection back here
setPluginsSearchChecker(keymapMatchesSearch);
setGesturesSearchChecker(gesturesMatchesSearch);
setSelectCategoryCallback(selectCategory);

// ── Load configuration ──

async function loadConfiguration(): Promise<void> {
    common.settingsList.innerHTML = '<div class="loading">'
        + t("Plugin.Settings.Loading", "Loading...")
        + "</div>";
    try {
        common.state.config = await tool.call<Config>("getConfiguration");
        renderCategoryTree();
        if (common.state.currentCategoryKey) {
            selectCategory(common.state.currentCategoryKey);
        } else {
            var first = findFirstSelectable(common.state.config.categories);
            if (first) selectCategory(first.key);
        }
        common.updateSaveButton();
    } catch (error) {
        common.settingsList.innerHTML = '<div class="loading">'
            + (error instanceof Error ? error.message : String(error))
            + "</div>";
    }
}

// ── Category selection ──

function selectCategory(key: string): void {
    common.state.currentCategoryKey = key;
    if (!common.state.config) return;

    // Update left sidebar highlight
    common.categoryTree.querySelectorAll(".category-item").forEach(el => {
        el.classList.toggle("active", (el as HTMLElement).dataset.key === key);
    });

    // Plugins category → keymap rendering
    if (key === "Plugins") {
        common.categoryTitle.innerHTML = t("Plugin.Settings.Category.Plugins", "Plugins");
        common.categoryDescription.innerHTML = "";
        renderKeymap();
        return;
    }

    // Gestures category → gesture list rendering
    if (key === "Gestures") {
        common.categoryTitle.innerHTML = t("Plugin.Settings.Category.Gestures", "Mouse Gestures");
        common.categoryDescription.innerHTML = "";
        renderGestures();
        return;
    }

    var category = findCategory(common.state.config.categories, key);
    if (!category) return;

    common.categoryTitle.innerHTML = category.name;
    common.categoryDescription.innerHTML = category.description || "";

    renderSettings(category);
}

// ── Unified save ──

async function saveSettings(): Promise<void> {
    if (common.state.dirtySettings.size === 0 && common.state.keymapDirty.size === 0 && !common.state.gesturesDirty) return;
    common.saveButton.disabled = true;

    try {
        var requiresRestart = false;

        // 1. Save scalar settings
        if (common.state.dirtySettings.size > 0) {
            var changes = Array.from(common.state.dirtySettings.entries()).map(([fullPath, value]) => ({
                fullPath: fullPath,
                value: value,
            }));

            var result = await tool.call<{ requiresRestart: boolean }>(
                "saveConfiguration",
                { changes: changes }
            );
            common.state.dirtySettings.clear();
            requiresRestart = result.requiresRestart;
        }

        // 2. Save keymap settings (validate first, then save)
        if (common.state.keymapDirty.size > 0) {
            var keymapSaved = await saveKeymapInternal();
            if (!keymapSaved) {
                common.updateSaveButton();
                return;
            }
        }

        // 3. Save gesture settings
        if (common.state.gesturesDirty) {
            await saveGesturesInternal();
        }

        common.updateSaveButton();

        if (requiresRestart) {
            common.restartModal.hidden = false;
        } else {
            common.showToast(t("Plugin.Settings.Saved", "Settings saved successfully."), "success");
        }
    } catch (error) {
        common.showToast(error instanceof Error ? error.message : String(error), "error");
        common.updateSaveButton();
    }
}

// ── Search ──

var searchTimer: ReturnType<typeof setTimeout> | null = null;
common.searchInput.addEventListener("input", () => {
    if (searchTimer) clearTimeout(searchTimer);
    searchTimer = setTimeout(() => {
        common.state.searchQuery = common.searchInput.value.trim().toLowerCase();
        renderCategoryTree();

        if (common.state.searchQuery) {
            // Searching: jump to the first category that has matches
            var first = findFirstVisibleCategory();
            if (first) {
                selectCategory(first.key);
            } else {
                common.settingsList.innerHTML = '<div class="loading">'
                    + t("Plugin.Settings.NoResults", "No matching settings found")
                    + "</div>";
            }
        } else {
            // Search cleared: re-render the currently selected category
            if (common.state.currentCategoryKey === "Plugins") {
                renderKeymap();
            } else if (common.state.currentCategoryKey === "Gestures") {
                renderGestures();
            } else if (common.state.currentCategoryKey && common.state.config) {
                var cat = findCategory(common.state.config.categories, common.state.currentCategoryKey);
                if (cat) renderSettings(cat);
            }
        }
    }, 150);
});

// ── Event bindings ──

common.saveButton.addEventListener("click", saveSettings);
common.restartCancel.addEventListener("click", () => { common.restartModal.hidden = true; });
common.restartConfirm.addEventListener("click", async () => {
    common.restartModal.hidden = true;
    common.showToast(t("Plugin.Settings.Restarting", "Restarting..."), "success");
    try {
        await tool.call("restart");
    } catch {
        // Host restart disconnects the bridge — expected, ignore.
    }
});

// ── Host events ──

tool.subscribe(hostEvents.initialize, async () => {
    await loadConfiguration();
});

tool.subscribe(hostEvents.languageChanged, async () => {
    tool.i18n.apply(document);
    await loadConfiguration();
});

tool.subscribe("mytools.host.theme-changed", () => {
    if (common.state.currentCategoryKey === "Plugins") {
        renderKeymap();
    } else if (common.state.currentCategoryKey === "Gestures") {
        renderGestures();
    } else if (common.state.currentCategoryKey && common.state.config) {
        var cat = findCategory(common.state.config.categories, common.state.currentCategoryKey);
        if (cat) renderSettings(cat);
    }
});

tool.ready("settings");
