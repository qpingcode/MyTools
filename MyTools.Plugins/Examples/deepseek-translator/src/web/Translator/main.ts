import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
import type { MyToolsHostInitializePayload, MyToolsHostKeyPayload, MyToolsHostSearchPayload } from "@qping/plugin-bus/web";

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
        input?: string;
        inputType?: string;
        cachedAt?: string;
        translation?: string;
        phonetic?: string;
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
    var sendMode = document.getElementById("sendMode") as HTMLElement;
    var historyButton = document.getElementById("historyButton") as HTMLButtonElement;
    var favoriteListButton = document.getElementById("favoriteListButton") as HTMLButtonElement;
    var historyBackButton = document.getElementById("historyBackButton") as HTMLButtonElement;
    var favoriteButton = document.getElementById("favoriteButton") as HTMLButtonElement;
    var debounceTimer: number | null = null;
    var loadingTimer: number | null = null;
    var loadingStartedAt = 0;
    var lastRequestedText = "";
    var currentState: TranslationState | null = null;
    var previousTranslationState: TranslationState | null = null;
    var favoritePendingText = "";
    var currentListMode = "";

    function normalize(value: unknown): string {
        return typeof value === "string" ? value.trim() : "";
    }

    function t(key: string, defaultValue: string, values: Record<string, unknown> = {}): string {
        return bus.i18n.t(key, { defaultValue: defaultValue, ...values });
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
        setHistoryMode(false);
        hideFavoriteButton();
        showLoadingStatus();
        translation.replaceChildren();
        translation.classList.remove("history");
        translation.classList.remove("pending");
        translation.textContent = normalize(text)
            ? t("Plugin.DeepSeekTranslator.Detail.PleaseWait", "Please wait")
            : t("Plugin.DeepSeekTranslator.Detail.Empty", "Translation appears here");
        translation.classList.toggle("empty", !normalize(text));
    }

    function setPendingEnter(text: unknown): void {
        setHistoryMode(false);
        stopLoadingTimer();
        hideSourceStatus();
        hideFavoriteButton();
        translation.replaceChildren();
        translation.classList.remove("history");
        translation.textContent = normalize(text)
            ? t("Plugin.DeepSeekTranslator.Detail.PressEnter", "Press Enter to translate")
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

    function setHistoryMode(enabled: boolean, activeMode = ""): void {
        currentListMode = enabled ? (activeMode || currentListMode) : "";
        historyBackButton.hidden = !enabled;
        historyButton.disabled = false;
        favoriteListButton.disabled = false;
        historyButton.classList.toggle("active", enabled && currentListMode === "history");
        favoriteListButton.classList.toggle("active", enabled && currentListMode === "favorites");
        historyButton.setAttribute("aria-pressed", enabled && currentListMode === "history" ? "true" : "false");
        favoriteListButton.setAttribute("aria-pressed", enabled && currentListMode === "favorites" ? "true" : "false");
    }

    function setResultTitle(text: unknown): void {
        resultTitle.textContent = normalize(text) || t("Plugin.DeepSeekTranslator.Detail.Translation", "Translation");
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
        favoriteButton.textContent = "☆";
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
        favoriteButton.title = current.isFavorite
            ? t("Plugin.DeepSeekTranslator.Detail.Favorite.Remove", "Remove from review")
            : t("Plugin.DeepSeekTranslator.Detail.Favorite.Save", "Save for review");
        favoriteButton.textContent = current.isFavorite ? "★" : "☆";
    }

    function showFavoriteLoading(current: TranslationState): void {
        favoritePendingText = normalize(current && current.input).toLowerCase();
        favoriteButton.hidden = false;
        favoriteButton.disabled = true;
        favoriteButton.setAttribute("aria-busy", "true");
        favoriteButton.className = current && current.isFavorite ? "star-button favorited loading" : "star-button loading";
        favoriteButton.title = current && current.isFavorite
            ? t("Plugin.DeepSeekTranslator.Detail.Favorite.Removing", "Removing from review")
            : t("Plugin.DeepSeekTranslator.Detail.Favorite.Saving", "Saving for review");
        favoriteButton.textContent = current && current.isFavorite ? "★" : "☆";
    }

    function clearFavoriteLoading() {
        favoritePendingText = "";
        favoriteButton.disabled = false;
        favoriteButton.removeAttribute("aria-busy");
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

    function renderHistory(current: TranslationState): void {
        setResultTitle(t("Plugin.DeepSeekTranslator.Detail.HistoryTitle", "History"));
        setHistoryMode(true, "history");
        stopLoadingTimer();
        hideSourceStatus();
        hideFavoriteButton();
        translation.replaceChildren();
        translation.className = "translation history";

        renderEntryList(current, t("Plugin.DeepSeekTranslator.Detail.NoHistory", "No translation history"));
    }

    function renderFavorites(current: TranslationState): void {
        setResultTitle(t("Plugin.DeepSeekTranslator.Detail.FavoritesTitle", "Favorites"));
        setHistoryMode(true, "favorites");
        stopLoadingTimer();
        hideSourceStatus();
        hideFavoriteButton();
        translation.replaceChildren();
        translation.className = "translation history";
        renderEntryList(current, t("Plugin.DeepSeekTranslator.Detail.NoFavorites", "No favorite items"));
    }

    function renderEntryList(current: TranslationState, emptyText: string): void {
        var entries = Array.isArray(current.entries) ? current.entries : [];
        if (entries.length === 0) {
            translation.classList.add("empty");
            translation.textContent = emptyText;
            return;
        }

        entries.forEach(function (entry: TranslationEntry) {
            var item = document.createElement("button");
            item.className = "history-item";
            if (entry.inputType === "sentence") {
                item.classList.add("sentence-item");
            }
            item.type = "button";
            item.title = t("Plugin.DeepSeekTranslator.Detail.TranslateAgain", "Translate again");
            item.addEventListener("click", function () {
                setInput(entry.input || "", false);
                translateNow();
            });

            var input = document.createElement("div");
            input.className = "history-input";
            input.textContent = entry.input || "";
            item.appendChild(input);

            var value = document.createElement("div");
            value.className = "history-translation";
            value.textContent = entry.translation || "";
            item.appendChild(value);

            var meta = document.createElement("div");
            meta.className = "history-meta";
            meta.textContent = [
                entry.inputType === "word"
                    ? t("Plugin.DeepSeekTranslator.Detail.InputType.Word", "Word")
                    : t("Plugin.DeepSeekTranslator.Detail.InputType.Text", "Text"),
                entry.phonetic || "",
                formatTime(entry.cachedAt)
            ].filter(Boolean).join(" · ");
            item.appendChild(meta);

            translation.appendChild(item);
        });
    }

    function updateState(state: TranslationState): void {
        var current = state || {};
        currentState = current;
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
            setHistoryMode(false);
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

        if (current.status === "history") {
            renderHistory(current);
            return;
        }

        if (current.status === "favorites") {
            renderFavorites(current);
            return;
        }

        if (current.status === "done") {
            setResultTitle(t("Plugin.DeepSeekTranslator.Detail.Translation", "Translation"));
            setHistoryMode(false);
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
        setHistoryMode(false);
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
        return sendMode.dataset.mode === "realtime" ? "realtime" : "enter";
    }

    function setSendMode(mode: unknown): void {
        var normalized = mode === "realtime" ? "realtime" : "enter";
        sendMode.dataset.mode = normalized;
        sendMode.textContent = normalized === "realtime"
            ? t("Plugin.DeepSeekTranslator.Detail.Mode.Realtime", "Real-time Mode")
            : t("Plugin.DeepSeekTranslator.Detail.Mode.Enter", "Enter Mode");
        sendMode.title = normalized === "realtime"
            ? t("Plugin.DeepSeekTranslator.Detail.Mode.RealtimeTip", "Real-time mode. Click to switch to enter mode.")
            : t("Plugin.DeepSeekTranslator.Detail.Mode.EnterTip", "Enter mode. Click to switch to real-time mode.");
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

    function returnFromList() {
        setHistoryMode(false);
        updateState(previousTranslationState || { status: "idle", input: sourceText.value });
    }

    setSendMode("enter");
    favoriteButton.addEventListener("click", function () {
        if (!currentState || favoriteButton.disabled) {
            return;
        }

        showFavoriteLoading(currentState);
        callState("favorite", { text: currentState.input, state: currentState });
    });

    sendMode.addEventListener("click", function () {
        var nextMode = getSendMode() === "realtime" ? "enter" : "realtime";
        setSendMode(nextMode);
        callState("setSendMode", {
            sendMode: nextMode,
            state: getStateForModeChange()
        });
        handleInputChanged();
    });

    historyButton.addEventListener("click", function () {
        if (debounceTimer !== null) {
            window.clearTimeout(debounceTimer);
        }
        if (currentListMode === "history") {
            returnFromList();
            return;
        }

        if (!currentListMode) {
            previousTranslationState = currentState;
        }
        setHistoryMode(true, "history");
        hideSourceStatus();
        hideFavoriteButton();
        translation.className = "translation empty";
        translation.textContent = t("Plugin.DeepSeekTranslator.Detail.LoadingHistory", "Loading history");
        callState("getHistory");
    });

    favoriteListButton.addEventListener("click", function () {
        if (debounceTimer !== null) {
            window.clearTimeout(debounceTimer);
        }
        if (currentListMode === "favorites") {
            returnFromList();
            return;
        }

        if (!currentListMode) {
            previousTranslationState = currentState;
        }
        setHistoryMode(true, "favorites");
        hideSourceStatus();
        hideFavoriteButton();
        translation.className = "translation empty";
        translation.textContent = t("Plugin.DeepSeekTranslator.Detail.LoadingFavorites", "Loading favorites");
        callState("getFavorites");
    });

    historyBackButton.addEventListener("click", function () {
        returnFromList();
    });

    sourceText.addEventListener("input", handleInputChanged);
    sourceText.addEventListener("keydown", function (event) {
        if (getSendMode() !== "enter" || event.key !== "Enter" || event.shiftKey) {
            return;
        }

        event.preventDefault();
        translateNow();
    }, true);

    bus.on<MyToolsHostSearchPayload>(HostEvents.Search, function (payload) {
        setInput(payload.query || "", true);
    });
    bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, function (payload) {
        setInput(payload.query || "", false);
        updateState(payload.initialState || {});
        if (normalize(payload.query || "")) {
            handleInputChanged();
        }
    });
    bus.on<MyToolsHostKeyPayload>(HostEvents.Key, function (payload) {
        if (payload.key === "Enter" && getSendMode() === "enter") {
            translateNow();
        }
    });
    bus.on(HostEvents.LanguageChanged, function () {
        setSendMode(getSendMode());
        updateState(currentState || { status: "idle", input: sourceText.value });
    });
})();
