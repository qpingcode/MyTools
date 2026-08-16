import { bus } from "./bus";
import type { Category, GestureConfig } from "./types";
import { highlight, t } from "./utils";
import * as common from "./common";
import { categorySelfMatches, renderSettingItem } from "./config-panel";
import { formatMouseButtonLabel, openInputActionPicker, type InputActionPickerLabels } from "./action-picker";

var gestureConfigs: GestureConfig[] | null = null;

// ── Direction helpers ──

var DIRECTION_ARROWS: Record<string, string> = {
    Up: "↑",
    Down: "↓",
    Left: "←",
    Right: "→"
};

function directionsToArrows(directions: string[]): string {
    return directions.map(d => DIRECTION_ARROWS[d] || d).join(" ");
}

var GESTURE_VISIBLE_DIRS = 4;

function formatGestureDisplay(directions: string[]): { visible: string; full: string; truncated: boolean } {
    var full = directionsToArrows(directions);
    if (directions.length <= GESTURE_VISIBLE_DIRS) {
        return { visible: full, full: full, truncated: false };
    }
    return {
        visible: directionsToArrows(directions.slice(0, GESTURE_VISIBLE_DIRS)) + " …",
        full: full,
        truncated: true
    };
}

function actionPickerLabels(): InputActionPickerLabels {
    return {
        title: t("Plugin.Settings.ActionPicker.Title", "Choose action"),
        tabKeyboard: t("Plugin.Settings.Gestures.TriggerHotkey", "Hotkey"),
        tabMouse: t("Plugin.Settings.Gestures.TriggerMouse", "Mouse Button"),
        recording: t("Plugin.Settings.Keymap.Recording", "Press shortcut..."),
        cancel: t("Plugin.Settings.Cancel", "Cancel"),
        mouseBack: t("Plugin.Settings.Gestures.MouseBack", "Back (XButton1)"),
        mouseForward: t("Plugin.Settings.Gestures.MouseForward", "Forward (XButton2)")
    };
}

function formatActionDisplay(gesture: GestureConfig): { text: string; empty: boolean; title: string } {
    var labels = actionPickerLabels();
    if (gesture.actionType === "mouse") {
        var mouseLabel = formatMouseButtonLabel(gesture.mouseButton, labels);
        if (mouseLabel) {
            var shortLabel = gesture.mouseButton === "XButton2"
                ? t("Plugin.Settings.Gestures.MouseForwardShort", "Forward")
                : t("Plugin.Settings.Gestures.MouseBackShort", "Back");
            return { text: shortLabel, empty: false, title: mouseLabel };
        }
    } else if (gesture.hotKey) {
        return { text: gesture.hotKey, empty: false, title: gesture.hotKey };
    }
    var none = t("Plugin.Settings.Gestures.NoAction", "Not set");
    return { text: none, empty: true, title: t("Plugin.Settings.Gestures.ClickToSetAction", "Click to set action") };
}

// ── Search checker (for category tree matching) ──

export function gesturesMatchesSearch(): boolean {
    if (!common.state.searchQuery || !gestureConfigs) return false;
    for (var g of gestureConfigs) {
        if (g.actionName.toLowerCase().includes(common.state.searchQuery)) return true;
        if (g.processNames.some(p => p.toLowerCase().includes(common.state.searchQuery))) return true;
    }
    return false;
}

// ── Load ──

export async function loadGestures(): Promise<void> {
    common.settingsList.innerHTML = '<div class="loading">' + t("Plugin.Settings.Loading", "Loading...") + "</div>";
    try {
        var data = await bus.call<{ gestures: GestureConfig[] }>("getGestures");
        gestureConfigs = data.gestures || [];
        common.state.gesturesDirty = false;
        renderGestures();
    } catch (error) {
        common.settingsList.innerHTML = '<div class="loading">'
            + (error instanceof Error ? error.message : String(error))
            + "</div>";
    }
}

function findGesturesCategory(): Category | null {
    if (!common.state.config) return null;
    for (var cat of common.state.config.categories) {
        if (cat.key === "Gestures") return cat;
    }
    return null;
}

// ── Conflict detection ──

