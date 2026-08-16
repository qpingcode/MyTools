import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
import type { MyToolsHostInitializePayload, MyToolsHostSearchPayload } from "@qping/plugin-bus/web";

(function () {
    type ChatMessage = {
        role?: string;
        content?: string;
    };

    type ChatState = {
        status?: string;
        conversationId?: string;
        messages?: ChatMessage[];
        streaming?: boolean;
        error?: string;
    };

    const bus = createWebBusClient();
    var POLL_INTERVAL_MS = 120;
    var messagesElement = document.getElementById("messages") as HTMLElement;
    var promptInput = document.getElementById("promptInput") as HTMLTextAreaElement;
    var sendButton = document.getElementById("sendButton") as HTMLButtonElement;
    var newChatButton = document.getElementById("newChatButton") as HTMLButtonElement;
    var conversationId = "";
    var pollTimer: number | null = null;
    var currentState: ChatState = {};
    // 用户是否手动向上滚动离开了底部。离开后暂停自动滚动，
    // 直到用户重新滚回底部附近才恢复，避免回答过程中把视图拉走。
    var userScrolledUp = false;
    var STICK_TO_BOTTOM_THRESHOLD = 24;

    async function callState(action: string, data: Record<string, unknown> = {}) {
        try {
            renderState(await bus.detailCall<ChatState>(action, data || {}));
        } catch (error) {
            renderState({
                status: "error",
                conversationId: conversationId,
                messages: [],
                streaming: false,
                error: error instanceof Error ? error.message : String(error)
            });
        }
    }

    function normalize(value: unknown): string {
        return typeof value === "string" ? value.trim() : "";
    }

    function renderState(state: ChatState | null | undefined): void {
        var current = state || {};
        currentState = current;
        conversationId = current.conversationId || conversationId;
        var messages = Array.isArray(current.messages) ? current.messages : [];
        messagesElement.replaceChildren();
        messagesElement.className = messages.length === 0 ? "messages empty" : "messages";

        if (messages.length === 0) {
            messagesElement.textContent = current.error || bus.i18n.t("Plugin.DeepSeekChat.Detail.Empty", {
                defaultValue: "Ask DeepSeek anything"
            });
        }

        messages.forEach(function (message: ChatMessage) {
            var bubble = document.createElement("div");
            bubble.className = message.role === "user" ? "message user" : "message assistant";
            bubble.textContent = message.content || (message.role === "assistant" && current.streaming
                ? bus.i18n.t("Plugin.DeepSeekChat.Detail.Streaming", { defaultValue: "…" })
                : "");
            messagesElement.appendChild(bubble);
        });

        if (current.error && messages.length > 0) {
            var error = document.createElement("div");
            error.className = "message error";
            error.textContent = current.error;
            messagesElement.appendChild(error);
        }

        sendButton.disabled = current.streaming === true;
        promptInput.disabled = current.streaming === true;
        scrollToBottom();

        if (current.streaming === true) {
            startPolling();
        } else {
            stopPolling();
        }
    }

    function scrollToBottom(force = false): void {
        if (!force && userScrolledUp) {
            return;
        }
        messagesElement.scrollTop = messagesElement.scrollHeight;
    }

    function isNearBottom(): boolean {
        return messagesElement.scrollHeight - messagesElement.scrollTop - messagesElement.clientHeight
            <= STICK_TO_BOTTOM_THRESHOLD;
    }

    messagesElement.addEventListener("scroll", function () {
        var nearBottom = isNearBottom();
        if (nearBottom) {
            userScrolledUp = false;
        } else if (currentState.streaming === true) {
            // 仅在流式输出期间把「离开底部」视为用户主动上滚，
            // 避免内容初次撑高容器时被误判。
            userScrolledUp = true;
        }
    });

    function startPolling(): void {
        if (pollTimer) {
            return;
        }

        pollTimer = window.setInterval(function () {
            callState("poll", { conversationId: conversationId });
        }, POLL_INTERVAL_MS);
    }

    function stopPolling(): void {
        if (pollTimer !== null) {
            window.clearInterval(pollTimer);
        }
        pollTimer = null;
    }

    function sendMessage(): void {
        var text = normalize(promptInput.value);
        if (!text || sendButton.disabled) {
            return;
        }

        promptInput.value = "";
        // 用户主动发送新消息，回到底部跟随最新回答。
        userScrolledUp = false;
        callState("send", {
            conversationId: conversationId,
            text: text
        });
    }

    sendButton.addEventListener("click", sendMessage);
    newChatButton.addEventListener("click", function () {
        stopPolling();
        userScrolledUp = false;
        callState("newChat");
        promptInput.focus();
    });
    promptInput.addEventListener("keydown", function (event) {
        if (event.key !== "Enter" || event.shiftKey) {
            return;
        }

        event.preventDefault();
        sendMessage();
    });

    bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, function (payload) {
        renderState(payload.initialState || {});
    });
    bus.on<MyToolsHostSearchPayload>(HostEvents.Search, function (payload) {
        var query = normalize(payload.query);
        if (query && !normalize(promptInput.value)) {
            promptInput.value = query;
        }
    });
    bus.on(HostEvents.LanguageChanged, function () {
        renderState(currentState);
    });
})();
