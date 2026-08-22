// Named-pipe backend for hello-search.

import { createPlugin } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

function buildItem(query: unknown) {
  const text = typeof query === "string" ? query.trim() : "";
  const normalized = text.length > 0 ? text : mytoolsI18n.t("Plugin.HelloSearch.Common.Empty", {
    defaultValue: "(empty)"
  });
  return {
    id: `hello:${normalized}`,
    title: text.length === 0
      ? mytoolsI18n.t("Plugin.HelloSearch.Name", { defaultValue: "Hello Search" })
      : mytoolsI18n.t("Plugin.HelloSearch.Result.Greeting", { defaultValue: "Hello {{name}}", name: normalized }),
    subtitle: mytoolsI18n.t("Plugin.HelloSearch.Result.Subtitle", {
      defaultValue: "Open the custom detail page powered by the Node runtime"
    }),
    priority: 100,
    icon: {
      kind: "mdi" as const,
      value: "mdi-hand-wave-outline"
    },
    actions: ["open-detail"]
  };
}

function createDetail(query: unknown, itemId: unknown, eventName = "initialize") {
  return {
    page: "web/index.html",
    title: mytoolsI18n.t("Plugin.HelloSearch.Name", { defaultValue: "Hello Search" }),
    initialState: {
      itemId,
      query,
      lastEvent: eventName || "initialize",
      generatedAt: new Date().toISOString()
    }
  };
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions([{
    id: "open-detail",
    title: { key: "Plugin.HelloSearch.Action.OpenDetail.Title", defaultValue: "Open Detail" },
    description: {
      key: "Plugin.HelloSearch.Action.OpenDetail.Description",
      defaultValue: "Open the custom detail page",
    },
    execute: (context) => ({
      message: {
        key: "Plugin.HelloSearch.Action.OpenDetail.Success",
        defaultValue: "Opened hello detail",
      },
      detail: createDetail(context.query, context.itemId || "hello:item"),
    }),
  }])
  .search((params) => ({
    items: [buildItem(params.query || "")]
  }))
  .handle("refresh", (payload, context) => ({
    itemId: context.itemId || "hello:item",
    query: context.query || "",
    lastEvent: "refresh",
    payload,
    generatedAt: new Date().toISOString()
  }))
  .start();
