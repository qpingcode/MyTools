declare var MyToolsEventSubjects: {
  readonly host: {
    readonly initialize: "mytools.host.initialize";
    readonly search: "mytools.host.search";
    readonly key: "mytools.host.key";
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
}

interface Window {
  chrome?: {
    webview?: MyToolsWebView;
  };
  DeepSeekTranslatorSpeech?: {
    appendPhoneticRow(parent: Element, options: unknown): void;
  };
}
