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
  .handle("captureInputAction", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("captureInputAction", params);
  })
  .handle("restart", async () => {
    return await plugin.hostCall("restart");
  })
  .start();
