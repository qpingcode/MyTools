import { createTool } from "@qping/plugin-common/node-tool";

function buildItem(query: unknown) {
  const text = typeof query === "string" ? query.trim() : "";
  const normalized = text.length > 0 ? text : "(empty)";
  return {
    id: `hello:${normalized}`,
    title: normalized === "(empty)" ? "Hello Search" : `Hello ${normalized}`,
    subtitle: "Open the custom detail page powered by the Node runtime",
    priority: 100,
    icon: {
      kind: "emoji",
      value: "👋"
    },
    actions: [
      {
        id: "open-detail",
        title: "Open Detail",
        kind: "detail",
        description: "Open the custom detail page"
      }
    ]
  };
}

function createDetail(query: unknown, itemId: unknown, eventName = "initialize") {
  return {
    type: "web-detail",
    htmlEntry: "web/index.html",
    title: "Hello Search",
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
  .search((params) => ({
    items: [buildItem(params.query || "")]
  }))
  .action((params) => ({
    message: "Opened hello detail",
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