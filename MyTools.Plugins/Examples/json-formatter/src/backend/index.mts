import { createPlugin } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

function buildSearchItem(query: unknown) {
  const text = typeof query === "string" ? query.trim() : "";
  return {
    id: "json-formatter",
    title: text
      ? mytoolsI18n.t("Plugin.JsonFormatter.Result.Title", { defaultValue: "Format JSON", text })
      : mytoolsI18n.t("Plugin.JsonFormatter.Name", { defaultValue: "JSON Formatter" }),
    subtitle: mytoolsI18n.t("Plugin.JsonFormatter.Result.Subtitle", {
      defaultValue: "Format, validate and minify JSON"
    }),
    priority: 100,
    icon: {
      kind: "emoji",
      value: "📄"
    },
    actions: [
      {
        id: "open-detail",
        title: mytoolsI18n.t("Plugin.JsonFormatter.Action.Open.Title", { defaultValue: "Open Formatter" }),
        kind: "detail",
        description: mytoolsI18n.t("Plugin.JsonFormatter.Action.Open.Description", {
          defaultValue: "Open the JSON formatter"
        })
      }
    ]
  };
}

function createDetail(query: unknown) {
  const text = typeof query === "string" ? query : "";
  return {
    type: "web-detail",
    htmlEntry: "web/index.html",
    title: mytoolsI18n.t("Plugin.JsonFormatter.Name", { defaultValue: "JSON Formatter" }),
    initialState: {
      input: text
    }
  };
}

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search((params) => ({
    items: [buildSearchItem(params.query || "")]
  }))
  .action((params) => ({
    message: mytoolsI18n.t("Plugin.JsonFormatter.Action.Open.Success", {
      defaultValue: "Opened JSON formatter"
    }),
    actionType: "none",
    detail: createDetail(params.query || "")
  }))
  .start();
