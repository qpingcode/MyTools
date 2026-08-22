import { createPlugin, HostAction, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

type PluginListItem = {
  pluginId?: string;
  name?: string;
  aliases?: string[];
  hotKey?: string;
};

type PluginListResponse = {
  plugins?: PluginListItem[];
};

const plugin = createPlugin();

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

function matches(item: PluginListItem, query: string): boolean {
  if (!query) return true;
  const name = item.name || "";
  const pluginId = item.pluginId || "";
  const hotKey = item.hotKey || "";
  const aliases = Array.isArray(item.aliases) ? item.aliases : [];
  if (name.toLowerCase().includes(query) || isSubsequence(query, name)) return true;
  if (pluginId.toLowerCase().includes(query)) return true;
  if (hotKey && hotKey.toLowerCase().includes(query)) return true;
  return aliases.some((alias) => {
    const text = String(alias || "");
    return text.toLowerCase().includes(query) || isSubsequence(query, text);
  });
}

function priority(item: PluginListItem, query: string): number {
  if (!query) return 80;
  const name = (item.name || "").toLowerCase();
  const aliases = (item.aliases || []).map((alias) => String(alias || "").toLowerCase());
  if (name === query) return 100;
  if (name.startsWith(query) || aliases.some((alias) => alias === query || alias.startsWith(query))) return 95;
  if (name.includes(query) || aliases.some((alias) => alias.includes(query))) return 85;
  return 70;
}

function displayOrDash(value: string): string {
  return value.trim() ? value.trim() : "—";
}

function toItem(pluginInfo: PluginListItem) {
  const pluginId = (pluginInfo.pluginId || "").trim();
  const name = (pluginInfo.name || "").trim() || pluginId;
  if (!pluginId) {
    return null;
  }

  const aliases = (pluginInfo.aliases || []).map((alias) => String(alias || "").trim()).filter((alias) => alias.length > 0);
  return {
    id: `plugin-search:${pluginId}`,
    title: name,
    subtitle: mytoolsI18n.t("Plugin.PluginSearch.Result.Subtitle", {
      defaultValue: "Hotkey: {{hotKey}}    Alias: {{alias}}",
      hotKey: displayOrDash(pluginInfo.hotKey || ""),
      alias: displayOrDash(aliases.join(", ")),
    }),
    priority: 80,
    icon: { kind: "mdi", value: "mdi-puzzle-outline" },
    pluginId,
    actions: ["open"],
  };
}

async function loadPlugins(): Promise<PluginListItem[]> {
  const result = (await plugin.hostCall("plugins.list")) as PluginListResponse;
  return Array.isArray(result?.plugins) ? result.plugins : [];
}

async function search(params: PluginSearchParams) {
  const query = (params.query || "").trim().toLowerCase();
  if (!query && params.mode !== "plugin") {
    return { items: [] };
  }

  let plugins: PluginListItem[] = [];
  try {
    plugins = await loadPlugins();
  } catch {
    return { items: [] };
  }

  return {
    items: plugins
      .filter((item) => matches(item, query))
      .map((item) => {
        const mapped = toItem(item);
        if (!mapped) return null;
        return { ...mapped, priority: priority(item, query) };
      })
      .filter((item) => item != null),
  };
}

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions<{ pluginId: string }>([{
    id: "open",
    title: { key: "Plugin.PluginSearch.Action.Open", defaultValue: "Open Plugin" },
    description: {
      key: "Plugin.PluginSearch.Action.OpenDescription",
      defaultValue: "Open the plugin window",
    },
    execute: ({ item }) => ({
      host: { kind: HostAction.OpenPlugin, pluginId: item?.pluginId ?? "" },
      close: true,
    }),
  }])
  .search(search)
  .start();
