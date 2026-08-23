import path from "node:path";
import { fileURLToPath } from "node:url";
import { createPlugin, HostAction, Key, Modifiers, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";
import { loadChromiumItems, resolveChromiumProfiles } from "./chromium.mjs";
import { loadFirefoxItems, resolveFirefoxProfiles } from "./firefox.mjs";
import { itemMatches, itemPriority } from "./match.mjs";
import { defaultSettings, settingsFromOwnConfiguration } from "./settings.mjs";
import type { BrowserItem, PluginSettings } from "./types.mjs";

const MaxResults = 50;
const plugin = createPlugin();

function browserLabel(browser: BrowserItem["browser"]): string {
  if (browser === "edge") {
    return mytoolsI18n.t("Plugin.BrowserSearch.Browser.Edge", { defaultValue: "Edge" });
  }
  if (browser === "firefox") {
    return mytoolsI18n.t("Plugin.BrowserSearch.Browser.Firefox", { defaultValue: "Firefox" });
  }
  return mytoolsI18n.t("Plugin.BrowserSearch.Browser.Chrome", { defaultValue: "Chrome" });
}

function kindLabel(kind: BrowserItem["kind"]): string {
  return kind === "bookmark"
    ? mytoolsI18n.t("Plugin.BrowserSearch.Kind.Bookmark", { defaultValue: "Bookmark" })
    : mytoolsI18n.t("Plugin.BrowserSearch.Kind.History", { defaultValue: "History" });
}

function browserIcon(item: BrowserItem): string {
  if (item.kind === "history") {
    return "mdi-history";
  }
  if (item.browser === "edge") {
    return "mdi-microsoft-edge";
  }
  if (item.browser === "firefox") {
    return "mdi-firefox";
  }
  return "mdi-google-chrome";
}

function toSearchItem(item: BrowserItem, query: string, index: number) {
  const location = item.folderPath || item.profileName || item.url;
  const subtitle = mytoolsI18n.t("Plugin.BrowserSearch.Result.Subtitle", {
    defaultValue: "{{browser}} · {{kind}} · {{location}}",
    browser: browserLabel(item.browser),
    kind: kindLabel(item.kind),
    location,
  });
  return {
    id: `browser-search:${item.browser}:${item.kind}:${index}:${item.url}`,
    title: item.title || item.url,
    subtitle: location === item.url ? subtitle : `${subtitle} — ${item.url}`,
    priority: itemPriority(item, query),
    icon: { kind: "mdi", value: browserIcon(item) },
    url: item.url,
    actions: ["open", "copy-url"],
  };
}

async function loadSettings(): Promise<PluginSettings> {
  try {
    const result = await plugin.hostCall("configuration.readOwn");
    return settingsFromOwnConfiguration(result as { values?: Record<string, unknown> });
  } catch {
    return defaultSettings();
  }
}

async function loadBrowserSafely(
  name: string,
  load: () => Promise<BrowserItem[]>,
): Promise<BrowserItem[]> {
  try {
    return await load();
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`[browser-search] ${name} failed: ${message}`);
    return [];
  }
}

async function loadItems(settings: PluginSettings): Promise<BrowserItem[]> {
  const items: BrowserItem[] = [];
  if (settings.chromeEnabled) {
    items.push(...await loadBrowserSafely("chrome", async () => {
      const profiles = resolveChromiumProfiles("chrome", settings.chromeUserDataDir, settings.chromeProfile);
      return loadChromiumItems("chrome", profiles, settings.searchBookmarks, settings.searchHistory);
    }));
  }
  if (settings.edgeEnabled) {
    items.push(...await loadBrowserSafely("edge", async () => {
      const profiles = resolveChromiumProfiles("edge", settings.edgeUserDataDir, settings.edgeProfile);
      return loadChromiumItems("edge", profiles, settings.searchBookmarks, settings.searchHistory);
    }));
  }
  if (settings.firefoxEnabled) {
    items.push(...await loadBrowserSafely("firefox", async () => {
      const profiles = resolveFirefoxProfiles(settings.firefoxProfilesDir, settings.firefoxProfile);
      return loadFirefoxItems(profiles, settings.searchBookmarks, settings.searchHistory);
    }));
  }
  return items;
}

async function search(params: PluginSearchParams) {
  const query = (params.query || "").trim().toLowerCase();
  if (!query && params.mode !== "plugin") {
    return { items: [] };
  }

  const settings = await loadSettings();
  if (!settings.searchBookmarks && !settings.searchHistory) {
    return { items: [] };
  }

  let items: BrowserItem[] = [];
  try {
    items = await loadItems(settings);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`[browser-search] search failed: ${message}`);
    return { items: [] };
  }
  const matched = items
    .filter((item) => itemMatches(item, query))
    .sort((left, right) => {
      const byPriority = itemPriority(right, query) - itemPriority(left, query);
      if (byPriority !== 0) {
        return byPriority;
      }
      return right.lastVisit - left.lastVisit;
    })
    .slice(0, MaxResults)
    .map((item, index) => toSearchItem(item, query, index));

  return { items: matched };
}

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions<{ url: string }>([
    {
      id: "open",
      title: { key: "Plugin.BrowserSearch.Action.Open", defaultValue: "Open in Browser" },
      description: {
        key: "Plugin.BrowserSearch.Action.OpenDescription",
        defaultValue: "Open this URL in the default browser",
      },
      execute: ({ item }) => ({
        target: { kind: "host", action: { kind: HostAction.OpenInBrowser, url: item?.url ?? "" } },
        after: "close",
      }),
    },
    {
      id: "copy-url",
      title: { key: "Plugin.BrowserSearch.Action.CopyUrl", defaultValue: "Copy URL" },
      description: {
        key: "Plugin.BrowserSearch.Action.CopyUrlDescription",
        defaultValue: "Copy this URL to the clipboard",
      },
      hotkey: { key: Key.E, modifiers: Modifiers.Control },
      execute: ({ item }) => ({
        target: { kind: "host", action: { kind: HostAction.Copy, text: item?.url ?? "" } },
      }),
    },
  ])
  .search(search);

function isDirectRun(): boolean {
  const entry = process.argv[1];
  if (!entry) {
    return false;
  }
  try {
    const self = fileURLToPath(import.meta.url);
    return path.normalize(path.resolve(entry)).toLowerCase() === path.normalize(self).toLowerCase();
  } catch {
    return false;
  }
}

if (isDirectRun()) {
  plugin.start();
}

export {
  asBool,
  parseSettings,
} from "./settings.mjs";
export { isSubsequence, itemMatches, itemPriority } from "./match.mjs";
export { parseChromiumBookmarksJson, resolveChromiumProfiles } from "./chromium.mjs";
export { parseFirefoxProfilesIni, resolveFirefoxProfiles } from "./firefox.mjs";
export { readSqliteQuery, readSqliteTable, readSqliteTableFromBuffer } from "./sqlite.mjs";
