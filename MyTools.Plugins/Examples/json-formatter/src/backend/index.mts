import { createPlugin, HostAction, Key, Modifiers } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

const plugin = createPlugin();
let output = "";

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions([
    {
      id: "copy",
      title: { key: "Plugin.JsonFormatter.Action.Copy", defaultValue: "Copy" },
      description: {
        key: "Plugin.JsonFormatter.Action.CopyDescription",
        defaultValue: "Copy the formatted JSON to the clipboard",
      },
      hotkey: { key: Key.E, modifiers: Modifiers.Control },
      execute: () => ({ host: { kind: HostAction.Copy, text: output }, close: true }),
    },
    {
      id: "format",
      title: { key: "Plugin.JsonFormatter.Action.Format", defaultValue: "Format" },
      hotkey: { key: Key.Enter, modifiers: Modifiers.Control },
      execute: () => ({ web: { payload: { action: "format" } } }),
    },
    {
      id: "minify",
      title: { key: "Plugin.JsonFormatter.Action.Minify", defaultValue: "Minify" },
      hotkey: { key: Key.M, modifiers: Modifiers.Control },
      execute: () => ({ web: { payload: { action: "minify" } } }),
    },
    {
      id: "clear",
      title: { key: "Plugin.JsonFormatter.Action.Clear", defaultValue: "Clear" },
      hotkey: { key: Key.L, modifiers: Modifiers.Control },
      execute: () => ({ web: { payload: { action: "clear" } } }),
    },
    {
      id: "collapse-all",
      title: { key: "Plugin.JsonFormatter.Action.CollapseAll", defaultValue: "Collapse All" },
      hotkey: { key: Key.Up, modifiers: Modifiers.ControlShift },
      execute: () => ({ web: { payload: { action: "collapse-all" } } }),
    },
    {
      id: "expand-all",
      title: { key: "Plugin.JsonFormatter.Action.ExpandAll", defaultValue: "Expand All" },
      hotkey: { key: Key.Down, modifiers: Modifiers.ControlShift },
      execute: () => ({ web: { payload: { action: "expand-all" } } }),
    },
  ])
  .handle("setOutput", (payload) => {
    output = typeof payload?.output === "string" ? payload.output : "";
    return {};
  })
  .start();
