import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";

type Job = { jobId: string; pluginId: string; sourcePath: string; distPath?: string; state: string; message: string };
type PluginRegistration = {
  pluginId: string;
  name: string;
  author: string;
  pluginType: "standard" | "custom-ui";
  sourcePath: string;
  distPath: string;
};
type PluginDetails = PluginRegistration & Partial<Job>;
const bus = createWebBusClient();
const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
let currentPlugin: PluginDetails | null = null;
let plugins: PluginRegistration[] = [];
let formErrorKey = "";
let formErrorDefault = "";

function t(key: string, defaultValue: string) {
  return bus.i18n.t(key, { defaultValue });
}

function setFormError(key = "", defaultValue = "") {
  formErrorKey = key;
  formErrorDefault = defaultValue;
  $("formError").textContent = key ? t(key, defaultValue) : "";
}

function showStep(step: number) {
  for (let i = 1; i <= 3; i++) $(`step${i}`).hidden = i !== step;
  document.querySelectorAll("nav span").forEach((item, index) => {
    item.classList.toggle("active", index < step);
    item.classList.toggle("complete", index < step - 1);
    if (index === step - 1) item.setAttribute("aria-current", "step");
    else item.removeAttribute("aria-current");
  });
}

function toast(message: string, error = false) {
  const element = $("toast");
  element.textContent = message;
  element.className = error ? "show error-toast" : "show";
  window.setTimeout(() => element.className = "", 2600);
}

function pluginTypeLabel(pluginType: PluginRegistration["pluginType"]) {
  return pluginType === "custom-ui"
    ? t("Plugin.CreatePlugin.Form.Type.Custom", "Custom UI plugin")
    : t("Plugin.CreatePlugin.Form.Type.Standard", "Standard results plugin");
}

function showPluginDetails(plugin: PluginDetails) {
  currentPlugin = plugin;
  $("detailName").textContent = plugin.name;
  $("detailId").textContent = plugin.pluginId;
  $("detailAuthor").textContent = plugin.author;
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
    const item = document.createElement("button");
    item.type = "button";
    item.className = "plugin-item";
    item.title = plugin.sourcePath;

    const name = document.createElement("strong");
    name.textContent = plugin.name;
    const id = document.createElement("small");
    id.textContent = plugin.pluginId;
    const arrow = document.createElement("span");
    arrow.className = "arrow";
    arrow.textContent = "›";
    arrow.setAttribute("aria-hidden", "true");
    item.append(name, id, arrow);
    item.addEventListener("click", () => showPluginDetails(plugin));
    list.append(item);
  }
}

async function loadPlugins() {
  try {
    const result = await bus.call<{ plugins: PluginRegistration[] }>("listDevelopmentPlugins");
    plugins = result.plugins;
    renderPluginList();
    if (currentPlugin && !plugins.some((plugin) => plugin.sourcePath === currentPlugin?.sourcePath)) {
      currentPlugin = null;
      showStep(1);
    }
  } catch {
    // A development refresh may briefly reset the plugin connection; keep the last good list.
  }
}

document.querySelectorAll<HTMLInputElement>('input[name="type"]').forEach((radio) => radio.addEventListener("change", () => {
  document.querySelectorAll(".choice").forEach((item) => item.classList.remove("selected"));
  radio.closest(".choice")?.classList.add("selected");
}));

$("create").addEventListener("click", async () => {
  const name = ($<HTMLInputElement>("name").value || "").trim();
  const pluginId = ($<HTMLInputElement>("pluginId").value || "").trim().toLowerCase();
  const author = ($<HTMLInputElement>("author").value || "").trim();
  const pluginType = document.querySelector<HTMLInputElement>('input[name="type"]:checked')!.value;
  const error = !name
    ? ["Plugin.CreatePlugin.Validation.NameRequired", "Enter a plugin name"]
    : !/^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$/.test(pluginId)
      ? ["Plugin.CreatePlugin.Validation.InvalidId", "The plugin ID format is invalid"]
      : !author
        ? ["Plugin.CreatePlugin.Validation.AuthorRequired", "Enter the plugin author"]
        : null;
  setFormError(error?.[0], error?.[1]);
  if (error) return;

  try {
    showStep(2);
    const job = await bus.call<Job>("createPlugin", { name, pluginId, author, pluginType });
    const plugin = { ...job, name, author, pluginType: pluginType as PluginRegistration["pluginType"], distPath: job.distPath ?? "" };
    showPluginDetails(plugin);
    await loadPlugins();
  } catch (error) {
    showStep(1);
    toast(error instanceof Error ? error.message : String(error), true);
  }
});

$("refreshAll").addEventListener("click", async () => {
  try {
    await bus.call("refreshDevelopmentPlugins");
    toast(t("Plugin.CreatePlugin.Toast.RefreshRequested", "Requested a refresh of all development plugins"));
    await loadPlugins();
  } catch {
    toast(t("Plugin.CreatePlugin.Toast.ConnectionReset", "The connection was reset during refresh"));
  }
});

$("backToCreate").addEventListener("click", () => {
  showStep(1);
  void loadPlugins();
});
$("openFolder").addEventListener("click", () => currentPlugin && void bus.call("openFolder", { sourcePath: currentPlugin.sourcePath }));
$("openCode").addEventListener("click", () => currentPlugin && void bus.call("openCode", { sourcePath: currentPlugin.sourcePath }));

window.addEventListener("focus", () => void loadPlugins());
document.addEventListener("visibilitychange", () => {
  if (!document.hidden) void loadPlugins();
});

bus.on(HostEvents.LanguageChanged, () => {
  if (formErrorKey) setFormError(formErrorKey, formErrorDefault);
  renderPluginList();
  if (currentPlugin) $("detailType").textContent = pluginTypeLabel(currentPlugin.pluginType);
});

void loadPlugins();
