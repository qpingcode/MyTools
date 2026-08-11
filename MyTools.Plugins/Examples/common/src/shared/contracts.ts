export interface MyToolsEvents {
  readonly host: {
    readonly initialize: "mytools.host.initialize";
    readonly search: "mytools.host.search";
    readonly key: "mytools.host.key";
    readonly languageChanged: "mytools.host.language-changed";
    readonly themeChanged: "mytools.host.theme-changed";
  };
}

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

export interface MyToolsEventPayloadMap {
  "mytools.host.initialize": MyToolsHostInitializePayload;
  "mytools.host.search": MyToolsHostSearchPayload;
  "mytools.host.key": MyToolsHostKeyPayload;
  "mytools.host.language-changed": MyToolsLanguageChangedPayload;
  "mytools.host.theme-changed": MyToolsThemeChangedPayload;
}

export type MyToolsEventPayload<TSubject extends string> =
  TSubject extends keyof MyToolsEventPayloadMap
    ? MyToolsEventPayloadMap[TSubject]
    : unknown;

export interface MyToolsEventMeta<TSubject extends string = string> {
  subjectId: TSubject;
}

export interface MyToolsI18nApi {
  readonly language: string;
  t(key: string, options: Record<string, unknown> & { defaultValue: string; translatorComment?: string }): string;
  apply(root?: ParentNode): void;
}

export interface MyToolsThemePayload {
  theme?: string;
  themeTokens?: Record<string, string>;
}

export interface MyToolsThemeApi {
  current: string;
  apply(payload: MyToolsThemePayload): void;
}

export interface MyToolsTool {
  call<T = unknown>(action: string, params?: unknown, options?: { timeout?: number }): Promise<T>;
  subscribe<TSubject extends string>(
    subjectId: TSubject,
    callback: (payload: MyToolsEventPayload<TSubject>, meta: MyToolsEventMeta<TSubject>) => void
  ): () => void;
  events: MyToolsEvents;
  ready(pluginId?: string): void;
  i18n: MyToolsI18nApi;
  theme: MyToolsThemeApi;
}

export interface MyToolsWebView {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
}

declare global {
  interface Window {
    chrome?: {
      webview?: MyToolsWebView;
    };
    DeepSeekTranslatorSpeech?: {
      appendPhoneticRow(parent: Element, options: unknown): void;
    };
  }
}

export {};
