import { createTool } from "@qping/plugin-common/server";
import { mytoolsI18n } from "@qping/plugin-common/i18n";

const tool = createTool();

tool
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search((params) => ({
    items: [
      {
        id: "settings:main",
        title: mytoolsI18n.t("Plugin.Settings.Name", { defaultValue: "Settings" }),
        subtitle: mytoolsI18n.t("Plugin.Settings.Subtitle", {
          defaultValue: "Application settings",
        }),
        priority: 100,
        icon: { kind: "emoji", value: "⚙️" },
        actions: [
          {
            id: "open-detail",
            title: mytoolsI18n.t("Plugin.Settings.Action.Open", { defaultValue: "Open Settings" }),
            kind: "detail",
          },
        ],
      },
    ],
  }))
  .handle("getConfiguration", async () => {
    return await tool.hostCall("getConfiguration");
  })
  .handle("saveConfiguration", async (payload) => {
    var params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await tool.hostCall("saveConfiguration", params);
  })
  .handle("getKeymap", async () => {
    return await tool.hostCall("getKeymap");
  })
  .handle("saveKeymap", async (payload) => {
    var params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await tool.hostCall("saveKeymap", params);
  })
  .handle("validateKeymap", async (payload) => {
    var params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await tool.hostCall("validateKeymap", params);
  })
  .handle("getGestures", async () => {
    return await tool.hostCall("getGestures");
  })
  .handle("saveGestures", async (payload) => {
    var params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await tool.hostCall("saveGestures", params);
  })
  .handle("suspendGestures", async () => {
    return await tool.hostCall("suspendGestures");
  })
  .handle("resumeGestures", async () => {
    return await tool.hostCall("resumeGestures");
  })
  .handle("suspendHotkeys", async () => {
    return await tool.hostCall("suspendHotkeys");
  })
  .handle("resumeHotkeys", async () => {
    return await tool.hostCall("resumeHotkeys");
  })
  .handle("restart", async () => {
    return await tool.hostCall("restart");
  })
  .start();
