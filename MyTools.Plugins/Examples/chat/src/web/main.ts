import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
import type { MyToolsHostInitializePayload, MyToolsHostSearchPayload } from "@qping/plugin-bus/web";
import DOMPurify from "dompurify";
import { marked } from "marked";

(function () {
    type ChatTokenUsage = { inputTokens: number; outputTokens: number; totalTokens: number };
    type ChatMessage = { role: string; content: string; usage?: ChatTokenUsage | null };
    type ChatState = {
        sessionId: string;
        messages: ChatMessage[];
        selectedModel: string;
        streaming: boolean;
        cancelled: boolean;
        error: string;
    };
    type ChatStatus = {
        available: boolean;
        provider: string;
        selectedModel: string;
        models: string[];
        unavailableReason?: string;
    };

    const bus = createWebBusClient();
    const POLL_INTERVAL_MS = 120;
    const STICK_TO_BOTTOM_THRESHOLD = 24;
    const messagesElement = document.getElementById("messages") as HTMLElement;
    const promptInput = document.getElementById("promptInput") as HTMLTextAreaElement;
    const sendButton = document.getElementById("sendButton") as HTMLButtonElement;
    const stopButton = document.getElementById("stopButton") as HTMLButtonElement;
    const newChatButton = document.getElementById("newChatButton") as HTMLButtonElement;
    const modelSelect = document.getElementById("modelSelect") as HTMLSelectElement;
    const modelProvider = document.getElementById("modelProvider") as HTMLElement;
    let sessionId = "";
    let pollTimer: number | null = null;
    let currentState: ChatState | null = null;
    let userScrolledUp = false;

    function normalize(value: unknown): string {
        return typeof value === "string" ? value.trim() : "";
    }

    function newSessionId(): string {
        return crypto.randomUUID().replaceAll("-", "");
    }

    function renderMarkdown(element: HTMLElement, markdown: string): void {
        const html = marked.parse(markdown, { async: false, breaks: true, gfm: true });
        element.innerHTML = DOMPurify.sanitize(html, {
            USE_PROFILES: { html: true },
            FORBID_TAGS: ["style", "iframe", "object", "embed"]
        });
        element.querySelectorAll<HTMLAnchorElement>("a").forEach(function (link) {
            link.target = "_blank";
            link.rel = "noopener noreferrer";
        });
    }

    function renderState(state: ChatState): void {
        currentState = state;
        sessionId = state.sessionId || sessionId;
        if (state.selectedModel) modelSelect.value = state.selectedModel;
        const messages = Array.isArray(state.messages) ? state.messages : [];
        messagesElement.replaceChildren();
        messagesElement.className = messages.length === 0 ? "messages empty" : "messages";
        if (messages.length === 0) {
            messagesElement.textContent = state.error || bus.i18n.t("Plugin.Chat.Detail.Empty", {
                defaultValue: "Ask MyTools anything"
            });
        }

        messages.forEach(function (message) {
            const bubble = document.createElement("div");
            bubble.className = message.role === "user" ? "message user" : "message assistant markdown";
            if (message.role === "assistant") {
                const content = message.content || (state.streaming
                    ? bus.i18n.t("Plugin.Chat.Detail.Streaming", { defaultValue: "…" }) : "");
                renderMarkdown(bubble, content);
            } else {
                bubble.textContent = message.content;
            }
            messagesElement.appendChild(bubble);
            if (message.role === "assistant" && message.usage) {
                const usage = document.createElement("div");
                usage.className = "token-usage";
                usage.textContent = bus.i18n.t("Plugin.Chat.Detail.TokenUsage", {
                    defaultValue: "{{total}} tokens · input {{input}} · output {{output}}",
                    total: message.usage.totalTokens,
                    input: message.usage.inputTokens,
                    output: message.usage.outputTokens
                });
                messagesElement.appendChild(usage);
            }
        });

        if (state.error && messages.length > 0) {
            const error = document.createElement("div");
            error.className = "message error";
            error.textContent = state.error;
            messagesElement.appendChild(error);
        } else if (state.cancelled) {
            const cancelled = document.createElement("div");
            cancelled.className = "status-message";
            cancelled.textContent = bus.i18n.t("Plugin.Chat.Detail.Cancelled", { defaultValue: "Response stopped" });
            messagesElement.appendChild(cancelled);
        }

        const streaming = state.streaming === true;
        sendButton.hidden = streaming;
        stopButton.hidden = !streaming;
        modelSelect.disabled = streaming;
        newChatButton.disabled = streaming;
        scrollToBottom();
        if (streaming) startPolling(); else stopPolling();
    }

    async function poll(): Promise<void> {
        try {
            renderState(await bus.call<ChatState>("poll", { sessionId, model: modelSelect.value }));
        } catch (error) {
            if (currentState?.streaming) {
                renderState({ ...currentState, streaming: false, error: error instanceof Error ? error.message : String(error) });
            }
        }
    }

    function startPolling(): void {
        if (pollTimer !== null) return;
        pollTimer = window.setInterval(function () { void poll(); }, POLL_INTERVAL_MS);
    }

    function stopPolling(): void {
        if (pollTimer !== null) window.clearInterval(pollTimer);
        pollTimer = null;
    }

    function scrollToBottom(force = false): void {
        if (!force && userScrolledUp) return;
        messagesElement.scrollTop = messagesElement.scrollHeight;
    }

    function isNearBottom(): boolean {
        return messagesElement.scrollHeight - messagesElement.scrollTop - messagesElement.clientHeight
            <= STICK_TO_BOTTOM_THRESHOLD;
    }

    async function sendMessage(): Promise<void> {
        const message = normalize(promptInput.value);
        if (!message || currentState?.streaming) return;
        const model = modelSelect.value;
        promptInput.value = "";
        userScrolledUp = false;
        const optimistic: ChatState = currentState
            ? { ...currentState, messages: [...currentState.messages, { role: "user", content: message }, { role: "assistant", content: "" }], selectedModel: model, streaming: true, cancelled: false, error: "" }
            : { sessionId, messages: [{ role: "user", content: message }, { role: "assistant", content: "" }], selectedModel: model, streaming: true, cancelled: false, error: "" };
        renderState(optimistic);
        try {
            await bus.call("send", { sessionId, message, model });
            await poll();
        } catch (error) {
            renderState({ ...optimistic, streaming: false, error: error instanceof Error ? error.message : String(error) });
        }
    }

    async function startNewChat(): Promise<void> {
        const previousSessionId = sessionId;
        stopPolling();
        if (previousSessionId) await bus.call("clear", { sessionId: previousSessionId });
        sessionId = newSessionId();
        userScrolledUp = false;
        await poll();
        promptInput.focus();
    }

    async function initialize(): Promise<void> {
        try {
            const status = await bus.call<ChatStatus>("status");
            modelSelect.replaceChildren();
            status.models.forEach(function (model) {
                const option = document.createElement("option");
                option.value = model;
                option.textContent = model;
                modelSelect.appendChild(option);
            });
            modelSelect.value = status.selectedModel;
            modelProvider.textContent = status.provider;
            if (!status.available) {
                promptInput.disabled = true;
                sendButton.disabled = true;
                messagesElement.textContent = status.unavailableReason || "AI unavailable";
                return;
            }
            sessionId = newSessionId();
            await poll();
            promptInput.focus();
        } catch (error) {
            messagesElement.textContent = error instanceof Error ? error.message : String(error);
        }
    }

    messagesElement.addEventListener("scroll", function () {
        if (isNearBottom()) userScrolledUp = false;
        else if (currentState?.streaming) userScrolledUp = true;
    });
    sendButton.addEventListener("click", function () { void sendMessage(); });
    stopButton.addEventListener("click", async function () {
        stopButton.disabled = true;
        try { await bus.call("cancel", { sessionId }); await poll(); }
        finally { stopButton.disabled = false; }
    });
    newChatButton.addEventListener("click", function () { void startNewChat(); });
    promptInput.addEventListener("keydown", function (event) {
        if (event.key !== "Enter" || event.shiftKey) return;
        event.preventDefault();
        void sendMessage();
    });

    bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, function () { void initialize(); });
    bus.on<MyToolsHostSearchPayload>(HostEvents.Search, function (payload) {
        const query = normalize(payload.query);
        if (query && !normalize(promptInput.value)) promptInput.value = query;
    });
    bus.on(HostEvents.LanguageChanged, function () { if (currentState) renderState(currentState); });
})();
