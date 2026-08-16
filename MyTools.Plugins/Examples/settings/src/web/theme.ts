import type { ThemeDefinition } from "vuetify";

function cssVar(name: string, fallback: string): string {
    const value = getComputedStyle(document.documentElement).getPropertyValue(name).trim();
    return value || fallback;
}

export function readThemeColors(): ThemeDefinition["colors"] {
    return {
        background: cssVar("--mt-surface-bg", "#1E1E1E"),
        surface: cssVar("--mt-surface", "#292929"),
        "surface-bright": cssVar("--mt-surface-hover", "#3A3A3A"),
        "surface-light": cssVar("--mt-surface-alt", "#333333"),
        "surface-variant": cssVar("--mt-surface-alt", "#333333"),
        primary: cssVar("--mt-accent", "#3F51B5"),
        "primary-darken-1": cssVar("--mt-accent-hover", "#303F9F"),
        secondary: cssVar("--mt-surface-alt", "#333333"),
        error: "#f44336",
        info: cssVar("--mt-accent", "#3F51B5"),
        success: cssVar("--mt-accent", "#3F51B5"),
        warning: "#fb8c00",
        "on-background": cssVar("--mt-text", "#FFFFFF"),
        "on-surface": cssVar("--mt-text", "#FFFFFF"),
        "on-primary": cssVar("--mt-accent-foreground", "#FFFFFF"),
        "on-secondary": cssVar("--mt-text", "#FFFFFF"),
        "on-error": "#FFFFFF",
    };
}

export function isDarkTheme(theme?: string): boolean {
    if (theme === "light") return false;
    if (theme === "dark") return true;
    return document.documentElement.getAttribute("data-theme") !== "light";
}
