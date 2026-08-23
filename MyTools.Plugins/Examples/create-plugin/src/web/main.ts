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
};
type PluginDetails = PluginRegistration & Partial<Job>;
type PluginValidation = { isValid: boolean; conflict?: "id" | "name" };
type AiStatus = { available: boolean; provider: string; model: string; requiredEnvironmentVariable: string; unavailableReason?: string };
type AiProgressEvent = { sequence: number; kind: string; detail?: string };
type AiProgressBatch = { events: AiProgressEvent[] };
type PluginSetupResult = { installed: boolean; watchStarted: boolean; error?: string };
type AiChatResponse = { sessionId: string; reply: string; createdPlugin?: PluginRegistration & { isUpdate?: boolean }; setup?: PluginSetupResult };

const bus = createWebBusClient();
const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
let currentPlugin: PluginDetails | null = null;
let plugins: PluginRegistration[] = [];
let aiSessionId = "";
let progressSequence = 0;
let progressTurn = 0;
let liveReply: HTMLDivElement | null = null;
let liveReplyMarkdown = "";
let accumulatedCreationMs = 0;
let formErrorKey = "";
let formErrorDefault = "";

function t(key: string, defaultValue: string) { return bus.i18n.t(key, { defaultValue }); }

function setFormError(key = "", defaultValue = "") {
  formErrorKey = key;
  formErrorDefault = defaultValue;
  $("formError").textContent = key ? t(key, defaultValue) : "";
}

