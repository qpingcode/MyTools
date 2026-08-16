import { bus } from "./bus";
import { t } from "./i18n";
import { store } from "./store";

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

const CaptureTimeoutMs = 24 * 60 * 60 * 1000;
var captureGeneration = 0;

export async function captureInputAction(
    options: CaptureInputActionOptions,
): Promise<InputActionValue | null> {
    const showKeyboard = options.showKeyboard !== false;
    const showMouse = options.showMouse === true;
    if (!showKeyboard && !showMouse) {
        return null;
    }

    store.capturing = true;
    const generation = ++captureGeneration;
    try {
        const result = await bus.call<CaptureInputActionResult>("captureInputAction", {
            showKeyboard,
            showMouse,
            kind: options.value?.kind === "mouse" && showMouse ? "mouse" : "hotkey",
            hotKey: options.value?.hotKey ?? null,
            mouseButton: options.value?.mouseButton ?? null,
            showReset: options.defaultHotKey !== undefined || options.defaultMouseButton !== undefined,
            defaultHotKey: options.defaultHotKey,
            defaultMouseButton: options.defaultMouseButton,
            excludePluginId: options.excludePluginId,
            excludeSearchHotKey: options.excludeSearchHotKey === true,
            excludeReservedHotKey: options.excludeReservedHotKey === true,
            currentSearchHotKey: options.currentSearchHotKey,
        }, CaptureTimeoutMs);
        if (!result || result.cancelled) {
            return null;
        }
        return {
            kind: result.kind === "mouse" ? "mouse" : "hotkey",
            hotKey: result.kind === "mouse" ? null : (result.hotKey ?? null),
            mouseButton: result.kind === "mouse" ? (result.mouseButton ?? null) : null,
        };
    } finally {
        if (generation === captureGeneration) {
            store.capturing = false;
        }
    }
}

export function capturingHint(): string {
    return t("Plugin.Settings.Capture.Waiting", "Waiting for input...");
}
