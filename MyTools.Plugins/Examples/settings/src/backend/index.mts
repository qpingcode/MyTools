// Named-pipe backend for settings. Each handler delegates to plugin.hostCall(...)
// which the SDK maps to host.call.<method>.

import { createPlugin } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

const plugin = createPlugin();

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search((_params) => ({
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
    return await plugin.hostCall("getConfiguration");
  })
  .handle("saveConfiguration", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("saveConfiguration", params);
  })
  .handle("getKeymap", async () => {
    return await plugin.hostCall("getKeymap");
  })
  .handle("saveKeymap", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("saveKeymap", params);
  })
  .handle("validateKeymap", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("validateKeymap", params);
  })
  .handle("getGestures", async () => {
    return await plugin.hostCall("getGestures");
  })
  .handle("saveGestures", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("saveGestures", params);
  })
  .handle("suspendGestures", async () => {
    return await plugin.hostCall("suspendGestures");
  })
  .handle("resumeGestures", async () => {
    return await plugin.hostCall("resumeGestures");
  })
  .handle("suspendHotkeys", async () => {
    return await plugin.hostCall("suspendHotkeys");
  })
  .handle("resumeHotkeys", async () => {
    return await plugin.hostCall("resumeHotkeys");
  })
  .handle("checkHotKey", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("checkHotKey", params);
  })
  .handle("restart", async () => {
    return await plugin.hostCall("restart");
  })
  .start();
