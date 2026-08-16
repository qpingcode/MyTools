import type { Category, Option, Setting } from "./types";
import { highlight, t } from "./utils";
import * as common from "./common";
import { captureInputAction } from "./capture-input-action";

// ── Hooks for special-category search (keymap, gestures) ──
// These are set by the respective panel modules to participate in search matching.
var pluginsSearchChecker: (() => boolean) | null = null;
var gesturesSearchChecker: (() => boolean) | null = null;

export function setPluginsSearchChecker(fn: (() => boolean) | null): void {
    pluginsSearchChecker = fn;
}

export function setGesturesSearchChecker(fn: (() => boolean) | null): void {
    gesturesSearchChecker = fn;
}

// ── Category list rendering (flat, no hierarchy) ──

// 系统分类 key（General、Gestures、Plugins）——分隔线上方
var SYSTEM_CATEGORY_KEYS = new Set(["General", "Gestures", "Plugins"]);

/**
 * 判定分类是否应该显示在侧栏：
 * - Plugins/Gestures 是特殊分类，即使没有标量 settings 也显示
 * - 其他分类（插件）至少要有一个 settings
 */
function shouldShowCategory(category: Category): boolean {
    if (category.key === "Plugins" || category.key === "Gestures") return true;
    return category.settings.length > 0;
}

export function renderCategoryTree(): void {
    if (!common.state.config) return;
    common.categoryTree.innerHTML = "";
    var hasVisible = false;
    var dividerInserted = false;

    for (let category of common.state.config.categories) {
        // 没有配置项的插件分类不显示
        if (!shouldShowCategory(category)) continue;

        // 搜索过滤
        if (common.state.searchQuery && !categorySelfMatches(category)) continue;

        // 在第一个插件分类（非系统分类）之前插入分隔线
        if (!dividerInserted && !SYSTEM_CATEGORY_KEYS.has(category.key)) {
            let divider = document.createElement("li");
            divider.className = "category-divider";
            divider.innerHTML = "<hr>";
            common.categoryTree.appendChild(divider);
            dividerInserted = true;
        }

        let li = document.createElement("li");
        let div = document.createElement("div");
        div.className = category.isSelectable ? "category-item" : "category-item not-selectable";
        if (category.key === common.state.currentCategoryKey) {
            div.classList.add("active");
        }
        div.innerHTML = highlight(category.name, common.state.searchQuery);
        div.dataset.key = category.key;

        if (category.isSelectable) {
            div.addEventListener("click", () => onSelectCategory(category.key));
        }

        li.appendChild(div);
        common.categoryTree.appendChild(li);
        hasVisible = true;
    }

    common.noResults.hidden = hasVisible;
}

// 扁平列表：搜索时只检查分类自身（名称/描述/自己的 settings），
// 不再递归子分类。
export function categoryMatchesSearch(category: Category): boolean {
    return categorySelfMatches(category);
}

export function settingMatchesSearch(setting: Setting): boolean {
    if (!common.state.searchQuery) return true;
    if (setting.title.toLowerCase().includes(common.state.searchQuery)) return true;
    if (setting.description && setting.description.toLowerCase().includes(common.state.searchQuery)) return true;
    return false;
}

// ── Category selection (delegates to main.ts via callback) ──

var selectCategoryCallback: ((key: string) => void) | null = null;

export function setSelectCategoryCallback(fn: (key: string) => void): void {
    selectCategoryCallback = fn;
}

function onSelectCategory(key: string): void {
    if (selectCategoryCallback) {
        selectCategoryCallback(key);
    }
}

// ── Find category helpers ──

export function findFirstSelectable(categories: Category[]): Category | null {
    for (var cat of categories) {
        if (shouldShowCategory(cat) && cat.isSelectable) return cat;
    }
    return null;
}

/**
 * 找到搜索后第一个有匹配项的可选分类（扁平列表）。
 */
export function findFirstVisibleCategory(): Category | null {
    if (!common.state.config) return null;
    for (var cat of common.state.config.categories) {
        if (shouldShowCategory(cat) && cat.isSelectable && categorySelfMatches(cat)) return cat;
    }
    return null;
}

/**
 * 判定分类自身是否匹配搜索（不递归子分类）。
 * 用于区分"父分类自己匹配"和"父分类因子分类匹配"：
 * 只有自身匹配时，右侧才显示配置内容；否则右侧显示"没有匹配项"。
 */
export function categorySelfMatches(category: Category): boolean {
    if (!common.state.searchQuery) return true;
    if (category.name.toLowerCase().includes(common.state.searchQuery)) return true;
    if (category.description && category.description.toLowerCase().includes(common.state.searchQuery)) return true;
    for (var setting of category.settings) {
        if (settingMatchesSearch(setting)) return true;
    }
    if (category.key === "Plugins" && pluginsSearchChecker) {
        if (pluginsSearchChecker()) return true;
    }
    if (category.key === "Gestures" && gesturesSearchChecker) {
        if (gesturesSearchChecker()) return true;
    }
    return false;
}

export function findCategory(categories: Category[], key: string): Category | null {
    for (var cat of categories) {
        if (cat.key === key) return cat;
        var child = findCategory(cat.children, key);
        if (child) return child;
    }
    return null;
}

