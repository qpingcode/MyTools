// v3 backend for settings: same handlers as index.mts, runs over the v3 named-pipe bus.
// Each handler delegates to tool.hostCall(...) which the v3 SDK maps to host.call.<method>.

import { createTool } from "@qping/plugin-bus/server";

// Minimal i18n shim (v3 dedicated i18n module deferred).
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
  configure(_params: unknown): void {},
};

const tool = createTool();

tool
  .initialize((_params) => {
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
