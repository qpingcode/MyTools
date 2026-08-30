import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";
import DOMPurify from "dompurify";
import { marked } from "marked";

type Job = { jobId: string; pluginId: string; sourcePath: string; distPath?: string; state: string; message: string };
type PluginRegistration = {
  pluginId: string;
  name: string;
  pluginType: "standard" | "custom-ui";
  sourcePath: string;
  distPath: string;
  aliases?: string[];
  hotKeys?: string[];
  testSteps?: string[];
  isDebugging?: boolean;
};
type PluginDetails = PluginRegistration & Partial<Job>;
type PluginValidation = { isValid: boolean; conflict?: "id" | "name" };
type HubPublishValidation = {
  isValid: boolean;
  pluginId: string;
  version: string;
  publishedVersion?: string;
  conflict?: "manifest" | "pluginId" | "version" | "account";
  message?: string;
};
type AiStatus = { available: boolean; provider: string; model: string; requiredEnvironmentVariable: string; unavailableReason?: string };
type AiProgressEvent = { sequence: number; kind: string; detail?: string };
type AiProgressBatch = { events: AiProgressEvent[] };
type PluginSetupResult = { installed: boolean; watchStarted: boolean; error?: string };
type AiChatResponse = { sessionId: string; reply: string; createdPlugin?: PluginRegistration & { isUpdate?: boolean }; setup?: PluginSetupResult; stopped?: boolean };
type InteractionQuestion = {
  id: string;
  prompt: string;
  options: string[];
  multiple: boolean;
  allowText: boolean;
  textPlaceholder: string;
};
type InteractionSpec = { id: string; title: string; questions: InteractionQuestion[] };
type InteractionAnswer = { questionId: string; prompt: string; values: string[]; text: string };
const AI_CHAT_TIMEOUT_MS = 21 * 60_000;
const INTERACTION_PATTERN = /```mytools-interaction\s*\r?\n([\s\S]*?)\r?\n```/i;
const newAiSessionId = () => crypto.randomUUID().replaceAll("-", "");

const bus = createWebBusClient();
const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
let currentPlugin: PluginDetails | null = null;
let plugins: PluginRegistration[] = [];
let aiSessionId = "";
let progressSequence = 0;
let progressTurn = 0;
let liveReply: HTMLDivElement | null = null;
let liveReplyMarkdown = "";
let streamedReplyReceived = false;
let activeReadGroup: {
  item: HTMLDivElement;
  label: HTMLElement;
  body: HTMLSpanElement;
  details: string[];
  count: number;
} | null = null;
let accumulatedCreationMs = 0;
let isAiWorking = false;
let aiStopRequested = false;
let formErrorKey = "";
let formErrorDefault = "";

function t(key: string, defaultValue: string) { return bus.i18n.t(key, { defaultValue }); }

function limitedText(value: unknown, maxLength: number) {
  return typeof value === "string" ? value.trim().slice(0, maxLength) : "";
}

function stableInteractionId(value: string) {
  let hash = 2166136261;
  for (let index = 0; index < value.length; index++) {
    hash ^= value.charCodeAt(index);
    hash = Math.imul(hash, 16777619);
  }
  return `interaction_${(hash >>> 0).toString(16)}`;
}

function structuredId(value: unknown, fallback: string) {
  const candidate = limitedText(value, 64);
  return /^[a-zA-Z0-9_.-]{1,64}$/.test(candidate) ? candidate : fallback;
}

function parseInteraction(markdown: string): { markdown: string; interaction: InteractionSpec | null } {
  const match = INTERACTION_PATTERN.exec(markdown);
  if (!match) return { markdown, interaction: null };
  try {
    const raw = JSON.parse(match[1]) as Record<string, unknown>;
    if (raw.version !== undefined && raw.version !== 1) return { markdown, interaction: null };
    if (!Array.isArray(raw.questions) || raw.questions.length === 0 || raw.questions.length > 12) {
      return { markdown, interaction: null };
    }
    const questions = raw.questions.map((value, index): InteractionQuestion | null => {
      if (!value || typeof value !== "object") return null;
      const question = value as Record<string, unknown>;
      const prompt = limitedText(question.prompt, 500);
      const options = Array.isArray(question.options)
        ? question.options.map(option => limitedText(option, 120)).filter(Boolean).slice(0, 12)
        : [];
      const allowText = question.allowText === true;
      if (!prompt || (options.length === 0 && !allowText)) return null;
      return {
        id: structuredId(question.id, `question_${index + 1}`),
        prompt,
        options,
        multiple: question.multiple === true,
        allowText,
        textPlaceholder: limitedText(question.textPlaceholder, 120)
      };
    });
    if (questions.some(question => question === null)) return { markdown, interaction: null };
    return {
      markdown: markdown.replace(match[0], "").trimEnd(),
      interaction: {
        id: structuredId(raw.id, stableInteractionId(match[1])),
        title: limitedText(raw.title, 160),
        questions: questions as InteractionQuestion[]
      }
    };
  } catch {
    return { markdown, interaction: null };
  }
}

function setFormError(key = "", defaultValue = "") {
  formErrorKey = key;
  formErrorDefault = defaultValue;
  $("formError").textContent = key ? t(key, defaultValue) : "";
}

function showStep(step: number) {
  for (let i = 1; i <= 3; i++) $(`step${i}`).hidden = i !== step;
  document.querySelectorAll(".stepper span").forEach((item, index) => {
    item.classList.toggle("active", index < step);
    item.classList.toggle("complete", index < step - 1);
    if (index === step - 1) item.setAttribute("aria-current", "step"); else item.removeAttribute("aria-current");
  });
}

