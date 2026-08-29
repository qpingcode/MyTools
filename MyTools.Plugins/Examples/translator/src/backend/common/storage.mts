import fs from "node:fs";
import path from "node:path";

export const DATA_DIR =
  normalizeText(process.env.MYTOOLS_PLUGIN_DATA_DIR) ||
  normalizeText(process.env.MYTOOLS_TRANSLATOR_DATA_DIR) ||
  path.join(process.cwd(), "data");
const PLUGINS_DATA_DIR = normalizeText(process.env.MYTOOLS_PLUGINS_DATA_DIR);
const LEGACY_DATA_DIR = PLUGINS_DATA_DIR ? path.join(PLUGINS_DATA_DIR, "deepseek-translator") : "";

// The plugin id was renamed to "translator". Migrate only when legacy data actually exists;
// otherwise leave the new per-plugin directory uncreated until the first write.
if (LEGACY_DATA_DIR && path.resolve(LEGACY_DATA_DIR) !== path.resolve(DATA_DIR)) {
  try {
    const targetIsEmpty = !fs.existsSync(DATA_DIR) || fs.readdirSync(DATA_DIR).length === 0;
    if (targetIsEmpty && fs.existsSync(LEGACY_DATA_DIR)) {
      fs.mkdirSync(DATA_DIR, { recursive: true });
      fs.cpSync(LEGACY_DATA_DIR, DATA_DIR, { recursive: true, force: false });
    }
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.warn(`[translator] legacy data migration skipped: ${message}`);
  }
}
export const CACHE_PATH = path.join(DATA_DIR, "translation-cache.json");
export const FAVORITES_PATH = path.join(DATA_DIR, "favorites.json");
export const SETTINGS_PATH = path.join(DATA_DIR, "settings.json");
export const ANKI_CARDS_PATH = path.join(DATA_DIR, "anki-cards.json");

export type TranslationCache = {
  entries: Record<string, unknown>[];
};

export function normalizeText(value: unknown): string {
  return typeof value === "string" ? value.trim() : "";
}

export function ensureDataDir(): void {
  fs.mkdirSync(DATA_DIR, { recursive: true });
}

export function readJsonFile<T>(filePath: string, fallback: T): T {
  if (!fs.existsSync(filePath)) {
    return fallback;
  }

  return JSON.parse(fs.readFileSync(filePath, "utf8")) as T;
}

export function writeJsonFile(filePath: string, value: unknown): void {
  ensureDataDir();
  fs.writeFileSync(filePath, `${JSON.stringify(value, null, 2)}\n`, "utf8");
}

export function readCache(): TranslationCache {
  const cache = readJsonFile(CACHE_PATH, { entries: [] });
  return {
    entries: Array.isArray(cache.entries) ? cache.entries : [],
  };
}

export function writeCache(cache: TranslationCache): void {
  writeJsonFile(CACHE_PATH, cache);
}
