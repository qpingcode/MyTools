/** Environment the host stamps onto every plugin.call payload. */

export type PluginTheme = "light" | "dark";

export type PluginHostEnv = {
  locale: string;
  fallbackLocale: string;
  theme: PluginTheme;
};

export function asTheme(value: unknown): PluginTheme {
  return value === "light" ? "light" : "dark";
}

export function asHostEnv(payload: any): PluginHostEnv {
  return {
    locale: typeof payload?.locale === "string" ? payload.locale : "en-US",
    fallbackLocale: typeof payload?.fallbackLocale === "string" ? payload.fallbackLocale : "en-US",
    theme: asTheme(payload?.theme),
  };
}
