import { bus } from "./bus";
import { t } from "./utils";

export type InputActionKind = "hotkey" | "mouse";

export type InputActionValue = {
    kind: InputActionKind;
    hotKey?: string | null;
    mouseButton?: string | null;
};

export type CaptureInputActionOptions = {
    showKeyboard?: boolean;
    showMouse?: boolean;
    value?: InputActionValue | null;
    defaultHotKey?: string;
    defaultMouseButton?: string;
    excludePluginId?: string;
    excludeSearchHotKey?: boolean;
    excludeReservedHotKey?: boolean;
    currentSearchHotKey?: string;
};

type CaptureInputActionResult = {
    cancelled?: boolean;
    kind?: string;
    hotKey?: string | null;
    mouseButton?: string | null;
};

/** Wait while the host capture window is open. */
const CaptureTimeoutMs = 24 * 60 * 60 * 1000;

var overlay: HTMLElement | null = null;
var captureGeneration = 0;

function ensureOverlay(): HTMLElement {
    if (overlay && overlay.isConnected) {
        return overlay;
    }

    overlay = document.createElement("div");
    overlay.className = "modal-overlay capture-loading-overlay";
    overlay.hidden = true;
    overlay.innerHTML =
        '<div class="modal capture-loading-modal">'
        + '<p class="modal-message" data-capture-loading-text></p>'
        + '<div class="capture-loading-bar"></div>'
        + "</div>";
    document.body.appendChild(overlay);
    return overlay;
}

function showLoading(): void {
    var el = ensureOverlay();
    var text = el.querySelector("[data-capture-loading-text]");
    if (text) {
        text.textContent = t("Plugin.Settings.Capture.Waiting", "Waiting for input...");
    }
    el.hidden = false;
}

function hideLoading(): void {
    if (overlay) {
        overlay.hidden = true;
    }
}

export async function captureInputAction(
    options: CaptureInputActionOptions
): Promise<InputActionValue | null> {
    var showKeyboard = options.showKeyboard !== false;
    var showMouse = options.showMouse === true;
    if (!showKeyboard && !showMouse) {
        return null;
    }

    showLoading();
    var generation = ++captureGeneration;
    try {
        var result = await bus.call<CaptureInputActionResult>("captureInputAction", {
            showKeyboard: showKeyboard,
            showMouse: showMouse,
            kind: options.value?.kind === "mouse" && showMouse ? "mouse" : "hotkey",
            hotKey: options.value?.hotKey ?? null,
            mouseButton: options.value?.mouseButton ?? null,
            showReset: options.defaultHotKey !== undefined || options.defaultMouseButton !== undefined,
            defaultHotKey: options.defaultHotKey,
            defaultMouseButton: options.defaultMouseButton,
            excludePluginId: options.excludePluginId,
            excludeSearchHotKey: options.excludeSearchHotKey === true,
            excludeReservedHotKey: options.excludeReservedHotKey === true,
            currentSearchHotKey: options.currentSearchHotKey
        }, CaptureTimeoutMs);
        if (!result || result.cancelled) {
            return null;
        }

        return {
            kind: result.kind === "mouse" ? "mouse" : "hotkey",
            hotKey: result.kind === "mouse" ? null : (result.hotKey ?? null),
            mouseButton: result.kind === "mouse" ? (result.mouseButton ?? null) : null
        };
    } finally {
        if (generation === captureGeneration) {
            hideLoading();
        }
    }
}