function toast(message: string, error = false) {
  const element = $("toast");
  element.textContent = message;
  element.className = error ? "show error-toast" : "show";
  window.setTimeout(() => element.className = "", 3000);
}

function scrollChatToBottom() {
  const history = $("chatHistory");
  history.scrollTop = history.scrollHeight;
}

function pluginTypeLabel(pluginType: PluginRegistration["pluginType"]) {
  return pluginType === "custom-ui"
    ? t("Plugin.CreatePlugin.Form.Type.Custom", "Custom UI plugin")
    : t("Plugin.CreatePlugin.Form.Type.Standard", "Standard results plugin");
}

function updateAiTarget() {
  const status = currentPlugin
    ? t("Plugin.CreatePlugin.Ai.Target.Selected", "Selected plugin: {{name}}. Describe what you want to change.")
      .replace("{{name}}", currentPlugin.name)
    : t("Plugin.CreatePlugin.Ai.Target.None", "No plugin selected. AI will create a new plugin.");
  $("aiTargetText").textContent = status;
  $("aiTarget").classList.toggle("selected", currentPlugin !== null);
  const prompt = $<HTMLTextAreaElement>("aiPrompt");
  prompt.placeholder = currentPlugin
    ? t("Plugin.CreatePlugin.Ai.Placeholder.Edit", "Describe what you want to change in the selected plugin...")
    : t("Plugin.CreatePlugin.Ai.Placeholder", "Describe the plugin you want to create...");
  const sendButton = $<HTMLButtonElement>("sendPrompt");
  const sendLabel = currentPlugin
    ? t("Plugin.CreatePlugin.Ai.Send.Edit", "Edit with AI")
    : t("Plugin.CreatePlugin.Ai.Send", "Create with AI");
  sendButton.title = sendLabel;
  sendButton.setAttribute("aria-label", sendLabel);
}

function resetAiSessionForTargetChange() {
  aiSessionId = newAiSessionId();
  progressTurn++;
  progressSequence = 0;
  liveReply = null;
  liveReplyMarkdown = "";
  streamedReplyReceived = false;
  activeReadGroup = null;
  accumulatedCreationMs = 0;
}

function clearSelectedPlugin(resetSession = false) {
  if (resetSession) resetAiSessionForTargetChange();
  currentPlugin = null;
  $("pluginTools").hidden = true;
  closeMenus();
  document.querySelectorAll(".plugin-item.selected").forEach(item => item.classList.remove("selected"));
  updateAiTarget();
}

function selectPlugin(plugin: PluginDetails, resetSession = false) {
  if (resetSession) resetAiSessionForTargetChange();
  currentPlugin = plugin;
  $("pluginTools").hidden = false;
  document.querySelectorAll(".plugin-item").forEach(item =>
    item.classList.toggle("selected", item.getAttribute("data-plugin-id") === plugin.pluginId));
  updateDebugAction();
  updateAiTarget();
}

function updateDebugAction() {
  const debugging = currentPlugin?.isDebugging === true;
  const button = $("selectedStartDebug");
  button.classList.toggle("debug-stop", debugging);
  $("debugMenuIcon").textContent = debugging ? "■" : "▶";
  $("debugMenuLabel").textContent = debugging
    ? t("Plugin.CreatePlugin.Action.StopDebug", "Stop debugging")
    : t("Plugin.CreatePlugin.Action.StartDebug", "Start debugging");
}

function showPluginDetails(plugin: PluginDetails, resetSession = false) {
  selectPlugin(plugin, resetSession);
  $("detailName").textContent = plugin.name;
  $("detailId").textContent = plugin.pluginId;
  $("detailType").textContent = pluginTypeLabel(plugin.pluginType);
  $("sourcePath").textContent = plugin.sourcePath;
  showStep(3);
}

function renderPluginList() {
  const list = $("pluginList");
  list.replaceChildren();
  $("pluginCount").textContent = String(plugins.length);
  $("pluginListEmpty").hidden = plugins.length !== 0;
  for (const plugin of plugins) {
    const row = document.createElement("div");
    row.className = "plugin-row";
    const item = document.createElement("button");
    item.type = "button";
    item.className = "plugin-item";
    item.setAttribute("data-plugin-id", plugin.pluginId);
    item.title = plugin.sourcePath;
    const name = document.createElement("strong"); name.textContent = plugin.name;
    const id = document.createElement("small"); id.textContent = `id: ${plugin.pluginId}`;
    item.append(name, id);
    item.addEventListener("click", () => {
      if (isAiWorking) return;
      if (currentPlugin?.pluginId === plugin.pluginId) clearSelectedPlugin(true);
      else showPluginDetails(plugin, true);
    });
    row.append(item);
    list.append(row);
  }
  if (currentPlugin) {
    const refreshed = plugins.find(plugin => plugin.pluginId === currentPlugin?.pluginId);
    if (refreshed) selectPlugin(refreshed);
    else clearSelectedPlugin();
  }
}

async function loadPlugins() {
  try {
    const result = await bus.call<{ plugins: PluginRegistration[] }>("listDevelopmentPlugins");
    plugins = result.plugins;
    renderPluginList();
  } catch { /* A refresh can briefly reset the connection. */ }
}

