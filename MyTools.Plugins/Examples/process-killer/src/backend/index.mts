import { execFile } from "node:child_process";
import { promisify } from "node:util";
import { createPlugin, HostAction, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";
import { isSubsequence } from "@qping/plugin-bus/search";

const execFileAsync = promisify(execFile);
const CacheTtlMs = 5000;
const CommandTimeoutMs = 4000;
const MaxResults = 30;

type ProcessInfo = {
  id: number;
  name: string;
  port: number;
};

type CacheEntry = {
  expiresAt: number;
  processes: ProcessInfo[];
};

var cache: CacheEntry | null = null;
var inflight: Promise<ProcessInfo[]> | null = null;

function parseCsvLine(line: string): string[] {
  var fields: string[] = [];
  var current = "";
  var inQuotes = false;
  for (var i = 0; i < line.length; i += 1) {
    var ch = line[i];
    if (ch === '"') {
      if (inQuotes && line[i + 1] === '"') {
        current += '"';
        i += 1;
      } else {
        inQuotes = !inQuotes;
      }
      continue;
    }
    if (ch === "," && !inQuotes) {
      fields.push(current);
      current = "";
      continue;
    }
    current += ch;
  }
  fields.push(current);
  return fields;
}

function run(file: string, args: string[]) {
  return execFileAsync(file, args, {
    windowsHide: true,
    maxBuffer: 8 * 1024 * 1024,
    timeout: CommandTimeoutMs,
  });
}

async function listTasklist(): Promise<ProcessInfo[]> {
  const { stdout } = await run("tasklist", ["/FO", "CSV", "/NH"]);
  var processes: ProcessInfo[] = [];
  for (var line of stdout.split(/\r?\n/)) {
    if (!line.trim()) continue;
    var parts = parseCsvLine(line);
    if (parts.length < 2) continue;
    var pid = Number.parseInt(parts[1], 10);
    if (!Number.isFinite(pid)) continue;
    processes.push({
      id: pid,
      name: parts[0].replace(/\.exe$/i, ""),
      port: -1,
    });
  }
  processes.sort((a, b) => a.name.localeCompare(b.name));
  return processes;
}

async function getPortMap(): Promise<Map<number, number>> {
  var map = new Map<number, number>();
  try {
    const { stdout } = await run("netstat", ["-ano"]);
    for (var line of stdout.split(/\r?\n/)) {
      var parts = line.trim().split(/\s+/);
      if (parts.length < 4) continue;
      if (parts[0] !== "TCP" && parts[0] !== "UDP") continue;
      var portText = parts[1].split(":").pop() || "";
      var port = Number.parseInt(portText, 10);
      var pid = Number.parseInt(parts[parts.length - 1], 10);
      if (Number.isFinite(port) && Number.isFinite(pid) && !map.has(pid)) {
        map.set(pid, port);
      }
    }
  } catch {
    // ignored
  }
  return map;
}

async function loadProcesses(): Promise<ProcessInfo[]> {
  if (cache && cache.expiresAt > Date.now()) {
    return cache.processes;
  }
  if (inflight) {
    return inflight;
  }

  inflight = (async () => {
    var processes: ProcessInfo[] = [];
    var ports = new Map<number, number>();
    var listed = await Promise.allSettled([listTasklist(), getPortMap()]);
    if (listed[0].status === "fulfilled") {
      processes = listed[0].value;
    }
    if (listed[1].status === "fulfilled") {
      ports = listed[1].value;
    }
    for (var processInfo of processes) {
      processInfo.port = ports.get(processInfo.id) ?? -1;
    }
    cache = { expiresAt: Date.now() + CacheTtlMs, processes };
    return processes;
  })().finally(() => {
    inflight = null;
  });

  return inflight;
}

function displaySubtitle(processInfo: ProcessInfo): string {
  var parts = [`PID: ${processInfo.id}`];
  if (processInfo.port > 0) {
    parts.push(mytoolsI18n.t("Plugin.ProcessKiller.Port", {
      defaultValue: "Port: {{port}}",
      port: String(processInfo.port),
    }));
  }
  return parts.join(" | ");
}

function matches(processInfo: ProcessInfo, query: string): boolean {
  if (!query) return true;
  var asInt = Number.parseInt(query, 10);
  if (String(asInt) === query && (processInfo.id === asInt || processInfo.port === asInt)) {
    return true;
  }
  return isSubsequence(query, processInfo.name);
}

async function search(params: PluginSearchParams) {
  const query = (params.query || "").trim();
  var processes = await loadProcesses();
  var items = processes
    .filter((processInfo) => matches(processInfo, query))
    .slice(0, MaxResults)
    .map((processInfo) => ({
      id: `process-killer:${processInfo.id}`,
      title: processInfo.name,
      subtitle: displaySubtitle(processInfo),
      priority: 100,
      icon: { kind: "emoji", value: "💀" },
      pid: processInfo.id,
      actions: ["kill"],
    }));
  return { items };
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions<{ pid: number }>([{
    id: "kill",
    title: { key: "Plugin.ProcessKiller.Action.Kill", defaultValue: "Kill Process" },
    description: {
      key: "Plugin.ProcessKiller.Action.KillDescription",
      defaultValue: "Terminate the selected process",
    },
    execute: ({ item }) => ({
      host: { kind: HostAction.Kill, pid: item?.pid ?? 0 },
      close: true,
    }),
  }])
  .search(search)
  .start();
