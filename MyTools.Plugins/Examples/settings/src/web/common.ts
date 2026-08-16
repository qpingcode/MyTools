import type { Config } from "./types";

// ── Shared DOM references ──

export var searchInput = document.getElementById("searchInput") as HTMLInputElement;
export var categoryTree = document.getElementById("categoryTree") as HTMLUListElement;
export var noResults = document.getElementById("noResults") as HTMLElement;
export var categoryTitle = document.getElementById("categoryTitle") as HTMLElement;
export var categoryDescription = document.getElementById("categoryDescription") as HTMLElement;
export var settingsList = document.getElementById("settingsList") as HTMLElement;
export var saveButton = document.getElementById("saveButton") as HTMLButtonElement;
export var toast = document.getElementById("toast") as HTMLElement;
export var restartModal = document.getElementById("restartModal") as HTMLElement;
export var restartConfirm = document.getElementById("restartConfirm") as HTMLButtonElement;
export var restartCancel = document.getElementById("restartCancel") as HTMLButtonElement;

// ── Shared mutable state ──
// Wrapped in an object so that `import * as common` consumers can mutate fields.

export var state = {
    config: null as Config | null,
    currentCategoryKey: "",
    searchQuery: "",

    // scalar settings dirty map (fullPath → string value)
    dirtySettings: new Map<string, string>(),

    // keymap dirty state (pluginId → partial overrides)
    keymapDirty: new Map<string, {
        hotKey?: string | null;
        keywords?: string[];
        isEnabled?: boolean;
        includeInGlobalResults?: boolean;
    }>(),

    // gestures dirty flag (whole-list replacement strategy)
    gesturesDirty: false
};

// ── Toast ──

var toastTimer: ReturnType<typeof setTimeout> | null = null;

export function showToast(message: string, type: string): void {
    toast.textContent = message;
    toast.className = "toast show " + type;
    toast.hidden = false;
    if (toastTimer) clearTimeout(toastTimer);
    toastTimer = setTimeout(() => {
        toast.classList.remove("show");
    }, 3000);
}

// ── Save button state ──

export function updateSaveButton(): void {
    saveButton.disabled = state.dirtySettings.size === 0 && state.keymapDirty.size === 0 && !state.gesturesDirty;
}
