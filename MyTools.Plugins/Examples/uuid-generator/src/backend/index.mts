import { randomUUID } from "node:crypto";
import { createPlugin, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

type UuidFormat = "standard" | "uppercase" | "nodash" | "braces" | "parentheses" | "base64";

function copyAction() {
  return {
    id: "copy",
    title: mytoolsI18n.t("Plugin.UuidGenerator.Action.Copy", { defaultValue: "Copy" }),
    kind: "copy",
    description: mytoolsI18n.t("Plugin.UuidGenerator.Action.CopyDescription", {
      defaultValue: "Copy the value to the clipboard",
    }),
  };
}

function helpText() {
  return mytoolsI18n.t("Plugin.UuidGenerator.Help", {
    defaultValue:
      "Press any character to start Generator.\nFormat Options:\n• uppercase\n• nodash\n• braces\n• parentheses\n• base64",
  });
}

function parseFormat(query: string): UuidFormat {
  if (query.includes("uppercase") || query.includes("upper")) return "uppercase";
  if (query.includes("nodash") || query.includes("no-dash") || query.includes("no dash")) return "nodash";
  if (query.includes("braces") || query.includes("curly") || query.includes("{}")) return "braces";
  if (query.includes("parentheses") || query.includes("parens") || query.includes("()")) return "parentheses";
  if (query.includes("base64") || query.includes("b64")) return "base64";
  return "standard";
}

function formatUuid(value: string, format: UuidFormat): string {
  const bytes = uuidToBytes(value);
  switch (format) {
    case "uppercase":
      return value.toUpperCase();
    case "nodash":
      return value.replaceAll("-", "").toUpperCase();
    case "braces":
      return `{${value.toUpperCase()}}`;
    case "parentheses":
      return `(${value.toUpperCase()})`;
    case "base64":
      return Buffer.from(bytes).toString("base64");
    default:
      return value;
  }
}

function uuidToBytes(value: string): Uint8Array {
  const hex = value.replaceAll("-", "");
  const bytes = new Uint8Array(16);
  for (let i = 0; i < 16; i += 1) {
    bytes[i] = Number.parseInt(hex.slice(i * 2, i * 2 + 2), 16);
  }
  return bytes;
}

function search(params: PluginSearchParams) {
  const query = (params.query || "").trim().toLowerCase();
  if (!query || query.includes("help")) {
    const text = helpText();
    return {
      items: [
        {
          id: "uuid-generator:help",
          title: mytoolsI18n.t("Plugin.UuidGenerator.HelpTitle", { defaultValue: "GUID Generator Help" }),
          subtitle: text,
          priority: 90,
          icon: { kind: "emoji", value: "🔑" },
          copyText: text,
          actions: [copyAction()],
        },
      ],
    };
  }

  const format = parseFormat(query);
  const uuid = formatUuid(randomUUID(), format);
  return {
    items: [
      {
        id: `uuid-generator:${uuid}`,
        title: uuid,
        subtitle: mytoolsI18n.t("Plugin.UuidGenerator.ResultSubtitle", {
          defaultValue: "GUID ({{format}})",
          format,
        }),
        priority: 100,
        icon: { kind: "emoji", value: "🔑" },
        copyText: uuid,
        actions: [copyAction()],
      },
    ],
  };
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search(search)
  .start();
