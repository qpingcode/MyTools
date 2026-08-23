/**
 * Action registry types.
 *
 * A plugin registers its full action set once at startup via `plugin.actions([...])`. Search
 * items and the detail page only reference actions by id; the parameters an action needs are
 * produced inside its own `execute`, never carried on the item.
 */

import type { PluginHostEnv } from "./hostEnv.ts";

/** An i18n message key plus the English fallback the host uses when the key is missing. */
export type LocalizedText = {
  key: string;
  defaultValue: string;
};

/** Keys the host deliberately permits for action shortcuts. */
export const Key = {
  Enter: "Enter", Tab: "Tab", Space: "Space", Delete: "Delete", Backspace: "Backspace",
  Escape: "Escape", Left: "Left", Right: "Right", Up: "Up", Down: "Down",
  A: "A", B: "B", C: "C", D: "D", E: "E", F: "F", G: "G", H: "H", I: "I",
  J: "J", K: "K", L: "L", M: "M", N: "N", O: "O", P: "P", Q: "Q", R: "R",
  S: "S", T: "T", U: "U", V: "V", W: "W", X: "X", Y: "Y", Z: "Z",
  D0: "D0", D1: "D1", D2: "D2", D3: "D3", D4: "D4",
  D5: "D5", D6: "D6", D7: "D7", D8: "D8", D9: "D9",
  F1: "F1", F2: "F2", F3: "F3", F4: "F4", F5: "F5", F6: "F6",
  F7: "F7", F8: "F8", F9: "F9", F10: "F10", F11: "F11", F12: "F12",
} as const;

export type HotkeyKey = (typeof Key)[keyof typeof Key];

/** Permitted modifier combinations, e.g. `Modifiers.ControlShift`. */
export const Modifiers = {
  None: 0,
  Control: 1,
  Alt: 2,
  ControlAlt: 3,
  Shift: 4,
  ControlShift: 5,
  AltShift: 6,
  ControlAltShift: 7,
} as const;

export type HotkeyModifiers = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 7;

export type Hotkey = {
  key: HotkeyKey;
  modifiers?: HotkeyModifiers;
};

/** Actions the host itself can carry out. Anything else belongs in `execute`. */
export const HostAction = {
  Copy: "copy",
  CopyAndPaste: "copyAndPaste",
  AddClipboardHistory: "addClipboardHistory",
  Execute: "execute",
  OpenInExplorer: "openInExplorer",
  OpenInBrowser: "openInBrowser",
  OpenPlugin: "openPlugin",
  Run: "run",
  Kill: "kill",
} as const;

export type HostActionKind = (typeof HostAction)[keyof typeof HostAction];

/** Command spec for {@link HostAction.Run}. */
export type RunSpec = {
  name?: string;
  command: string;
  args?: string;
  workingDirectory?: string;
  runAsAdmin?: boolean;
  isBashScript?: boolean;
  scripts?: string | string[];
};

/**
 * What the host should do, with the parameters that kind actually needs. The discriminated union
 * is the point: `{ kind: HostAction.Copy, path }` does not compile.
 */
export type HostActionRequest =
  | { kind: typeof HostAction.Copy; text: string }
  | { kind: typeof HostAction.CopyAndPaste; text: string }
  | { kind: typeof HostAction.AddClipboardHistory; texts: string[] }
  | { kind: typeof HostAction.Execute; path: string; args?: string; runAsAdmin?: boolean }
  | { kind: typeof HostAction.OpenInExplorer; path: string }
  | { kind: typeof HostAction.OpenInBrowser; url: string | string[] }
  | { kind: typeof HostAction.OpenPlugin; pluginId: string }
  | { kind: typeof HostAction.Run; command: RunSpec }
  | { kind: typeof HostAction.Kill; pid: number };

/** Opens a web detail page. `page` defaults to the entry declared in plugin.json. */
export type DetailRequest = {
  page?: string;
  title?: string;
  initialState?: unknown;
};

export type ActionTarget =
  /** Run one privileged host-side action (clipboard, process launch, browser, ...). */
  | { kind: "host"; action: HostActionRequest }
  /** Hand off to the currently active detail page as host.event.detailAction. */
  | { kind: "web"; payload?: unknown }
  /** Open a new web detail page. */
  | ({ kind: "detail" } & DetailRequest);

export type ActionAfter = "keep" | "close" | "refresh";

type ActionOutcomeBase = {
  /** Status bar text. */
  message?: LocalizedText;
};

/**
 * The result of running an action. An action selects at most one execution target. Host targets
 * may choose a follow-up lifecycle action; web and detail targets keep their current surface alive.
 */
export type ActionOutcome = ActionOutcomeBase & (
  | { target?: undefined; after?: ActionAfter }
  | { target: Extract<ActionTarget, { kind: "host" }>; after?: ActionAfter }
  | { target: Exclude<ActionTarget, { kind: "host" }>; after?: "keep" }
);

/**
 * What an action sees when it runs. `item` is the original object returned by `search()`,
 * including fields the host never saw — the SDK keeps it so actions do not have to re-derive
 * their data from the item id.
 */
export type ActionContext<TItem = unknown> = PluginHostEnv & {
  actionId: string;
  itemId: string;
  query: string;
  item?: TItem;
};

/**
 * One registered action. `hotkey` is optional: without it the first registered action gets Enter
 * and the rest are click-only, matching search result items.
 */
export type ActionDefinition<TItem = any> = {
  id: string;
  title: LocalizedText;
  description?: LocalizedText;
  hotkey?: Hotkey;
  execute: (context: ActionContext<TItem>) => ActionOutcome | void | Promise<ActionOutcome | void>;
};

/** The registry shape sent to the host in the initialize response (no `execute`). */
export type ActionManifestEntry = {
  id: string;
  title: LocalizedText;
  description?: LocalizedText;
  hotkey?: Hotkey;
};

export function toActionManifest(definition: ActionDefinition): ActionManifestEntry {
  const entry: ActionManifestEntry = {
    id: definition.id,
    title: definition.title,
  };
  if (definition.description) entry.description = definition.description;
  if (definition.hotkey) entry.hotkey = definition.hotkey;
  return entry;
}
