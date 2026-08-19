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
    return await plugin.hostCall("configuration.read");
  })
  .handle("saveConfiguration", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("configuration.write", params);
  })
  .handle("getPluginOverrides", async () => {
    const [keymap, hotkeys] = await Promise.all([
      plugin.hostCall("keymap.read") as Promise<any>,
      plugin.hostCall("hotkeys.read") as Promise<any>,
    ]);
    const hotkeysByPlugin = new Map<string, Record<string, unknown>>(
      (hotkeys?.plugins || []).map((item: Record<string, unknown>) => [String(item.pluginId), item]),
    );
    return {
      plugins: (keymap?.plugins || []).map((item: any) => ({
        ...item,
        ...(hotkeysByPlugin.get(item.pluginId) || {}),
      })),
    };
  })
  .handle("saveKeymap", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("keymap.write", params);
  })
  .handle("saveHotKeys", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("hotkeys.write", params);
  })
  .handle("validateKeymap", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("keymap.validate", params);
  })
  .handle("validateHotKeys", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("hotkeys.validate", params);
  })
  .handle("getGestures", async () => {
    return await plugin.hostCall("gestures.read");
  })
  .handle("saveGestures", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("gestures.write", params);
  })
  .handle("suspendGestures", async () => {
    return await plugin.hostCall("gestures.suspend");
  })
  .handle("resumeGestures", async () => {
    return await plugin.hostCall("gestures.resume");
  })
  .handle("captureInputAction", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("action.capture", params);
  })
  .handle("pickPath", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("path.pick", params);
  })
  .handle("validatePath", async (payload: any) => {
    const params = (payload && typeof payload === "object" ? payload : {}) as Record<string, unknown>;
    return await plugin.hostCall("path.validate", params);
  })
  .handle("restart", async () => {
    return await plugin.hostCall("restart");
  })
  .start();