function addChatMessage(role: "user" | "assistant", message: string) {
  $("chatEmpty").hidden = true;
  if (role === "user") {
    $("chatHistory").querySelectorAll<HTMLElement>(".interaction-card:not(.submitted)").forEach(card => {
      card.classList.add("submitted");
      card.querySelectorAll<HTMLInputElement | HTMLTextAreaElement | HTMLButtonElement>("input, textarea, button")
        .forEach(control => { control.disabled = true; });
    });
  }
  const item = document.createElement("div");
  item.className = `chat-message ${role}`;
  const label = document.createElement("b");
  label.textContent = role === "user" ? t("Plugin.CreatePlugin.Ai.You", "You") : t("Plugin.CreatePlugin.Ai.Assistant", "MyTools AI");
  const content = document.createElement("div");
  content.className = "message-content";
  const parsed = role === "assistant" ? parseInteraction(message) : null;
  if (parsed) renderMarkdown(content, parsed.markdown); else content.textContent = message;
  item.append(label, content);
  if (parsed?.interaction) item.append(createInteraction(parsed.interaction));
  $("chatHistory").append(item);
  scrollChatToBottom();
}

function renderMarkdown(element: HTMLElement, markdown: string) {
  const html = marked.parse(markdown, { async: false, breaks: true, gfm: true });
  element.innerHTML = DOMPurify.sanitize(html, {
    USE_PROFILES: { html: true },
    FORBID_TAGS: ["style", "iframe", "object", "embed"]
  });
  element.querySelectorAll<HTMLAnchorElement>("a").forEach(link => {
    link.target = "_blank";
    link.rel = "noopener noreferrer";
  });
}

function addElapsedTime(elapsedMs: number, isUpdate: boolean) {
  const totalSeconds = Math.max(1, Math.round(elapsedMs / 1000));
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  const template = isUpdate
    ? minutes
      ? t("Plugin.CreatePlugin.Ai.Elapsed.Edit.Minutes", "Plugin update took {{minutes}} min {{seconds}} sec.")
      : t("Plugin.CreatePlugin.Ai.Elapsed.Edit.Seconds", "Plugin update took {{seconds}} seconds.")
    : minutes
      ? t("Plugin.CreatePlugin.Ai.Elapsed.Minutes", "Plugin creation took {{minutes}} min {{seconds}} sec.")
      : t("Plugin.CreatePlugin.Ai.Elapsed.Seconds", "Plugin creation took {{seconds}} seconds.");
  const item = document.createElement("div");
  item.className = "elapsed-event";
  item.textContent = `◷ ${template.replace("{{minutes}}", String(minutes)).replace("{{seconds}}", String(seconds))}`;
  $("chatHistory").append(item);
  scrollChatToBottom();
}

const progressLabels: Record<string, [string, string]> = {
  readingSkill: ["Plugin.CreatePlugin.Ai.Progress.ReadingSkill", "Reading plugin creation skill"],
  thinking: ["Plugin.CreatePlugin.Ai.Progress.Thinking", "AI is thinking"],
  readingContext: ["Plugin.CreatePlugin.Ai.Progress.ReadingContext", "Reading MyTools context"],
  resolvingLocation: ["Plugin.CreatePlugin.Ai.Progress.ResolvingLocation", "Resolving approximate city"],
  listingFiles: ["Plugin.CreatePlugin.Ai.Progress.ListingFiles", "Inspecting plugin files"],
  readingFile: ["Plugin.CreatePlugin.Ai.Progress.ReadingFile", "Reading file"],
  searchingWeb: ["Plugin.CreatePlugin.Ai.Progress.SearchingWeb", "Searching the web"],
  fetchingUrl: ["Plugin.CreatePlugin.Ai.Progress.FetchingUrl", "Opening web page"],
  writingFile: ["Plugin.CreatePlugin.Ai.Progress.WritingFile", "Writing file"],
  editingFile: ["Plugin.CreatePlugin.Ai.Progress.EditingFile", "Editing file"],
  validatingPlugin: ["Plugin.CreatePlugin.Ai.Progress.Validating", "Validating plugin"],
  pluginReady: ["Plugin.CreatePlugin.Ai.Progress.PluginReady", "Plugin files are ready"],
  pluginRegistered: ["Plugin.CreatePlugin.Ai.Progress.Registered", "Registering development plugin"],
  installingDependencies: ["Plugin.CreatePlugin.Ai.Progress.Installing", "Installing npm dependencies"],
  installOutput: ["Plugin.CreatePlugin.Ai.Progress.InstallOutput", "npm install"],
  startingWatch: ["Plugin.CreatePlugin.Ai.Progress.StartingWatch", "Opening watch terminal"],
  setupComplete: ["Plugin.CreatePlugin.Ai.Progress.SetupComplete", "Dependencies installed and watch started"],
  setupFailed: ["Plugin.CreatePlugin.Ai.Progress.SetupFailed", "Plugin setup failed"],
  stopped: ["Plugin.CreatePlugin.Ai.Progress.Stopped", "Plugin creation stopped"],
  failed: ["Plugin.CreatePlugin.Ai.Progress.Failed", "AI creation failed"]
};

function ensureLiveReply() {
  if (liveReply) return liveReply;
  $("chatEmpty").hidden = true;
  const item = document.createElement("div");
  item.className = "chat-message assistant live";
  const label = document.createElement("b");
  label.textContent = t("Plugin.CreatePlugin.Ai.Assistant", "MyTools AI");
  liveReply = document.createElement("div");
  liveReply.className = "message-content";
  item.append(label, liveReply);
  $("chatHistory").append(item);
  return liveReply;
}

function finishLiveReplySegment() {
  liveReply?.parentElement?.classList.remove("live");
  liveReply = null;
  liveReplyMarkdown = "";
}

function finalizeLiveReplyInteraction() {
  if (!liveReply || !liveReplyMarkdown) return;
  const parsed = parseInteraction(liveReplyMarkdown);
  if (!parsed.interaction) return;
  renderMarkdown(liveReply, parsed.markdown);
  liveReply.parentElement?.append(createInteraction(parsed.interaction));
}

