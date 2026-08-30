import { createPlugin, HostAction, Key, Modifiers } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

const plugin = createPlugin();
let content = "";

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions([
    {
      id: "copy",
      title: { key: "Plugin.Formatter.Action.Copy", defaultValue: "Copy" },
      description: {
        key: "Plugin.Formatter.Action.CopyDescription",
        defaultValue: "Copy the current content to the clipboard",
      },
      hotkey: { key: Key.E, modifiers: Modifiers.Control },
      execute: () => ({
        target: { kind: "host", action: { kind: HostAction.Copy, text: content } },
        after: "close",
      }),
    },
    {
      id: "format",
      title: { key: "Plugin.Formatter.Action.Format", defaultValue: "Format" },
      hotkey: { key: Key.Enter, modifiers: Modifiers.Control },
      execute: () => ({ target: { kind: "web", payload: { action: "format" } } }),
    },
    {
      id: "clear",
      title: { key: "Plugin.Formatter.Action.Clear", defaultValue: "Clear" },
      hotkey: { key: Key.L, modifiers: Modifiers.Control },
      execute: () => ({ target: { kind: "web", payload: { action: "clear" } } }),
    },
    {
      id: "collapse-all",
      title: { key: "Plugin.Formatter.Action.CollapseAll", defaultValue: "Collapse All" },
      hotkey: { key: Key.Up, modifiers: Modifiers.ControlShift },
      execute: () => ({ target: { kind: "web", payload: { action: "collapse-all" } } }),
    },
    {
      id: "expand-all",
      title: { key: "Plugin.Formatter.Action.ExpandAll", defaultValue: "Expand All" },
      hotkey: { key: Key.Down, modifiers: Modifiers.ControlShift },
      execute: () => ({ target: { kind: "web", payload: { action: "expand-all" } } }),
    },
  ])
  .handle("setContent", (payload) => {
    content = typeof payload?.content === "string" ? payload.content : "";
    return {};
  })
  .start();
