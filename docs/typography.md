# MyTools typography

Typography is independent from the light and dark color themes. Switching a color theme changes color tokens only; WPF views and plugin WebViews keep the same font families, semantic sizes, and line heights.

| Role | WPF resource | Web token | Size |
| --- | --- | --- | ---: |
| Caption | `FontSizeCaption` | `--mt-font-size-caption` | 11 |
| Small/supporting text | `FontSizeSmall` | `--mt-font-size-small` | 12 |
| Body/control text | `FontSizeBody` | `--mt-font-size-body` | 14 |
| Subheading | `FontSizeSubheading` | `--mt-font-size-subheading` | 16 |
| Level 2 heading | `FontSizeHeading2` | `--mt-font-size-heading-2` | 18 |
| Level 1 heading | `FontSizeHeading1` | `--mt-font-size-heading-1` | 24 |
| Display text | `FontSizeDisplay` | `--mt-font-size-display` | 32 |

WPF resources live in `MyTools.Desktop/Themes/Typography.xaml`. Use them through `DynamicResource`, for example `FontSize="{DynamicResource FontSizeBody}"`. Matching `LineHeight*`, `UiFontFamily`, and `MonoFontFamily` resources are available there.

Web resources live in `MyTools.Desktop/Themes/WebTypographyTokens.cs` and are injected into every plugin page together with the active color variables. Plugin CSS should use a fallback so it remains usable outside MyTools, for example `font-size: var(--mt-font-size-body, 14px)`.

Use typography tokens for readable text. Keep icon-font, emoji, gesture-arrow, spinner, and similar glyph sizes local to their component because those values control visual geometry rather than text hierarchy.
