import { createWebBusClient, HostEvents, renderHotkeyKeycaps } from "@qping/plugin-bus/web";
import type {
    MyToolsHostDetailActionPayload,
    MyToolsHostInitializePayload,
    MyToolsHostSearchPayload
} from "@qping/plugin-bus/web";

(function () {
    type TokenUsage = {
        totalTokens?: number;
        promptTokens?: number;
        completionTokens?: number;
        cachedPromptTokens?: number;
    };

    type TranslationDefinition = {
        meaning?: string;
        example?: string;
    };

    type TranslationEntry = {
        id?: string;
        input?: string;
        inputType?: string;
        cachedAt?: string;
        translation?: string;
        phonetic?: string;
        state?: TranslationState;
    };

    type TranslationState = {
        status?: string;
        input?: string;
        inputType?: string;
        translation?: string;
        phonetic?: string;
        definitions?: TranslationDefinition[];
        chineseTranslation?: string;
        isValidWord?: boolean;
        isFavorite?: boolean;
        fromCache?: boolean;
        tokenUsage?: TokenUsage | null;
        sendMode?: string;
        isExpanded?: boolean;
        error?: string;
        entries?: TranslationEntry[];
    };

    const bus = createWebBusClient();
    var REALTIME_DEBOUNCE_MS = 1500;
    var sourceText = document.getElementById("sourceText") as HTMLTextAreaElement;
    var translation = document.getElementById("translation") as HTMLElement;
    var resultTitle = document.getElementById("resultTitle") as HTMLElement;
    var sourceStatus = document.getElementById("sourceStatus") as HTMLElement;
    var sendMode = document.getElementById("sendMode") as HTMLSelectElement;
    var translateButton = document.getElementById("translateButton") as HTMLButtonElement;
    var historyButton = document.getElementById("historyButton") as HTMLButtonElement;
    var favoriteListButton = document.getElementById("favoriteListButton") as HTMLButtonElement;
    var favoriteButton = document.getElementById("favoriteButton") as HTMLButtonElement;
    var drawer = document.getElementById("drawer") as HTMLElement;
    var drawerTitle = document.getElementById("drawerTitle") as HTMLElement;
    var drawerList = document.getElementById("drawerList") as HTMLElement;
    var drawerCloseButton = document.getElementById("drawerCloseButton") as HTMLButtonElement;
    var debounceTimer: number | null = null;
    var loadingTimer: number | null = null;
    var loadingStartedAt = 0;
    var lastRequestedText = "";
    var currentState: TranslationState | null = null;
    var favoritePendingText = "";
    var currentDrawerMode = "";
    var drawerEntries: TranslationEntry[] = [];
    var selectedDrawerIndex = -1;
    var copiedTimer: number | null = null;
    var titleBeforeCopy: string | null = null;
    var actionHotkeys = new Map<string, string>();

    function normalize(value: unknown): string {
        return typeof value === "string" ? value.trim() : "";
    }

    function t(key: string, defaultValue: string, values: Record<string, unknown> = {}): string {
        return bus.i18n.t(key, { defaultValue: defaultValue, ...values });
    }

    function applyActionDefinitions(actions: MyToolsHostInitializePayload["actions"]): void {
        actionHotkeys = new Map((actions || []).map(action => [action.id, action.hotkey || ""]));
        var actionNames = new Map((actions || []).map(action => [action.id, action.name || action.id]));
        document.querySelectorAll<HTMLElement>("[data-action-name]").forEach(function (element) {
            var actionId = element.dataset.actionName || "";
            element.textContent = actionNames.get(actionId) || actionId;
        });
        document.querySelectorAll<HTMLElement>("[data-action-hotkey]").forEach(function (element) {
            var actionId = element.dataset.actionHotkey || "";
            var hotkey = actionHotkeys.get(actionId) || "";
            renderHotkeyKeycaps(element, hotkey);
        });
        if (currentState) {
            updateFavoriteButton(currentState);
        }
    }

    function withActionHotkey(label: string, actionId: string): string {
        var hotkey = actionHotkeys.get(actionId);
        return hotkey ? label + " (" + hotkey + ")" : label;
    }

    function keyboardEventHotkey(event: KeyboardEvent): string {
        if (event.metaKey || event.key === "Control" || event.key === "Alt" || event.key === "Shift") {
            return "";
        }

        var tokens: string[] = [];
        if (event.ctrlKey) tokens.push("Ctrl");
        if (event.altKey) tokens.push("Alt");
        if (event.shiftKey) tokens.push("Shift");
        var key = event.key === " " ? "Space" : event.key.length === 1 ? event.key.toUpperCase() : event.key;
        tokens.push(key);
        return tokens.join("+");
    }

    function matchesActionHotkey(event: KeyboardEvent, actionId: string): boolean {
        var hotkey = actionHotkeys.get(actionId);
        return !!hotkey && keyboardEventHotkey(event) === hotkey;
    }

    function setInput(text: unknown, requestTranslation: boolean): void {
        var normalized = typeof text === "string" ? text : "";
        if (sourceText.value !== normalized) {
            sourceText.value = normalized;
        }

        if (requestTranslation) {
            handleInputChanged();
        }
    }

    function setLoading(text: unknown): void {
        closeDrawer();
        hideFavoriteButton();
        showLoadingStatus();
        translation.replaceChildren();
        translation.classList.remove("pending");
        translation.textContent = normalize(text)
            ? t("Plugin.DeepSeekTranslator.Detail.PleaseWait", "Please wait")
            : t("Plugin.DeepSeekTranslator.Detail.Empty", "Translation appears here");
        translation.classList.toggle("empty", !normalize(text));
    }

    function setPendingEnter(text: unknown): void {
        closeDrawer();
        stopLoadingTimer();
        hideSourceStatus();
        hideFavoriteButton();
        translation.replaceChildren();
        translation.textContent = normalize(text)
            ? t("Plugin.DeepSeekTranslator.Detail.PressEnter", "Press Ctrl+Enter to translate")
            : t("Plugin.DeepSeekTranslator.Detail.Empty", "Translation appears here");
        translation.classList.toggle("empty", !normalize(text));
        translation.classList.toggle("pending", !!normalize(text));
    }

    function hideSourceStatus() {
        stopLoadingTimer();
        sourceStatus.hidden = true;
        sourceStatus.textContent = "";
        sourceStatus.title = "";
        sourceStatus.className = "source-status";
    }

    function updateDrawerButtons(): void {
        historyButton.classList.toggle("active", currentDrawerMode === "history");
        favoriteListButton.classList.toggle("active", currentDrawerMode === "favorites");
        historyButton.setAttribute("aria-pressed", currentDrawerMode === "history" ? "true" : "false");
        favoriteListButton.setAttribute("aria-pressed", currentDrawerMode === "favorites" ? "true" : "false");
    }

    function setResultTitle(text: unknown): void {
        cancelCopiedFlash();
        resultTitle.textContent = normalize(text) || t("Plugin.DeepSeekTranslator.Detail.Translation", "Translation");
    }

    // The text a user means by "the translation": the sentence translation, or for a word the
    // Chinese meaning, falling back to whatever the model returned as the translation.
    function resolveCopyText(): string {
        var current = currentState;
        if (!current || current.status !== "done") {
            return "";
        }

        if (current.inputType === "word") {
            return current.isValidWord === true
                ? normalize(current.chineseTranslation) || normalize(current.translation)
                : "";
        }

        return normalize(current.translation);
    }

    function copyTranslation(): void {
        var text = resolveCopyText();
        if (!text) {
            return;
        }

        void navigator.clipboard.writeText(text).then(showCopied);
    }

    function showCopied(): void {
        if (copiedTimer !== null) {
            window.clearTimeout(copiedTimer);
        } else {
            titleBeforeCopy = resultTitle.textContent;
        }

        resultTitle.textContent = t("Plugin.DeepSeekTranslator.Detail.Copied", "Copied");
        resultTitle.classList.add("copied");
        copiedTimer = window.setTimeout(function () {
            var restore = titleBeforeCopy;
            cancelCopiedFlash();
            setResultTitle(restore);
        }, 1200);
    }

    function cancelCopiedFlash(): void {
        if (copiedTimer !== null) {
            window.clearTimeout(copiedTimer);
            copiedTimer = null;
        }
        titleBeforeCopy = null;
        resultTitle.classList.remove("copied");
    }

    function showSourceStatus(current: TranslationState): void {
        stopLoadingTimer();
        sourceStatus.hidden = false;
        sourceStatus.textContent = current.fromCache
            ? t("Plugin.DeepSeekTranslator.Detail.Source.Cache", "From Cache")
            : formatApiStatus(current.tokenUsage);
        sourceStatus.title = current.fromCache ? "" : formatTokenUsageTitle(current.tokenUsage);
        sourceStatus.className = current.fromCache ? "source-status cache" : "source-status api";
    }

    function formatApiStatus(usage: TokenUsage | null | undefined): string {
        if (!usage || typeof usage.totalTokens !== "number") {
            return t("Plugin.DeepSeekTranslator.Detail.Source.Api", "From API");
        }

        return t("Plugin.DeepSeekTranslator.Detail.Source.ApiTokens", "From API · {{count}} tokens", {
            count: usage.totalTokens
        });
    }

    function formatTokenUsageTitle(usage: TokenUsage | null | undefined): string {
        if (!usage || typeof usage.totalTokens !== "number") {
            return "";
        }

        var parts = [t("Plugin.DeepSeekTranslator.Detail.Tokens.Total", "Total: {{count}}", { count: usage.totalTokens })];
        if (typeof usage.promptTokens === "number") {
            parts.push(t("Plugin.DeepSeekTranslator.Detail.Tokens.Input", "Input: {{count}}", { count: usage.promptTokens }));
        }
        if (typeof usage.completionTokens === "number") {
            parts.push(t("Plugin.DeepSeekTranslator.Detail.Tokens.Output", "Output: {{count}}", { count: usage.completionTokens }));
        }
        if (typeof usage.cachedPromptTokens === "number") {
            parts.push(t("Plugin.DeepSeekTranslator.Detail.Tokens.CachedInput", "Cached input: {{count}}", { count: usage.cachedPromptTokens }));
        }
        return parts.join(" · ");
    }

    function hideFavoriteButton() {
        favoritePendingText = "";
        favoriteButton.hidden = true;
        favoriteButton.disabled = false;
        favoriteButton.removeAttribute("aria-busy");
        favoriteButton.className = "star-button";
        favoriteButton.title = "";
    }

    function updateFavoriteButton(current: TranslationState): void {
        var canSaveWord = current.inputType === "word" && current.isValidWord === true;
        var canSaveSentence = current.inputType === "sentence" && !!normalize(current.translation);
        if (!canSaveWord && !canSaveSentence) {
            hideFavoriteButton();
            return;
        }

        favoriteButton.hidden = false;
        favoriteButton.disabled = false;
        favoriteButton.removeAttribute("aria-busy");
        favoriteButton.className = current.isFavorite ? "star-button favorited" : "star-button";
        favoriteButton.title = withActionHotkey(current.isFavorite
            ? t("Plugin.DeepSeekTranslator.Detail.Favorite.Remove", "Remove from favorites")
            : t("Plugin.DeepSeekTranslator.Detail.Favorite.Save", "Save to favorites"), "toggle-favorite");
        favoriteButton.setAttribute("aria-label", favoriteButton.title);
    }

    function showFavoriteLoading(current: TranslationState): void {
        favoritePendingText = normalize(current && current.input).toLowerCase();
        favoriteButton.hidden = false;
        favoriteButton.disabled = true;
        favoriteButton.setAttribute("aria-busy", "true");
        favoriteButton.className = current && current.isFavorite ? "star-button favorited loading" : "star-button loading";
        favoriteButton.title = current && current.isFavorite
            ? t("Plugin.DeepSeekTranslator.Detail.Favorite.Removing", "Removing from favorites")
            : t("Plugin.DeepSeekTranslator.Detail.Favorite.Saving", "Saving to favorites");
        favoriteButton.setAttribute("aria-label", favoriteButton.title);
    }

    function clearFavoriteLoading() {
        favoritePendingText = "";
        favoriteButton.disabled = false;
        favoriteButton.removeAttribute("aria-busy");
    }

    async function toggleFavorite(): Promise<void> {
        if (!currentState || favoriteButton.disabled) {
            return;
        }
        var stateBeforeToggle = currentState;
        showFavoriteLoading(stateBeforeToggle);
        var startedAt = Date.now();
        try {
            var state = await bus.call<TranslationState>("favorite", {
                text: stateBeforeToggle.input,
                state: stateBeforeToggle
            });
            var remaining = Math.max(0, 650 - (Date.now() - startedAt));
            window.setTimeout(function () {
                updateState(state);
            }, remaining);
        } catch (error) {
            updateState({
                ...stateBeforeToggle,
                status: "error",
                error: error instanceof Error ? error.message : String(error)
            });
        }
    }

    function showLoadingStatus() {
        loadingStartedAt = Date.now();
        sourceStatus.hidden = false;
        sourceStatus.className = "source-status loading";
        sourceStatus.replaceChildren();
        var dot = document.createElement("span");
        dot.className = "loading-dot";
        dot.setAttribute("aria-hidden", "true");
        var label = document.createElement("span");
        label.textContent = t("Plugin.DeepSeekTranslator.Detail.Loading", "Loading ");
        var elapsed = document.createElement("span");
        elapsed.className = "loading-elapsed";
        sourceStatus.append(dot, label, elapsed);
        updateLoadingStatus();
        if (loadingTimer !== null) {
            window.clearInterval(loadingTimer);
        }
        loadingTimer = window.setInterval(updateLoadingStatus, 100);
    }

    function updateLoadingStatus() {
        var elapsedSeconds = Math.max(0, (Date.now() - loadingStartedAt) / 1000);
        var elapsed = sourceStatus.querySelector(".loading-elapsed");
        if (elapsed) {
            elapsed.textContent = elapsedSeconds.toFixed(1) + "s";
        }
    }

    function stopLoadingTimer() {
        if (loadingTimer !== null) {
            window.clearInterval(loadingTimer);
        }
        loadingTimer = null;
    }

    function appendTextElement(parent: Element, className: string, text: unknown): void {
        if (!normalize(text)) {
            return;
        }

        var element = document.createElement("div");
        element.className = className;
        element.textContent = normalize(text);
        parent.appendChild(element);
    }

    function appendPhoneticRow(parent: Element, current: TranslationState): void {
        if (!window.DeepSeekTranslatorSpeech) {
            return;
        }

        window.DeepSeekTranslatorSpeech.appendPhoneticRow(parent, {
            phonetic: current.phonetic,
            word: current.input
        });
    }

    function renderWord(current: TranslationState): void {
        translation.replaceChildren();

        if (current.isValidWord !== true) {
            hideFavoriteButton();
            appendTextElement(translation, "invalid-word",
                t("Plugin.DeepSeekTranslator.Detail.InvalidWord", "This is not a valid English word."));
            translation.classList.remove("empty");
            return;
        }

        updateFavoriteButton(current);

        appendPhoneticRow(translation, current);

        var definitions = Array.isArray(current.definitions) ? current.definitions.slice(0, 3) : [];
        if (definitions.length === 0) {
            appendTextElement(translation, "definition", current.translation ||
                t("Plugin.DeepSeekTranslator.Detail.NoDefinition", "No English definition returned"));
        }

        definitions.forEach(function (definition: TranslationDefinition) {
            var item = document.createElement("div");
            item.className = "definition-item";
            appendTextElement(item, "definition", definition.meaning);
            appendTextElement(item, "example", definition.example);
            translation.appendChild(item);
        });

        var chinese = normalize(current.chineseTranslation || current.translation);
        if (chinese) {
            var button = document.createElement("button");
            button.className = "expand-button";
            button.type = "button";

            var chineseElement = document.createElement("div");
            chineseElement.className = "chinese-meaning";
            chineseElement.textContent = chinese;
            var isExpanded: boolean = current.isExpanded === true;
            chineseElement.hidden = !isExpanded;
            button.textContent = isExpanded
                ? t("Plugin.DeepSeekTranslator.Detail.Collapse", "Collapse")
                : t("Plugin.DeepSeekTranslator.Detail.Expand", "Expand");

            button.addEventListener("click", function () {
                var nextExpanded: boolean = chineseElement.hidden === true;
                chineseElement.hidden = !nextExpanded;
                button.textContent = nextExpanded
                    ? t("Plugin.DeepSeekTranslator.Detail.Collapse", "Collapse")
                    : t("Plugin.DeepSeekTranslator.Detail.Expand", "Expand");
                current.isExpanded = nextExpanded;
                currentState = current;
                callState("setExpanded", {
                    text: current.input,
                    isExpanded: nextExpanded,
                    state: current
                });
            });

            translation.appendChild(button);
            translation.appendChild(chineseElement);
        }

        translation.classList.remove("empty");
    }

    function formatTime(value: unknown): string {
        var date = new Date(typeof value === "string" || typeof value === "number" ? value : "");
        return Number.isNaN(date.getTime()) ? "" : date.toLocaleString();
    }

    function closeDrawer(): void {
        drawer.hidden = true;
        currentDrawerMode = "";
        drawerEntries = [];
        selectedDrawerIndex = -1;
        updateDrawerButtons();
    }

    async function openDrawer(mode: "history" | "favorites"): Promise<void> {
        if (currentDrawerMode === mode && !drawer.hidden) {
            closeDrawer();
            sourceText.focus();
            return;
        }

        currentDrawerMode = mode;
        drawer.hidden = false;
        drawerTitle.textContent = mode === "history"
            ? t("Plugin.DeepSeekTranslator.Detail.HistoryTitle", "History")
            : t("Plugin.DeepSeekTranslator.Detail.FavoritesTitle", "Favorites");
        drawerList.className = "drawer-list";
        drawerList.textContent = mode === "history"
            ? t("Plugin.DeepSeekTranslator.Detail.LoadingHistory", "Loading history")
            : t("Plugin.DeepSeekTranslator.Detail.LoadingFavorites", "Loading favorites");
        updateDrawerButtons();
        drawer.focus();

        try {
            var state = await bus.call<TranslationState>(mode === "history" ? "getHistory" : "getFavorites");
            if (currentDrawerMode === mode) {
                renderDrawer(state);
            }
        } catch (error) {
            drawerList.textContent = error instanceof Error ? error.message : String(error);
        }
    }

    function renderDrawer(state: TranslationState): void {
        drawerEntries = Array.isArray(state.entries) ? state.entries.slice(0, 100) : [];
        selectedDrawerIndex = drawerEntries.length > 0 ? 0 : -1;
        drawerList.replaceChildren();

        if (drawerEntries.length === 0) {
            var empty = document.createElement("div");
            empty.className = "drawer-empty";
            empty.textContent = currentDrawerMode === "history"
                ? t("Plugin.DeepSeekTranslator.Detail.NoHistory", "No translation history")
                : t("Plugin.DeepSeekTranslator.Detail.NoFavorites", "No favorite items");
            drawerList.appendChild(empty);
            return;
        }

        drawerEntries.forEach(function (entry: TranslationEntry, index: number) {
            var item = document.createElement("div");
            item.className = "history-item";
            item.setAttribute("role", "option");
            item.setAttribute("aria-selected", index === selectedDrawerIndex ? "true" : "false");
            item.classList.toggle("selected", index === selectedDrawerIndex);
            if (entry.inputType === "sentence") {
                item.classList.add("sentence-item");
            }

            var main = document.createElement("button");
            main.className = "drawer-item-main";
            main.type = "button";
            main.addEventListener("click", function () {
                selectDrawerIndex(index);
                activateDrawerEntry();
            });

            var input = document.createElement("div");
            input.className = "history-input";
            input.textContent = entry.input || "";
            main.appendChild(input);

            var value = document.createElement("div");
            value.className = "history-translation";
            value.textContent = entry.translation || "";
            main.appendChild(value);

            var meta = document.createElement("div");
            meta.className = "history-meta";
            meta.textContent = [
                entry.inputType === "word"
                    ? t("Plugin.DeepSeekTranslator.Detail.InputType.Word", "Word")
                    : t("Plugin.DeepSeekTranslator.Detail.InputType.Text", "Text"),
                entry.phonetic || "",
                formatTime(entry.cachedAt)
            ].filter(Boolean).join(" · ");
            main.appendChild(meta);

            var remove = document.createElement("button");
            remove.className = "drawer-delete";
            remove.type = "button";
            remove.textContent = "×";
            remove.title = t("Plugin.DeepSeekTranslator.Detail.Delete", "Delete");
            remove.setAttribute("aria-label", remove.title);
            remove.addEventListener("click", function (event) {
                event.stopPropagation();
                selectDrawerIndex(index);
                void deleteSelectedDrawerEntry();
            });

            item.append(main, remove);
            drawerList.appendChild(item);
        });
    }

    function selectDrawerIndex(index: number): void {
        if (drawerEntries.length === 0) {
            selectedDrawerIndex = -1;
            return;
        }
        selectedDrawerIndex = Math.max(0, Math.min(index, drawerEntries.length - 1));
        Array.from(drawerList.querySelectorAll<HTMLElement>(".history-item")).forEach(function (item, itemIndex) {
            var selected = itemIndex === selectedDrawerIndex;
            item.classList.toggle("selected", selected);
            item.setAttribute("aria-selected", selected ? "true" : "false");
            if (selected) {
                item.scrollIntoView({ block: "nearest" });
            }
        });
    }

    function activateDrawerEntry(): void {
        var entry = drawerEntries[selectedDrawerIndex];
        if (!entry) {
            return;
        }
        var restored = entry.state || {
            status: "done",
            input: entry.input,
            inputType: entry.inputType,
            translation: entry.translation,
            phonetic: entry.phonetic
        };
        closeDrawer();
        updateState(restored);
        sourceText.focus();
    }

    async function deleteSelectedDrawerEntry(): Promise<void> {
        var entry = drawerEntries[selectedDrawerIndex];
        var mode = currentDrawerMode;
        if (!entry || !mode) {
            return;
        }
        var previousIndex = selectedDrawerIndex;
        var state = await bus.call<TranslationState>(mode === "history" ? "deleteHistory" : "deleteFavorite", {
            id: entry.id
        });
        if (mode === "favorites" && currentState
            && normalize(currentState.input).toLowerCase() === normalize(entry.input).toLowerCase()) {
            currentState.isFavorite = false;
            updateFavoriteButton(currentState);
        }
        if (currentDrawerMode === mode) {
            renderDrawer(state);
            selectDrawerIndex(Math.min(previousIndex, drawerEntries.length - 1));
        }
    }

    function updateState(state: TranslationState): void {
        var current = state || {};
        currentState = current;
        void bus.call("setCopyText", { text: resolveCopyText() });
        clearFavoriteLoading();
        var text = normalize(current.input);

        if (current.sendMode) {
            setSendMode(current.sendMode);
        }

        if (sourceText.value !== text && text) {
            sourceText.value = text;
        }

        if (current.status === "error") {
            setResultTitle(t("Plugin.DeepSeekTranslator.Detail.Translation", "Translation"));
            closeDrawer();
            stopLoadingTimer();
            hideFavoriteButton();
            sourceStatus.hidden = false;
            sourceStatus.textContent = t("Plugin.DeepSeekTranslator.Detail.ErrorStatus", "Error");
            sourceStatus.className = "source-status error";
            translation.replaceChildren();
            translation.classList.remove("pending");
            translation.textContent = current.error || t("Plugin.DeepSeekTranslator.Detail.TranslationFailed", "Translation failed");
            translation.classList.remove("empty");
            return;
        }

        if (current.status === "done") {
            setResultTitle(t("Plugin.DeepSeekTranslator.Detail.Translation", "Translation"));
            showSourceStatus(current);
            translation.classList.remove("pending");
            translation.classList.remove("history");
            if (current.inputType === "word") {
                renderWord(current);
                return;
            }

            updateFavoriteButton(current);
            var output = current.translation || t("Plugin.DeepSeekTranslator.Detail.NoTranslation", "No translation returned");
            translation.replaceChildren();
            translation.textContent = output;
            translation.classList.remove("empty");
            return;
        }

        hideSourceStatus();
        setResultTitle(t("Plugin.DeepSeekTranslator.Detail.Translation", "Translation"));
        hideFavoriteButton();
        translation.replaceChildren();
        translation.classList.remove("history");
        translation.classList.remove("pending");
        translation.textContent = t("Plugin.DeepSeekTranslator.Detail.Empty", "Translation appears here");
        translation.classList.add("empty");
    }

    async function callState(action: string, data: { text?: string; [key: string]: unknown } = {}, options?: { timeout?: number }): Promise<void> {
        try {
            updateState(await bus.call(action, data || {}, options?.timeout));
        } catch (error) {
            var current = data as { text?: string };
            updateState({
                status: "error",
                input: current && current.text || sourceText.value,
                error: error instanceof Error ? error.message : String(error)
            });
        }
    }

    function sendTranslateAfterPaint(text: string): void {
        window.setTimeout(async function () {
            try {
                var state = await bus.call<{ input?: string }>("translate", { text: text }, 45000);
                if (normalize(state && state.input) !== normalize(lastRequestedText)) {
                    return;
                }

                updateState(state);
            } catch (error) {
                if (normalize(text) !== normalize(lastRequestedText)) {
                    return;
                }

                updateState({
                    status: "error",
                    input: text,
                    error: error.message
                });
            }
        }, 0);
    }

    function translateNow() {
        var text = normalize(sourceText.value);

        if (debounceTimer !== null) {
            window.clearTimeout(debounceTimer);
        }
        if (!text) {
            lastRequestedText = "";
            updateState({ status: "idle", input: "" });
            return;
        }

        lastRequestedText = text;
        setLoading(text);
        sendTranslateAfterPaint(text);
    }

    function scheduleTranslate() {
        var text = normalize(sourceText.value);

        if (debounceTimer !== null) {
            window.clearTimeout(debounceTimer);
        }
        if (!text) {
            lastRequestedText = "";
            updateState({ status: "idle", input: "" });
            return;
        }

        debounceTimer = window.setTimeout(function () {
            translateNow();
        }, REALTIME_DEBOUNCE_MS);
    }

    function getSendMode() {
        return sendMode.value === "realtime" ? "realtime" : "enter";
    }

    function setSendMode(mode: unknown): void {
        var normalized = mode === "realtime" ? "realtime" : "enter";
        sendMode.value = normalized;
        sendMode.title = normalized === "realtime"
            ? t("Plugin.DeepSeekTranslator.Detail.Mode.RealtimeTip", "Real-time mode")
            : t("Plugin.DeepSeekTranslator.Detail.Mode.EnterTip",
                "Manual mode. Press Ctrl+Enter to translate.");
    }

    function handleInputChanged() {
        if (getSendMode() === "realtime") {
            scheduleTranslate();
            return;
        }

        if (debounceTimer !== null) {
            window.clearTimeout(debounceTimer);
        }
        if (!normalize(sourceText.value)) {
            lastRequestedText = "";
        }
        setPendingEnter(sourceText.value);
    }

    function getStateForModeChange() {
        if (currentState && normalize(currentState.input) === normalize(sourceText.value)) {
            return currentState;
        }

        return {
            input: sourceText.value,
            status: "idle"
        };
    }

    setSendMode("enter");
    favoriteButton.addEventListener("click", function () {
        void toggleFavorite();
    });

    sendMode.addEventListener("change", function () {
        var nextMode = getSendMode();
        callState("setSendMode", {
            sendMode: nextMode,
            state: getStateForModeChange()
        });
        handleInputChanged();
    });

    translateButton.addEventListener("click", translateNow);

    historyButton.addEventListener("click", function () {
        void openDrawer("history");
    });

    favoriteListButton.addEventListener("click", function () {
        void openDrawer("favorites");
    });

    drawerCloseButton.addEventListener("click", function () {
        closeDrawer();
        sourceText.focus();
    });
    drawer.addEventListener("keydown", function (event) {
        if (event.key === "ArrowDown") {
            event.preventDefault();
            selectDrawerIndex(selectedDrawerIndex + 1);
        } else if (event.key === "ArrowUp") {
            event.preventDefault();
            selectDrawerIndex(selectedDrawerIndex - 1);
        } else if (event.key === "Enter") {
            event.preventDefault();
            activateDrawerEntry();
        } else if (event.key === "Delete") {
            event.preventDefault();
            void deleteSelectedDrawerEntry();
        } else if (event.key === "Escape") {
            event.preventDefault();
            closeDrawer();
            sourceText.focus();
        }
    });

    sourceText.addEventListener("input", handleInputChanged);
    sourceText.addEventListener("keydown", function (event) {
        if (!matchesActionHotkey(event, "translate")) {
            return;
        }

        event.preventDefault();
        translateNow();
    });
    bus.on<MyToolsHostSearchPayload>(HostEvents.Search, function (payload) {
        setInput(payload.query || "", true);
    });
    bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, function (payload) {
        applyActionDefinitions(payload.actions);
        setInput(payload.query || "", false);
        updateState(payload.initialState || {});
        if (normalize(payload.query || "")) {
            handleInputChanged();
        }
    });
    bus.on<MyToolsHostDetailActionPayload>(HostEvents.DetailAction, function (payload) {
        if (payload.action === "translate") {
            translateNow();
        } else if (payload.action === "toggle-mode") {
            var nextMode = getSendMode() === "realtime" ? "enter" : "realtime";
            setSendMode(nextMode);
            callState("setSendMode", { sendMode: nextMode, state: getStateForModeChange() });
            handleInputChanged();
        } else if (payload.action === "history") {
            void openDrawer("history");
        } else if (payload.action === "favorites") {
            void openDrawer("favorites");
        } else if (payload.action === "toggle-favorite") {
            void toggleFavorite();
        }
    });
    bus.on(HostEvents.LanguageChanged, function () {
        setSendMode(getSendMode());
        updateState(currentState || { status: "idle", input: sourceText.value });
    });
})();