function selectMode(mode: "automatic" | "manual") {
  const automatic = mode === "automatic";
  $("automaticPanel").hidden = !automatic;
  $("manualPanel").hidden = automatic;
  $("automaticMode").classList.toggle("active", automatic);
  $("manualMode").classList.toggle("active", !automatic);
  $("automaticMode").setAttribute("aria-selected", String(automatic));
  $("manualMode").setAttribute("aria-selected", String(!automatic));
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

function pluginTypeLabel(pluginType: PluginRegistration["pluginType"]) {
  return pluginType === "custom-ui"
    ? t("Plugin.CreatePlugin.Form.Type.Custom", "Custom UI plugin")
    : t("Plugin.CreatePlugin.Form.Type.Standard", "Standard results plugin");
}

function defaultTestSteps() {
  return [
    t("Plugin.CreatePlugin.Next.OpenTerminal", "Open a terminal in the plugin directory"),
    t("Plugin.CreatePlugin.Next.Install.Command", "Run npm install"),
    t("Plugin.CreatePlugin.Next.Watch.Command", "Run npm run watch"),
    t("Plugin.CreatePlugin.Next.Test", "Test the alias or hotkey in MyTools")
  ];
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
  $<HTMLButtonElement>("sendPrompt").textContent = currentPlugin
    ? t("Plugin.CreatePlugin.Ai.Send.Edit", "Edit with AI")
    : t("Plugin.CreatePlugin.Ai.Send", "Create with AI");
}

function resetAiSessionForTargetChange() {
  const previousSessionId = aiSessionId;
  aiSessionId = "";
  progressTurn++;
  progressSequence = 0;
  liveReply = null;
  liveReplyMarkdown = "";
  accumulatedCreationMs = 0;
  if (previousSessionId) void bus.call("clearAiConversation", { sessionId: previousSessionId });
}

function clearSelectedPlugin(resetSession = false) {
  if (resetSession) resetAiSessionForTargetChange();
  currentPlugin = null;
  $("selectedPlugin").hidden = true;
  document.querySelectorAll(".plugin-item.selected").forEach(item => item.classList.remove("selected"));
  updateAiTarget();
}

function selectPlugin(plugin: PluginDetails, resetSession = false) {
  if (resetSession) resetAiSessionForTargetChange();
  currentPlugin = plugin;
  $("selectedPlugin").hidden = false;
  $("selectedName").textContent = `${plugin.name} · ${plugin.pluginId}`;
  $("selectedPath").textContent = plugin.sourcePath;
  $("selectedAlias").textContent = plugin.aliases?.join(", ") || t("Plugin.CreatePlugin.List.None", "None");
  $("selectedHotKey").textContent = plugin.hotKeys?.join(", ") || t("Plugin.CreatePlugin.List.None", "None");
  const steps = $("selectedTestSteps");
  steps.replaceChildren();
  for (const text of plugin.testSteps?.length ? plugin.testSteps : defaultTestSteps()) {
    const item = document.createElement("li");
    item.textContent = text;
    steps.append(item);
  }
  document.querySelectorAll(".plugin-item").forEach(item =>
    item.classList.toggle("selected", item.getAttribute("data-plugin-id") === plugin.pluginId));
  updateAiTarget();
}

function showPluginDetails(plugin: PluginDetails, resetSession = false) {
  selectPlugin(plugin, resetSession);
  $("detailName").textContent = plugin.name;
  $("detailId").textContent = plugin.pluginId;
  $("detailType").textContent = pluginTypeLabel(plugin.pluginType);
  $("sourcePath").textContent = plugin.sourcePath;
  if (!$("manualPanel").hidden) showStep(3);
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
    const id = document.createElement("small"); id.textContent = plugin.pluginId;
    const route = document.createElement("span");
    route.className = "route";
    route.textContent = [...(plugin.aliases ?? []), ...(plugin.hotKeys ?? [])].join(" · ") || "—";
    item.append(name, id, route);
    item.addEventListener("click", () => {
      if ($<HTMLButtonElement>("sendPrompt").disabled) return;
      if (currentPlugin?.pluginId === plugin.pluginId) clearSelectedPlugin(true);
      else showPluginDetails(plugin, true);
    });
    const deleteButton = document.createElement("button");
    deleteButton.type = "button";
    deleteButton.className = "icon-delete-btn";
    deleteButton.title = t("Plugin.CreatePlugin.Action.Delete", "Delete plugin");
    deleteButton.setAttribute("aria-label", deleteButton.title);
    deleteButton.innerHTML = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 3h6l1 2h4v2H4V5h4l1-2Zm-2 6h10l-1 11H8L7 9Zm3 2v7h2v-7h-2Zm4 0v7h2v-7h-2Z"/></svg>';
    deleteButton.addEventListener("click", async () => {
      if ($<HTMLButtonElement>("sendPrompt").disabled) return;
      const question = t("Plugin.CreatePlugin.Delete.Confirm", "Delete {{name}} and its entire plugin folder? This cannot be undone.")
        .replace("{{name}}", plugin.name);
      if (!window.confirm(question)) return;
      deleteButton.disabled = true;
      try {
        await bus.call("deleteDevelopmentPlugin", { pluginId: plugin.pluginId }, 30_000);
        if (currentPlugin?.pluginId === plugin.pluginId) clearSelectedPlugin(true);
        await loadPlugins();
        toast(t("Plugin.CreatePlugin.Delete.Success", "Plugin deleted."));
      } catch (error) {
        deleteButton.disabled = false;
        toast(error instanceof Error ? error.message : String(error), true);
      }
    });
    row.append(item, deleteButton);
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
  const item = document.createElement("div");
  item.className = `chat-message ${role}`;
  const label = document.createElement("b");
  label.textContent = role === "user" ? t("Plugin.CreatePlugin.Ai.You", "You") : t("Plugin.CreatePlugin.Ai.Assistant", "MyTools AI");
  const content = document.createElement("div");
  content.className = "message-content";
  if (role === "assistant") renderMarkdown(content, message); else content.textContent = message;
  item.append(label, content);
  $("chatHistory").append(item);
  item.scrollIntoView({ block: "end" });
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
  item.scrollIntoView({ block: "end" });
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

function hasStreamedReply() { return Boolean(liveReplyMarkdown); }

function addProgress(event: AiProgressEvent) {
  if (event.kind === "responseDelta") {
    const reply = ensureLiveReply();
    liveReplyMarkdown += event.detail ?? "";
    renderMarkdown(reply, liveReplyMarkdown);
    reply.parentElement?.scrollIntoView({ block: "end" });
    return;
  }
  if (event.kind === "responseComplete" || event.kind === "turnComplete") return;
  const definition = progressLabels[event.kind];
  if (!definition) return;
  $("chatEmpty").hidden = true;
  const item = document.createElement("div");
  const isError = event.kind === "failed" || event.kind === "setupFailed";
  const isDone = event.kind === "pluginReady" || event.kind === "setupComplete";
  item.className = `progress-event${isError ? " error" : isDone ? " done" : ""}`;
  const indicator = document.createElement("span");
  indicator.className = "progress-indicator";
  indicator.textContent = isError ? "!" : isDone ? "✓" : "·";
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
  item.scrollIntoView({ block: "end" });
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
        complete ||= event.kind === "turnComplete" || event.kind === "failed";
      }
      if (complete) return;
    } catch {
      if (turn !== progressTurn || sessionId !== aiSessionId) return;
    }
  }
}

