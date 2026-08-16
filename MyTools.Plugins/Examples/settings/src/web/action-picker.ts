/**
 * Reusable input-action picker: choose a keyboard shortcut and/or a mouse button.
 * Host-specific hooks (suspend hotkeys, i18n) are injected so other plugins can reuse this later.
 */

export type InputActionKind = "hotkey" | "mouse";

export type InputActionValue = {
    kind: InputActionKind;
    hotKey?: string | null;
    mouseButton?: string | null;
};

export type InputActionPickerLabels = {
    title: string;
    tabKeyboard: string;
    tabMouse: string;
    recording: string;
    cancel: string;
    mouseBack: string;
    mouseForward: string;
};

export type OpenInputActionPickerOptions = {
    /** Show the keyboard-shortcut tab. Default true. */
    showKeyboard?: boolean;
    /** Show the mouse-button tab. Default true. */
    showMouse?: boolean;
    value?: InputActionValue | null;
    labels: InputActionPickerLabels;
    onSuspendHotkeys?: () => void;
    onResumeHotkeys?: () => void;
};

const STYLE_ID = "mt-action-picker-style";
const MOUSE_BACK = "XButton1";
const MOUSE_FORWARD = "XButton2";

var pickerOpen = false;

export function formatMouseButtonLabel(
    mouseButton: string | null | undefined,
    labels: Pick<InputActionPickerLabels, "mouseBack" | "mouseForward">
): string | null {
    if (mouseButton === MOUSE_FORWARD) return labels.mouseForward;
    if (mouseButton === MOUSE_BACK) return labels.mouseBack;
    return null;
}

export function openInputActionPicker(
    options: OpenInputActionPickerOptions
): Promise<InputActionValue | null> {
    var showKeyboard = options.showKeyboard !== false;
    var showMouse = options.showMouse !== false;
    if (!showKeyboard && !showMouse) {
        return Promise.resolve(null);
    }
    if (pickerOpen) {
        return Promise.resolve(null);
    }
    pickerOpen = true;
    ensureStyles();

    return new Promise((resolve) => {
        var labels = options.labels;
        var initial = options.value;
        var kind: InputActionKind = pickInitialKind(showKeyboard, showMouse, initial);
        var draftHotKey = initial?.kind === "hotkey" ? (initial.hotKey ?? null) : null;
        var draftMouse = initial?.kind === "mouse"
            ? (initial.mouseButton || MOUSE_BACK)
            : MOUSE_BACK;
        var keyHandler: ((e: KeyboardEvent) => void) | null = null;
        var settled = false;

        var overlay = document.createElement("div");
        overlay.className = "mt-action-picker-overlay";

        var dialog = document.createElement("div");
        dialog.className = "mt-action-picker";
        dialog.setAttribute("role", "dialog");
        dialog.setAttribute("aria-modal", "true");

        var title = document.createElement("div");
        title.className = "mt-action-picker-title";
        title.textContent = labels.title;
        dialog.appendChild(title);

        var tabs: HTMLElement | null = null;
        var keyboardTab: HTMLButtonElement | null = null;
        var mouseTab: HTMLButtonElement | null = null;
        if (showKeyboard && showMouse) {
            tabs = document.createElement("div");
            tabs.className = "mt-action-picker-tabs";
            keyboardTab = makeTab(labels.tabKeyboard);
            mouseTab = makeTab(labels.tabMouse);
            tabs.appendChild(keyboardTab);
            tabs.appendChild(mouseTab);
            dialog.appendChild(tabs);
        }

        var body = document.createElement("div");
        body.className = "mt-action-picker-body";
        dialog.appendChild(body);

        var footer = document.createElement("div");
        footer.className = "mt-action-picker-footer";
        var cancelBtn = document.createElement("button");
        cancelBtn.type = "button";
        cancelBtn.className = "mt-action-picker-btn";
        cancelBtn.textContent = labels.cancel;
        footer.appendChild(cancelBtn);
        dialog.appendChild(footer);

        overlay.appendChild(dialog);
        document.body.appendChild(overlay);

        function onDocumentKeyDown(e: KeyboardEvent): void {
            if (e.key === "Escape") finish(null);
        }
        document.addEventListener("keydown", onDocumentKeyDown);

        function makeTab(text: string): HTMLButtonElement {
            var btn = document.createElement("button");
            btn.type = "button";
            btn.className = "mt-action-picker-tab";
            btn.textContent = text;
            return btn;
        }

        function finish(result: InputActionValue | null): void {
            if (settled) return;
            settled = true;
            stopHotkeyCapture();
            overlay.remove();
            document.removeEventListener("keydown", onDocumentKeyDown);
            pickerOpen = false;
            resolve(result);
        }

        function applyHotkey(hotKey: string): void {
            finish({ kind: "hotkey", hotKey: hotKey, mouseButton: null });
        }

        function applyMouse(button: string): void {
            finish({ kind: "mouse", hotKey: null, mouseButton: button });
        }

        function startHotkeyCapture(): void {
            stopHotkeyCapture();
            options.onSuspendHotkeys?.();
            keyHandler = (e: KeyboardEvent) => {
                e.preventDefault();
                e.stopPropagation();
                if (e.key === "Escape") {
                    finish(null);
                    return;
                }
                if (["Control", "Shift", "Alt", "Meta"].includes(e.key)) {
                    return;
                }
                var parts: string[] = [];
                if (e.ctrlKey) parts.push("Ctrl");
                if (e.shiftKey) parts.push("Shift");
                if (e.altKey) parts.push("Alt");
                if (e.metaKey) parts.push("Win");
                var keyName = e.key;
                if (keyName === " ") keyName = "Space";
                else if (keyName.length === 1) keyName = keyName.toUpperCase();
                parts.push(keyName);
                applyHotkey(parts.join("+"));
            };
            document.addEventListener("keydown", keyHandler, true);
        }

        function stopHotkeyCapture(): void {
            if (keyHandler) {
                document.removeEventListener("keydown", keyHandler, true);
                keyHandler = null;
                options.onResumeHotkeys?.();
            }
        }

        function renderBody(): void {
            body.innerHTML = "";
            if (keyboardTab && mouseTab) {
                keyboardTab.classList.toggle("is-active", kind === "hotkey");
                mouseTab.classList.toggle("is-active", kind === "mouse");
            }

            if (kind === "hotkey") {
                var capture = document.createElement("div");
                capture.className = "mt-action-picker-capture";
                capture.textContent = draftHotKey || labels.recording;
                if (!draftHotKey) capture.classList.add("is-empty");
                body.appendChild(capture);
                startHotkeyCapture();
                return;
            }

            stopHotkeyCapture();
            var list = document.createElement("div");
            list.className = "mt-action-picker-mouse-list";
            list.appendChild(mouseChoice(MOUSE_BACK, labels.mouseBack));
            list.appendChild(mouseChoice(MOUSE_FORWARD, labels.mouseForward));
            body.appendChild(list);
        }

        function mouseChoice(value: string, label: string): HTMLButtonElement {
            var btn = document.createElement("button");
            btn.type = "button";
            btn.className = "mt-action-picker-choice";
            if (draftMouse === value) btn.classList.add("is-active");
            btn.textContent = label;
            btn.addEventListener("click", () => applyMouse(value));
            return btn;
        }

        keyboardTab?.addEventListener("click", () => {
            if (kind === "hotkey") return;
            kind = "hotkey";
            renderBody();
        });
        mouseTab?.addEventListener("click", () => {
            if (kind === "mouse") return;
            kind = "mouse";
            renderBody();
        });
        cancelBtn.addEventListener("click", () => finish(null));
        overlay.addEventListener("mousedown", (e) => {
            if (e.target === overlay) finish(null);
        });

        renderBody();
        cancelBtn.focus();
    });
}

