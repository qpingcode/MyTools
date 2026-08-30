import { createPlugin, HostAction, Key, Modifiers } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";
import {
  CodecError,
  transformText,
  type CodecAlgorithm,
  type CodecMode,
  type HashOutputFormat,
} from "../shared/codec.js";

const plugin = createPlugin();
let copyText = "";

function record(payload: unknown): Record<string, unknown> {
  return payload && typeof payload === "object" ? payload as Record<string, unknown> : {};
}

function stringValue(value: unknown): string {
  return typeof value === "string" ? value : "";
}

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .actions([
    {
      id: "copy",
      title: { key: "Plugin.EncoderDecoder.Action.Copy", defaultValue: "Copy result" },
      description: { key: "Plugin.EncoderDecoder.Action.CopyDescription", defaultValue: "Copy the current result to the clipboard" },
      hotkey: { key: Key.E, modifiers: Modifiers.Control },
      execute: () => ({ target: { kind: "host", action: { kind: HostAction.Copy, text: copyText } }, after: "keep" }),
    },
    {
      id: "encode",
      title: { key: "Plugin.EncoderDecoder.Action.Encode", defaultValue: "Encode" },
      hotkey: { key: Key.E, modifiers: Modifiers.Control },
      execute: () => ({ target: { kind: "web", payload: { action: "encode" } } }),
    },
    {
      id: "decode",
      title: { key: "Plugin.EncoderDecoder.Action.Decode", defaultValue: "Decode" },
      hotkey: { key: Key.D, modifiers: Modifiers.Control },
      execute: () => ({ target: { kind: "web", payload: { action: "decode" } } }),
    },
    {
      id: "swap",
      title: { key: "Plugin.EncoderDecoder.Action.Swap", defaultValue: "Swap" },
      hotkey: { key: Key.R, modifiers: Modifiers.Control },
      execute: () => ({ target: { kind: "web", payload: { action: "swap" } } }),
    },
    {
      id: "clear",
      title: { key: "Plugin.EncoderDecoder.Action.Clear", defaultValue: "Clear" },
      hotkey: { key: Key.L, modifiers: Modifiers.Control },
      execute: () => ({ target: { kind: "web", payload: { action: "clear" } } }),
    },
    {
      id: "cycle-algorithm",
      title: { key: "Plugin.EncoderDecoder.Action.CycleAlgorithm", defaultValue: "Switch algorithm" },
      hotkey: { key: Key.Tab, modifiers: Modifiers.Control },
      execute: () => ({ target: { kind: "web", payload: { action: "cycle-algorithm" } } }),
    },
  ])
  .handle("setCopyText", (payload) => {
    copyText = stringValue(record(payload).text);
    return {};
  })
  .handle("transform", (payload) => {
    const data = record(payload);
    try {
      return {
        ok: true,
        ...transformText(
          stringValue(data.input),
          stringValue(data.mode) as CodecMode,
          stringValue(data.algorithm) as CodecAlgorithm,
          stringValue(data.hashFormat) as HashOutputFormat,
        ),
      };
    } catch (error) {
      return { ok: false, error: error instanceof CodecError ? error.code : "conversion-failed" };
    }
  })
  .start();
