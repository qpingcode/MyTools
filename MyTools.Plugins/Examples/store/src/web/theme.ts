import type { GlobalThemeOverrides } from "naive-ui";

function cssVar(name: string, fallback: string): string {
    const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return value || fallback;
}

export function readThemeOverrides(): GlobalThemeOverrides {
    const accent = cssVar("--settings-accent", "#22c55e");
    const accentHover = cssVar("--settings-accent-hover", "#16a34a");
    const accentPressed = cssVar("--settings-accent-pressed", "#15803d");
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
            errorColor: "#f44336",
            warningColor: "#fb8c00",
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
    };
}

export function isDarkTheme(theme?: string): boolean {
    if (theme === "light") return false;
    if (theme === "dark") return true;
    return document.documentElement.getAttribute("data-theme") !== "light";
}
