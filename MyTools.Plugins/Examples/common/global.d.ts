declare var MyToolsEventSubjects: {
  readonly host: {
    readonly initialize: "mytools.host.initialize";
    readonly search: "mytools.host.search";
    readonly key: "mytools.host.key";
    readonly languageChanged: "mytools.host.language-changed";
    readonly themeChanged: "mytools.host.theme-changed";
  };
};

type MyToolsEvents = typeof MyToolsEventSubjects;

interface MyToolsHostInitializePayload {
  protocolVersion: string;
  pluginId: string;
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

interface MyToolsLanguageChangedPayload {
  locale: string;
  fallbackLocale: string;
  translationRevision: string;
  messages: Record<string, string>;
}

interface MyToolsThemeChangedPayload {
  theme: string;
  themeTokens: Record<string, string>;
}

interface MyToolsHostSearchPayload {
  query: string;
}

interface MyToolsHostKeyPayload {
  key: string;
}

interface MyToolsEventPayloadMap {
  "mytools.host.initialize": MyToolsHostInitializePayload;
  "mytools.host.search": MyToolsHostSearchPayload;
  "mytools.host.key": MyToolsHostKeyPayload;
  "mytools.host.language-changed": MyToolsLanguageChangedPayload;
  "mytools.host.theme-changed": MyToolsThemeChangedPayload;
}

type MyToolsEventPayload<TSubject extends string> =
  TSubject extends keyof MyToolsEventPayloadMap
    ? MyToolsEventPayloadMap[TSubject]
    : unknown;

interface MyToolsEventMeta<TSubject extends string = string> {
  subjectId: TSubject;
}

interface MyToolsWebView {
  postMessage(message: unknown): void;
  addEventListener(type: "message", listener: (event: { data: unknown }) => void): void;
}

interface MyToolsTool {
  call<T = unknown>(action: string, params?: unknown, options?: { timeout?: number }): Promise<T>;
  subscribe<TSubject extends string>(
    subjectId: TSubject,
    callback: (payload: MyToolsEventPayload<TSubject>, meta: MyToolsEventMeta<TSubject>) => void
  ): () => void;
  events: MyToolsEvents;
  ready(pluginId?: string): void;
  i18n: {
    readonly language: string;
    t(key: string, options: Record<string, unknown> & { defaultValue: string; translatorComment?: string }): string;
    apply(root?: ParentNode): void;
  };
  theme: {
    current: string;
    apply(payload: { theme?: string; themeTokens?: Record<string, string> }): void;
  };
}

interface Window {
  chrome?: {
    webview?: MyToolsWebView;
  };
  DeepSeekTranslatorSpeech?: {
    appendPhoneticRow(parent: Element, options: unknown): void;
  };
}
