import { matchesSearchText, normalizeSearchText, isSubsequence } from "@qping/plugin-bus/search";

export type PluginListItem = {
  pluginId?: string;
  name?: string;
  aliases?: string[];
  hotKey?: string;
};

const Scores = { exact: 100, prefix: 95, contains: 85, fuzzyOrMetadata: 70, homeWithHotkey: 90, home: 80 } as const;

export function matches(item: PluginListItem, query: string): boolean {
  query = normalizeSearchText(query);
  if (!query) return true;
  const name = item.name || item.pluginId || "";
  const pluginId = item.pluginId || "";
  const hotKey = item.hotKey || "";
  const aliases = Array.isArray(item.aliases) ? item.aliases : [];
  if (matchesSearchText(query, name)) return true;
  if (pluginId.toLowerCase().includes(query)) return true;
  if (hotKey && hotKey.toLowerCase().includes(query)) return true;
  return aliases.some((alias) => {
    const text = String(alias || "");
    return text.toLowerCase().includes(query) || isSubsequence(query, text);
  });
}

export function priority(item: PluginListItem, query: string): number {
  query = normalizeSearchText(query);
  if (!query) return (item.hotKey || "").trim() ? Scores.homeWithHotkey : Scores.home;
  const name = normalizeSearchText(item.name || item.pluginId || "");
  const aliases = (item.aliases || []).map((alias) => String(alias || "").toLowerCase());
  if (name === query) return Scores.exact;
  if (name.startsWith(query) || aliases.some((alias) => alias === query || alias.startsWith(query))) return Scores.prefix;
  if (name.includes(query) || aliases.some((alias) => alias.includes(query))) return Scores.contains;
  return Scores.fuzzyOrMetadata;
}

