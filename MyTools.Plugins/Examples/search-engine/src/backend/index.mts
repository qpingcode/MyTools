import fs from "node:fs";
import path from "node:path";
import { createPlugin, HostAction, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

type SearchEngine = {
  name?: string;
  url?: string;
  urls?: string | string[];
};

type OwnConfiguration = {
  values?: {
    Engines?: SearchEngine[];
  };
};

const plugin = createPlugin();

function splitLines(value: string | undefined): string[] {
  return String(value || "")
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter((line) => line.length > 0);
}

function urlTemplates(engine: SearchEngine): string[] {
  const fromUrl = splitLines(engine.url);
  if (fromUrl.length > 0) {
    return fromUrl;
  }

  if (Array.isArray(engine.urls)) {
    return engine.urls.map((item) => String(item || "").trim()).filter((item) => item.length > 0);
  }

  return splitLines(engine.urls);
}

function createUrl(template: string, query: string): string {
  return template.replaceAll("{query}", encodeURIComponent(query));
}

function engineUrls(engine: SearchEngine, query: string): string[] {
  return urlTemplates(engine)
    .map((template) => createUrl(template, query));
}

function hasUrl(engine: SearchEngine): boolean {
  return urlTemplates(engine).length > 0;
}

function parseEngineList(value: unknown): SearchEngine[] {
  if (!Array.isArray(value)) {
    return [];
  }

  return value.filter(hasUrl);
}

function loadLegacyEngines(): SearchEngine[] {
  const appData = process.env.APPDATA;
  if (!appData) {
    return [];
  }

  const configPath = path.join(appData, "MyTools.Desktop", "SearchEnginePlugin.json");
  try {
    const json = fs.readFileSync(configPath, "utf8");
    return parseEngineList(JSON.parse(json));
  } catch {
    return [];
  }
}

async function loadEngines(): Promise<SearchEngine[]> {
  try {
    const result = (await plugin.hostCall("configuration.readOwn")) as OwnConfiguration;
    const engines = parseEngineList(result?.values?.Engines);
    if (engines.length > 0) {
      return engines;
    }
  } catch {
    // Fall through to the previous AppData JSON file.
  }

  return loadLegacyEngines();
}

function searchItem(engine: SearchEngine, query: string, index: number) {
  const name = (engine.name || "").trim() || String(index + 1);
  const urls = engineUrls(engine, query);
  if (urls.length === 0) {
    return null;
  }

  return {
    id: `search-engine:${index}:${name}`,
    title: mytoolsI18n.t("Plugin.SearchEngine.Result.Title", {
      defaultValue: "Search {{engine}}: {{query}}",
      engine: name,
      query,
    }),
    subtitle: mytoolsI18n.t("Plugin.SearchEngine.Result.Subtitle", {
      defaultValue: "Search using {{engine}}",
      engine: name,
    }),
    priority: 0,
    icon: { kind: "mdi", value: "mdi-web" },
    urls,
    actions: ["open"],
  };
}

async function search(params: PluginSearchParams) {
  const query = (params.query || "").trim();
  if (!query) {
    return { items: [] };
  }

  const engines = await loadEngines();
  return {
    items: engines
      .map((engine, index) => searchItem(engine, query, index))
      .filter((item) => item != null),
  };
}

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions<{ urls: string[] }>([{
    id: "open",
    title: { key: "Plugin.SearchEngine.Action.Open", defaultValue: "Open in Browser" },
    description: {
      key: "Plugin.SearchEngine.Action.OpenDescription",
      defaultValue: "Open the search URL in the default browser",
    },
    execute: ({ item }) => ({
      target: { kind: "host", action: { kind: HostAction.OpenInBrowser, url: item?.urls ?? [] } },
      after: "close",
    }),
  }])
  .search(search)
  .start();
