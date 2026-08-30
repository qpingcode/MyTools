import type { GlobalThemeOverrides } from "naive-ui";

function cssVar(name: string, fallback: string): string {
    const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return value || fallback;
}

export function readThemeOverrides(): GlobalThemeOverrides {
    const accent = cssVar("--mt-accent", "#607cff");
    const accentHover = cssVar("--mt-accent-hover", "#718bff");
    const accentPressed = cssVar("--mt-accent-pressed", "#4e67db");
    const border = cssVar("--mt-border", "#404040");

    return {
        common: {
            fontFamily: cssVar(
                "--mt-font-ui",
                '"Segoe UI Variable Text", "Segoe UI", "Microsoft YaHei UI", "Microsoft YaHei", sans-serif',
            ),
            primaryColor: accent,
            primaryColorHover: accentHover,
            primaryColorPressed: accentPressed,
            primaryColorSuppl: accent,
            textColorBase: cssVar("--mt-text", "#FFFFFF"),
            bodyColor: cssVar("--mt-surface-bg", "#1E1E1E"),
            cardColor: cssVar("--mt-surface", "#292929"),
            modalColor: cssVar("--mt-surface", "#292929"),
            borderColor: border,
            inputColor: cssVar("--mt-surface", "#292929"),
            tableColor: "transparent",
            scrollbarColor: border,
            errorColor: cssVar("--mt-error", "#e86a6a"),
            warningColor: cssVar("--mt-warning", "#b98525"),
            successColor: accent,
            infoColor: accent,
        },
        Input: {
            color: cssVar("--mt-surface", "#292929"),
            colorFocus: cssVar("--mt-surface-hover", "#3A3A3A"),
            border: `1px solid ${border}`,
            borderHover: `1px solid ${accentHover}`,
            borderFocus: `1px solid ${accent}`,
            borderRadius: "10px",
            caretColor: accent,
        },
        Select: {
            peers: {
                InternalSelection: {
                    color: cssVar("--mt-surface", "#292929"),
                    border: `1px solid ${border}`,
                    borderHover: `1px solid ${accentHover}`,
                    borderFocus: `1px solid ${accent}`,
                    borderRadius: "10px",
                },
            },
        },
        Button: {
            fontSizeSmall: "13px",
            fontWeight: "500",
            textColorPrimary: cssVar("--mt-accent-foreground", "#FFFFFF"),
            textColorHoverPrimary: cssVar("--mt-accent-foreground", "#FFFFFF"),
            textColorPressedPrimary: cssVar("--mt-accent-foreground", "#FFFFFF"),
            textColorFocusPrimary: cssVar("--mt-accent-foreground", "#FFFFFF"),
            textColorDisabledPrimary: cssVar("--mt-accent-foreground", "#FFFFFF"),
        },
        Switch: {
            railColor: cssVar("--mt-surface-alt", "#333333"),
            railColorActive: accent,
        },
        Checkbox: {
            colorChecked: accent,
            borderChecked: `1px solid ${accent}`,
        },
        Card: {
            color: cssVar("--mt-surface", "#292929"),
            borderColor: border,
            borderRadius: "12px",
        },
        Tabs: {
            tabTextColorLine: cssVar("--mt-text-tertiary", "#898f9c"),
            tabTextColorActiveLine: cssVar("--mt-accent", "#9AABFF"),
            tabTextColorHoverLine: cssVar("--mt-accent-hover", "#C5CEFF"),
            barColor: accent,
        },
    };
}

export function isDarkTheme(theme?: string): boolean {
    if (theme === "light") return false;
    if (theme === "dark") return true;
    return document.documentElement.getAttribute("data-theme") !== "light";
}
