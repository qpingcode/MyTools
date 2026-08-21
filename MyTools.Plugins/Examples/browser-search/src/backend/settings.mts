import type { PluginSettings } from "./types.mjs";

type OwnConfiguration = {
  values?: Record<string, unknown>;
};

function asString(value: unknown): string {
  if (typeof value === "string") {
    return value.trim();
  }
  if (value == null) {
    return "";
  }
  return String(value).trim();
}

export function asBool(value: unknown, fallback: boolean): boolean {
  if (typeof value === "boolean") {
    return value;
  }
  if (typeof value === "number") {
    return value !== 0;
  }
  const text = asString(value).toLowerCase();
  if (text === "true" || text === "1" || text === "yes") {
    return true;
  }
  if (text === "false" || text === "0" || text === "no") {
    return false;
  }
  return fallback;
}

export function parseSettings(values: Record<string, unknown> | undefined): PluginSettings {
  const source = values || {};
  return {
    chromeEnabled: asBool(source.ChromeEnabled, true),
    edgeEnabled: asBool(source.EdgeEnabled, true),
    firefoxEnabled: asBool(source.FirefoxEnabled, true),
    searchBookmarks: asBool(source.SearchBookmarks, true),
    searchHistory: asBool(source.SearchHistory, true),
    chromeUserDataDir: asString(source.ChromeUserDataDir),
    chromeProfile: asString(source.ChromeProfile),
    edgeUserDataDir: asString(source.EdgeUserDataDir),
    edgeProfile: asString(source.EdgeProfile),
    firefoxProfilesDir: asString(source.FirefoxProfilesDir),
    firefoxProfile: asString(source.FirefoxProfile),
  };
}

export function defaultSettings(): PluginSettings {
  return parseSettings(undefined);
}

export function settingsFromOwnConfiguration(result: OwnConfiguration | null | undefined): PluginSettings {
  return parseSettings(result?.values);
}
