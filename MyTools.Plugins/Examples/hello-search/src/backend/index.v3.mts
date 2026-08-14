// v3 backend for hello-search: same business logic as index.mts, but runs over the v3 named-pipe
// message bus via sdk-v3/src/server.ts instead of v2 stdio JSON-RPC. Activated when the host loads
// plugin.v3.json (protocolVersion "3.0").

import { createTool } from "@qping/plugin-bus/server";

// Minimal i18n: v3 plugins can opt into a dedicated i18n module later; for now a defaultValue-only
// shim keeps the existing t() call sites working without the v2 @qping/plugin-common dependency.
const mytoolsI18n = {
  t(_key: string, opts?: { defaultValue?: string; [k: string]: unknown }): string {
    let s = (opts?.defaultValue as string) ?? "";
    if (opts) {
      for (const [k, v] of Object.entries(opts)) {
        if (k === "defaultValue") continue;
        s = s.replace(new RegExp(`{{\\s*${k}\\s*}}`, "g"), String(v));
      }
    }
    return s;
  },
  configure(_params: unknown): void { /* v3 i18n hydration deferred */ },
};

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
      kind: "emoji",
      value: "👋"
    },
    actions: [
      {
        id: "open-detail",
        title: mytoolsI18n.t("Plugin.HelloSearch.Action.OpenDetail.Title", { defaultValue: "Open Detail" }),
        kind: "detail",
        description: mytoolsI18n.t("Plugin.HelloSearch.Action.OpenDetail.Description", {
          defaultValue: "Open the custom detail page"
        })
      }
    ]
  };
}

function createDetail(query: unknown, itemId: unknown, eventName = "initialize") {
  return {
    type: "web-detail",
    htmlEntry: "web/index.html",
    title: mytoolsI18n.t("Plugin.HelloSearch.Name", { defaultValue: "Hello Search" }),
    initialState: {
      itemId,
      query,
      lastEvent: eventName || "initialize",
      generatedAt: new Date().toISOString()
    }
  };
}

const tool = createTool();

tool
  .initialize((_params) => {
    return {};
  })
  .search((params) => ({
    items: [buildItem(params.query || "")]
  }))
  .action((params) => ({
    message: mytoolsI18n.t("Plugin.HelloSearch.Action.OpenDetail.Success", {
      defaultValue: "Opened hello detail"
    }),
    actionType: "none",
    detail: createDetail(params.query || "", params.itemId || "hello:item")
  }))
  .handle("refresh", (payload, context) => ({
    itemId: context.itemId || "hello:item",
    query: context.query || "",
    lastEvent: "refresh",
    payload,
    generatedAt: new Date().toISOString()
  }))
  .start();
