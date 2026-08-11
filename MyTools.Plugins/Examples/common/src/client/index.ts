import { MyToolsEventSubjects } from "../shared/events.js";
import { mytoolsI18n } from "../shared/i18n.js";
import type {
  MyToolsEventMeta,
  MyToolsEventPayload,
  MyToolsEvents,
  MyToolsThemePayload,
  MyToolsTool,
} from "../shared/contracts.js";

/**
 * Minimal theme helper: applies the host-provided CSS custom properties to
 * <code>:root</code>. The bootstrap script injected before first frame already
 * does this for the initial render; this re-applies on <code>initialize</code>
 * and <code>theme-changed</code> so plugin JS can also read the active theme.
 */
const mytoolsTheme = {
  current: "dark",
  apply(payload: MyToolsThemePayload): void {
    if (typeof payload.theme === "string") {
      this.current = payload.theme;
    }
    const root = typeof document !== "undefined" ? document.documentElement : null;
    if (!root) {
      return;
    }
    if (typeof payload.theme === "string") {
      root.setAttribute("data-theme", payload.theme);
      root.style.colorScheme = payload.theme;
    }
    if (payload.themeTokens) {
      for (const [key, value] of Object.entries(payload.themeTokens)) {
        if (typeof value === "string") {
          root.style.setProperty(key, value);
        }
      }
    }
  }
};

type PendingCall = {
  resolve: (value: unknown) => void;
  reject: (reason?: unknown) => void;
  timeoutId: number;
};

type SubscriptionCallback<TSubject extends string = string> = (
  payload: MyToolsEventPayload<TSubject>,
  meta: MyToolsEventMeta<TSubject>
) => void;

const DEFAULT_TIMEOUT_MS = 30000;
const events: MyToolsEvents = MyToolsEventSubjects;
let nextRequestId = 1;
const pendingCalls = new Map<string, PendingCall>();
const subscriptions = new Map<string, Set<SubscriptionCallback>>();

function hasWebView(): boolean {
  return !!(window.chrome && window.chrome.webview);
}

function post(message: unknown): void {
  if (hasWebView()) {
    window.chrome!.webview!.postMessage(message);
  }
}

function createRequestId(): string {
  return `${Date.now().toString(36)}-${(nextRequestId++).toString(36)}`;
}

function call<T = unknown>(action: string, params?: unknown, options?: { timeout?: number }): Promise<T> {
  if (!action || typeof action !== "string") {
    return Promise.reject(new Error("tool.call requires an action name."));
  }

  const timeoutMs = options && Number.isFinite(options.timeout)
    ? options.timeout
    : DEFAULT_TIMEOUT_MS;
  const requestId = createRequestId();

  return new Promise<T>((resolve, reject) => {
    const timeoutId = window.setTimeout(() => {
      if (!pendingCalls.delete(requestId)) {
        return;
      }

      reject(new Error(`Tool call timed out: ${action}`));
    }, timeoutMs);

    pendingCalls.set(requestId, {
      resolve: resolve as (value: unknown) => void,
      reject,
      timeoutId
    });

    post({
      type: "tool-call",
      requestId,
      action,
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

  let callbacks = subscriptions.get(subjectId);
  if (!callbacks) {
    callbacks = new Set<SubscriptionCallback>();
    subscriptions.set(subjectId, callbacks);
    post({
      type: "tool-subscribe",
      subjectId
    });
  }

  callbacks.add(callback as SubscriptionCallback);
  return function unsubscribe() {
    const currentCallbacks = subscriptions.get(subjectId);
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
      subjectId
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
  const requestId = typeof message.requestId === "string" ? message.requestId : "";
  const pending = pendingCalls.get(requestId);
  if (!pending) {
    return;
  }

  pendingCalls.delete(requestId);
  window.clearTimeout(pending.timeoutId);
  if (message.ok === false) {
    const error = isRecord(message.error) && typeof message.error.message === "string"
      ? message.error.message
      : "Tool call failed.";
    pending.reject(new Error(error));
    return;
  }

  pending.resolve(message.payload);
}

function handleEvent(message: Record<string, unknown>): void {
  const subjectId = typeof message.subjectId === "string" ? message.subjectId : "";
  if (subjectId === events.host.initialize && isRecord(message.payload)) {
    mytoolsI18n.configure(message.payload);
    mytoolsI18n.apply();
    mytoolsTheme.apply(message.payload);
  }
  const callbacks = subscriptions.get(subjectId);
  if (!callbacks) {
    return;
  }

  callbacks.forEach((callback) => {
    callback(message.payload, {
      subjectId
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

  if (message.type === "theme-changed" && isRecord(message.payload)) {
    mytoolsTheme.apply(message.payload);
    handleEvent({
      type: "tool-event",
      subjectId: events.host.themeChanged,
      payload: message.payload
    });
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

if (hasWebView()) {
  window.chrome!.webview!.addEventListener("message", (event) => {
    dispatch(event.data);
  });
}

export const tool: MyToolsTool = {
  call,
  subscribe,
  events,
  ready,
  i18n: mytoolsI18n,
  theme: mytoolsTheme
};
