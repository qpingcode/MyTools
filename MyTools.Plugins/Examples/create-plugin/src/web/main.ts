import { createWebBusClient, HostEvents } from "@qping/plugin-bus/web";

type Job = { jobId: string; pluginId: string; sourcePath: string; distPath?: string; state: string; message: string };
const bus = createWebBusClient();
const $ = <T extends HTMLElement>(id: string) => document.getElementById(id) as T;
let currentJob: Job | null = null;
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
  document.querySelectorAll("nav span").forEach((item, index) => item.classList.toggle("active", index < step));
}

function toast(message: string, error = false) {
  const element = $("toast");
  element.textContent = message;
  element.className = error ? "show error-toast" : "show";
  window.setTimeout(() => element.className = "", 2600);
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
    currentJob = job;
    $("sourcePath").textContent = job.sourcePath;
    showStep(3);
  } catch (error) {
    showStep(1);
    toast(error instanceof Error ? error.message : String(error), true);
  }
});

$("refreshAll").addEventListener("click", async () => {
  try {
    await bus.call("refreshDevelopmentPlugins");
    toast(t("Plugin.CreatePlugin.Toast.RefreshRequested", "Requested a refresh of all development plugins"));
  } catch {
    toast(t("Plugin.CreatePlugin.Toast.ConnectionReset", "The connection was reset during refresh"));
  }
});

$("openFolder").addEventListener("click", () => currentJob && void bus.call("openFolder", { sourcePath: currentJob.sourcePath }));
$("openCode").addEventListener("click", () => currentJob && void bus.call("openCode", { sourcePath: currentJob.sourcePath }));

bus.on(HostEvents.LanguageChanged, () => {
  if (formErrorKey) setFormError(formErrorKey, formErrorDefault);
});