/**
 * 判定两个手势是否冲突：相同方向 + 相同进程范围 + 两者都启用。
 * C# 匹配逻辑是先精确进程匹配再通配符，所以 Any 和特定进程不会互相干扰。
 */
function gesturesConflict(a: GestureConfig, b: GestureConfig): boolean {
    if (!a.isEnabled || !b.isEnabled) return false;
    if (a.directions.length === 0 || !directionsEqual(a.directions, b.directions)) return false;

    var aAny = a.processNames.length === 0;
    var bAny = b.processNames.length === 0;
    if (aAny && bAny) return true;       // 两个都是全局 → 冲突
    if (aAny || bAny) return false;      // 一个全局一个特定进程 → 不冲突（各不影响）
    // 两个都是特定进程，检查是否有共同进程名
    return a.processNames.some(p => b.processNames.includes(p));
}

function directionsEqual(a: string[], b: string[]): boolean {
    if (a.length !== b.length) return false;
    for (var i = 0; i < a.length; i++) {
        if (a[i] !== b[i]) return false;
    }
    return true;
}

/**
 * 计算每个手势的冲突描述（gestureId → 提示文字）。
 */
function computeConflictMap(configs: GestureConfig[]): Map<string, string> {
    var map = new Map<string, string>();
    for (var i = 0; i < configs.length; i++) {
        for (var j = i + 1; j < configs.length; j++) {
            var a = configs[i];
            var b = configs[j];
            if (!gesturesConflict(a, b)) continue;

            // 判定谁优先：特定进程 > 全局；同为全局/同进程时后注册的覆盖先注册的
            var aPriority = getPriority(a, b);
            appendConflict(map, a, b, aPriority === "a");
            appendConflict(map, b, a, aPriority === "b");
        }
    }
    return map;
}

function getPriority(a: GestureConfig, b: GestureConfig): "a" | "b" {
    var aAny = a.processNames.length === 0;
    var bAny = b.processNames.length === 0;
    // 特定进程优先于全局
    if (!aAny && bAny) return "a";
    if (aAny && !bAny) return "b";
    // 同级：后注册的（数组中靠后的）优先（字典覆盖）
    return "b";
}

function appendConflict(map: Map<string, string>, self: GestureConfig, other: GestureConfig, selfWins: boolean): void {
    var otherName = other.actionName || t("Plugin.Settings.Gestures.Unnamed", "Unnamed");
    var msg = selfWins
        ? t("Plugin.Settings.Gestures.ConflictWins",
            "Conflicts with \"{{name}}\". This one takes priority.",
            { name: otherName })
        : t("Plugin.Settings.Gestures.ConflictLose",
            "Conflicts with \"{{name}}\", which will take priority.",
            { name: otherName });

    var existing = map.get(self.id);
    map.set(self.id, existing ? existing + "\n" + msg : msg);
}

function headerCell(cls: string, label: string, tooltip: string): string {
    return '<div class="' + cls + '" title="' + escapeAttr(tooltip) + '">' + label + '</div>';
}

