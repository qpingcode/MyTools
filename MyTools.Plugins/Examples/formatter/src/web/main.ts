import { createWebBusClient, HostEvents, renderHotkeyKeycaps } from "@qping/plugin-bus/web";
import type {
  MyToolsHostDetailActionPayload,
  MyToolsHostInitializePayload,
  MyToolsHostSearchPayload,
} from "@qping/plugin-bus/web";
import { basicSetup } from "codemirror";
import { defaultKeymap, historyKeymap } from "@codemirror/commands";
import { css } from "@codemirror/lang-css";
import { html } from "@codemirror/lang-html";
import { javascript } from "@codemirror/lang-javascript";
import { json } from "@codemirror/lang-json";
import { xml } from "@codemirror/lang-xml";
import { yaml } from "@codemirror/lang-yaml";
import { foldAll, foldKeymap, HighlightStyle, syntaxHighlighting, unfoldAll } from "@codemirror/language";
import { searchKeymap } from "@codemirror/search";
import { Compartment, EditorSelection, EditorState } from "@codemirror/state";
import { EditorView, keymap } from "@codemirror/view";
import { tags } from "@lezer/highlight";
import { isLanguageId, type LanguageId, type LanguageSelection } from "../shared/language";

type WorkerResponse =
  | { id: number; ok: true; detected: LanguageId | null }
  | { id: number; ok: true; formatted: string }
  | { id: number; ok: false; error: string };

type PendingRequest = {
  resolve: (response: WorkerResponse) => void;
  reject: (error: Error) => void;
};

