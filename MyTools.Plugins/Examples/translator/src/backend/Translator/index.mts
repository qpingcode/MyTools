import fs from "node:fs";
import { createPlugin, HostAction, Key, Modifiers } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

import {
  FAVORITES_PATH,
  SETTINGS_PATH,
  normalizeText,
  readCache,
  readJsonFile,
  writeCache,
  writeJsonFile,
} from "../common/storage.mjs";

const DEEPSEEK_API_URL = process.env.DEEPSEEK_API_URL || "https://api.deepseek.com/chat/completions";
const DEEPSEEK_MODEL = process.env.DEEPSEEK_MODEL || "deepseek-chat";
const DEEPSEEK_API_KEY = process.env.DEEPSEEK_API_KEY || "";
const CACHE_MAX_AGE_MS = 7 * 24 * 60 * 60 * 1000;
const CACHE_MIN_RECENT_COUNT = 200;
const LIST_LIMIT = 100;

type JsonRecord = Record<string, unknown>;

type TokenUsage = {
  promptTokens: number | null;
  completionTokens: number | null;
  totalTokens: number;
  cachedPromptTokens: number | null;
};

type TranslationDefinition = {
  meaning: string;
  example: string;
};

type TranslationState = JsonRecord & {
  input: string;
  status: string;
  inputType: string;
  translation: string;
  phonetic: string;
  definitions: TranslationDefinition[];
  chineseTranslation: string;
  isValidWord: boolean;
  isFavorite: boolean;
  fromCache: boolean;
  tokenUsage: TokenUsage | null;
  sendMode: string;
  isExpanded: boolean;
  error: string;
};

type FavoriteEntry = {
  savedAt: string;
  word: string;
  result: TranslationState;
};

type CacheEntry = JsonRecord & {
  key: string;
  input: string;
  inputType: string;
  cachedAt: string;
  state?: TranslationState;
};

type PluginSettings = {
  sendMode: string;
  isExpanded: boolean;
};