function hasStreamedReply() { return streamedReplyReceived; }

const groupedReadKinds = new Set(["readingSkill", "readingContext", "listingFiles", "readingFile"]);
const visibleReadDetailCount = 10;

function updateReadGroup(event: AiProgressEvent) {
  if (!activeReadGroup) {
    const item = document.createElement("div");
    item.className = "progress-event read-group";
    const indicator = document.createElement("span");
    indicator.className = "progress-indicator";
    indicator.textContent = "·";
    const body = document.createElement("span");
    const label = document.createElement("strong");
    body.append(label);
    item.append(indicator, body);
    $("chatHistory").append(item);
    activeReadGroup = { item, label, body, details: [], count: 0 };
  }

  activeReadGroup.count++;
  if (event.detail) activeReadGroup.details.push(event.detail);
  activeReadGroup.label.textContent = `${t("Plugin.CreatePlugin.Ai.Progress.ReadingFiles", "Reading files")} (${activeReadGroup.count})`;
  activeReadGroup.body.querySelectorAll("small").forEach(element => element.remove());
  const hiddenCount = Math.max(0, activeReadGroup.details.length - visibleReadDetailCount);
  if (hiddenCount) {
    const hidden = document.createElement("small");
    hidden.textContent = `… +${hiddenCount}`;
    activeReadGroup.body.append(hidden);
  }
  for (const detailText of activeReadGroup.details.slice(-visibleReadDetailCount)) {
    const detail = document.createElement("small");
    detail.textContent = detailText;
    detail.title = detailText;
    activeReadGroup.body.append(detail);
  }
  scrollChatToBottom();
}

function addProgress(event: AiProgressEvent) {
  if (event.kind === "responseDelta") {
    activeReadGroup = null;
    const reply = ensureLiveReply();
    liveReplyMarkdown += event.detail ?? "";
    streamedReplyReceived ||= Boolean(event.detail);
    renderMarkdown(reply, liveReplyMarkdown);
    scrollChatToBottom();
    return;
  }
  if (event.kind === "responseComplete" || event.kind === "turnComplete") {
    finalizeLiveReplyInteraction();
  }
  finishLiveReplySegment();
  if (event.kind === "responseComplete" || event.kind === "turnComplete") {
    activeReadGroup = null;
    return;
  }
  const definition = progressLabels[event.kind];
  if (!definition) return;
  if (groupedReadKinds.has(event.kind)) {
    updateReadGroup(event);
    return;
  }
  activeReadGroup = null;
  $("chatEmpty").hidden = true;
  const item = document.createElement("div");
  const isError = event.kind === "failed" || event.kind === "setupFailed";
  const isStopped = event.kind === "stopped";
  const isDone = event.kind === "pluginReady" || event.kind === "setupComplete";
  item.className = `progress-event${isError ? " error" : isDone ? " done" : ""}`;
  const indicator = document.createElement("span");
  indicator.className = "progress-indicator";
  indicator.textContent = isError ? "!" : isStopped ? "■" : isDone ? "✓" : "·";
  const body = document.createElement("span");
  const label = document.createElement("strong");
  label.textContent = t(definition[0], definition[1]);
  body.append(label);
  if (event.detail) {
    const detail = document.createElement("small");
    detail.textContent = event.detail;
    detail.title = event.detail;
    body.append(detail);
  }
  item.append(indicator, body);
  $("chatHistory").append(item);
  scrollChatToBottom();
}

async function streamProgress(sessionId: string, turn: number) {
  while (turn === progressTurn && sessionId === aiSessionId) {
    try {
      const batch = await bus.call<AiProgressBatch>(
        "getAiProgress", { sessionId, afterSequence: progressSequence }, 25_000);
      if (turn !== progressTurn || sessionId !== aiSessionId) return;
      let complete = false;
      for (const event of batch.events.sort((left, right) => left.sequence - right.sequence)) {
        progressSequence = Math.max(progressSequence, event.sequence);
        addProgress(event);
        complete ||= event.kind === "turnComplete" || event.kind === "failed" || event.kind === "stopped";
      }
      if (complete) return;
    } catch {
      if (turn !== progressTurn || sessionId !== aiSessionId) return;
    }
  }
}

