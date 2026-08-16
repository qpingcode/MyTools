// Named-pipe backend for settings. Each handler delegates to tool.hostCall(...)
// which the SDK maps to host.call.<method>.

import { createTool } from "@qping/plugin-bus/server";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

const tool = createTool();

tool
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search((_params: any) => ({
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
  .handle("saveConfiguration", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await tool.hostCall("saveConfiguration", params);
  })
  .handle("getKeymap", async () => {
    return await tool.hostCall("getKeymap");
  })
  .handle("saveKeymap", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await tool.hostCall("saveKeymap", params);
  })
  .handle("validateKeymap", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await tool.hostCall("validateKeymap", params);
  })
  .handle("getGestures", async () => {
    return await tool.hostCall("getGestures");
  })
  .handle("saveGestures", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
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