function isWord(text: unknown): boolean {
  const normalized = normalizeText(text);
  return /^[A-Za-z][A-Za-z'-]*$/.test(normalized);
}

function createInitialState(query: unknown, status = "idle"): TranslationState {
  const text = normalizeText(query);
  return {
    input: text,
    status,
    inputType: isWord(text) ? "word" : "sentence",
    translation: "",
    phonetic: "",
    definitions: [],
    chineseTranslation: "",
    isValidWord: false,
    isFavorite: false,
    fromCache: false,
    tokenUsage: null,
    sendMode: getConfiguredSendMode(),
    isExpanded: getConfiguredIsExpanded(),
    error: "",
  };
}
function createHistoryState() {
  const cache = readCache();
  const entries = toCacheEntries(cache.entries)
    .slice()
    .sort((left, right) => new Date(right.cachedAt).getTime() - new Date(left.cachedAt).getTime())
    .slice(0, LIST_LIMIT)
    .map((entry: CacheEntry) => {
      const state = {
        ...asTranslationState(entry.state),
        isFavorite: isFavoriteWord(entry.input),
        sendMode: getConfiguredSendMode(),
        isExpanded: getConfiguredIsExpanded(),
      };
      return {
        id: entry.key,
        input: normalizeText(entry.input),
        inputType: normalizeText(entry.inputType) || "text",
        cachedAt: normalizeText(entry.cachedAt),
        translation: state.inputType === "word"
          ? normalizeText(state.chineseTranslation || state.translation)
          : normalizeText(state.translation),
        phonetic: normalizeText(state.phonetic),
        state,
      };
    })
    .filter((entry) => entry.input);

  return {
    status: "history",
    input: "",
    entries,
    error: "",
    sendMode: getConfiguredSendMode(),
    isExpanded: getConfiguredIsExpanded(),
  };
}

function createFavoriteListState() {
  const entries = [...favoriteEntriesByWord.values()]
    .slice()
    .sort((left, right) => new Date(right.savedAt).getTime() - new Date(left.savedAt).getTime())
    .slice(0, LIST_LIMIT)
    .map((entry) => {
      const state = asTranslationState(entry.result, entry.word);
      state.sendMode = getConfiguredSendMode();
      state.isExpanded = getConfiguredIsExpanded();
      return {
        id: normalizeText(entry.word).toLowerCase(),
        input: normalizeText(entry.word),
        inputType: normalizeText(state.inputType) || (isWord(entry.word) ? "word" : "sentence"),
        cachedAt: normalizeText(entry.savedAt),
        translation: state.inputType === "word"
          ? normalizeText(state.chineseTranslation || state.translation)
          : normalizeText(state.translation),
        phonetic: normalizeText(state.phonetic),
        state,
      };
    })
    .filter((entry) => entry.input);

  return {
    status: "favorites",
    input: "",
    entries,
    error: "",
    sendMode: getConfiguredSendMode(),
    isExpanded: getConfiguredIsExpanded(),
  };
}

function buildPrompt(text: string, word: boolean): string {
  const sourceRule = "If the source text is Simplified Chinese or Traditional Chinese, translate it into natural English. Otherwise translate it into Simplified Chinese.";
  const outputRule = word
    ? "The input is a single English token. First decide if it is a valid English dictionary word. Set isValidWord to true only for valid words, otherwise false. For a valid word, explain it in English first. Include a standard IPA phonetic transcription when available. If it has multiple common meanings, return up to three English definitions, each with one natural example sentence. Also include a concise Simplified Chinese meaning, but keep it only in chineseTranslation. For an invalid word, return empty phonetic, definitions, and chineseTranslation."
    : "The input is a sentence or phrase. Return the complete sentence meaning. Do not include phonetic transcription.";
  const schema = word
    ? "{\"inputType\":\"word\",\"isValidWord\":true,\"phonetic\":\"/.../\",\"definitions\":[{\"meaning\":\"English definition\",\"example\":\"English example sentence\"}],\"chineseTranslation\":\"中文释义\"}"
    : "{\"inputType\":\"sentence\",\"translation\":\"...\",\"phonetic\":\"\"}";

  return [
    "You are a concise translation engine.",
    sourceRule,
    outputRule,
    "Return JSON only, without markdown fences or extra text, using this schema:",
    schema,
    `Input: ${text}`,
  ].join("\n");
}

function extractJsonObject(content: unknown): string {
  const trimmed = normalizeText(content);
  if (trimmed.startsWith("{") && trimmed.endsWith("}")) {
    return trimmed;
  }

  const start = trimmed.indexOf("{");
  const end = trimmed.lastIndexOf("}");
  if (start >= 0 && end > start) {
    return trimmed.slice(start, end + 1);
  }

  return "";
}

function normalizeTokenUsage(usage: unknown): TokenUsage | null {
  if (!usage || typeof usage !== "object") {
    return null;
  }

  const data = usage as JsonRecord;
  const promptTokens = Number.isFinite(data.prompt_tokens) ? Number(data.prompt_tokens) : null;
  const completionTokens = Number.isFinite(data.completion_tokens) ? Number(data.completion_tokens) : null;
  const totalTokens = Number.isFinite(data.total_tokens)
    ? Number(data.total_tokens)
    : (promptTokens ?? 0) + (completionTokens ?? 0);
  if (!Number.isFinite(totalTokens) || totalTokens <= 0) {
    return null;
  }

  return {
    promptTokens,
    completionTokens,
    totalTokens,
    cachedPromptTokens: Number.isFinite(data.prompt_cache_hit_tokens) ? Number(data.prompt_cache_hit_tokens) : null,
  };
}

function normalizeSendMode(mode: unknown): string {
  return mode === "realtime" ? "realtime" : "enter";
}

function readSettings(): PluginSettings {
  const settings = readJsonFile(SETTINGS_PATH, { sendMode: "enter", isExpanded: false });
  return {
    sendMode: normalizeSendMode(settings.sendMode),
    isExpanded: settings.isExpanded === true,
  };
}

function writeSettings(settings: PluginSettings): void {
  writeJsonFile(SETTINGS_PATH, settings);
}

const pluginSettings = readSettings();

function getConfiguredSendMode(): string {
  return pluginSettings.sendMode;
}

function getConfiguredIsExpanded(): boolean {
  return pluginSettings.isExpanded === true;
}

function setConfiguredSendMode(mode: unknown): string {
  pluginSettings.sendMode = normalizeSendMode(mode);
  writeSettings(pluginSettings);
  return pluginSettings.sendMode;
}

function setConfiguredIsExpanded(isExpanded: unknown): boolean {
  pluginSettings.isExpanded = isExpanded === true;
  writeSettings(pluginSettings);
  return pluginSettings.isExpanded;
}

function normalizeFavoriteEntry(entry: unknown): FavoriteEntry | null {
  const data = payloadRecord(entry);
  const word = normalizeText(data.word);
  if (!word) {
    return null;
  }

  return {
    savedAt: normalizeText(data.savedAt) || new Date().toISOString(),
    word,
    result: asTranslationState(data.result, word),
  };
}

function readFavoriteEntriesFromDisk(): FavoriteEntry[] {
  if (!fs.existsSync(FAVORITES_PATH)) {
    return [];
  }

  const favorites = readJsonFile<{ entries: unknown[] }>(FAVORITES_PATH, { entries: [] });
  return Array.isArray(favorites.entries)
    ? favorites.entries.map(normalizeFavoriteEntry).filter((entry) => entry !== null)
    : [];
}

function writeFavoriteEntriesToDisk(entries: FavoriteEntry[]): void {
  writeJsonFile(FAVORITES_PATH, { entries });
}

function buildFavoriteMap(entries: FavoriteEntry[]): Map<string, FavoriteEntry> {
  const map = new Map<string, FavoriteEntry>();
  for (const entry of entries) {
    map.set(normalizeText(entry.word).toLowerCase(), entry);
  }

  return map;
}

const favoriteEntriesByWord = buildFavoriteMap(readFavoriteEntriesFromDisk());

function persistFavoriteEntries(): void {
  writeFavoriteEntriesToDisk([...favoriteEntriesByWord.values()]);
}

function getCacheKey(text: string, word: boolean): string {
  return `${word ? "word" : "sentence"}:${word ? text.toLowerCase() : text}`;
}

function pruneCacheEntries(entries: CacheEntry[], now = Date.now()): CacheEntry[] {
  const sortedEntries = [...entries].sort((left, right) => {
    return new Date(right.cachedAt).getTime() - new Date(left.cachedAt).getTime();
  });
  const recentKeys = new Set(sortedEntries.slice(0, CACHE_MIN_RECENT_COUNT).map((entry) => entry.key));
  const cutoff = now - CACHE_MAX_AGE_MS;

  return sortedEntries.filter((entry) => {
    const cachedAt = new Date(entry.cachedAt).getTime();
    return cachedAt >= cutoff || recentKeys.has(entry.key);
  });
}

function getCachedTranslation(text: string, word: boolean): TranslationState | null {
  const cache = readCache();
  const prunedEntries = pruneCacheEntries(toCacheEntries(cache.entries));
  if (prunedEntries.length !== cache.entries.length) {
    writeCache({ entries: prunedEntries });
  }

  const key = getCacheKey(text, word);
  const entry = prunedEntries.find((item) => item.key === key);
  if (!entry?.state) {
    return null;
  }

  return {
    ...entry.state,
    isFavorite: isFavoriteWord(text),
    fromCache: true,
    sendMode: getConfiguredSendMode(),
    isExpanded: getConfiguredIsExpanded(),
  };
}

function cacheTranslation(text: string, word: boolean, state: TranslationState): void {
  const cache = readCache();
  const key = getCacheKey(text, word);
  const stateToCache = {
    ...state,
    isFavorite: false,
    fromCache: false,
  };
  const entries = toCacheEntries(cache.entries).filter((entry: CacheEntry) => entry.key !== key);
  entries.unshift({
    key,
    input: text,
    inputType: word ? "word" : "sentence",
    cachedAt: new Date().toISOString(),
    state: stateToCache,
  });
  writeCache({ entries: pruneCacheEntries(entries) });
}

function isFavoriteWord(text: unknown): boolean {
  const normalized = normalizeText(text).toLowerCase();
  if (!normalized) {
    return false;
  }

  return favoriteEntriesByWord.has(normalized);
}

function toggleFavoriteWord(text: string, state: TranslationState): TranslationState {
  const normalized = normalizeText(text);
  const word = isWord(normalized);
  if (!normalized || (word && state?.isValidWord !== true) || (!word && !normalizeText(state?.translation))) {
    return {
      ...state,
      isFavorite: false,
      sendMode: getConfiguredSendMode(),
      isExpanded: getConfiguredIsExpanded(),
      error: word
        ? mytoolsI18n.t("Plugin.DeepSeekTranslator.Error.InvalidFavorite", {
          defaultValue: "Only valid English words can be saved.",
        })
        : mytoolsI18n.t("Plugin.DeepSeekTranslator.Error.TranslateBeforeSaving", {
          defaultValue: "Translate the sentence before saving it.",
        }),
    };
  }

  const normalizedKey = normalized.toLowerCase();
  if (favoriteEntriesByWord.has(normalizedKey)) {
    favoriteEntriesByWord.delete(normalizedKey);
    persistFavoriteEntries();
    return {
      ...state,
      isFavorite: false,
      sendMode: getConfiguredSendMode(),
      isExpanded: getConfiguredIsExpanded(),
    };
  }

  favoriteEntriesByWord.set(normalizedKey, {
    savedAt: new Date().toISOString(),
    word: normalized,
    result: {
      ...state,
      isFavorite: true,
    },
  });
  if (favoriteEntriesByWord.size > LIST_LIMIT) {
    const oldestKeys = [...favoriteEntriesByWord.entries()]
      .sort((left, right) => new Date(left[1].savedAt).getTime() - new Date(right[1].savedAt).getTime())
      .slice(0, favoriteEntriesByWord.size - LIST_LIMIT)
      .map(([key]) => key);
    oldestKeys.forEach((key) => favoriteEntriesByWord.delete(key));
  }
  persistFavoriteEntries();

  return {
    ...state,
    isFavorite: true,
    sendMode: getConfiguredSendMode(),
    isExpanded: getConfiguredIsExpanded(),
  };
}

function deleteHistoryEntry(id: unknown) {
  const key = normalizeText(id);
  const cache = readCache();
  writeCache({ entries: toCacheEntries(cache.entries).filter((entry) => entry.key !== key) });
  return createHistoryState();
}

function deleteFavoriteEntry(id: unknown) {
  favoriteEntriesByWord.delete(normalizeText(id).toLowerCase());
  persistFavoriteEntries();
  return createFavoriteListState();
}

function updateSendMode(mode: unknown, state: TranslationState): TranslationState {
  return {
    ...(state || createInitialState("")),
    sendMode: setConfiguredSendMode(mode),
    isExpanded: getConfiguredIsExpanded(),
  };
}

function updateIsExpanded(isExpanded: unknown, state: TranslationState): TranslationState {
  return {
    ...(state || createInitialState("")),
    sendMode: getConfiguredSendMode(),
    isExpanded: setConfiguredIsExpanded(isExpanded),
  };
}

async function callDeepSeekTranslate(text: string, word: boolean): Promise<{ parsed: JsonRecord; tokenUsage: TokenUsage | null }> {
  const response = await fetch(DEEPSEEK_API_URL, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      Authorization: `Bearer ${DEEPSEEK_API_KEY}`,
    },
    body: JSON.stringify({
      model: DEEPSEEK_MODEL,
      temperature: 0.2,
      messages: [
        {
          role: "system",
          content: "You translate accurately and respond with valid JSON only.",
        },
        {
          role: "user",
          content: buildPrompt(text, word),
        },
      ],
    }),
  });

  if (!response.ok) {
    const body = await response.text();
    throw new Error(mytoolsI18n.t("Plugin.DeepSeekTranslator.Error.ApiFailed", {
      defaultValue: "DeepSeek API request failed ({{status}}): {{body}}",
      status: response.status,
      body,
    }));
  }

  const data = await response.json() as JsonRecord;
  const tokenUsage = normalizeTokenUsage(data?.usage);
  const choices = Array.isArray(data.choices) ? data.choices : [];
  const firstChoice = payloadRecord(choices[0]);
  const message = payloadRecord(firstChoice.message);
  const content = message.content;
  if (typeof content !== "string" || !content.trim()) {
    throw new Error(mytoolsI18n.t("Plugin.DeepSeekTranslator.Error.EmptyTranslation", {
      defaultValue: "DeepSeek API returned an empty translation.",
    }));
  }

  const jsonText = extractJsonObject(content);
  if (!jsonText) {
    throw new Error(mytoolsI18n.t("Plugin.DeepSeekTranslator.Error.NonJson", {
      defaultValue: "DeepSeek API returned non-JSON content.",
    }));
  }

  const parsed = JSON.parse(jsonText) as JsonRecord;
  return { parsed, tokenUsage };
}