async function sendPrompt(messageOverride?: string): Promise<boolean> {
  const input = $<HTMLTextAreaElement>("aiPrompt");
  const button = $<HTMLButtonElement>("sendPrompt");
  const message = (messageOverride ?? input.value).trim();
  if (!message || isAiWorking || input.disabled) return false;
  if (messageOverride === undefined) input.value = "";
  addChatMessage("user", message);
  aiSessionId ||= newAiSessionId();
  const turn = ++progressTurn;
  liveReply = null;
  liveReplyMarkdown = "";
  streamedReplyReceived = false;
  activeReadGroup = null;
  const startedAt = performance.now();
  const progressTask = streamProgress(aiSessionId, turn);
  let pluginCompleted = false;
  let operationIsUpdate = false;
  let operationStopped = false;
  isAiWorking = true;
  aiStopRequested = false;
  button.classList.add("working");
  button.title = t("Plugin.CreatePlugin.Ai.Stop", "Stop creation");
  button.setAttribute("aria-label", button.title);
  try {
    const response = await bus.call<AiChatResponse>(
      "chatWithAi", { sessionId: aiSessionId, message, selectedPluginId: currentPlugin?.pluginId }, AI_CHAT_TIMEOUT_MS);
    aiSessionId = response.sessionId;
    if (response.stopped) {
      operationStopped = true;
      addChatMessage("assistant", t(
        "Plugin.CreatePlugin.Ai.Stopped.Detail",
        "Plugin creation was stopped. Files already written to the development folder were kept."));
    } else if (!hasStreamedReply()) {
      addChatMessage("assistant", response.reply);
    }
    await loadPlugins();
    if (response.createdPlugin) {
      pluginCompleted = true;
      operationIsUpdate = response.createdPlugin.isUpdate === true;
      const created = plugins.find(plugin => plugin.pluginId === response.createdPlugin?.pluginId) ?? response.createdPlugin;
      selectPlugin(created);
    }
    if (response.setup?.watchStarted) {
      addChatMessage("assistant", t("Plugin.CreatePlugin.Ai.Setup.Success", "Dependencies are installed. A terminal was opened and npm run watch is running."));
    } else if (response.setup?.error) {
      addChatMessage("assistant", `${t("Plugin.CreatePlugin.Ai.Setup.Failed", "npm install failed. Resolve the npm or network problem, then run npm install and npm run watch in the plugin directory.")}\n\n${response.setup.error}`);
    }
    return !response.stopped;
  } catch (error) {
    operationStopped = aiStopRequested;
    addChatMessage("assistant", aiStopRequested
      ? t("Plugin.CreatePlugin.Ai.Stopped.Detail", "Plugin creation was stopped. Files already written to the development folder were kept.")
      : error instanceof Error ? error.message : String(error));
    return false;
  } finally {
    if (operationStopped) accumulatedCreationMs = 0;
    else accumulatedCreationMs += performance.now() - startedAt;
    await Promise.race([progressTask, new Promise(resolve => window.setTimeout(resolve, 1500))]);
    if (turn === progressTurn) progressTurn++;
    if (pluginCompleted) {
      addElapsedTime(accumulatedCreationMs, operationIsUpdate);
      accumulatedCreationMs = 0;
    }
    isAiWorking = false;
    aiStopRequested = false;
    button.classList.remove("working");
    updateAiTarget();
    input.focus();
  }
}

async function stopAiCreation() {
  if (!isAiWorking || aiStopRequested || !aiSessionId) return;
  aiStopRequested = true;
  const button = $<HTMLButtonElement>("sendPrompt");
  button.title = t("Plugin.CreatePlugin.Ai.Stopping", "Stopping creation...");
  button.setAttribute("aria-label", button.title);
  try {
    await bus.call("cancelAiCreation", { sessionId: aiSessionId });
  } catch (error) {
    aiStopRequested = false;
    button.title = t("Plugin.CreatePlugin.Ai.Stop", "Stop creation");
    button.setAttribute("aria-label", button.title);
    toast(error instanceof Error ? error.message : String(error), true);
  }
}

document.querySelectorAll<HTMLInputElement>('input[name="type"]').forEach(radio => radio.addEventListener("change", () => {
  document.querySelectorAll(".choice").forEach(item => item.classList.remove("selected"));
  radio.closest(".choice")?.classList.add("selected");
}));

$("sendPrompt").addEventListener("click", () => void (isAiWorking ? stopAiCreation() : sendPrompt()));
$<HTMLTextAreaElement>("aiPrompt").addEventListener("keydown", event => {
  if (event.key === "Enter" && event.ctrlKey) { event.preventDefault(); void sendPrompt(); }
});
$("clearChat").addEventListener("click", () => {
  if (isAiWorking) return;
  aiSessionId = newAiSessionId();
  progressTurn++;
  progressSequence = 0;
  liveReply = null;
  liveReplyMarkdown = "";
  streamedReplyReceived = false;
  activeReadGroup = null;
  accumulatedCreationMs = 0;
  $("chatHistory").querySelectorAll(".chat-message, .progress-event, .elapsed-event").forEach(item => item.remove());
  $("chatEmpty").hidden = false;
});

$("create").addEventListener("click", async () => {
  const name = ($<HTMLInputElement>("name").value || "").trim();
  const pluginId = ($<HTMLInputElement>("pluginId").value || "").trim().toLowerCase();
  const pluginType = document.querySelector<HTMLInputElement>('input[name="type"]:checked')!.value;
  const error = !name
    ? ["Plugin.CreatePlugin.Validation.NameRequired", "Enter a plugin name"]
    : !/^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$/.test(pluginId)
      ? ["Plugin.CreatePlugin.Validation.InvalidId", "The plugin ID format is invalid"] : null;
  setFormError(error?.[0], error?.[1]);
  if (error) return;
  try {
    const validation = await bus.call<PluginValidation>("validatePlugin", { name, pluginId });
    if (!validation.isValid) {
      const conflict = validation.conflict === "id"
        ? ["Plugin.CreatePlugin.Validation.IdExists", "A plugin with this ID already exists"]
        : ["Plugin.CreatePlugin.Validation.NameExists", "A plugin with this name already exists"];
      setFormError(conflict[0], conflict[1]); return;
    }
    showStep(2);
    const job = await bus.call<Job>("createPlugin", { name, pluginId, pluginType });
    const plugin = { ...job, name, pluginType: pluginType as PluginRegistration["pluginType"], distPath: job.distPath ?? "" };
    showPluginDetails(plugin);
    await loadPlugins();
  } catch (error) { showStep(1); toast(error instanceof Error ? error.message : String(error), true); }
});

$("refreshAll").addEventListener("click", async () => {
  try {
    await bus.call("refreshDevelopmentPlugins");
    toast(t("Plugin.CreatePlugin.Toast.RefreshRequested", "Requested a refresh of all development plugins"));
    await loadPlugins();
  } catch { toast(t("Plugin.CreatePlugin.Toast.ConnectionReset", "The connection was reset during refresh")); }
});