function escapeAttr(text: string): string {
    return text.replace(/"/g, "&quot;");
}

// ── Render ──

export function renderGestures(): void {
    if (!gestureConfigs) {
        void loadGestures();
        return;
    }

    common.settingsList.innerHTML = "";

    // 搜索时，如果该分类自身不匹配（只是因子分类匹配才出现在树上），
    // 不显示其内容，而是提示"没有匹配项"。
    var category = findGesturesCategory();
    if (common.state.searchQuery && category && !categorySelfMatches(category)) {
        common.settingsList.innerHTML = '<div class="loading">'
            + t("Plugin.Settings.NoResults", "No matching settings found")
            + "</div>";
        return;
    }

    // Render scalar settings that belong to the Gestures category (e.g. EnableGesture checkbox).
    // No filtering — show all. Search only highlights.
    if (category && category.settings.length > 0) {
        for (var setting of category.settings) {
            common.settingsList.appendChild(renderSettingItem(setting));
        }
    }

    if (gestureConfigs.length === 0) {
        common.settingsList.innerHTML = '<div class="loading">'
            + t("Plugin.Settings.Gestures.Empty", "No gestures configured")
            + "</div>";
    } else {
        // 计算冲突
        var conflictMap = computeConflictMap(gestureConfigs);

        // 表头
        var header = document.createElement("div");
        header.className = "gesture-header";
        header.innerHTML =
            headerCell("gesture-col-name",
                t("Plugin.Settings.Gestures.HeaderAction", "Action Name"),
                t("Plugin.Settings.Gestures.HeaderActionTip", "The name of this gesture action."))
            + headerCell("gesture-col-gesture",
                t("Plugin.Settings.Gestures.HeaderGesture", "Trigger Gesture"),
                t("Plugin.Settings.Gestures.HeaderGestureTip", "Click to re-record. Hold the right mouse button and draw."))
            + headerCell("gesture-col-process",
                t("Plugin.Settings.Gestures.HeaderProcess", "Target Process"),
                t("Plugin.Settings.Gestures.HeaderProcessTip", "Only trigger this gesture in the specified processes. Leave empty to apply to all processes."))
            + headerCell("gesture-col-trigger",
                t("Plugin.Settings.Gestures.HeaderTrigger", "Action"),
                t("Plugin.Settings.Gestures.HeaderTriggerTip", "The action to run. Click to choose a keyboard shortcut or mouse button."))
            + headerCell("gesture-col-enabled",
                t("Plugin.Settings.Gestures.HeaderEnabled", "Enabled"),
                t("Plugin.Settings.Gestures.HeaderEnabledTip", "Enable or disable this gesture."))
            + '<div class="gesture-col-actions"></div>';
        common.settingsList.appendChild(header);

        for (var gesture of gestureConfigs) {
            common.settingsList.appendChild(renderGestureRow(gesture, conflictMap));
        }
    }

    // Add button
    var addBar = document.createElement("div");
    addBar.className = "gesture-add-bar";
    var addBtn = document.createElement("button");
    addBtn.className = "btn btn-secondary gesture-add-btn";
    addBtn.textContent = "+ " + t("Plugin.Settings.Gestures.Add", "Add Gesture");
    addBtn.addEventListener("click", () => {
        if (!gestureConfigs) return;
        var newGesture: GestureConfig = {
            id: "",
            directions: [],
            actionName: "",
            actionType: "hotkey",
            hotKey: null,
            mouseButton: null,
            processNames: [],
            isEnabled: true
        };
        gestureConfigs.push(newGesture);
        common.state.gesturesDirty = true;
        renderGestures();
        common.updateSaveButton();
    });
    addBar.appendChild(addBtn);
    common.settingsList.appendChild(addBar);
}

function renderGestureRow(gesture: GestureConfig, conflictMap: Map<string, string>): HTMLElement {
    var row = document.createElement("div");
    row.className = "gesture-row";
    row.dataset.gestureId = gesture.id;

    // Conflict indicator (if any)
    var conflictMsg = conflictMap.get(gesture.id);

    // Action Name
    var nameDiv = document.createElement("div");
    nameDiv.className = "gesture-col-name";

    if (conflictMsg) {
        var warning = document.createElement("span");
        warning.className = "gesture-conflict-icon";
        warning.textContent = "⚠";
        warning.title = conflictMsg;
        nameDiv.appendChild(warning);
    }

    var nameInput = document.createElement("input");
    nameInput.type = "text";
    nameInput.className = "setting-input";
    nameInput.value = gesture.actionName;
    nameInput.placeholder = t("Plugin.Settings.Gestures.NamePlaceholder", "e.g. Close Tab");
    nameInput.addEventListener("input", () => {
        gesture.actionName = nameInput.value;
        common.state.gesturesDirty = true;
        common.updateSaveButton();
    });
    nameDiv.appendChild(nameInput);
    row.appendChild(nameDiv);

    // Gesture (click to re-record)
    var gestureDiv = document.createElement("div");
    gestureDiv.className = "gesture-col-gesture";
    var gestureBtn = document.createElement("button");
    gestureBtn.type = "button";
    gestureBtn.className = "gesture-display";
    if (gesture.directions.length === 0) {
        gestureBtn.classList.add("gesture-display-empty");
        gestureBtn.textContent = t("Plugin.Settings.Gestures.NoGesture", "Not set");
        gestureBtn.title = t("Plugin.Settings.Gestures.ClickToRecord", "Click to record");
    } else {
        var shown = formatGestureDisplay(gesture.directions);
        gestureBtn.innerHTML = highlight(shown.visible, common.state.searchQuery);
        gestureBtn.title = shown.truncated
            ? shown.full
            : t("Plugin.Settings.Gestures.ClickToRecord", "Click to record");
    }
    gestureBtn.addEventListener("click", () => {
        startGestureRecording((dirs) => {
            gesture.directions = dirs;
            common.state.gesturesDirty = true;
            renderGestures();
            common.updateSaveButton();
        });
    });
    gestureDiv.appendChild(gestureBtn);
    row.appendChild(gestureDiv);

    // Process filter
    var processDiv = document.createElement("div");
    processDiv.className = "gesture-col-process";
    var processInput = document.createElement("input");
    processInput.type = "text";
    processInput.className = "setting-input";
    processInput.value = (gesture.processNames || []).join(", ");
    processInput.placeholder = t("Plugin.Settings.Gestures.ProcessPlaceholder", "Any");
    processInput.title = t("Plugin.Settings.Gestures.ProcessHint", "Comma-separated process names");
    processInput.addEventListener("change", () => {
        var procs = processInput.value.split(",").map(p => p.trim()).filter(p => p);
        gesture.processNames = procs;
        processInput.value = procs.join(", ");
        common.state.gesturesDirty = true;
        renderGestures();
        common.updateSaveButton();
    });
    processDiv.appendChild(processInput);
    row.appendChild(processDiv);

    // Action (actual operation; click opens picker)
    var triggerDiv = document.createElement("div");
    triggerDiv.className = "gesture-col-trigger";
    var actionBtn = document.createElement("button");
    actionBtn.type = "button";
    actionBtn.className = "gesture-action-display";
    var actionShown = formatActionDisplay(gesture);
    actionBtn.textContent = actionShown.text;
    actionBtn.title = actionShown.title;
    if (actionShown.empty) actionBtn.classList.add("is-empty");
    actionBtn.addEventListener("click", () => {
        void openInputActionPicker({
            showKeyboard: true,
            showMouse: true,
            value: {
                kind: gesture.actionType === "mouse" ? "mouse" : "hotkey",
                hotKey: gesture.hotKey ?? null,
                mouseButton: gesture.mouseButton ?? null
            },
            labels: actionPickerLabels(),
            onSuspendHotkeys: () => { void bus.call("suspendHotkeys"); },
            onResumeHotkeys: () => { void bus.call("resumeHotkeys"); }
        }).then((result) => {
            if (!result) return;
            gesture.actionType = result.kind;
            gesture.hotKey = result.kind === "hotkey" ? (result.hotKey ?? null) : null;
            gesture.mouseButton = result.kind === "mouse" ? (result.mouseButton ?? null) : null;
            common.state.gesturesDirty = true;
            renderGestures();
            common.updateSaveButton();
        });
    });
    triggerDiv.appendChild(actionBtn);
    row.appendChild(triggerDiv);

    // Enabled checkbox
    var enabledDiv = document.createElement("div");
    enabledDiv.className = "gesture-col-enabled";
    var checkbox = document.createElement("input");
    checkbox.type = "checkbox";
    checkbox.className = "keymap-checkbox";
    checkbox.checked = gesture.isEnabled;
    checkbox.addEventListener("change", () => {
        gesture.isEnabled = checkbox.checked;
        common.state.gesturesDirty = true;
        renderGestures();
        common.updateSaveButton();
    });
    enabledDiv.appendChild(checkbox);
    row.appendChild(enabledDiv);

    // Delete button
    var actionsDiv = document.createElement("div");
    actionsDiv.className = "gesture-col-actions";
    var deleteBtn = document.createElement("button");
    deleteBtn.className = "hotkey-clear gesture-delete-btn";
    deleteBtn.textContent = "×";
    deleteBtn.title = t("Plugin.Settings.Gestures.Delete", "Delete");
    deleteBtn.addEventListener("click", () => {
        if (!gestureConfigs) return;
        var idx = gestureConfigs.indexOf(gesture);
        if (idx >= 0) {
            gestureConfigs.splice(idx, 1);
            common.state.gesturesDirty = true;
            renderGestures();
            common.updateSaveButton();
        }
    });
    actionsDiv.appendChild(deleteBtn);
    row.appendChild(actionsDiv);

    return row;
}

// ── Gesture recording (right-button drag inside the settings window) ──

var GESTURE_DISTANCE_THRESHOLD = 30;

function startGestureRecording(onCapture: (dirs: string[]) => void): void {
    // Pause global gesture detection so it doesn't interfere with recording.
    // The host ignores this if gesture detection was never started (disabled).
    void bus.call("suspendGestures");

    // Create a full-screen overlay to capture right-button drag
    var overlay = document.createElement("div");
    overlay.className = "gesture-record-overlay";

    var hint = document.createElement("div");
    hint.className = "gesture-record-hint";
    hint.textContent = t("Plugin.Settings.Gestures.Recording", "Hold right button to draw...");
    overlay.appendChild(hint);

    var trailDisplay = document.createElement("div");
    trailDisplay.className = "gesture-record-trail";
    overlay.appendChild(trailDisplay);

    var directions: string[] = [];
    var lastPoint: { x: number; y: number } | null = null;
    var isDrawing = false;

    function updateTrail(): void {
        trailDisplay.textContent = directionsToArrows(directions);
    }

    function detectDirection(currentX: number, currentY: number): void {
        if (!lastPoint) return;

        var deltaX = currentX - lastPoint.x;
        var deltaY = currentY - lastPoint.y;

        if (Math.abs(deltaX) < GESTURE_DISTANCE_THRESHOLD && Math.abs(deltaY) < GESTURE_DISTANCE_THRESHOLD) {
            return;
        }

        var direction: string;
        if (Math.abs(deltaX) > Math.abs(deltaY)) {
            direction = deltaX > 0 ? "Right" : "Left";
        } else {
            direction = deltaY > 0 ? "Down" : "Up";
        }

        // Collapse consecutive same directions
        if (directions.length > 0 && directions[directions.length - 1] === direction) {
            lastPoint = { x: currentX, y: currentY };
            return;
        }

        directions.push(direction);
        lastPoint = { x: currentX, y: currentY };
        updateTrail();
    }

    function onMouseDown(e: MouseEvent): void {
        if (e.button !== 2) return; // right button only
        e.preventDefault();
        isDrawing = true;
        directions = [];
        lastPoint = { x: e.clientX, y: e.clientY };
        updateTrail();
    }

    function onMouseMove(e: MouseEvent): void {
        if (!isDrawing) return;
        e.preventDefault();
        detectDirection(e.clientX, e.clientY);
    }

    function onMouseUp(e: MouseEvent): void {
        if (e.button !== 2 || !isDrawing) return;
        e.preventDefault();
        cleanup();
        onCapture(directions);
    }

    function onContextMenu(e: MouseEvent): void {
        e.preventDefault();
    }

    function onKeyDown(e: KeyboardEvent): void {
        if (e.key === "Escape") {
            cleanup();
        }
    }

    function cleanup(): void {
        overlay.removeEventListener("mousedown", onMouseDown);
        overlay.removeEventListener("mousemove", onMouseMove);
        overlay.removeEventListener("mouseup", onMouseUp);
        overlay.removeEventListener("contextmenu", onContextMenu);
        document.removeEventListener("keydown", onKeyDown);
        if (overlay.parentNode) {
            overlay.parentNode.removeChild(overlay);
        }
        // Resume global gesture detection. The host ignores this if gestures are disabled.
        void bus.call("resumeGestures");
    }

    overlay.addEventListener("mousedown", onMouseDown);
    overlay.addEventListener("mousemove", onMouseMove);
    overlay.addEventListener("mouseup", onMouseUp);
    overlay.addEventListener("contextmenu", onContextMenu);
    document.addEventListener("keydown", onKeyDown);

    document.body.appendChild(overlay);
    updateTrail();
}

// ── Save ──

export async function saveGesturesInternal(): Promise<boolean> {
    if (!common.state.gesturesDirty || !gestureConfigs) return true;

    await bus.call("saveGestures", { gestures: gestureConfigs });
    common.state.gesturesDirty = false;
    common.showToast(t("Plugin.Settings.Gestures.Saved", "Gestures saved and applied."), "success");
    return true;
}