async function translate(text: unknown): Promise<TranslationState> {
  const normalized = normalizeText(text);
  if (!normalized) {
    return createInitialState("", "idle");
  }

  const word = isWord(normalized);
  const cached = getCachedTranslation(normalized, word);
  if (cached) {
    return cached;
  }

  if (!DEEPSEEK_API_KEY) {
    return {
      ...createInitialState(normalized, "error"),
      error: mytoolsI18n.t("Plugin.DeepSeekTranslator.Error.MissingApiKey", {
        defaultValue: "Missing DEEPSEEK_API_KEY environment variable.",
      }),
    };
  }

  let result: { parsed: JsonRecord; tokenUsage: TokenUsage | null };
  try {
    result = await callDeepSeekTranslate(normalized, word);
  } catch (error) {
    // DeepSeek 偶尔返回非 JSON（格式错误）。检测到此类情况后重试一次，
    // 避免把可恢复的格式问题直接暴露给用户。
    const message = error instanceof Error ? error.message : String(error);
    const isFormatError = message.includes("non-JSON") || message.includes("Unexpected token") || message.includes("JSON");
    if (!isFormatError) {
      throw error;
    }
    result = await callDeepSeekTranslate(normalized, word);
  }

  const { parsed, tokenUsage } = result;
  const validWord = word && parsed.isValidWord === true;
  const definitions: TranslationDefinition[] = Array.isArray(parsed.definitions)
    ? parsed.definitions.slice(0, 3).map((definition: JsonRecord) => ({
      meaning: normalizeText(definition?.meaning),
      example: normalizeText(definition?.example),
    })).filter((definition: TranslationDefinition) => definition.meaning || definition.example)
    : [];

  const state: TranslationState = {
    input: normalized,
    status: "done",
    inputType: word ? "word" : "sentence",
    translation: word && !validWord ? "" : normalizeText(parsed.translation),
    phonetic: validWord ? normalizeText(parsed.phonetic) : "",
    definitions: validWord ? definitions : [],
    chineseTranslation: validWord ? normalizeText(parsed.chineseTranslation || parsed.translation) : "",
    isValidWord: validWord,
    isFavorite: isFavoriteWord(normalized),
    fromCache: false,
    tokenUsage,
    sendMode: getConfiguredSendMode(),
    isExpanded: getConfiguredIsExpanded(),
    error: "",
  };
  cacheTranslation(normalized, word, state);
  return state;
}

