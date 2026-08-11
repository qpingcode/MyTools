import { createTool } from "@qping/plugin-common/server";
import { mytoolsI18n } from "@qping/plugin-common/i18n";

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
  .initialize((params) => {
    mytoolsI18n.configure(params);
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