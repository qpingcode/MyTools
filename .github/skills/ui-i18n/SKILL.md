---
name: ui-i18n
description: Implement or review user-visible MyTools UI text across the WPF/.NET host and Node/Web plugins. Use whenever a feature changes windows, menus, dialogs, settings, notifications, status or error messages, plugin results/actions, WebView pages, tooltips, placeholders, or accessibility labels.
---

# MyTools UI internationalization

Localize user-visible text as part of the feature that introduces or changes it. First identify whether the text belongs to the WPF/.NET host or to a Node/Web plugin; their resource formats and runtime APIs are intentionally different.

If the localization architecture itself is being changed, or behavior is ambiguous, read [`docs/i18n-architecture.md`](../../../docs/i18n-architecture.md). Ordinary UI work can follow this skill directly.

## Scope

Localize window titles, navigation and menu items, buttons, labels, headings, empty states, placeholders, tooltips, accessibility names, validation and error messages, toasts, status text, action/result text, and backend messages that reach users.

Do not translate developer-only logs, exception stacks, telemetry, stable IDs, protocol/capability names, configuration paths, storage keys, filenames, or persisted wire values. If a technical exception is exposed in UI, give the user-facing summary a localized message while preserving technical details separately.

Use stable PascalCase keys with a literal English fallback. Do not use English display text as a key or construct keys/default values dynamically. Use named placeholders such as `{{name}}`; source and every translation must contain the same placeholder set.

## WPF and .NET host

Host and built-in .NET text uses:

- `MyTools.Desktop/Localization/HostStrings.resx` as the English fallback source.
- `HostStrings.zh-CN.resx` and `HostStrings.fr-FR.resx` as the currently supported host translations.
- `Plugin.{PluginId}.*` keys for built-in .NET plugin text; otherwise use a stable feature-oriented host key.

In XAML, add the localization namespace when needed and use:

```xml
xmlns:loc="clr-namespace:MyTools.Desktop.Localization"
Content="{loc:Loc Feature.Action.Save, DefaultValue=Save}"
```

`LocExtension` listens for `LocaleChanged`, so prefer it for dependency properties that should refresh automatically. Localize `AutomationProperties.Name`, `ToolTip`, `Title`, placeholders, and other non-body text as well.

In ViewModels, services, and code-behind, prefer injected `ILocalizationService`:

```csharp
localization.GetCaption(
    "Feature.Status.Saved",
    "Saved {{name}}.",
    new { name });
```

Use `LocalizedMessage` when text crosses a layer before presentation, so the key, fallback, values, and optional translator comment remain intact. The static `LanguageService.GetCaption` overloads are compatibility APIs; do not introduce them where dependency injection or `LocalizedMessage` is practical.

For each new or changed host key, update all three host RESX files in the same change. Keep the English fallback at the call site consistent with `HostStrings.resx`. Code-computed text that remains visible while the language changes must be recomputed on `LocaleChanged`; translating it only once in a constructor is insufficient for live language switching.

## Node backend and Web frontend

Each plugin owns its JSON resources. Preserve or add the `plugin.json.i18n` block with `defaultLocale`, `catalog`, `localesPath`, and `supportedLocales`. Manifest display names use `{ "key", "defaultValue" }`, not a bare localized string.

The Node backend must configure localization from host initialization:

```ts
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

plugin.initialize((params) => {
  mytoolsI18n.configure(params);
  return {};
});
```

Localize backend-generated search results, action names/descriptions, validation errors, and user-facing outcomes with a literal call:

```ts
mytoolsI18n.t("Plugin.Example.Status.Ready", {
  defaultValue: "Ready for {{name}}",
  name,
});
```

Web pages create one shared `createWebBusClient()` instance. Use `bus.i18n.t(key, { defaultValue, ...values })` for dynamic text. Static HTML uses `data-i18n` together with `data-i18n-default-value`, including attribute targets such as `[placeholder]`, `[title]`, and `[aria-label]`.

Reactive pages must rerender translated computed state after both `HostEvents.Initialize` and `HostEvents.LanguageChanged`. Follow the existing `src/web/i18n.ts` locale-revision pattern in plugins such as `settings` and `store`; merely translating module-level constants once will leave stale text after a language change.

For every new or changed plugin key:

1. Add or update its entry in `i18n/catalog.en-US.json`, preserving that catalog's schema and placeholder metadata.
2. Update every JSON locale named by `plugin.json.i18n.supportedLocales`; at minimum, keep the English locale synchronized with the fallback and provide the corresponding translations for the other declared locales.
3. Keep locale files as flat key-to-string objects and preserve identical placeholder names.
4. Do not hand-edit `dist`; run the plugin build so packaged resources are regenerated.

When a Web page calls a Node handler, keep the established boundary: Web `bus.call(...)` → Node `plugin.handle(...)` → optional `plugin.hostCall(...)`. Localize at the layer that owns the user-facing meaning; do not send WPF RESX keys into plugin JSON or plugin keys into the host RESX.

## Verification

Before completing a UI change:

- Review changed UI paths for newly introduced literal display strings, including tooltips and error/status branches.
- Confirm every key has a literal English fallback and entries for all locales declared by that surface.
- Confirm translations preserve named placeholders.
- Build the affected .NET project for WPF changes and run its relevant tests.
- Run the affected plugin's `npm run check` and `npm run build` for Node/Web changes.