function payloadRecord(payload: unknown): Record<string, unknown> {
  return typeof payload === "object" && payload !== null ? payload as Record<string, unknown> : {};
}

function asTranslationState(value: unknown, fallbackInput = ""): TranslationState {
  const state = payloadRecord(value);
  const initial = createInitialState(fallbackInput);
  return {
    ...initial,
    ...state,
    input: normalizeText(state.input) || initial.input,
    status: normalizeText(state.status) || initial.status,
    inputType: normalizeText(state.inputType) || initial.inputType,
    translation: normalizeText(state.translation),
    phonetic: normalizeText(state.phonetic),
    definitions: Array.isArray(state.definitions)
      ? state.definitions.map((definition: unknown) => {
        const item = payloadRecord(definition);
        return {
          meaning: normalizeText(item.meaning),
          example: normalizeText(item.example),
        };
      }).filter((definition: TranslationDefinition) => definition.meaning || definition.example)
      : [],
    chineseTranslation: normalizeText(state.chineseTranslation),
    isValidWord: state.isValidWord === true,
    isFavorite: state.isFavorite === true,
    fromCache: state.fromCache === true,
    tokenUsage: normalizeTokenUsage(state.tokenUsage),
    sendMode: normalizeSendMode(state.sendMode),
    isExpanded: state.isExpanded === true,
    error: normalizeText(state.error),
  };
}