function pickInitialKind(
    showKeyboard: boolean,
    showMouse: boolean,
    value?: InputActionValue | null
): InputActionKind {
    if (value?.kind === "mouse" && showMouse) return "mouse";
    if (value?.kind === "hotkey" && showKeyboard) return "hotkey";
    if (showKeyboard) return "hotkey";
    return "mouse";
}

function ensureStyles(): void {
    if (document.getElementById(STYLE_ID)) return;
    var style = document.createElement("style");
    style.id = STYLE_ID;
    style.textContent = `
.mt-action-picker-overlay {
    position: fixed;
    inset: 0;
    z-index: 250;
    background: rgba(0, 0, 0, 0.5);
    display: flex;
    align-items: center;
    justify-content: center;
}
.mt-action-picker {
    width: 280px;
    background: var(--mt-surface, #2a2a2a);
    border: 1px solid var(--mt-border, #3a3a3a);
    border-radius: 12px;
    box-shadow: 0 8px 24px var(--mt-shadow, rgba(0,0,0,0.4));
    padding: 16px;
    color: var(--mt-text, #e0e0e0);
}
.mt-action-picker-title {
    font-size: 15px;
    font-weight: 600;
    margin-bottom: 12px;
}
.mt-action-picker-tabs {
    display: flex;
    gap: 4px;
    margin-bottom: 12px;
    background: var(--mt-surface-bg, #1e1e1e);
    border-radius: 8px;
    padding: 3px;
}
.mt-action-picker-tab {
    flex: 1;
    border: none;
    background: transparent;
    color: var(--mt-text-muted, #b0b0b0);
    padding: 6px 8px;
    border-radius: 6px;
    cursor: pointer;
    font-size: 13px;
}
.mt-action-picker-tab.is-active {
    background: var(--mt-surface, #2a2a2a);
    color: var(--mt-text, #e0e0e0);
    box-shadow: 0 0 0 1px var(--mt-border, #3a3a3a);
}
.mt-action-picker-body {
    min-height: 72px;
    margin-bottom: 12px;
}
.mt-action-picker-capture {
    min-height: 72px;
    display: flex;
    align-items: center;
    justify-content: center;
    border: 1px dashed var(--mt-accent, #4a9eff);
    border-radius: 8px;
    font-size: 16px;
    text-align: center;
    padding: 12px;
}
.mt-action-picker-capture.is-empty {
    color: var(--mt-text-tertiary, #888);
    font-style: italic;
    font-size: 13px;
}
.mt-action-picker-mouse-list {
    display: flex;
    flex-direction: column;
    gap: 8px;
}
.mt-action-picker-choice {
    width: 100%;
    padding: 10px 12px;
    border: 1px solid var(--mt-border, #3a3a3a);
    border-radius: 8px;
    background: var(--mt-surface-bg, #1e1e1e);
    color: var(--mt-text, #e0e0e0);
    cursor: pointer;
    font-size: 13px;
    text-align: left;
}
.mt-action-picker-choice:hover,
.mt-action-picker-choice.is-active {
    border-color: var(--mt-accent, #4a9eff);
}
.mt-action-picker-footer {
    display: flex;
    justify-content: flex-end;
}
.mt-action-picker-btn {
    padding: 6px 14px;
    font-size: 13px;
    border: none;
    border-radius: 8px;
    cursor: pointer;
    background: var(--mt-surface-alt, #333);
    color: var(--mt-text-muted, #b0b0b0);
}
.mt-action-picker-btn:hover {
    color: var(--mt-text, #e0e0e0);
}
`;
    document.head.appendChild(style);
}
