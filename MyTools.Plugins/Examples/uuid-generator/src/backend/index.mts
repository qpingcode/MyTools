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
const actionIds = ["standard", "nodash", "uppercase", "braces", "copy-all-history"];

function helpText(): string {
  return mytoolsI18n.t("Plugin.UuidGenerator.Help", {
    defaultValue: "Enter a number to generate that many UUIDs (maximum 1000). Use an action to choose the output format.",
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

  const count = requestedCount(query);
  const batch = Array.from({ length: count }, () => randomUUID());
  return {
    items: batch.map((uuid, index) => ({
      id: `uuid-generator:${uuid}`,
      title: uuid,
      subtitle: mytoolsI18n.t("Plugin.UuidGenerator.ResultSubtitle", {
        defaultValue: "GUID {{index}} of {{count}}",
        index: index + 1,
        count,
      }),
      priority: 100,
      icon: { kind: "emoji", value: "🔑" },
      uuid,
      batch,
      actions: actionIds,
    })),
  };
}

function copyAs(format: UuidFormat) {
  return ({ item }: ActionContext<UuidItem>) => ({
    host: { kind: HostAction.Copy, text: formatUuid(item?.uuid ?? "", format) },
  });
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions<UuidItem>([
    {
      id: "standard",
      title: { key: "Plugin.UuidGenerator.Action.Standard", defaultValue: "Standard format" },
      description: { key: "Plugin.UuidGenerator.Action.StandardDescription", defaultValue: "Copy with hyphens" },
      execute: copyAs("standard"),
    },
    {
      id: "nodash",
      title: { key: "Plugin.UuidGenerator.Action.NoDash", defaultValue: "Without hyphens" },
      description: { key: "Plugin.UuidGenerator.Action.NoDashDescription", defaultValue: "Copy without hyphens" },
      hotkey: { key: Key.D2, modifiers: Modifiers.Control },
      execute: copyAs("nodash"),
    },
    {
      id: "uppercase",
      title: { key: "Plugin.UuidGenerator.Action.Uppercase", defaultValue: "Uppercase format" },
      description: { key: "Plugin.UuidGenerator.Action.UppercaseDescription", defaultValue: "Copy in uppercase" },
      hotkey: { key: Key.D3, modifiers: Modifiers.Control },
      execute: copyAs("uppercase"),
    },
    {
      id: "braces",
      title: { key: "Plugin.UuidGenerator.Action.Braces", defaultValue: "Braced format" },
      description: { key: "Plugin.UuidGenerator.Action.BracesDescription", defaultValue: "Copy wrapped in braces" },
      hotkey: { key: Key.D4, modifiers: Modifiers.Control },
      execute: copyAs("braces"),
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
        host: { kind: HostAction.AddClipboardHistory, texts: item?.batch ?? [] },
        message: {
          key: "Plugin.UuidGenerator.Action.CopyAllHistorySuccess",
          defaultValue: "Added all UUIDs to clipboard history",
        },
      }),
    },
  ])
  .search(search)
  .start();
