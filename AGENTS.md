# MyTools repository instructions

These instructions apply to the entire repository.

## UI localization is required

Before implementing or reviewing any change that adds or modifies user-visible UI, read `.github/skills/ui-i18n/SKILL.md` completely and follow it.

- Localize every new or changed user-visible string in the same change. This includes window and menu text, buttons, tooltips, placeholders, status and validation messages, notifications, accessibility names, and backend-generated text displayed by a UI.
- Use the repository's WPF/RESX path for Desktop and built-in .NET UI, and its plugin JSON/i18next path for Node/Web UI. Do not mix the two resource systems.
- Update every locale declared by the affected surface. For the Desktop host this currently means the English fallback, `zh-CN`, and `fr-FR`; for a Node plugin, follow `plugin.json.i18n.supportedLocales`.
- Developer-only logs and diagnostics do not require translation unless their text is directly presented to users.
- Treat stable identifiers, protocol fields, configuration keys, plugin IDs, file paths, and persisted values as non-localizable.