function openCreateModal() {
  setFormError();
  showStep(1);
  $("createModal").hidden = false;
  window.setTimeout(() => $<HTMLInputElement>("name").focus());
}

function createInteraction(spec: InteractionSpec): HTMLDivElement {
  const card = document.createElement("div");
  card.className = "interaction-card";
  const heading = document.createElement("div");
  heading.className = "interaction-heading";
  heading.textContent = spec.title || t("Plugin.CreatePlugin.Interaction.Questions", "A few questions");
  const progress = document.createElement("div");
  progress.className = "interaction-progress";
  const body = document.createElement("div");
  body.className = "interaction-body";
  const footer = document.createElement("div");
  footer.className = "interaction-footer";
  card.append(heading, progress, body, footer);

  const answers = spec.questions.map(() => ({ values: new Set<string>(), text: "" }));
  const inputGroup = `interaction-${crypto.randomUUID()}`;
  let page = 0;
  let submitting = false;

  function hasAnswer(index: number) {
    return answers[index].values.size > 0 || answers[index].text.trim().length > 0;
  }

  function actionButton(className: string, text: string, action: () => void) {
    const button = document.createElement("button");
    button.type = "button";
    button.className = className;
    button.textContent = text;
    button.addEventListener("click", action);
    return button;
  }

  function updateActions() {
    const primary = footer.querySelector<HTMLButtonElement>(".interaction-primary");
    if (primary) primary.disabled = submitting || !hasAnswer(page);
  }

  function renderActions() {
    footer.replaceChildren();
    const previous = actionButton("interaction-button", t("Plugin.CreatePlugin.Interaction.Previous", "Previous"), () => {
      page--;
      renderPage();
    });
    previous.disabled = page === 0 || submitting;
    previous.hidden = spec.questions.length === 1;
    footer.append(previous);
    if (page < spec.questions.length - 1) {
      const next = actionButton("interaction-button interaction-primary", t("Plugin.CreatePlugin.Interaction.Next", "Next"), () => {
        page++;
        renderPage();
      });
      next.disabled = submitting || !hasAnswer(page);
      footer.append(next);
      return;
    }
    const submit = actionButton("interaction-button interaction-primary", t("Plugin.CreatePlugin.Interaction.Submit", "Submit"), () => {
      if (submitting || answers.some((_, index) => !hasAnswer(index))) return;
      submitting = true;
      card.querySelectorAll<HTMLInputElement | HTMLTextAreaElement | HTMLButtonElement>("input, textarea, button")
        .forEach(control => { control.disabled = true; });
      const result = spec.questions.map((question, index): InteractionAnswer => ({
        questionId: question.id,
        prompt: question.prompt,
        values: [...answers[index].values],
        text: answers[index].text.trim()
      }));
      void sendInteractionAnswers(result).then(sent => {
        if (sent) showInteractionSummary(card, spec, result);
        else {
          card.classList.remove("submitted");
          submitting = false;
          renderPage();
        }
      });
    });
    submit.disabled = submitting || answers.some((_, index) => !hasAnswer(index));
    footer.append(submit);
  }

  function renderPage() {
    const question = spec.questions[page];
    const answer = answers[page];
    progress.textContent = t("Plugin.CreatePlugin.Interaction.Progress", "{{current}} of {{total}}")
      .replace("{{current}}", String(page + 1)).replace("{{total}}", String(spec.questions.length));
    body.replaceChildren();
    const prompt = document.createElement("div");
    prompt.className = "interaction-prompt";
    prompt.textContent = question.prompt;
    const choices = document.createElement("div");
    choices.className = "interaction-choices";
    question.options.forEach(option => {
      const label = document.createElement("label");
      label.className = "interaction-choice";
      const input = document.createElement("input");
      input.type = question.multiple ? "checkbox" : "radio";
      input.name = `${inputGroup}-${page}`;
      input.value = option;
      input.checked = answer.values.has(option);
      input.disabled = submitting;
      label.classList.toggle("selected", input.checked);
      input.addEventListener("change", () => {
        if (question.multiple) {
          if (input.checked) answer.values.add(option); else answer.values.delete(option);
        } else {
          answer.values.clear();
          if (input.checked) answer.values.add(option);
          answer.text = "";
          const textInput = body.querySelector<HTMLTextAreaElement>(".interaction-text");
          if (textInput) textInput.value = "";
        }
        choices.querySelectorAll<HTMLLabelElement>(".interaction-choice").forEach(choice => {
          choice.classList.toggle("selected", choice.querySelector<HTMLInputElement>("input")?.checked === true);
        });
        updateActions();
      });
      const marker = document.createElement("span");
      marker.className = "interaction-choice-marker";
      const text = document.createElement("span");
      text.textContent = option;
      label.append(input, marker, text);
      choices.append(label);
    });
    body.append(prompt, choices);
    if (question.allowText) {
      const textInput = document.createElement("textarea");
      textInput.className = "interaction-text";
      textInput.rows = 2;
      textInput.maxLength = 1000;
      textInput.value = answer.text;
      textInput.disabled = submitting;
      textInput.placeholder = question.textPlaceholder || t("Plugin.CreatePlugin.Interaction.Other", "Enter another answer");
      textInput.addEventListener("input", () => {
        answer.text = textInput.value;
        if (!question.multiple && answer.text.trim()) {
          answer.values.clear();
          choices.querySelectorAll<HTMLInputElement>("input").forEach(input => {
            input.checked = false;
            input.closest(".interaction-choice")?.classList.remove("selected");
          });
        }
        updateActions();
      });
      body.append(textInput);
    }
    renderActions();
  }

  renderPage();
  return card;
}

