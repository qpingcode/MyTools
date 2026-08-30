import { createWebBusClient, HostEvents, renderHotkeyKeycaps } from "@qping/plugin-bus/web";
import type { MyToolsHostDetailActionPayload, MyToolsHostInitializePayload, MyToolsHostSearchPayload } from "@qping/plugin-bus/web";

type CodecMode = "encode" | "decode";
type EncodingAlgorithm = "base64" | "base64url" | "url" | "hex";
type HashAlgorithm = "md5" | "sha1" | "sha256" | "sha512";
type CodecAlgorithm = EncodingAlgorithm | HashAlgorithm;
type HashOutputFormat = "hex-lower" | "hex-upper" | "base64";
type TransformResponse = { ok: true; output: string; binary: boolean } | { ok: false; error: string };

(function () {
  const bus = createWebBusClient();
  const encodingAlgorithms: EncodingAlgorithm[] = ["base64", "base64url", "url", "hex"];
  const hashAlgorithms: HashAlgorithm[] = ["md5", "sha1", "sha256", "sha512"];
  const codecAlgorithms: CodecAlgorithm[] = [...encodingAlgorithms, ...hashAlgorithms];

  function isHashAlgorithm(value: string): value is HashAlgorithm {
    return hashAlgorithms.includes(value as HashAlgorithm);
  }

  let algorithmLabels: Record<string, string> = {};
  let groupLabels = { encoding: "Encoding", hash: "Hash" };
  let hashFormatLabels: Record<HashOutputFormat, string> = { "hex-lower": "Hex", "hex-upper": "HEX", base64: "Base64" };

  const inputText = element<HTMLTextAreaElement>("inputText");
  const outputText = element<HTMLTextAreaElement>("outputText");
  const algorithmSelect = element<HTMLSelectElement>("algorithmSelect");
  const hashFormatGroup = element<HTMLElement>("hashFormatGroup");
  const encodeButton = element<HTMLButtonElement>("encodeButton");
  const decodeButton = element<HTMLButtonElement>("decodeButton");
  const swapButton = element<HTMLButtonElement>("swapButton");
  const binaryBadge = element<HTMLElement>("binaryBadge");
  const textMessage = element<HTMLElement>("textMessage");
  let mode: CodecMode = "encode";
  let hashFormat: HashOutputFormat = "hex-lower";
  let binaryOutput = false;
  let conversionTimer: number | undefined;
  let conversionGeneration = 0;

  function element<T extends HTMLElement>(id: string): T {
    const value = document.getElementById(id);
    if (!value) throw new Error(`Missing #${id}`);
    return value as T;
  }

  function currentAlgorithm(): CodecAlgorithm {
    return (algorithmSelect.value || "base64") as CodecAlgorithm;
  }

  function hashing(): boolean {
    return isHashAlgorithm(currentAlgorithm());
  }

  function refreshLocalizedOptions(): void {
    algorithmLabels = {
      base64: "Base64",
      base64url: "Base64URL",
      url: bus.i18n.t("Plugin.EncoderDecoder.Algorithm.Url", { defaultValue: "URL Component" }),
      hex: "Hex",
      md5: "MD5",
      sha1: "SHA-1",
      sha256: "SHA-256",
      sha512: "SHA-512",
    };
    groupLabels = {
      encoding: bus.i18n.t("Plugin.EncoderDecoder.AlgorithmGroup.Encoding", { defaultValue: "Encoding" }),
      hash: bus.i18n.t("Plugin.EncoderDecoder.AlgorithmGroup.Hash", { defaultValue: "Hash" }),
    };
    hashFormatLabels = {
      "hex-lower": bus.i18n.t("Plugin.EncoderDecoder.Format.Hex", { defaultValue: "Hex" }),
      "hex-upper": bus.i18n.t("Plugin.EncoderDecoder.Format.HexUpperShort", { defaultValue: "HEX" }),
      base64: "Base64",
    };
    const previous = algorithmSelect.value;
    populateAlgorithms();
    if (codecAlgorithms.includes(previous as CodecAlgorithm)) algorithmSelect.value = previous;
    hashFormatGroup.querySelectorAll<HTMLButtonElement>("[data-format]").forEach(button => {
      button.textContent = hashFormatLabels[button.dataset.format as HashOutputFormat];
    });
    syncChrome();
  }

  function populateAlgorithms(): void {
    const encodingGroup = document.createElement("optgroup");
    encodingGroup.label = groupLabels.encoding;
    encodingGroup.append(...encodingAlgorithms.map(createAlgorithmOption));
    const hashGroup = document.createElement("optgroup");
    hashGroup.label = groupLabels.hash;
    hashGroup.append(...hashAlgorithms.map(createAlgorithmOption));
    algorithmSelect.replaceChildren(encodingGroup, hashGroup);
    algorithmSelect.value = "base64";
  }

  function createAlgorithmOption(value: CodecAlgorithm): HTMLOptionElement {
    const option = document.createElement("option");
    option.value = value;
    option.textContent = algorithmLabels[value];
    return option;
  }

  function setMode(nextMode: CodecMode, convert = true): void {
    if (nextMode === "decode" && hashing()) return;
    mode = nextMode;
    syncChrome();
    if (convert) void convertText();
  }

  function cycleAlgorithm(): void {
    const index = codecAlgorithms.indexOf(currentAlgorithm());
    algorithmSelect.value = codecAlgorithms[(index + 1) % codecAlgorithms.length];
    onAlgorithmChanged();
  }

  function onAlgorithmChanged(): void {
    if (hashing()) mode = "encode";
    syncChrome();
    void convertText();
  }

  function setHashFormat(next: HashOutputFormat): void {
    hashFormat = next;
    syncChrome();
    void convertText();
  }

  function syncChrome(): void {
    const hash = hashing();
    encodeButton.classList.toggle("primary", mode === "encode");
    decodeButton.classList.toggle("primary", mode === "decode");
    decodeButton.disabled = hash;
    hashFormatGroup.classList.toggle("hidden", !hash);
    hashFormatGroup.querySelectorAll<HTMLButtonElement>("[data-format]").forEach(button => {
      button.classList.toggle("active", button.dataset.format === hashFormat);
    });
    updateSwapState();
  }

  function updateSwapState(): void {
    swapButton.disabled = hashing() || binaryOutput || !outputText.value;
  }

  function scheduleConversion(): void {
    if (conversionTimer !== undefined) window.clearTimeout(conversionTimer);
    conversionTimer = window.setTimeout(() => { void convertText(); }, 180);
  }

  async function convertText(): Promise<void> {
    const generation = ++conversionGeneration;
    clearMessage(textMessage);
    if (!inputText.value) {
      setOutput("", false);
      return;
    }
    try {
      const response = await bus.call<TransformResponse>("transform", {
        input: inputText.value,
        mode: hashing() ? "encode" : mode,
        algorithm: currentAlgorithm(),
        hashFormat,
      });
      if (generation !== conversionGeneration) return;
      if (!response.ok) {
        setOutput("", false);
        showMessage(textMessage, conversionError(response.error));
        return;
      }
      setOutput(response.output, response.binary);
    } catch (error) {
      if (generation !== conversionGeneration) return;
      setOutput("", false);
      showMessage(textMessage, error);
    }
  }

  function conversionError(code: string): string {
    switch (code) {
      case "invalid-base64-characters": return bus.i18n.t("Plugin.EncoderDecoder.Error.Base64Characters", { defaultValue: "The Base64 input contains invalid characters." });
      case "invalid-base64-padding": return bus.i18n.t("Plugin.EncoderDecoder.Error.Base64Padding", { defaultValue: "The Base64 input has invalid padding." });
      case "invalid-base64-length": return bus.i18n.t("Plugin.EncoderDecoder.Error.Base64Length", { defaultValue: "The Base64 input has an invalid length." });
      case "invalid-url-encoding": return bus.i18n.t("Plugin.EncoderDecoder.Error.Url", { defaultValue: "The URL-encoded input is invalid." });
      case "invalid-hex-length": return bus.i18n.t("Plugin.EncoderDecoder.Error.HexLength", { defaultValue: "Hex input must contain an even number of digits." });
      case "invalid-hex-characters": return bus.i18n.t("Plugin.EncoderDecoder.Error.HexCharacters", { defaultValue: "Hex input contains invalid characters." });
      case "hash-decode-unsupported": return bus.i18n.t("Plugin.EncoderDecoder.Error.HashDecode", { defaultValue: "Hash algorithms can only encode." });
      default: return bus.i18n.t("Plugin.EncoderDecoder.Error.Conversion", { defaultValue: "Unable to convert the input." });
    }
  }

  function setOutput(value: string, binary: boolean): void {
    outputText.value = value;
    binaryOutput = binary;
    binaryBadge.classList.toggle("hidden", !binary);
    updateSwapState();
    updateCopyText(value);
  }

  function swap(): void {
    if (hashing() || binaryOutput || !outputText.value) return;
    const previousInput = inputText.value;
    inputText.value = outputText.value;
    outputText.value = previousInput;
    setMode(mode === "encode" ? "decode" : "encode", false);
    void convertText();
    inputText.focus();
  }

  function clearText(): void {
    conversionGeneration++;
    inputText.value = "";
    setOutput("", false);
    clearMessage(textMessage);
    inputText.focus();
  }

  async function copy(value: string): Promise<void> {
    if (!value) return;
    try {
      await navigator.clipboard.writeText(value);
    } catch {
      const area = document.createElement("textarea");
      area.value = value;
      area.style.position = "fixed";
      area.style.opacity = "0";
      document.body.appendChild(area);
      area.select();
      document.execCommand("copy");
      area.remove();
    }
  }

  function updateCopyText(value: string): void {
    void bus.call("setCopyText", { text: value }).catch(() => undefined);
  }

  function showMessage(container: HTMLElement, error: unknown): void {
    container.textContent = error instanceof Error ? error.message : String(error);
    container.classList.remove("hidden");
  }

  function clearMessage(container: HTMLElement): void {
    container.textContent = "";
    container.classList.add("hidden");
  }

  function applyActions(actions: MyToolsHostInitializePayload["actions"]): void {
    const names = new Map((actions || []).map(action => [action.id, action.name || action.id]));
    const hotkeys = new Map((actions || []).map(action => [action.id, action.hotkey || ""]));
    document.querySelectorAll<HTMLElement>("[data-action-name]").forEach(item => { item.textContent = names.get(item.dataset.actionName || "") || item.textContent; });
    document.querySelectorAll<HTMLElement>("[data-action-hotkey]").forEach(item => renderHotkeyKeycaps(item, hotkeys.get(item.dataset.actionHotkey || "") || ""));
  }

  refreshLocalizedOptions();
  inputText.addEventListener("input", scheduleConversion);
  algorithmSelect.addEventListener("change", onAlgorithmChanged);
  encodeButton.addEventListener("click", () => setMode("encode"));
  decodeButton.addEventListener("click", () => setMode("decode"));
  swapButton.addEventListener("click", swap);
  element("copyButton").addEventListener("click", () => { void copy(outputText.value); });
  element("clearButton").addEventListener("click", clearText);
  hashFormatGroup.querySelectorAll<HTMLButtonElement>("[data-format]").forEach(button => {
    button.addEventListener("click", () => setHashFormat(button.dataset.format as HashOutputFormat));
  });

  bus.on<MyToolsHostInitializePayload>(HostEvents.Initialize, payload => {
    applyActions(payload.actions);
    refreshLocalizedOptions();
    const initialState = payload.initialState as { input?: unknown } | undefined;
    if (typeof initialState?.input === "string") inputText.value = initialState.input;
    if (inputText.value) void convertText();
    inputText.focus();
  });
  bus.on<MyToolsHostSearchPayload>(HostEvents.Search, payload => {
    if (typeof payload.query === "string" && payload.query) {
      inputText.value = payload.query;
      void convertText();
    }
  });
  bus.on<MyToolsHostDetailActionPayload>(HostEvents.DetailAction, payload => {
    switch (payload.action) {
      case "encode": setMode("encode"); break;
      case "decode": setMode("decode"); break;
      case "swap": swap(); break;
      case "clear": clearText(); break;
      case "cycle-algorithm": cycleAlgorithm(); break;
    }
  });
  bus.on(HostEvents.LanguageChanged, () => {
    refreshLocalizedOptions();
  });
})();
