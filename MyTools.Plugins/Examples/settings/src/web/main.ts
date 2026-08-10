import { tool } from "@qping/plugin-common/web-tool";

(function () {
    type Option = { value: string; label: string };
    type Setting = {
        fullPath: string;
        title: string;
        description?: string;
        valueType: string;
        currentValue?: string;
        requiresRestart: boolean;
    };
    type Category = {
        key: string;
        name: string;
        description?: string;
        isSelectable: boolean;
        children: Category[];
        settings: Setting[];
    };
    type Config = {
        categories: Category[];
        supportedLocales: Option[];
        supportedThemes: Option[];
        supportedLogLevels: Option[];
    };

    var hostEvents = tool.events.host;
    var config: Config | null = null;
    var dirtySettings = new Map<string, string>();
    var currentCategoryKey = "";
    var searchQuery = "";

    var searchInput = document.getElementById("searchInput") as HTMLInputElement;
    var categoryTree = document.getElementById("categoryTree") as HTMLUListElement;
    var noResults = document.getElementById("noResults") as HTMLElement;
    var categoryTitle = document.getElementById("categoryTitle") as HTMLElement;
    var categoryDescription = document.getElementById("categoryDescription") as HTMLElement;
    var settingsList = document.getElementById("settingsList") as HTMLElement;
    var saveButton = document.getElementById("saveButton") as HTMLButtonElement;
    var toast = document.getElementById("toast") as HTMLElement;
    var restartModal = document.getElementById("restartModal") as HTMLElement;
    var restartConfirm = document.getElementById("restartConfirm") as HTMLButtonElement;
    var restartCancel = document.getElementById("restartCancel") as HTMLButtonElement;

    function t(key: string, defaultValue: string, values: Record<string, unknown> = {}): string {
        return tool.i18n.t(key, { defaultValue: defaultValue, ...values });
    }

    function escapeHtml(text: string): string {
        var div = document.createElement("div");
        div.textContent = text;
        return div.innerHTML;
    }

    function highlight(text: string, query: string): string {
        if (!query) return escapeHtml(text);
        var lower = text.toLowerCase();
        var idx = lower.indexOf(query);
        if (idx < 0) return escapeHtml(text);
        return escapeHtml(text.slice(0, idx))
            + "<mark>" + escapeHtml(text.slice(idx, idx + query.length)) + "</mark>"
            + highlightRemaining(text.slice(idx + query.length), query);
    }

    function highlightRemaining(text: string, query: string): string {
        var lower = text.toLowerCase();
        var idx = lower.indexOf(query);
        if (idx < 0) return escapeHtml(text);
        return escapeHtml(text.slice(0, idx))
            + "<mark>" + escapeHtml(text.slice(idx, idx + query.length)) + "</mark>"
            + highlightRemaining(text.slice(idx + query.length), query);
    }

    // ── 加载配置 ──
    async function loadConfiguration(): Promise<void> {
        settingsList.innerHTML = '<div class="loading">'
            + t("Plugin.Settings.Loading", "Loading...")
            + "</div>";
        try {
            config = await tool.call<Config>("getConfiguration");
            renderCategoryTree();
            if (currentCategoryKey) {
                selectCategory(currentCategoryKey);
            } else {
                var first = findFirstSelectable(config.categories);
                if (first) selectCategory(first.key);
            }
            updateSaveButton();
        } catch (error) {
            settingsList.innerHTML = '<div class="loading">'
                + (error instanceof Error ? error.message : String(error))
                + "</div>";
        }
    }

    function findFirstSelectable(categories: Category[]): Category | null {
        for (var cat of categories) {
            if (cat.isSelectable && cat.settings.length > 0) return cat;
            var child = findFirstSelectable(cat.children);
            if (child) return child;
        }
        return null;
    }

    // ── 渲染分类树 ──
    function renderCategoryTree(): void {
        if (!config) return;
        categoryTree.innerHTML = "";
        var hasVisible = false;

        for (var category of config.categories) {
            var li = renderCategoryNode(category);
            if (li) {
                categoryTree.appendChild(li);
                hasVisible = true;
            }
        }

        noResults.hidden = hasVisible;
    }

    function renderCategoryNode(category: Category): HTMLLIElement | null {
        var matchesSearch = categoryMatchesSearch(category);
        if (!matchesSearch && searchQuery) return null;

        var li = document.createElement("li");

        var div = document.createElement("div");
        div.className = category.isSelectable ? "category-item" : "category-item not-selectable";
        if (category.key === currentCategoryKey) {
            div.classList.add("active");
        }
        div.innerHTML = highlight(category.name, searchQuery);
        div.dataset.key = category.key;

        if (category.isSelectable) {
            div.addEventListener("click", () => selectCategory(category.key));
        }

        li.appendChild(div);

        if (category.children.length > 0) {
            var ul = document.createElement("ul");
            for (var child of category.children) {
                var childLi = renderCategoryNode(child);
                if (childLi) {
                    ul.appendChild(childLi);
                }
            }
            if (ul.children.length > 0) {
                li.appendChild(ul);
            }
        }

        return li;
    }

    function categoryMatchesSearch(category: Category): boolean {
        if (!searchQuery) return true;
        if (category.name.toLowerCase().includes(searchQuery)) return true;
        if (category.description && category.description.toLowerCase().includes(searchQuery)) return true;
        for (var setting of category.settings) {
            if (settingMatchesSearch(setting)) return true;
        }
        for (var child of category.children) {
            if (categoryMatchesSearch(child)) return true;
        }
        // Plugins 分类还要检查 keymap 插件列表
        if (category.key === "Plugins" && keymapPlugins) {
            for (var plugin of keymapPlugins) {
                if (plugin.name.toLowerCase().includes(searchQuery)) return true;
            }
        }
        return false;
    }

    function settingMatchesSearch(setting: Setting): boolean {
        if (!searchQuery) return true;
        if (setting.title.toLowerCase().includes(searchQuery)) return true;
        if (setting.description && setting.description.toLowerCase().includes(searchQuery)) return true;
        return false;
    }

    // ── 选择分类 ──
    function selectCategory(key: string): void {
        currentCategoryKey = key;
        if (!config) return;

        // 更新左侧高亮
        categoryTree.querySelectorAll(".category-item").forEach(el => {
            el.classList.toggle("active", (el as HTMLElement).dataset.key === key);
        });

        // Plugins 分类走 keymap 渲染路径
        if (key === "Plugins")
        {
            categoryTitle.innerHTML = highlight(t("Plugin.Settings.Category.Plugins", "Plugins"), searchQuery);
            categoryDescription.innerHTML = "";
            renderKeymap();
            return;
        }

        var category = findCategory(config.categories, key);
        if (!category) return;

        categoryTitle.innerHTML = highlight(category.name, searchQuery);
        categoryDescription.innerHTML = category.description
            ? highlight(category.description, searchQuery)
            : "";

        renderSettings(category);
    }

    function findCategory(categories: Category[], key: string): Category | null {
        for (var cat of categories) {
            if (cat.key === key) return cat;
            var child = findCategory(cat.children, key);
            if (child) return child;
        }
        return null;
    }

    // ── 渲染设置项 ──
    function renderSettings(category: Category): void {
        settingsList.innerHTML = "";

        var settings = category.settings;
        if (settings.length === 0) {
            settingsList.innerHTML = '<div class="loading">'
                + t("Plugin.Settings.NoSettings", "No settings in this category")
                + "</div>";
            return;
        }

        // 搜索过滤
        var filtered = searchQuery
            ? settings.filter(s => settingMatchesSearch(s))
            : settings;

        if (filtered.length === 0) {
            settingsList.innerHTML = '<div class="loading">'
                + t("Plugin.Settings.NoResults", "No matching settings found")
                + "</div>";
            return;
        }

        for (var setting of filtered) {
            settingsList.appendChild(renderSettingItem(setting));
        }
    }

    function renderSettingItem(setting: Setting): HTMLElement {
        var div = document.createElement("div");
        div.className = "setting-item";

        var row = document.createElement("div");
        row.className = "setting-row";

        var label = document.createElement("div");
        label.className = "setting-label";
        label.innerHTML = highlight(setting.title, searchQuery);

        var control = document.createElement("div");
        control.className = "setting-control";
        control.appendChild(createEditor(setting));

        row.appendChild(label);
        row.appendChild(control);
        div.appendChild(row);

        if (setting.description) {
            var desc = document.createElement("div");
            desc.className = "setting-description";
            desc.innerHTML = highlight(setting.description!, searchQuery);
            div.appendChild(desc);
        }

        return div;
    }

    function createEditor(setting: Setting): HTMLElement {
        var currentVal = setting.currentValue ?? "";
        var dirtyVal = dirtySettings.get(setting.fullPath);

        switch (setting.valueType) {
            case "Bool": {
                var checkbox = document.createElement("input");
                checkbox.type = "checkbox";
                checkbox.className = "setting-checkbox";
                checkbox.checked = dirtyVal !== undefined
                    ? dirtyVal === "True"
                    : currentVal === "True";
                checkbox.addEventListener("change", () => {
                    markDirty(setting.fullPath, checkbox.checked ? "True" : "False");
                });
                return checkbox;
            }
            case "Language": {
                return createSelect(setting, currentVal, dirtyVal, config?.supportedLocales ?? []);
            }
            case "Theme": {
                return createSelect(setting, currentVal, dirtyVal, config?.supportedThemes ?? []);
            }
            case "LogLevel": {
                return createSelect(setting, currentVal, dirtyVal, config?.supportedLogLevels ?? []);
            }
            case "Integer":
            case "Double": {
                var input = document.createElement("input");
                input.type = "number";
                input.className = "setting-input";
                input.value = dirtyVal !== undefined ? dirtyVal : currentVal;
                if (setting.valueType === "Integer") input.step = "1";
                input.addEventListener("input", () => markDirty(setting.fullPath, input.value));
                return input;
            }
            default: {
                var textInput = document.createElement("input");
                textInput.type = "text";
                textInput.className = "setting-input";
                textInput.value = dirtyVal !== undefined ? dirtyVal : currentVal;
                textInput.addEventListener("input", () => markDirty(setting.fullPath, textInput.value));
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

        select.addEventListener("change", () => markDirty(setting.fullPath, select.value));
        return select;
    }

    // ── 标记修改 ──
    function markDirty(fullPath: string, value: string): void {
        dirtySettings.set(fullPath, value);
        updateSaveButton();
    }

    function updateSaveButton(): void {
        saveButton.disabled = dirtySettings.size === 0 && keymapDirty.size === 0;
    }

    // ── 保存（统一保存 General 设置 + Keymap 设置）──
    async function saveSettings(): Promise<void> {
        if (dirtySettings.size === 0 && keymapDirty.size === 0) return;
        saveButton.disabled = true;

        try {
            var requiresRestart = false;

            // 1. 保存 General 分类设置
            if (dirtySettings.size > 0) {
                var changes = Array.from(dirtySettings.entries()).map(([fullPath, value]) => ({
                    fullPath: fullPath,
                    value: value,
                }));

                var result = await tool.call<{ requiresRestart: boolean }>(
                    "saveConfiguration",
                    { changes: changes }
                );
                dirtySettings.clear();
                requiresRestart = result.requiresRestart;
            }

            // 2. 保存 Keymap 设置（先验证冲突，再保存）
            if (keymapDirty.size > 0) {
                var keymapSaved = await saveKeymapInternal();
                if (!keymapSaved) {
                    // keymap 有冲突，中断保存
                    updateSaveButton();
                    return;
                }
            }

            updateSaveButton();

            if (requiresRestart) {
                restartModal.hidden = false;
            } else {
                showToast(t("Plugin.Settings.Saved", "Settings saved successfully."), "success");
            }
        } catch (error) {
            showToast(error instanceof Error ? error.message : String(error), "error");
            updateSaveButton();
        }
    }

    // ── Keymap (Plugins 分类) ──

    type KeymapPlugin = {
        pluginId: string;
        name: string;
        defaultHotKey: string;
        currentHotKey: string;
        defaultKeywords: string[];
        currentKeywords: string[];
        isEnabled: boolean;
        isNodePlugin: boolean;
    };

    type KeymapConflict = {
        pluginId: string;
        field: string;
        value: string;
        conflictsWith: string;
    };

    var keymapPlugins: KeymapPlugin[] | null = null;
    // 记录用户在 keymap 页面的修改（相对于 getKeymap 返回的值）
    // hotKeyOverrides: pluginId → 新热键（null = 清除，undefined = 未改）
    var keymapDirty = new Map<string, { hotKey?: string | null; keywords?: string[]; isEnabled?: boolean }>();

    async function loadKeymap(): Promise<void> {
        settingsList.innerHTML = '<div class="loading">' + t("Plugin.Settings.Loading", "Loading...") + "</div>";
        try {
            var data = await tool.call<{ plugins: KeymapPlugin[] }>("getKeymap");
            keymapPlugins = data.plugins || [];
            keymapDirty.clear();
            renderKeymap();
        } catch (error) {
            settingsList.innerHTML = '<div class="loading">'
                + (error instanceof Error ? error.message : String(error))
                + "</div>";
        }
    }

    function renderKeymap(): void {
        if (!keymapPlugins) {
            void loadKeymap();
            return;
        }

        settingsList.innerHTML = "";

        var plugins = keymapPlugins;
        if (searchQuery) {
            plugins = plugins.filter(p => p.name.toLowerCase().includes(searchQuery));
        }

        if (plugins.length === 0) {
            settingsList.innerHTML = '<div class="loading">'
                + t("Plugin.Settings.NoResults", "No matching settings found")
                + "</div>";
            return;
        }

        // 表头
        var header = document.createElement("div");
        header.className = "keymap-header";
        header.innerHTML =
            '<div class="keymap-col-name">' + t("Plugin.Settings.Keymap.HeaderName", "Plugin") + '</div>'
            + '<div class="keymap-col-hotkey">' + t("Plugin.Settings.Keymap.HeaderHotkey", "Hotkey") + '</div>'
            + '<div class="keymap-col-keywords">' + t("Plugin.Settings.Keymap.HeaderKeywords", "Keywords") + '</div>'
            + '<div class="keymap-col-enabled">' + t("Plugin.Settings.Keymap.HeaderEnabled", "Enabled") + '</div>';
        settingsList.appendChild(header);

        for (var plugin of plugins) {
            settingsList.appendChild(renderKeymapRow(plugin));
        }
    }

    function renderKeymapRow(plugin: KeymapPlugin): HTMLElement {
        var dirty = keymapDirty.get(plugin.pluginId);

        var row = document.createElement("div");
        row.className = "keymap-row";
        row.dataset.pluginId = plugin.pluginId;

        // 插件名
        var nameDiv = document.createElement("div");
        nameDiv.className = "keymap-col-name";
        nameDiv.innerHTML = highlight(plugin.name, searchQuery);
        row.appendChild(nameDiv);

        // 热键录制器
        var hotKeyDiv = document.createElement("div");
        hotKeyDiv.className = "keymap-col-hotkey";
        var hotKeyBtn = document.createElement("button");
        hotKeyBtn.className = "hotkey-recorder";
        var hotKeyVal = dirty?.hotKey !== undefined ? dirty.hotKey : plugin.currentHotKey;
        hotKeyBtn.textContent = hotKeyVal || t("Plugin.Settings.Keymap.NoHotkey", "None");

        hotKeyBtn.addEventListener("click", () => {
            startHotKeyRecording(hotKeyBtn, plugin, (newVal) => {
                markKeymapDirty(plugin.pluginId, { hotKey: newVal });
                hotKeyBtn.textContent = newVal || t("Plugin.Settings.Keymap.NoHotkey", "None");
                updateSaveButton();
            });
        });

        // 清除热键按钮
        var clearHotKeyBtn = document.createElement("button");
        clearHotKeyBtn.className = "hotkey-clear";
        clearHotKeyBtn.textContent = "×";
        clearHotKeyBtn.title = t("Plugin.Settings.Keymap.ClearHotkey", "Clear hotkey");
        clearHotKeyBtn.addEventListener("click", () => {
            markKeymapDirty(plugin.pluginId, { hotKey: null });
            hotKeyBtn.textContent = t("Plugin.Settings.Keymap.NoHotkey", "None");
            updateSaveButton();
        });

        hotKeyDiv.appendChild(hotKeyBtn);
        hotKeyDiv.appendChild(clearHotKeyBtn);
        row.appendChild(hotKeyDiv);

        // 关键词输入（最多 3 个）
        var keywordsDiv = document.createElement("div");
        keywordsDiv.className = "keymap-col-keywords";
        var keywordsInput = document.createElement("input");
        keywordsInput.type = "text";
        keywordsInput.className = "setting-input";
        var kwVal = dirty?.keywords !== undefined ? dirty.keywords : plugin.currentKeywords;
        keywordsInput.value = (kwVal || []).join(", ");
        keywordsInput.placeholder = t("Plugin.Settings.Keymap.KeywordsPlaceholder", "Up to 3 keywords, comma separated");
        keywordsInput.addEventListener("change", () => {
            // 最多 3 个关键词
            var kws = keywordsInput.value.split(",").map(k => k.trim()).filter(k => k).slice(0, 3);
            keywordsInput.value = kws.join(", ");
            markKeymapDirty(plugin.pluginId, { keywords: kws });
            updateSaveButton();
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
            updateSaveButton();
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

    function startHotKeyRecording(
        btn: HTMLButtonElement,
        plugin: KeymapPlugin,
        onCapture: (hotKey: string | null) => void
    ): void {
        var originalText = btn.textContent;
        btn.textContent = t("Plugin.Settings.Keymap.Recording", "Press shortcut...");
        btn.classList.add("recording");

        // 录制期间暂停所有全局热键，避免系统拦截按键（如 Alt+C 打开了 translator）。
        void tool.call("suspendHotkeys");

        var handler = (e: KeyboardEvent) => {
            e.preventDefault();
            e.stopPropagation();

            if (e.key === "Escape") {
                cleanup();
                btn.textContent = originalText;
                btn.classList.remove("recording");
                return;
            }

            // 忽略单独的 modifier 按键
            if (["Control", "Shift", "Alt", "Meta"].includes(e.key)) {
                return;
            }

            var parts: string[] = [];
            if (e.ctrlKey) parts.push("Ctrl");
            if (e.shiftKey) parts.push("Shift");
            if (e.altKey) parts.push("Alt");
            if (e.metaKey) parts.push("Win");

            // 将 e.key 转为 WPF Key 枚举名（大致兼容）
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
            // 录制结束，恢复全局热键。
            void tool.call("resumeHotkeys");
        }

        document.addEventListener("keydown", handler, true);
    }

    function markKeymapDirty(pluginId: string, change: { hotKey?: string | null; keywords?: string[]; isEnabled?: boolean }): void {
        var existing = keymapDirty.get(pluginId) || {};
        if (change.hotKey !== undefined) existing.hotKey = change.hotKey;
        if (change.keywords !== undefined) existing.keywords = change.keywords;
        if (change.isEnabled !== undefined) existing.isEnabled = change.isEnabled;
        keymapDirty.set(pluginId, existing);
        updateSaveButton();
    }

    /// <summary>
    /// 保存 keymap 覆盖。返回 true 表示保存成功，false 表示有冲突未保存。
    /// 不自己管理 toast 或按钮状态——由统一的 saveSettings 负责。
    /// </summary>
    async function saveKeymapInternal(): Promise<boolean> {
        if (keymapDirty.size === 0 || !keymapPlugins) return true;

        var overrides: Record<string, { hotKey?: string | null; keywords?: string[]; isEnabled?: boolean }> = {};
        var hotKeysToValidate: Record<string, string | null> = {};
        var keywordsToValidate: Record<string, string[] | null> = {};

        for (var [pluginId, dirty] of keymapDirty) {
            overrides[pluginId] = dirty;
            if (dirty.hotKey !== undefined) {
                hotKeysToValidate[pluginId] = dirty.hotKey;
            }
            if (dirty.keywords !== undefined) {
                keywordsToValidate[pluginId] = dirty.keywords;
            }
        }

        // 验证冲突
        var validateResult = await tool.call<{ conflicts: KeymapConflict[] }>("validateKeymap", {
            hotKeys: hotKeysToValidate,
            keywords: keywordsToValidate,
        });

        // 清除旧冲突提示
        settingsList.querySelectorAll(".keymap-conflict").forEach(el => {
            (el as HTMLElement).hidden = true;
            el.textContent = "";
        });

        if (validateResult.conflicts && validateResult.conflicts.length > 0) {
            for (var c of validateResult.conflicts) {
                var row = settingsList.querySelector(`[data-plugin-id="${c.pluginId}"]`);
                if (row) {
                    var conflictEl = row.querySelector(".keymap-conflict") as HTMLElement;
                    conflictEl.hidden = false;
                    conflictEl.textContent = "⚠ " + c.field + " '" + c.value + "' "
                        + t("Plugin.Settings.Keymap.ConflictsWith", "conflicts with") + " " + c.conflictsWith;
                }
            }
            showToast(t("Plugin.Settings.Keymap.HasConflicts", "Conflicts detected. Resolve them before saving."), "error");
            return false;
        }

        // 无冲突，保存
        await tool.call("saveKeymap", { overrides: overrides });
        keymapDirty.clear();

        // 重新加载 keymap 数据以反映新状态
        await loadKeymap();
        return true;
    }

    // ── Toast ──
    var toastTimer: ReturnType<typeof setTimeout> | null = null;
    function showToast(message: string, type: string): void {
        toast.textContent = message;
        toast.className = "toast show " + type;
        toast.hidden = false;
        if (toastTimer) clearTimeout(toastTimer);
        toastTimer = setTimeout(() => {
            toast.classList.remove("show");
        }, 3000);
    }

    // ── 搜索 ──
    var searchTimer: ReturnType<typeof setTimeout> | null = null;
    searchInput.addEventListener("input", () => {
        if (searchTimer) clearTimeout(searchTimer);
        searchTimer = setTimeout(() => {
            searchQuery = searchInput.value.trim().toLowerCase();
            renderCategoryTree();
            if (currentCategoryKey === "Plugins") {
                renderKeymap();
            } else if (currentCategoryKey) {
                var cat = config ? findCategory(config.categories, currentCategoryKey) : null;
                if (cat) renderSettings(cat);
            }
        }, 150);
    });

    // ── 事件绑定 ──
    saveButton.addEventListener("click", saveSettings);
    restartCancel.addEventListener("click", () => { restartModal.hidden = true; });
    restartConfirm.addEventListener("click", async () => {
        restartModal.hidden = true;
        showToast(t("Plugin.Settings.Restarting", "Restarting..."), "success");
        try {
            await tool.call("restart");
        } catch {
            // 宿主重启过程中连接断开是正常的，忽略
        }
    });

    // ── 宿主事件 ──
    tool.subscribe(hostEvents.initialize, async () => {
        await loadConfiguration();
    });

    tool.subscribe(hostEvents.languageChanged, async () => {
        tool.i18n.apply(document);
        await loadConfiguration();
    });

    tool.subscribe("mytools.host.theme-changed", (payload: unknown) => {
        console.log("[settings] theme-changed received", payload);
        // CSS 变量自动更新，重渲染当前分类以刷新动态颜色
        if (currentCategoryKey === "Plugins") {
            renderKeymap();
        } else if (currentCategoryKey && config) {
            var cat = findCategory(config.categories, currentCategoryKey);
            if (cat) renderSettings(cat);
        }
    });

    tool.ready("settings");
})();
