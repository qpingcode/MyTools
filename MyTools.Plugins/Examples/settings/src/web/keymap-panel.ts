import { bus } from "./bus";
import type { Category, KeymapConflict, KeymapPlugin } from "./types";
import { highlight, t } from "./utils";
import * as common from "./common";
import { categorySelfMatches, findCategory } from "./config-panel";

var keymapPlugins: KeymapPlugin[] | null = null;

// ── Search checker (for category tree matching) ──

export function keymapMatchesSearch(): boolean {
    if (!common.state.searchQuery || !keymapPlugins) return false;
    for (var plugin of keymapPlugins) {
        if (plugin.name.toLowerCase().includes(common.state.searchQuery)) return true;
    }
    return false;
}

// ── Load ──

export async function loadKeymap(): Promise<void> {
    common.settingsList.innerHTML = '<div class="loading">' + t("Plugin.Settings.Loading", "Loading...") + "</div>";
    try {
        var data = await bus.call<{ plugins: KeymapPlugin[] }>("getKeymap");
        keymapPlugins = data.plugins || [];
        common.state.keymapDirty.clear();
        renderKeymap();
    } catch (error) {
        common.settingsList.innerHTML = '<div class="loading">'
            + (error instanceof Error ? error.message : String(error))
            + "</div>";
    }
}

// ── Render ──

export function renderKeymap(): void {
    if (!keymapPlugins) {
        void loadKeymap();
        return;
    }

    common.settingsList.innerHTML = "";

    // 搜索时，如果该分类自身不匹配（只是因子分类匹配才出现在树上），
    // 不显示其内容，而是提示"没有匹配项"。
    if (common.state.searchQuery && common.state.config) {
        var pluginsCat = findCategory(common.state.config.categories, "Plugins") as Category | null;
        if (pluginsCat && !categorySelfMatches(pluginsCat)) {
            common.settingsList.innerHTML = '<div class="loading">'
                + t("Plugin.Settings.NoResults", "No matching settings found")
                + "</div>";
            return;
        }
    }

    // 表头
    var header = document.createElement("div");
    header.className = "keymap-header";
    header.innerHTML =
        '<div class="keymap-col-name">' + t("Plugin.Settings.Keymap.HeaderName", "Plugin Name") + '</div>'
        + '<div class="keymap-col-hotkey">' + t("Plugin.Settings.Keymap.HeaderHotkey", "Hotkey") + '</div>'
        + '<div class="keymap-col-keywords">' + t("Plugin.Settings.Keymap.HeaderKeywords", "Keywords") + '</div>'
        + '<div class="keymap-col-enabled">' + t("Plugin.Settings.Keymap.HeaderEnabled", "Enabled") + '</div>';
    common.settingsList.appendChild(header);

    // No filtering — show all plugins. Search only highlights.
    for (var plugin of keymapPlugins) {
        common.settingsList.appendChild(renderKeymapRow(plugin));
    }
}

function renderKeymapRow(plugin: KeymapPlugin): HTMLElement {
    var dirty = common.state.keymapDirty.get(plugin.pluginId);

    var row = document.createElement("div");
    row.className = "keymap-row";
    row.dataset.pluginId = plugin.pluginId;

    // 插件名
    var nameDiv = document.createElement("div");
    nameDiv.className = "keymap-col-name";
    nameDiv.innerHTML = highlight(plugin.name, common.state.searchQuery);
    row.appendChild(nameDiv);

    // 热键录制器
    var hotKeyDiv = document.createElement("div");
    hotKeyDiv.className = "keymap-col-hotkey";
    var hotKeyBtn = document.createElement("button");
    hotKeyBtn.className = "hotkey-recorder";
    var hotKeyVal = dirty?.hotKey !== undefined ? dirty.hotKey : plugin.currentHotKey;
    hotKeyBtn.textContent = hotKeyVal || t("Plugin.Settings.Keymap.NoHotkey", "None");

    hotKeyBtn.addEventListener("click", () => {
        startHotKeyRecording(hotKeyBtn, (newVal) => {
            markKeymapDirty(plugin.pluginId, { hotKey: newVal });
            hotKeyBtn.textContent = newVal || t("Plugin.Settings.Keymap.NoHotkey", "None");
            common.updateSaveButton();
        });
    });

    var clearHotKeyBtn = document.createElement("button");
    clearHotKeyBtn.className = "hotkey-clear";
    clearHotKeyBtn.textContent = "×";
    clearHotKeyBtn.title = t("Plugin.Settings.Keymap.ClearHotkey", "Clear hotkey");
    clearHotKeyBtn.addEventListener("click", () => {
        markKeymapDirty(plugin.pluginId, { hotKey: null });
        hotKeyBtn.textContent = t("Plugin.Settings.Keymap.NoHotkey", "None");
        common.updateSaveButton();
    });

    hotKeyDiv.appendChild(hotKeyBtn);
    hotKeyDiv.appendChild(clearHotKeyBtn);
    row.appendChild(hotKeyDiv);

    // 关键词输入
    var keywordsDiv = document.createElement("div");
    keywordsDiv.className = "keymap-col-keywords";
    var keywordsInput = document.createElement("input");
    keywordsInput.type = "text";
    keywordsInput.className = "setting-input";
    var kwVal = dirty?.keywords !== undefined ? dirty.keywords : plugin.currentKeywords;
    keywordsInput.value = (kwVal || []).join(", ");
    keywordsInput.placeholder = t("Plugin.Settings.Keymap.KeywordsPlaceholder", "Up to 3 keywords, comma separated");
    keywordsInput.addEventListener("change", () => {
        var kws = keywordsInput.value.split(",").map(k => k.trim()).filter(k => k).slice(0, 3);
        keywordsInput.value = kws.join(", ");
        markKeymapDirty(plugin.pluginId, { keywords: kws });
        common.updateSaveButton();
    });
    keywordsDiv.appendChild(keywordsInput);
    row.appendChild(keywordsDiv);

    // 启用 checkbox
    var enabledDiv = document.createElement("div");
    enabledDiv.className = "keymap-col-enabled";
    var checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.className = "keymap-checkbox";
    checkbox.checked = dirty?.isEnabled !== undefined ? dirty.isEnabled : plugin.isEnabled;
    checkbox.addEventListener("change", () => {
        markKeymapDirty(plugin.pluginId, { isEnabled: checkbox.checked });
        common.updateSaveButton();
    });
    enabledDiv.appendChild(checkbox);
    row.appendChild(enabledDiv);

    // 冲突提示占位
    var conflictDiv = document.createElement("div");
    conflictDiv.className = "keymap-conflict";
    conflictDiv.hidden = true;
    row.appendChild(conflictDiv);

    return row;
}

