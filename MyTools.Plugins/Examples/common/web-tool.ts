import { MyToolsEventSubjects } from "./events.js";
import { mytoolsI18n } from "./i18n.js";

type PendingCall = {
    resolve: (value: unknown) => void;
    reject: (reason?: unknown) => void;
    timeoutId: number;
};

type SubscriptionCallback<TSubject extends string = string> = (
    payload: MyToolsEventPayload<TSubject>,
    meta: MyToolsEventMeta<TSubject>
) => void;

var DEFAULT_TIMEOUT_MS = 30000;
var events: MyToolsEvents = MyToolsEventSubjects;
var nextRequestId = 1;
var pendingCalls = new Map<string, PendingCall>();
var subscriptions = new Map<string, Set<SubscriptionCallback>>();

function hasWebView() {
    return !!(window.chrome && window.chrome.webview);
}

function post(message: unknown): void {
    if (hasWebView()) {
        window.chrome.webview.postMessage(message);
    }
}

function createRequestId() {
    return Date.now().toString(36) + "-" + (nextRequestId++).toString(36);
}

function call<T = unknown>(action: string, params?: unknown, options?: { timeout?: number }): Promise<T> {
    if (!action || typeof action !== "string") {
        return Promise.reject(new Error("tool.call requires an action name."));
    }

    var timeoutMs = options && Number.isFinite(options.timeout)
        ? options.timeout
        : DEFAULT_TIMEOUT_MS;
    var requestId = createRequestId();

    return new Promise<T>(function (resolve, reject) {
        var timeoutId = window.setTimeout(function () {
            if (!pendingCalls.delete(requestId)) {
                return;
            }

            reject(new Error("Tool call timed out: " + action));
        }, timeoutMs);

        pendingCalls.set(requestId, {
            resolve: resolve as (value: unknown) => void,
            reject: reject,
            timeoutId: timeoutId
        });

        post({
            type: "tool-call",
            requestId: requestId,
            action: action,
            payload: params ?? {}
        });
    });
}

function subscribe<TSubject extends string>(
    subjectId: TSubject,
    callback: SubscriptionCallback<TSubject>
): () => void {
    if (!subjectId || typeof subjectId !== "string") {
        throw new Error("tool.subscribe requires a subject id.");
    }

    if (typeof callback !== "function") {
        throw new Error("tool.subscribe requires a callback.");
    }

    var callbacks = subscriptions.get(subjectId);
    if (!callbacks) {
        callbacks = new Set<SubscriptionCallback>();
        subscriptions.set(subjectId, callbacks);
        post({
            type: "tool-subscribe",
            subjectId: subjectId
        });
    }

    callbacks.add(callback as SubscriptionCallback);
    return function unsubscribe() {
        var currentCallbacks = subscriptions.get(subjectId);
        if (!currentCallbacks) {
            return;
        }

        currentCallbacks.delete(callback);
        if (currentCallbacks.size > 0) {
            return;
        }

        subscriptions.delete(subjectId);
        post({
            type: "tool-unsubscribe",
            subjectId: subjectId
        });
    };
}

function ready(pluginId?: string): void {
    post({
        type: "ready",
        payload: { pluginId: pluginId || "" }
    });
}

function handleResponse(message: Record<string, unknown>): void {
    var requestId = typeof message.requestId === "string" ? message.requestId : "";
    var pending = pendingCalls.get(requestId);
    if (!pending) {
        return;
    }

    pendingCalls.delete(requestId);
    window.clearTimeout(pending.timeoutId);
    if (message.ok === false) {
        var error = isRecord(message.error) && typeof message.error.message === "string"
            ? message.error.message
            : "Tool call failed.";
        pending.reject(new Error(error));
        return;
    }

    pending.resolve(message.payload);
}

function handleEvent(message: Record<string, unknown>): void {
    var subjectId = typeof message.subjectId === "string" ? message.subjectId : "";
    if (subjectId === events.host.initialize && isRecord(message.payload)) {
        mytoolsI18n.configure(message.payload);
        mytoolsI18n.apply();
    }
    var callbacks = subscriptions.get(subjectId);
    if (!callbacks) {
        return;
    }

    callbacks.forEach(function (callback) {
        callback(message.payload, {
            subjectId: subjectId
        });
    });
}

function dispatch(message: unknown): void {
    if (!isRecord(message)) {
        return;
    }

    if (message.type === "tool-response") {
        handleResponse(message);
        return;
    }

    if (message.type === "tool-event") {
        handleEvent(message);
        return;
    }

    if (message.type === "language-changed" && isRecord(message.payload)) {
        mytoolsI18n.configure(message.payload);
        mytoolsI18n.apply();
        handleEvent({
            type: "tool-event",
            subjectId: events.host.languageChanged,
            payload: message.payload
        });
        return;
    }
}

function isRecord(value: unknown): value is Record<string, unknown> {
    return typeof value === "object" && value !== null;
}

if (hasWebView()) {
    window.chrome.webview.addEventListener("message", function (event) {
        dispatch(event.data);
    });
}

export const tool: MyToolsTool = {
    call: call,
    subscribe: subscribe,
    events: events,
    ready: ready,
    i18n: mytoolsI18n
};
