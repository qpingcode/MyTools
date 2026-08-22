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
      execute: () => ({ host: { kind: HostAction.Copy, text: output } }),
    },
    {
      id: "minify",
      title: { key: "Plugin.JsonFormatter.Detail.Minify", defaultValue: "Minify" },
      hotkey: { key: Key.M, modifiers: Modifiers.Control },
      execute: () => ({ web: { payload: { action: "minify" } } }),
    },
  ])
  .handle("setOutput", (payload) => {
    output = typeof payload?.output === "string" ? payload.output : "";
    return {};
  })
  .start();