function toCacheEntries(entries: Record<string, unknown>[]): CacheEntry[] {
  return entries.map((entry: Record<string, unknown>) => ({
    ...entry,
    key: normalizeText(entry.key),
    input: normalizeText(entry.input),
    inputType: normalizeText(entry.inputType),
    cachedAt: normalizeText(entry.cachedAt),
    state: entry.state ? asTranslationState(entry.state, normalizeText(entry.input)) : undefined,
  }));
}

const plugin = createPlugin();
let copyText = "";

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions([
    {
      id: "copy",
      title: { key: "Plugin.DeepSeekTranslator.Action.Copy", defaultValue: "Copy" },
      description: {
        key: "Plugin.DeepSeekTranslator.Action.CopyDescription",
        defaultValue: "Copy the translation to the clipboard",
      },
      hotkey: { key: Key.E, modifiers: Modifiers.Control },
      execute: () => ({ host: { kind: HostAction.Copy, text: copyText }, close: true }),
    },
    {
      id: "translate",
      title: { key: "Plugin.DeepSeekTranslator.Action.Translate", defaultValue: "Translate" },
      hotkey: { key: Key.Enter, modifiers: Modifiers.Control },
      execute: () => ({ web: { payload: { action: "translate" } } }),
    },
    {
      id: "toggle-mode",
      title: { key: "Plugin.DeepSeekTranslator.Action.ToggleMode", defaultValue: "Switch Translation Mode" },
      hotkey: { key: Key.Tab, modifiers: Modifiers.Control },
      execute: () => ({ web: { payload: { action: "toggle-mode" } } }),
    },
    {
      id: "history",
      title: { key: "Plugin.DeepSeekTranslator.Action.History", defaultValue: "History List" },
      hotkey: { key: Key.H, modifiers: Modifiers.Control },
      execute: () => ({ web: { payload: { action: "history" } } }),
    },
    {
      id: "favorites",
      title: { key: "Plugin.DeepSeekTranslator.Action.Favorites", defaultValue: "Favorites List" },
      hotkey: { key: Key.D, modifiers: Modifiers.Control },
      execute: () => ({ web: { payload: { action: "favorites" } } }),
    },
    {
      id: "toggle-favorite",
      title: { key: "Plugin.DeepSeekTranslator.Action.ToggleFavorite", defaultValue: "Favorite / Unfavorite" },
      hotkey: { key: Key.D, modifiers: Modifiers.ControlShift },
      execute: () => ({ web: { payload: { action: "toggle-favorite" } } }),
    },
  ])
  .handle("setCopyText", (payload) => {
    copyText = typeof payload?.text === "string" ? payload.text : "";
    return {};
  })
  .handle("translate", async (payload, context) => {
    const data = payloadRecord(payload);
    const text = normalizeText(data.text || context.query || "");
    try {
      return await translate(text);
    } catch (error) {
      return {
        ...createInitialState(text, "error"),
        error: error instanceof Error ? error.message : String(error),
      };
    }
  })
  .handle("favorite", async (payload, context) => {
    const data = payloadRecord(payload);
    const text = normalizeText(data.text || context.query || "");
    return toggleFavoriteWord(text, data.state ? asTranslationState(data.state, text) : getCachedTranslation(text, isWord(text)) || createInitialState(text));
  })
  .handle("getHistory", () => createHistoryState())
  .handle("getFavorites", () => createFavoriteListState())
  .handle("deleteHistory", (payload) => deleteHistoryEntry(payloadRecord(payload).id))
  .handle("deleteFavorite", (payload) => deleteFavoriteEntry(payloadRecord(payload).id))
  .handle("setSendMode", (payload) => {
    const data = payloadRecord(payload);
    return updateSendMode(data.sendMode, data.state ? asTranslationState(data.state) : createInitialState(""));
  })
  .handle("setExpanded", (payload) => {
    const data = payloadRecord(payload);
    return updateIsExpanded(data.isExpanded, data.state ? asTranslationState(data.state) : createInitialState(""));
  })
  .start();
