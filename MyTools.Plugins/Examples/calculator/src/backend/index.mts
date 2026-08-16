import { createPlugin, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

const ArithmeticPattern = /^[\d\s.+\-*/()]+$/;

function copyAction() {
  return {
    id: "copy",
    title: mytoolsI18n.t("Plugin.Calculator.Action.Copy", { defaultValue: "Copy" }),
    kind: "copy",
    description: mytoolsI18n.t("Plugin.Calculator.Action.CopyDescription", {
      defaultValue: "Copy the result to the clipboard",
    }),
  };
}

function evaluate(expression: string): number {
  const value = Function(`"use strict"; return (${expression});`)();
  if (typeof value !== "number" || !Number.isFinite(value)) {
    throw new Error("invalid");
  }
  return value;
}

function search(params: PluginSearchParams) {
  const query = (params.query || "").trim();
  if (!query || !ArithmeticPattern.test(query)) {
    return { items: [] };
  }

  try {
    const result = String(evaluate(query));
    return {
      items: [
        {
          id: `calculator:${result}`,
          title: result,
          subtitle: mytoolsI18n.t("Plugin.Calculator.Name", { defaultValue: "Calculator" }),
          priority: 100,
          icon: { kind: "emoji", value: "🧮" },
          copyText: result,
          actions: [copyAction()],
        },
      ],
    };
  } catch {
    return { items: [] };
  }
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search(search)
  .start();