function showInteractionSummary(card: HTMLDivElement, spec: InteractionSpec, answers: InteractionAnswer[]) {
  card.className = "interaction-card submitted";
  card.replaceChildren();

  const row = document.createElement("div");
  row.className = "interaction-summary-row";
  const status = document.createElement("span");
  status.className = "interaction-summary-status";
  status.textContent = "✓";
  const text = document.createElement("span");
  text.className = "interaction-summary-text";
  text.textContent = t("Plugin.CreatePlugin.Interaction.AnswersSubmitted", "Submitted {{count}} answers")
    .replace("{{count}}", String(answers.length));
  const toggle = document.createElement("button");
  toggle.type = "button";
  toggle.className = "interaction-summary-toggle";
  toggle.textContent = t("Plugin.CreatePlugin.Interaction.ViewAnswers", "View");
  row.append(status, text, toggle);

  const details = document.createElement("div");
  details.className = "interaction-summary-details";
  details.hidden = true;
  if (spec.title) {
    const title = document.createElement("div");
    title.className = "interaction-summary-title";
    title.textContent = spec.title;
    details.append(title);
  }
  answers.forEach((answer, index) => {
    const item = document.createElement("div");
    item.className = "interaction-summary-answer";
    const prompt = document.createElement("div");
    prompt.className = "interaction-summary-prompt";
    prompt.textContent = `${index + 1}. ${answer.prompt}`;
    const value = document.createElement("div");
    value.className = "interaction-summary-value";
    value.textContent = [...answer.values, answer.text].filter(Boolean).join(" · ");
    item.append(prompt, value);
    details.append(item);
  });
  toggle.addEventListener("click", () => {
    details.hidden = !details.hidden;
    toggle.textContent = details.hidden
      ? t("Plugin.CreatePlugin.Interaction.ViewAnswers", "View")
      : t("Plugin.CreatePlugin.Interaction.HideAnswers", "Hide");
  });
  card.append(row, details);
}

async function sendInteractionAnswers(answers: InteractionAnswer[]) {
  const lines = [t("Plugin.CreatePlugin.Interaction.AnswerHeading", "My answers:")];
  answers.forEach((answer, index) => {
    lines.push(`${index + 1}. ${answer.prompt}`);
    answer.values.forEach(value => lines.push(`   - ${value}`));
    if (answer.text) lines.push(`   - ${answer.text}`);
  });
  return sendPrompt(lines.join("\n"));
}

function closeCreateModal() { $("createModal").hidden = true; }

$("newPlugin").addEventListener("click", openCreateModal);
$("closeCreateModal").addEventListener("click", closeCreateModal);
$("cancelCreate").addEventListener("click", closeCreateModal);
$("backToCreate").addEventListener("click", () => {
  closeCreateModal();
  $<HTMLTextAreaElement>("aiPrompt").focus();
  void loadPlugins();
});
$("openFolder").addEventListener("click", () => currentPlugin && void bus.call("openFolder", { sourcePath: currentPlugin.sourcePath }));
$("openCode").addEventListener("click", () => currentPlugin && void bus.call("openCode", { sourcePath: currentPlugin.sourcePath }));

function closeMenus() {
  $("developMenu").hidden = true;
  $("publishMenu").hidden = true;
  $("developMenuButton").setAttribute("aria-expanded", "false");
  $("publishMenuButton").setAttribute("aria-expanded", "false");
}

function toggleMenu(menuId: string, buttonId: string) {
  const menu = $(menuId);
  const open = menu.hidden;
  closeMenus();
  menu.hidden = !open;
  $(buttonId).setAttribute("aria-expanded", String(open));
}

$("developMenuButton").addEventListener("click", () => toggleMenu("developMenu", "developMenuButton"));
$("publishMenuButton").addEventListener("click", () => toggleMenu("publishMenu", "publishMenuButton"));
document.addEventListener("click", event => {
  if (!(event.target as Element).closest(".menu-root")) closeMenus();
});
document.addEventListener("keydown", event => { if (event.key === "Escape") closeMenus(); });
$("selectedOpenFolder").addEventListener("click", () => {
  closeMenus();
  if (currentPlugin) void bus.call("openFolder", { sourcePath: currentPlugin.sourcePath });
});
$("selectedOpenCode").addEventListener("click", () => {
  closeMenus();
  if (currentPlugin) void bus.call("openCode", { sourcePath: currentPlugin.sourcePath });
});

async function runSelectedPluginOperation(buttonId: string, callName: string, successKey: string, successDefault: string) {
  if (!currentPlugin) return;
  const pluginId = currentPlugin.pluginId;
  const button = $<HTMLButtonElement>(buttonId);
  button.disabled = true;
  try {
    await bus.call(callName, { pluginId }, 180_000);
    toast(t(successKey, successDefault));
    await loadPlugins();
  } catch (error) {
    window.alert(error instanceof Error ? error.message : String(error));
  } finally {
    button.disabled = false;
  }
}

