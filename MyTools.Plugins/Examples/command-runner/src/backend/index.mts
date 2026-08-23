import { createPlugin, HostAction, type PluginSearchParams, type RunSpec } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";
import { isSubsequence } from "@qping/plugin-bus/search";

type CommandConfig = {
  name?: string;
  command?: string;
  args?: string;
  runAsAdmin?: boolean;
  isBashScript?: boolean;
  scripts?: string | string[];
  workingDirectory?: string;
};

type OwnConfiguration = {
  values?: {
    Commands?: CommandConfig[];
  };
};

function normalizeScripts(scripts: string | string[] | undefined): string[] {
  if (Array.isArray(scripts)) {
    return scripts.map((line) => String(line ?? "").trimEnd()).filter((line) => line.length > 0);
  }
  return String(scripts || "")
    .split(/\r?\n/)
    .map((line) => line.trimEnd())
    .filter((line) => line.length > 0);
}

function matches(config: CommandConfig, query: string): boolean {
  if (!query) return true;
  var name = config.name || "";
  if (name.toLowerCase().includes(query.toLowerCase())) return true;
  return isSubsequence(query, name);
}

function subtitle(config: CommandConfig): string {
  if (config.isBashScript) {
    return normalizeScripts(config.scripts).join(" && ");
  }
  return `${config.command || ""} ${config.args || ""}`.trim();
}

function toRunSpec(config: CommandConfig): RunSpec {
  return {
    name: config.name || "",
    command: config.command || "",
    args: config.args || "",
    runAsAdmin: !!config.runAsAdmin,
    isBashScript: !!config.isBashScript,
    scripts: normalizeScripts(config.scripts),
    workingDirectory: config.workingDirectory || undefined,
  };
}

async function loadCommands(): Promise<CommandConfig[]> {
  try {
    const result = (await plugin.hostCall("configuration.readOwn")) as OwnConfiguration;
    const commands = result?.values?.Commands;
    return Array.isArray(commands) ? commands : [];
  } catch {
    return [];
  }
}

async function search(params: PluginSearchParams) {
  const query = (params.query || "").trim();
  var commands = await loadCommands();
  var items = commands.filter((config) => matches(config, query)).map((config, index) => ({
    id: `command-runner:${index}:${config.name || ""}`,
    title: config.name || mytoolsI18n.t("Plugin.CommandRunner.Untitled", { defaultValue: "Untitled command" }),
    subtitle: subtitle(config),
    priority: 100,
    icon: { kind: "emoji", value: "🚀" },
    command: toRunSpec(config),
    actions: ["run"],
  }));
  return { items };
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions<{ command: RunSpec }>([{
    id: "run",
    title: { key: "Plugin.CommandRunner.Action.Run", defaultValue: "Run" },
    description: {
      key: "Plugin.CommandRunner.Action.RunDescription",
      defaultValue: "Run the selected command",
    },
    execute: ({ item }) => ({
      host: { kind: HostAction.Run, command: item?.command ?? { command: "" } },
      close: true,
    }),
  }])
  .search(search)
  .start();
