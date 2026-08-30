import { createPlugin, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

type PluginSummary = {
  id?: string;
  name?: string;
  currentVersion?: string;
  downloadCount?: number;
  ownerUsername?: string;
};

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search(async (params: PluginSearchParams) => {
    const query = (params.query || "").trim();
    if (!query && params.mode !== "plugin") {
      return { items: [] };
    }
    const result = await plugin.hostCall("marketplace.search", { query, locale: params.locale }) as { items?: PluginSummary[] };
    const items = (result.items || []).slice(0, 8).map((item) => ({
      id: `store:${item.id}`,
      title: item.name || item.id || "",
      subtitle: mytoolsI18n.t("Plugin.Store.Result.Subtitle", {
        defaultValue: "{{owner}} · v{{version}} · {{downloads}} downloads",
        owner: item.ownerUsername || "",
        version: item.currentVersion || "",
        downloads: String(item.downloadCount ?? 0),
      }),
      icon: { kind: "mdi", value: "mdi-puzzle-outline" },
      pluginId: item.id,
      actions: ["open"],
    }));
    return { items };
  })
  .actions([{
    id: "open",
    title: { key: "Plugin.Store.Action.Open", defaultValue: "Open Store" },
    execute: ({ item }) => ({
      target: {
        kind: "detail",
        title: mytoolsI18n.t("Plugin.Store.Name", { defaultValue: "Plugin Store" }),
        initialState: { pluginId: item?.pluginId ?? "" },
      },
    }),
  }])
  .handle("searchPlugins", async (payload: { query?: string; locale?: string }) =>
    plugin.hostCall("marketplace.search", { query: payload?.query ?? "", locale: payload?.locale }))
  .handle("getPlugin", async (payload: { pluginId: string; locale?: string }) =>
    plugin.hostCall("marketplace.get", payload))
  .handle("installPlugin", async (payload: { pluginId: string; version?: string }) =>
    plugin.hostCall("marketplace.install", payload, 180_000))
  .handle("uninstallPlugin", async (payload: { pluginId: string }) =>
    plugin.hostCall("marketplace.uninstall", payload, 60_000))
  .start();
