import { createTool } from "@qping/plugin-bus/server";

// v3 i18n shim (dedicated i18n module deferred).
const mytoolsI18n = {
  t(_key: string, opts: any) {
    let s = (opts && opts.defaultValue) || "";
    if (opts) for (const [k, v] of Object.entries(opts)) { if (k === "defaultValue") continue; s = s.replace(new RegExp("{{\s*" + k + "\s*}}", "g"), String(v)); }
    return s;
  },
  configure(_p: any) {},
};

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
    message: mytoolsI18n.t("Plugin.JsonFormatter.Action.Open.Success", {
      defaultValue: "Opened JSON formatter"
    }),
    actionType: "none",
    detail: createDetail(params.query || "")
  }))
  .start();
