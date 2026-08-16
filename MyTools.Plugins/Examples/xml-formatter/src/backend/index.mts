import { createTool } from "@qping/plugin-bus/server";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

function buildSearchItem(query: unknown) {
  const text = typeof query === "string" ? query.trim() : "";
  return {
    id: "xml-formatter",
    title: text
      ? mytoolsI18n.t("Plugin.XmlFormatter.Result.Title", { defaultValue: "Format XML", text })
      : mytoolsI18n.t("Plugin.XmlFormatter.Name", { defaultValue: "XML Formatter" }),
    subtitle: mytoolsI18n.t("Plugin.XmlFormatter.Result.Subtitle", {
      defaultValue: "Format and validate XML"
    }),
    priority: 100,
    icon: {
      kind: "emoji",
      value: "📄"
    },
    actions: [
      {
        id: "open-detail",
        title: mytoolsI18n.t("Plugin.XmlFormatter.Action.Open.Title", { defaultValue: "Open Formatter" }),
        kind: "detail",
        description: mytoolsI18n.t("Plugin.XmlFormatter.Action.Open.Description", {
          defaultValue: "Open the XML formatter"
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
    title: mytoolsI18n.t("Plugin.XmlFormatter.Name", { defaultValue: "XML Formatter" }),
    initialState: {
      input: text
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
    items: [buildSearchItem(params.query || "")]
  }))
  .action((params) => ({
    message: mytoolsI18n.t("Plugin.XmlFormatter.Action.Open.Success", {
      defaultValue: "Opened XML formatter"
    }),
    actionType: "none",
    detail: createDetail(params.query || "")
  }))
  .start();