async function sendPrompt() {
  const input = $<HTMLTextAreaElement>("aiPrompt");
  const message = input.value.trim();
  if (!message) return;
  input.value = "";
  addChatMessage("user", message);
  aiSessionId ||= crypto.randomUUID().replaceAll("-", "");
  const turn = ++progressTurn;
  liveReply = null;
  liveReplyMarkdown = "";
  const startedAt = performance.now();
  const progressTask = streamProgress(aiSessionId, turn);
  let pluginCompleted = false;
  let operationIsUpdate = false;
  const button = $<HTMLButtonElement>("sendPrompt");
  button.disabled = true;
  button.textContent = t("Plugin.CreatePlugin.Ai.Working", "AI is creating...");
  try {
    const response = await bus.call<AiChatResponse>(
      "chatWithAi", { sessionId: aiSessionId, message, selectedPluginId: currentPlugin?.pluginId }, 600_000);
    aiSessionId = response.sessionId;
    if (!hasStreamedReply()) addChatMessage("assistant", response.reply);
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
  } catch (error) {
    addChatMessage("assistant", error instanceof Error ? error.message : String(error));
  } finally {
    accumulatedCreationMs += performance.now() - startedAt;
    await Promise.race([progressTask, new Promise(resolve => window.setTimeout(resolve, 1500))]);
    if (turn === progressTurn) progressTurn++;
    if (pluginCompleted) {
      addElapsedTime(accumulatedCreationMs, operationIsUpdate);
      accumulatedCreationMs = 0;
    }
    button.disabled = false;
    updateAiTarget();
    input.focus();
  }
}

document.querySelectorAll<HTMLInputElement>('input[name="type"]').forEach(radio => radio.addEventListener("change", () => {
  document.querySelectorAll(".choice").forEach(item => item.classList.remove("selected"));
  radio.closest(".choice")?.classList.add("selected");
}));

$("automaticMode").addEventListener("click", () => {
  if (!$<HTMLButtonElement>("automaticMode").disabled) selectMode("automatic");
});
$("manualMode").addEventListener("click", () => selectMode("manual"));
$("sendPrompt").addEventListener("click", () => void sendPrompt());
$<HTMLTextAreaElement>("aiPrompt").addEventListener("keydown", event => {
  if (event.key === "Enter" && event.ctrlKey) { event.preventDefault(); void sendPrompt(); }
});
$("clearChat").addEventListener("click", async () => {
  await bus.call("clearAiConversation", { sessionId: aiSessionId || undefined });
  aiSessionId = "";
  progressTurn++;
  progressSequence = 0;
  liveReply = null;
  liveReplyMarkdown = "";
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

$("backToCreate").addEventListener("click", () => { showStep(1); void loadPlugins(); });
$("openFolder").addEventListener("click", () => currentPlugin && void bus.call("openFolder", { sourcePath: currentPlugin.sourcePath }));
$("openCode").addEventListener("click", () => currentPlugin && void bus.call("openCode", { sourcePath: currentPlugin.sourcePath }));
$("selectedOpenFolder").addEventListener("click", () => currentPlugin && void bus.call("openFolder", { sourcePath: currentPlugin.sourcePath }));
$("selectedOpenCode").addEventListener("click", () => currentPlugin && void bus.call("openCode", { sourcePath: currentPlugin.sourcePath }));

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

$("selectedStartDebug").addEventListener("click", () => void runSelectedPluginOperation(
  "selectedStartDebug", "startDebug", "Plugin.CreatePlugin.Debug.Success", "Development watch started."));
$("selectedPublish").addEventListener("click", () => {
  if (!currentPlugin) return;
  const question = t(
    "Plugin.CreatePlugin.Publish.Confirm",
    "Stop debugging and install {{name}} as a regular MyTools plugin?").replace("{{name}}", currentPlugin.name);
  if (window.confirm(question)) void runSelectedPluginOperation(
    "selectedPublish", "publishPlugin", "Plugin.CreatePlugin.Publish.Success", "Plugin installed in MyTools and reloaded.");
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
    $<HTMLButtonElement>("manualMode").disabled = false;
    if (status.available) {
      $<HTMLButtonElement>("automaticMode").disabled = false;
      selectMode("automatic");
    } else {
      const automatic = $<HTMLButtonElement>("automaticMode");
      automatic.disabled = true;
      automatic.title = status.unavailableReason ?? "";
      $("aiUnavailable").hidden = false;
      $("aiUnavailable").textContent = t(
        "Plugin.CreatePlugin.Ai.MissingKey",
        "Automatic mode is unavailable. Set the {{variable}} environment variable and restart MyTools.")
        .replace("{{variable}}", status.requiredEnvironmentVariable);
      selectMode("manual");
    }
  } catch {
    $<HTMLButtonElement>("automaticMode").disabled = true;
    $<HTMLButtonElement>("manualMode").disabled = false;
    selectMode("manual");
  }
}

void initialize();