// ── Hotkey recording (shared with gestures panel) ──

export function startHotKeyRecording(
    btn: HTMLButtonElement,
    onCapture: (hotKey: string | null) => void
): void {
    var originalText = btn.textContent;
    btn.textContent = t("Plugin.Settings.Keymap.Recording", "Press shortcut...");
    btn.classList.add("recording");

    void bus.call("suspendHotkeys");

    var handler = (e: KeyboardEvent) => {
        e.preventDefault();
        e.stopPropagation();

        if (e.key === "Escape") {
            cleanup();
            btn.textContent = originalText;
            btn.classList.remove("recording");
            return;
        }

        if (["Control", "Shift", "Alt", "Meta"].includes(e.key)) {
            return;
        }

        var parts: string[] = [];
        if (e.ctrlKey) parts.push("Ctrl");
        if (e.shiftKey) parts.push("Shift");
        if (e.altKey) parts.push("Alt");
        if (e.metaKey) parts.push("Win");

        var keyName = e.key;
        if (keyName === " ") keyName = "Space";
        else if (keyName.length === 1) keyName = keyName.toUpperCase();
        parts.push(keyName);

        var hotKey = parts.join("+");
        cleanup();
        btn.classList.remove("recording");
        onCapture(hotKey);
    };

    function cleanup() {
        document.removeEventListener("keydown", handler, true);
        void bus.call("resumeHotkeys");
    }

    document.addEventListener("keydown", handler, true);
}

function markKeymapDirty(pluginId: string, change: { hotKey?: string | null; keywords?: string[]; isEnabled?: boolean }): void {
    var existing = common.state.keymapDirty.get(pluginId) || {};
    if (change.hotKey !== undefined) existing.hotKey = change.hotKey;
    if (change.keywords !== undefined) existing.keywords = change.keywords;
    if (change.isEnabled !== undefined) existing.isEnabled = change.isEnabled;
    common.state.keymapDirty.set(pluginId, existing);
    common.updateSaveButton();
}

// ── Save ──

export async function saveKeymapInternal(): Promise<boolean> {
    if (common.state.keymapDirty.size === 0 || !keymapPlugins) return true;

    var overrides: Record<string, { hotKey?: string | null; keywords?: string[]; isEnabled?: boolean }> = {};
    var hotKeysToValidate: Record<string, string | null> = {};
    var keywordsToValidate: Record<string, string[] | null> = {};

    for (var [pluginId, dirty] of common.state.keymapDirty) {
        overrides[pluginId] = dirty;
        if (dirty.hotKey !== undefined) {
            hotKeysToValidate[pluginId] = dirty.hotKey;
        }
        if (dirty.keywords !== undefined) {
            keywordsToValidate[pluginId] = dirty.keywords;
        }
    }

    var validateResult = await bus.call<{ conflicts: KeymapConflict[] }>("validateKeymap", {
        hotKeys: hotKeysToValidate,
        keywords: keywordsToValidate,
    });

    common.settingsList.querySelectorAll(".keymap-conflict").forEach(el => {
        (el as HTMLElement).hidden = true;
        el.textContent = "";
    });

    if (validateResult.conflicts && validateResult.conflicts.length > 0) {
        for (var c of validateResult.conflicts) {
            var row = common.settingsList.querySelector(`[data-plugin-id="${c.pluginId}"]`);
            if (row) {
                var conflictEl = row.querySelector(".keymap-conflict") as HTMLElement;
                conflictEl.hidden = false;
                conflictEl.textContent = "⚠ " + c.field + " '" + c.value + "' "
                    + t("Plugin.Settings.Keymap.ConflictsWith", "conflicts with") + " " + c.conflictsWith;
            }
        }
        common.showToast(t("Plugin.Settings.Keymap.HasConflicts", "Conflicts detected. Resolve them before saving."), "error");
        return false;
    }

    await bus.call("saveKeymap", { overrides: overrides });
    common.state.keymapDirty.clear();

    await loadKeymap();
    return true;
}
