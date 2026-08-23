import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { createPlugin, HostAction, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

const ArithmeticPattern = /^[\d\s.+\-*/()]+$/;
const HistoryLimit = 50;
const VisibleHistoryLimit = 20;
const DataDirectory = (process.env.MYTOOLS_PLUGIN_DATA_DIR || "").trim() || path.join(process.cwd(), "data");
const HistoryPath = path.join(DataDirectory, "history.json");

export type CalculationHistoryEntry = {
  expression: string;
  result: string;
  timestamp: number;
};

type CalculatorItem = CalculationHistoryEntry & {
  id: string;
  title: string;
};

export function evaluate(expression: string): number {
  const value = Function(`"use strict"; return (${expression});`)();
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error("invalid");
  }
  return value;
}

function isHistoryEntry(value: unknown): value is CalculationHistoryEntry {
  if (!value || typeof value !== "object") {
    return false;
  }
  const entry = value as Record<string, unknown>;
  return typeof entry.expression === "string"
    && typeof entry.result === "string"
    && typeof entry.timestamp === "number"
    && Number.isFinite(entry.timestamp);
}

export function normalizeHistory(value: unknown): CalculationHistoryEntry[] {
  const entries = value && typeof value === "object"
    ? (value as { entries?: unknown }).entries
    : undefined;
  if (!Array.isArray(entries)) {
    return [];
  }
  return entries.filter(isHistoryEntry).slice(0, HistoryLimit);
}

export function addHistoryEntry(
  entries: CalculationHistoryEntry[],
  expression: string,
  result: string,
  timestamp = Date.now(),
): CalculationHistoryEntry[] {
  const normalizedExpression = expression.trim();
  if (!normalizedExpression || !result) {
    return entries;
  }
  return [
    { expression: normalizedExpression, result, timestamp },
    ...entries.filter((entry) => entry.expression !== normalizedExpression),
  ].slice(0, HistoryLimit);
}

function readHistory(): CalculationHistoryEntry[] {
  try {
    if (!fs.existsSync(HistoryPath)) {
      return [];
    }
    return normalizeHistory(JSON.parse(fs.readFileSync(HistoryPath, "utf8")));
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.warn(`[calculator] failed to read history: ${message}`);
    return [];
  }
}

function writeHistory(entries: CalculationHistoryEntry[]): void {
  try {
    fs.mkdirSync(DataDirectory, { recursive: true });
    fs.writeFileSync(HistoryPath, `${JSON.stringify({ entries }, null, 2)}\n`, "utf8");
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.warn(`[calculator] failed to write history: ${message}`);
  }
}

let history = readHistory();

function recordCalculation(expression: string, result: string): void {
  history = addHistoryEntry(history, expression, result);
  writeHistory(history);
}

function toCalculatorItem(entry: CalculationHistoryEntry, id: string, priority: number) {
  return {
    ...entry,
    id,
    title: entry.result,
    subtitle: entry.expression,
    priority,
    icon: { kind: "emoji", value: "🧮" },
    actions: ["copy"],
  };
}

function tryCalculate(query: string): CalculationHistoryEntry | null {
  if (!query || !ArithmeticPattern.test(query)) {
    return null;
  }
  try {
    return { expression: query, result: String(evaluate(query)), timestamp: Date.now() };
  } catch {
    return null;
  }
}

function search(params: PluginSearchParams) {
  const query = (params.query || "").trim();
  const current = tryCalculate(query);

  if (params.mode !== "plugin") {
    return {
      items: current
        ? [toCalculatorItem(current, `calculator:current:${encodeURIComponent(current.expression)}`, 100)]
        : [],
    };
  }

  const normalizedQuery = query.toLowerCase();
  const previous = history
    .filter((entry) => !current || entry.expression !== current.expression)
    .filter((entry) => current
      || !normalizedQuery
      || entry.expression.toLowerCase().includes(normalizedQuery)
      || entry.result.toLowerCase().includes(normalizedQuery))
    .slice(0, VisibleHistoryLimit)
    .map((entry, index) => toCalculatorItem(entry, `calculator:history:${entry.timestamp}:${index}`, 90 - index));

  return {
    items: current
      ? [toCalculatorItem(current, `calculator:current:${encodeURIComponent(current.expression)}`, 100), ...previous]
      : previous,
  };
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions<CalculatorItem>([{
    id: "copy",
    title: { key: "Plugin.Calculator.Action.Copy", defaultValue: "Copy" },
    description: {
      key: "Plugin.Calculator.Action.CopyDescription",
      defaultValue: "Copy the result to the clipboard",
    },
    execute: ({ item }) => {
      if (item?.expression && item.result) {
        recordCalculation(item.expression, item.result);
      }
      return {
        target: { kind: "host", action: { kind: HostAction.Copy, text: item?.result ?? "" } },
        after: "close",
      };
    },
  }])
  .search(search);

function isDirectRun(): boolean {
  const entry = process.argv[1];
  if (!entry) {
    return false;
  }
  try {
    return path.normalize(path.resolve(entry)).toLowerCase()
      === path.normalize(fileURLToPath(import.meta.url)).toLowerCase();
  } catch {
    return false;
  }
}

if (isDirectRun()) {
  plugin.start();
}