$("selectedStartDebug").addEventListener("click", () => {
  closeMenus();
  const stopping = currentPlugin?.isDebugging === true;
  void runSelectedPluginOperation(
    "selectedStartDebug",
    stopping ? "stopDebug" : "startDebug",
    stopping ? "Plugin.CreatePlugin.Debug.Stopped" : "Plugin.CreatePlugin.Debug.Success",
    stopping ? "Development watch stopped." : "Development watch started.");
});
$("selectedDelete").addEventListener("click", async () => {
  closeMenus();
  if (!currentPlugin || isAiWorking) return;
  const plugin = currentPlugin;
  const question = t("Plugin.CreatePlugin.Delete.Confirm", "Delete {{name}} and its entire plugin folder? This cannot be undone.")
    .replace("{{name}}", plugin.name);
  if (!window.confirm(question)) return;
  const button = $<HTMLButtonElement>("selectedDelete");
  button.disabled = true;
  try {
    await bus.call("deleteDevelopmentPlugin", { pluginId: plugin.pluginId }, 30_000);
    if (currentPlugin?.pluginId === plugin.pluginId) clearSelectedPlugin(true);
    await loadPlugins();
    toast(t("Plugin.CreatePlugin.Delete.Success", "Plugin deleted."));
  } catch (error) {
    toast(error instanceof Error ? error.message : String(error), true);
  } finally {
    button.disabled = false;
  }
});
$("selectedPublish").addEventListener("click", () => {
  closeMenus();
  if (!currentPlugin) return;
  const question = t(
    "Plugin.CreatePlugin.Publish.Confirm",
    "Stop debugging and install {{name}} as a regular MyTools plugin?").replace("{{name}}", currentPlugin.name);
  if (window.confirm(question)) void runSelectedPluginOperation(
    "selectedPublish", "publishPlugin", "Plugin.CreatePlugin.Publish.Success", "Plugin installed in MyTools and reloaded.");
});
let publishValidationTurn = 0;

function closePublishModal() {
  publishValidationTurn++;
  $("publishModal").hidden = true;
}

async function validateHubPublish() {
  if (!currentPlugin) return;
  const pluginId = currentPlugin.pluginId;
  const turn = ++publishValidationTurn;
  const status = $("publishValidation");
  const publish = $<HTMLButtonElement>("confirmPublish");
  publish.disabled = true;
  status.className = "validation-status checking";
  status.textContent = t("Plugin.CreatePlugin.PublishHub.Checking", "Checking plugin ID and version...");
  try {
    const result = await bus.call<HubPublishValidation>("validateHubPublish", { pluginId }, 30_000);
    if (turn !== publishValidationTurn || currentPlugin?.pluginId !== pluginId) return;
    $<HTMLInputElement>("publishPluginId").value = result.pluginId;
    $<HTMLInputElement>("publishVersion").value = result.version;
    status.className = `validation-status ${result.isValid ? "valid" : "invalid"}`;
    status.textContent = result.isValid
      ? result.publishedVersion
        ? t("Plugin.CreatePlugin.PublishHub.Valid.Update", "Ready to publish. Current store version: {{version}}.").replace("{{version}}", result.publishedVersion)
        : t("Plugin.CreatePlugin.PublishHub.Valid.New", "This plugin ID is available and ready to publish.")
      : result.message || t("Plugin.CreatePlugin.PublishHub.Invalid", "Fix plugin.json and validate again.");
    publish.disabled = !result.isValid;
  } catch (error) {
    if (turn !== publishValidationTurn) return;
    status.className = "validation-status invalid";
    status.textContent = error instanceof Error ? error.message : String(error);
  }
}

$("selectedPublishHub").addEventListener("click", () => {
  closeMenus();
  if (!currentPlugin) return;
  $<HTMLInputElement>("publishPluginId").value = currentPlugin.pluginId;
  $<HTMLInputElement>("publishVersion").value = "";
  $("publishModal").hidden = false;
  void validateHubPublish();
});
$("closePublishModal").addEventListener("click", closePublishModal);
$("cancelPublish").addEventListener("click", closePublishModal);
$<HTMLInputElement>("publishPluginId").addEventListener("blur", () => void validateHubPublish());
$<HTMLInputElement>("publishVersion").addEventListener("blur", () => void validateHubPublish());
$("confirmPublish").addEventListener("click", async () => {
  if (!currentPlugin) return;
  const button = $<HTMLButtonElement>("confirmPublish");
  button.disabled = true;
  try {
    await bus.call("publishToHub", { pluginId: currentPlugin.pluginId }, 180_000);
    closePublishModal();
    toast(t("Plugin.CreatePlugin.PublishHub.Success", "Plugin published to the store."));
  } catch (error) {
    $("publishValidation").className = "validation-status invalid";
    $("publishValidation").textContent = error instanceof Error ? error.message : String(error);
    await validateHubPublish();
  }
});

window.addEventListener("focus", () => void loadPlugins());
document.addEventListener("visibilitychange", () => { if (!document.hidden) void loadPlugins(); });
bus.on(HostEvents.LanguageChanged, () => {
  if (formErrorKey) setFormError(formErrorKey, formErrorDefault);
  renderPluginList();
  if (currentPlugin) selectPlugin(currentPlugin);
  else updateAiTarget();
});

async function initialize() {
  await loadPlugins();
  updateAiTarget();
  try {
    const status = await bus.call<AiStatus>("getAiStatus");
    if (!status.available) {
      $("aiUnavailable").hidden = false;
      $("aiUnavailable").textContent = t(
        "Plugin.CreatePlugin.Ai.MissingKey",
        "Automatic mode is unavailable. Set the {{variable}} environment variable and restart MyTools.")
        .replace("{{variable}}", status.requiredEnvironmentVariable);
      $<HTMLButtonElement>("sendPrompt").disabled = true;
      $<HTMLTextAreaElement>("aiPrompt").disabled = true;
    }
  } catch {
    $("aiUnavailable").hidden = false;
    $<HTMLButtonElement>("sendPrompt").disabled = true;
    $<HTMLTextAreaElement>("aiPrompt").disabled = true;
  }
}

void initialize();
