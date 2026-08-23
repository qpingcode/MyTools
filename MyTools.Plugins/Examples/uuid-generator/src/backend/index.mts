import { randomUUID } from "node:crypto";
import {
  createPlugin,
  HostAction,
  Key,
  Modifiers,
  type ActionContext,
  type PluginSearchParams,
} from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

type UuidFormat = "standard" | "nodash" | "uppercase" | "braces";
type UuidItem = { uuid: string; batch: string[] };

const MaxBatchSize = 1000;
const formats: UuidFormat[] = ["standard", "nodash", "uppercase", "braces"];
const actionIds = ["copy", "toggle-format", "copy-all-history"];
let currentFormat: UuidFormat = "standard";
let currentQuery = "";
let currentBatch: string[] = [];

function helpText(): string {
  return mytoolsI18n.t("Plugin.UuidGenerator.Help", {
    defaultValue: "Enter a number to generate that many UUIDs (maximum 1000). Use the toggle action to switch format.",
  });
}

function requestedCount(query: string): number {
  if (!/^\d+$/.test(query)) return 1;
  return Math.max(1, Math.min(Number.parseInt(query, 10), MaxBatchSize));
}

function formatUuid(value: string, format: UuidFormat): string {
  switch (format) {
    case "nodash": return value.replaceAll("-", "");
    case "uppercase": return value.toUpperCase();
    case "braces": return `{${value}}`;
    default: return value;
  }
}

function batchFor(query: string): string[] {
  if (query !== currentQuery || currentBatch.length === 0) {
    currentQuery = query;
    currentBatch = Array.from({ length: requestedCount(query) }, () => randomUUID());
  }
  return currentBatch;
}

function formatName(format: UuidFormat) {
  const names = {
    standard: ["Plugin.UuidGenerator.Format.Standard", "Standard"],
    nodash: ["Plugin.UuidGenerator.Format.NoDash", "Without hyphens"],
    uppercase: ["Plugin.UuidGenerator.Format.Uppercase", "Uppercase"],
    braces: ["Plugin.UuidGenerator.Format.Braces", "Braced"],
  } as const;
  const [key, defaultValue] = names[format];
  return mytoolsI18n.t(key, { defaultValue });
}

function search(params: PluginSearchParams) {
  const query = (params.query || "").trim().toLowerCase();
  if (!query || query.includes("help")) {
    const text = helpText();
    return {
      items: [{
        id: "uuid-generator:help",
        title: mytoolsI18n.t("Plugin.UuidGenerator.HelpTitle", { defaultValue: "GUID Generator Help" }),
        subtitle: text,
        priority: 90,
        icon: { kind: "emoji", value: "🔑" },
      }],
    };
  }

  const batch = batchFor(query);
  const count = batch.length;
  return {
    items: batch.map((uuid, index) => ({
      id: `uuid-generator:${uuid}`,
      title: formatUuid(uuid, currentFormat),
      subtitle: mytoolsI18n.t("Plugin.UuidGenerator.ResultSubtitle", {
        defaultValue: "GUID {{index}} of {{count}} · {{format}}",
        index: index + 1,
        count,
        format: formatName(currentFormat),
      }),
      priority: 100,
      icon: { kind: "emoji", value: "🔑" },
      uuid,
      batch,
      actions: actionIds,
    })),
  };
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions<UuidItem>([
    {
      id: "copy",
      title: { key: "Plugin.UuidGenerator.Action.Copy", defaultValue: "Copy" },
      description: { key: "Plugin.UuidGenerator.Action.CopyDescription", defaultValue: "Copy the selected UUID" },
      execute: ({ item }: ActionContext<UuidItem>) => ({
        host: { kind: HostAction.Copy, text: formatUuid(item?.uuid ?? "", currentFormat) },
        close: true
      }),
    },
    {
      id: "toggle-format",
      title: { key: "Plugin.UuidGenerator.Action.ToggleFormat", defaultValue: "Switch format" },
      description: { key: "Plugin.UuidGenerator.Action.ToggleFormatDescription", defaultValue: "Switch every UUID to the next format" },
      hotkey: { key: Key.T, modifiers: Modifiers.Control },
      execute: () => {
        currentFormat = formats[(formats.indexOf(currentFormat) + 1) % formats.length];
        return {
          refresh: true,
          message: { key: "Plugin.UuidGenerator.Action.ToggleFormatSuccess", defaultValue: "UUID format switched" },
        };
      },
    },
    {
      id: "copy-all-history",
      title: { key: "Plugin.UuidGenerator.Action.CopyAllHistory", defaultValue: "Add all to clipboard history" },
      description: {
        key: "Plugin.UuidGenerator.Action.CopyAllHistoryDescription",
        defaultValue: "Add every generated UUID for sequential paste",
      },
      hotkey: { key: Key.C, modifiers: Modifiers.ControlShift },
      execute: ({ item }) => ({
        host: {
          kind: HostAction.AddClipboardHistory,
          texts: (item?.batch ?? []).map((uuid) => formatUuid(uuid, currentFormat)),
        },
        message: {
          key: "Plugin.UuidGenerator.Action.CopyAllHistorySuccess",
          defaultValue: "Added all UUIDs to clipboard history",
        },
        close: true
      }),
    },
  ])
  .search(search)
  .start();
