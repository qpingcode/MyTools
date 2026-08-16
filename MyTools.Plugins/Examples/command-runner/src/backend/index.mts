import { readFile, stat } from "node:fs/promises";
import path from "node:path";
import { createPlugin, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

type CommandConfig = {
  name: string;
  command?: string;
  args?: string;
  runAsAdmin?: boolean;
  isBashScript?: boolean;
  scripts?: string[];
  workingDirectory?: string;
};

type CacheEntry = {
  mtimeMs: number;
  commands: CommandConfig[];
};

var cache: CacheEntry | null = null;

function configFilePath(): string {
  var appData = process.env.APPDATA;
  if (!appData) {
    throw new Error("APPDATA is not set");
  }
  return path.join(appData, "MyTools.Desktop", "CommandRunner.json");
}

function parseJsonLenient(text: string): CommandConfig[] {
  try {
    var parsed = JSON.parse(text);
    return Array.isArray(parsed) ? parsed : [];
  } catch {
    var stripped = text.replace(/,(\s*[}\]])/g, "$1");
    var parsed = JSON.parse(stripped);
    return Array.isArray(parsed) ? parsed : [];
  }
}

async function loadCommands(): Promise<CommandConfig[]> {
  var filePath = configFilePath();
  try {
    var fileStat = await stat(filePath);
    if (cache && cache.mtimeMs === fileStat.mtimeMs) {
      return cache.commands;
    }
    var text = await readFile(filePath, "utf8");
    var commands = parseJsonLenient(text);
    cache = { mtimeMs: fileStat.mtimeMs, commands };
    return commands;
  } catch {
    cache = null;
    return [];
  }
}

function isSubsequence(pattern: string, target: string): boolean {
  if (!pattern) return true;
  if (!target) return false;
  var pi = 0;
  var ti = 0;
  var needle = pattern.toLowerCase();
  var haystack = target.toLowerCase();
  while (ti < haystack.length && pi < needle.length) {
    if (haystack[ti] === needle[pi]) pi += 1;
    ti += 1;
  }
  return pi === needle.length;
}

function matches(config: CommandConfig, query: string): boolean {
  if (!query) return true;
  var name = config.name || "";
  if (name.toLowerCase().includes(query.toLowerCase())) return true;
  return isSubsequence(query, name);
}

function subtitle(config: CommandConfig): string {
  if (config.isBashScript) {
    return (config.scripts || []).join(" && ");
  }
  return `${config.command || ""} ${config.args || ""}`.trim();
}

function runAction() {
  return {
    id: "run",
    title: mytoolsI18n.t("Plugin.CommandRunner.Action.Run", { defaultValue: "Run" }),
    kind: "run",
    description: mytoolsI18n.t("Plugin.CommandRunner.Action.RunDescription", {
      defaultValue: "Run the selected command",
    }),
  };
}

async function search(params: PluginSearchParams) {
  const query = (params.query || "").trim();
  var commands = await loadCommands();
  var items = commands.filter((config) => matches(config, query)).map((config, index) => ({
    id: `command-runner:${index}:${config.name}`,
    title: config.name,
    subtitle: subtitle(config),
    priority: 100,
    icon: { kind: "emoji", value: "🚀" },
    copyText: JSON.stringify(config),
    actions: [runAction()],
  }));
  return { items };
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    cache = null;
    return {};
  })
  .search(search)
  .start();