(function () {
  const bus = createWebBusClient();
  const typeSelect = requiredElement<HTMLSelectElement>("typeSelect");
  const formatButton = requiredElement<HTMLButtonElement>("formatButton");
  const copyButton = requiredElement<HTMLButtonElement>("copyButton");
  const clearButton = requiredElement<HTMLButtonElement>("clearButton");
  const collapseAllButton = requiredElement<HTMLButtonElement>("collapseAllButton");
  const expandAllButton = requiredElement<HTMLButtonElement>("expandAllButton");
  const editorElement = requiredElement<HTMLElement>("editor");
  const messageElement = requiredElement<HTMLElement>("message");
  const autoOption = typeSelect.querySelector<HTMLOptionElement>('option[value="auto"]');
  const languageCompartment = new Compartment();
  const worker = new Worker("formatter.worker.js");
  const pendingRequests = new Map<number, PendingRequest>();
  let requestId = 0;
  let detectionTimer: number | undefined;
  let detectionGeneration = 0;
  let detectedLanguage: LanguageId | null = null;
  let autoLabel = "Auto";
  let formatting = false;

  const formatterHighlightStyle = HighlightStyle.define([
    { tag: [tags.keyword, tags.modifier, tags.operatorKeyword], color: "var(--formatter-keyword)" },
    { tag: [tags.string, tags.special(tags.string), tags.attributeValue], color: "var(--formatter-string)" },
    { tag: [tags.number, tags.bool, tags.null], color: "var(--formatter-number)" },
    { tag: [tags.comment, tags.meta], color: "var(--formatter-comment)", fontStyle: "italic" },
    { tag: [tags.tagName, tags.typeName, tags.className], color: "var(--formatter-type)" },
    { tag: [tags.propertyName, tags.attributeName], color: "var(--formatter-property)" },
    { tag: [tags.function(tags.variableName), tags.labelName], color: "var(--formatter-function)" },
    { tag: [tags.regexp, tags.escape], color: "var(--formatter-regexp)" },
    { tag: tags.invalid, color: "var(--mt-danger, #ef4444)", textDecoration: "underline wavy" },
  ]);

  const view = new EditorView({
    parent: editorElement,
    state: EditorState.create({
      doc: "",
      extensions: [
        basicSetup,
        syntaxHighlighting(formatterHighlightStyle),
        languageCompartment.of([]),
        keymap.of([
          {
            key: "Mod-Enter",
            run: () => {
              void formatDocument();
              return true;
            },
          },
          ...foldKeymap,
          ...searchKeymap,
          ...historyKeymap,
          ...defaultKeymap,
        ]),
        EditorView.lineWrapping,
        EditorView.updateListener.of(update => {
          if (!update.docChanged) return;
          const content = update.state.doc.toString();
          void bus.call("setContent", { content });
          clearMessage();
          if (currentSelection() === "auto") scheduleDetection(content);
        }),
        EditorView.theme({
          "&": { height: "100%", backgroundColor: "transparent" },
          ".cm-scroller": {
            fontFamily: '"Cascadia Code", "Cascadia Mono", Consolas, monospace',
            fontSize: "13px",
            lineHeight: "1.55",
          },
          ".cm-content": { padding: "12px 0", caretColor: "var(--mt-accent, #60a5fa)" },
          ".cm-gutters": {
            backgroundColor: "var(--mt-surface-alt, #171717)",
            color: "var(--mt-text-tertiary, #858585)",
            borderRight: "1px solid var(--mt-border, #303030)",
          },
          ".cm-activeLine, .cm-activeLineGutter": { backgroundColor: "var(--mt-surface-hover, rgba(127,127,127,.1))" },
          ".cm-selectionBackground, &.cm-focused .cm-selectionBackground, ::selection": {
            backgroundColor: "color-mix(in srgb, var(--mt-accent, #60a5fa) 32%, transparent)",
          },
          ".cm-foldGutter span": { color: "var(--mt-text-tertiary, #999)" },
          "&.cm-focused": { outline: "none" },
        }),
      ],
    }),
  });

  function requiredElement<T extends HTMLElement>(id: string): T {
    const element = document.getElementById(id);
    if (!element) throw new Error(`Missing #${id}`);
    return element as T;
  }

  function currentSelection(): LanguageSelection {
    const value = typeSelect.value;
    return value === "auto" || isLanguageId(value) ? value : "auto";
  }

  function languageExtension(language: LanguageId | null) {
    switch (language) {
      case "javascript": return javascript({ jsx: true });
      case "typescript": return javascript({ typescript: true, jsx: true });
      case "html": return html();
      case "css": return css();
      case "json": return json();
      case "yaml": return yaml();
      case "xml": return xml();
      default: return [];
    }
  }

  function displayName(language: LanguageId): string {
    return typeSelect.querySelector<HTMLOptionElement>(`option[value="${language}"]`)?.textContent?.trim() || language;
  }

  function updateAutoLabel(): void {
    if (!autoOption) return;
    autoOption.textContent = detectedLanguage ? `${autoLabel} (${displayName(detectedLanguage)})` : autoLabel;
  }

  function setEditorLanguage(language: LanguageId | null): void {
    view.dispatch({ effects: languageCompartment.reconfigure(languageExtension(language)) });
  }

  function applySelectedLanguage(): void {
    const selection = currentSelection();
    setEditorLanguage(selection === "auto" ? detectedLanguage : selection);
  }

  function postWorker(request: Record<string, unknown>): Promise<WorkerResponse> {
    const id = ++requestId;
    return new Promise((resolve, reject) => {
      pendingRequests.set(id, { resolve, reject });
      worker.postMessage({ ...request, id });
    });
  }

  worker.addEventListener("message", (event: MessageEvent<WorkerResponse>) => {
    const response = event.data;
    const pending = pendingRequests.get(response.id);
    if (!pending) return;
    pendingRequests.delete(response.id);
    pending.resolve(response);
  });

  worker.addEventListener("error", event => {
    const error = new Error(event.message || "Formatter worker failed.");
    pendingRequests.forEach(pending => pending.reject(error));
    pendingRequests.clear();
    showMessage(error.message);
  });

  function scheduleDetection(source: string): void {
    if (detectionTimer !== undefined) window.clearTimeout(detectionTimer);
    const generation = ++detectionGeneration;
    if (!source.trim()) {
      detectedLanguage = null;
      updateAutoLabel();
      setEditorLanguage(null);
      return;
    }
    detectionTimer = window.setTimeout(() => {
      void detect(source, generation);
    }, 250);
  }

  async function detect(source: string, generation: number): Promise<void> {
    try {
      const response = await postWorker({ operation: "detect", source });
      if (generation !== detectionGeneration || currentSelection() !== "auto") return;
      detectedLanguage = response.ok && "detected" in response ? response.detected : null;
      updateAutoLabel();
      setEditorLanguage(detectedLanguage);
    } catch {
      // Formatting still remains available through an explicit type selection.
    }
  }

  function setBusy(value: boolean): void {
    formatting = value;
    formatButton.disabled = value;
    typeSelect.disabled = value;
    document.body.classList.toggle("busy", value);
  }

  async function formatDocument(): Promise<void> {
    if (formatting) return;
    const source = view.state.doc.toString();
    if (!source.trim()) return;
    const selection = currentSelection();
    let language = selection === "auto" ? detectedLanguage : selection;
    if (selection === "auto" && !language) {
      setBusy(true);
      try {
        const detection = await postWorker({ operation: "detect", source });
        if (detection.ok && "detected" in detection) language = detection.detected;
        if (view.state.doc.toString() !== source) return;
        detectedLanguage = language;
        updateAutoLabel();
        setEditorLanguage(language);
      } catch {
        language = null;
      } finally {
        setBusy(false);
      }
    }
    if (!language) {
      showMessage(bus.i18n.t("Plugin.Formatter.Error.UnknownLanguage", {
        defaultValue: "Could not detect the input type. Select a language and try again.",
      }));
      return;
    }

    setBusy(true);
    clearMessage();
    try {
      const response = await postWorker({ operation: "format", source, language });
      if (!response.ok) throw new Error(response.error);
      if (view.state.doc.toString() !== source) return;
      if (!("formatted" in response) || response.formatted === source) return;

      const head = view.state.selection.main.head;
      const oldLine = view.state.doc.lineAt(head);
      const lineNumber = oldLine.number;
      const column = head - oldLine.from;
      const newDoc = EditorState.create({ doc: response.formatted }).doc;
      const newLine = newDoc.line(Math.min(lineNumber, newDoc.lines));
      const newHead = Math.min(newLine.from + column, newLine.to);
      const scrollTop = view.scrollDOM.scrollTop;

      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: response.formatted },
        selection: EditorSelection.cursor(newHead),
        scrollIntoView: false,
      });
      requestAnimationFrame(() => { view.scrollDOM.scrollTop = scrollTop; });
      view.focus();
    } catch (error) {
      showMessage(bus.i18n.t("Plugin.Formatter.Error.FormatFailed", {
        defaultValue: "Unable to format {{language}}: {{message}}",
        language: displayName(language),
        message: error instanceof Error ? error.message : String(error),
      }));
    } finally {
      setBusy(false);
    }
  }

  async function copyDocument(): Promise<void> {
    const content = view.state.doc.toString();
    if (!content) return;
    try {
      await navigator.clipboard.writeText(content);
    } catch {
      const textarea = document.createElement("textarea");
      textarea.value = content;
      textarea.style.position = "fixed";
      textarea.style.opacity = "0";
      document.body.appendChild(textarea);
      textarea.select();
      document.execCommand("copy");
      textarea.remove();
    }
    view.focus();
  }

  function replaceDocument(content: string): void {
    view.dispatch({ changes: { from: 0, to: view.state.doc.length, insert: content } });
    view.focus();
  }

  function applyActionDefinitions(actions: MyToolsHostInitializePayload["actions"]): void {
    const names = new Map((actions || []).map(action => [action.id, action.name || action.id]));
    const hotkeys = new Map((actions || []).map(action => [action.id, action.hotkey || ""]));
    document.querySelectorAll<HTMLElement>("[data-action-name]").forEach(element => {
      const id = element.dataset.actionName || "";
      element.textContent = names.get(id) || id;
    });
    document.querySelectorAll<HTMLElement>("[data-action-hotkey]").forEach(element => {
      const id = element.dataset.actionHotkey || "";
      renderHotkeyKeycaps(element, hotkeys.get(id) || "");
    });
    autoLabel = bus.i18n.t("Plugin.Formatter.Detail.Auto", { defaultValue: "Auto" });
    updateAutoLabel();
  }

  function showMessage(message: string): void {
    messageElement.textContent = message;
    messageElement.classList.remove("hidden");
  }

  function clearMessage(): void {
    messageElement.textContent = "";
    messageElement.classList.add("hidden");
  }

  typeSelect.addEventListener("change", () => {
    if (currentSelection() === "auto") scheduleDetection(view.state.doc.toString());
    else {
      detectionGeneration++;
      applySelectedLanguage();
    }
    view.focus();
  });
  formatButton.addEventListener("click", () => { void formatDocument(); });
  copyButton.addEventListener("click", () => { void copyDocument(); });
  clearButton.addEventListener("click", () => replaceDocument(""));
  collapseAllButton.addEventListener("click", () => { foldAll(view); view.focus(); });
  expandAllButton.addEventListener("click", () => { unfoldAll(view); view.focus(); });

  bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, payload => {
    applyActionDefinitions(payload.actions);
    const initialState = payload.initialState as { input?: string } | undefined;
    if (typeof initialState?.input === "string" && initialState.input) replaceDocument(initialState.input);
    else scheduleDetection(view.state.doc.toString());
    view.focus();
  });

  bus.on<MyToolsHostDetailActionPayload>(HostEvents.DetailAction, payload => {
    switch (payload.action) {
      case "format": void formatDocument(); break;
      case "clear": replaceDocument(""); break;
      case "collapse-all": foldAll(view); break;
      case "expand-all": unfoldAll(view); break;
    }
  });

  bus.on<MyToolsHostSearchPayload>(HostEvents.Search, payload => {
    if (typeof payload.query === "string" && payload.query) replaceDocument(payload.query);
  });

  bus.on(HostEvents.LanguageChanged, () => {
    autoLabel = bus.i18n.t("Plugin.Formatter.Detail.Auto", { defaultValue: "Auto" });
    updateAutoLabel();
  });
})();