// ── Scalar settings rendering ──

export function renderSettings(category: Category): void {
    common.settingsList.innerHTML = "";

    // 搜索时，如果该分类自身不匹配（只是因子分类匹配才出现在树上），
    // 不显示其配置内容，而是提示"没有匹配项"。
    if (common.state.searchQuery && !categorySelfMatches(category)) {
        common.settingsList.innerHTML = '<div class="loading">'
            + t("Plugin.Settings.NoResults", "No matching settings found")
            + "</div>";
        return;
    }

    var settings = category.settings;
    if (settings.length === 0) {
        common.settingsList.innerHTML = '<div class="loading">'
            + t("Plugin.Settings.NoSettings", "No settings in this category")
            + "</div>";
        return;
    }

    // No filtering — show all settings in the category. Search only highlights.
    for (var setting of settings) {
        common.settingsList.appendChild(renderSettingItem(setting));
    }
}

export function renderSettingItem(setting: Setting): HTMLElement {
    var div = document.createElement("div");
    div.className = "setting-item";

    var row = document.createElement("div");
    row.className = "setting-row";

    var label = document.createElement("div");
    label.className = "setting-label";
    label.innerHTML = highlight(setting.title, common.state.searchQuery);

    var control = document.createElement("div");
    control.className = "setting-control";
    control.appendChild(createEditor(setting));

    row.appendChild(label);
    row.appendChild(control);
    div.appendChild(row);

    if (setting.description) {
        var desc = document.createElement("div");
        desc.className = "setting-description";
        desc.innerHTML = highlight(setting.description!, common.state.searchQuery);
        div.appendChild(desc);
    }

    return div;
}

function createEditor(setting: Setting): HTMLElement {
    var currentVal = setting.currentValue ?? "";
    var dirtyVal = common.state.dirtySettings.get(setting.fullPath);

    switch (setting.valueType) {
        case "Bool": {
            var checkbox = document.createElement("input");
            checkbox.type = "checkbox";
            checkbox.className = "setting-checkbox";
            checkbox.checked = dirtyVal !== undefined
                ? dirtyVal === "True"
                : currentVal === "True";
            checkbox.addEventListener("change", () => {
                common.state.dirtySettings.set(setting.fullPath, checkbox.checked ? "True" : "False");
                common.updateSaveButton();
            });
            return checkbox;
        }
        case "Language": {
            return createSelect(setting, currentVal, dirtyVal, common.state.config?.supportedLocales ?? []);
        }
        case "Theme": {
            return createSelect(setting, currentVal, dirtyVal, common.state.config?.supportedThemes ?? []);
        }
        case "LogLevel": {
            return createSelect(setting, currentVal, dirtyVal, common.state.config?.supportedLogLevels ?? []);
        }
        case "HotKey": {
            return createHotKeyEditor(setting, currentVal, dirtyVal);
        }
        case "Integer":
        case "Double": {
            var input = document.createElement("input");
            input.type = "number";
            input.className = "setting-input";
            input.value = dirtyVal !== undefined ? dirtyVal : currentVal;
            if (setting.valueType === "Integer") input.step = "1";
            input.addEventListener("input", () => {
                common.state.dirtySettings.set(setting.fullPath, input.value);
                common.updateSaveButton();
            });
            return input;
        }
        default: {
            var textInput = document.createElement("input");
            textInput.type = "text";
            textInput.className = "setting-input";
            textInput.value = dirtyVal !== undefined ? dirtyVal : currentVal;
            textInput.addEventListener("input", () => {
                common.state.dirtySettings.set(setting.fullPath, textInput.value);
                common.updateSaveButton();
            });
            return textInput;
        }
    }
}

function createSelect(setting: Setting, currentVal: string, dirtyVal: string | undefined, options: Option[]): HTMLElement {
    var select = document.createElement("select");
    select.className = "setting-select";

    var selectedVal = dirtyVal !== undefined ? dirtyVal : currentVal;

    for (var opt of options) {
        var option = document.createElement("option");
        option.value = opt.value;
        option.textContent = opt.label;
        if (opt.value === selectedVal) option.selected = true;
        select.appendChild(option);
    }

    select.addEventListener("change", () => {
        common.state.dirtySettings.set(setting.fullPath, select.value);
        common.updateSaveButton();
    });
    return select;
}

function createHotKeyEditor(setting: Setting, currentVal: string, dirtyVal: string | undefined): HTMLElement {
    var btn = document.createElement("button");
    btn.type = "button";
    btn.className = "hotkey-recorder";
    var value = dirtyVal !== undefined ? dirtyVal : currentVal;

    function renderLabel(): void {
        btn.textContent = value || t("Plugin.Settings.Keymap.NoHotkey", "None");
    }

    renderLabel();
    btn.addEventListener("click", () => {
        void captureInputAction({
            showKeyboard: true,
            showMouse: false,
            value: { kind: "hotkey", hotKey: value || null },
            defaultHotKey: setting.defaultValue ?? "",
            excludeSearchHotKey: true
        }).then((result) => {
            if (!result) return;
            value = result.hotKey || "";
            renderLabel();
            common.state.dirtySettings.set(setting.fullPath, value);
            common.updateSaveButton();
        });
    });
    return btn;
}
