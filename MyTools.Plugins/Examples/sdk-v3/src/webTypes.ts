import { Routes } from "./protocol.ts";

export interface MyToolsHostInitializePayload {
  protocolVersion: string;
  pluginId: string;
  version?: string;
  itemId: string;
  query: string;
  keyword: string;
  initialState: unknown;
  locale: string;
  fallbackLocale: string;
  translationRevision: string;
  messages: Record<string, string>;
  theme?: string;
  themeTokens?: Record<string, string>;
}

export interface MyToolsLanguageChangedPayload {
  locale: string;
  fallbackLocale: string;
  translationRevision: string;
  messages: Record<string, string>;
}

export interface MyToolsThemeChangedPayload {
  theme: string;
  themeTokens: Record<string, string>;
}

export interface MyToolsHostSearchPayload {
  query: string;
}

export interface MyToolsHostKeyPayload {
  key: string;
}

export interface MyToolsInputActionCapturedPayload {
  requestId: string;
  cancelled?: boolean;
  kind?: "hotkey" | "mouse";
  hotKey?: string | null;
  mouseButton?: string | null;
}

export interface MyToolsThemePayload {
  theme?: string;
  themeTokens?: Record<string, string>;
}

export const HostEvents = Routes.HostEvent;
