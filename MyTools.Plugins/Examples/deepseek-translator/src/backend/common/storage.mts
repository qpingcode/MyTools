import fs from "node:fs";
import path from "node:path";

export const DATA_DIR = process.env.MYTOOLS_TRANSLATOR_DATA_DIR || path.join(process.cwd(), "data");
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
