import i18next, { type i18n } from "i18next";

export type TranslationValues = Record<string, unknown>;

export type TranslationOptions = TranslationValues & {
  defaultValue: string;
  translatorComment?: string;
};

type LocalizationPayload = {
  locale?: string;
  fallbackLocale?: string;
  messages?: Record<string, string>;
};

class MyToolsI18n {
  readonly #instance: i18n = i18next.createInstance();
  #locale = "en-US";
  #fallbackLocale = "en-US";
  #messages: Record<string, string> = {};

  get language(): string {
    return this.#locale;
  }

  configure(payload: LocalizationPayload | Record<string, unknown> | null | undefined): void {
    if (!payload || typeof payload !== "object") {
      return;
    }
    this.#locale = typeof payload.locale === "string" ? payload.locale : this.#locale;
    this.#fallbackLocale = typeof payload.fallbackLocale === "string"
      ? payload.fallbackLocale
      : this.#fallbackLocale;
    this.#messages = isRecord(payload.messages)
      ? Object.fromEntries(Object.entries(payload.messages).filter((entry): entry is [string, string] => typeof entry[1] === "string"))
      : {};
    void this.#instance.init({
      lng: this.#locale,
      fallbackLng: this.#fallbackLocale,
      initImmediate: false,
      debug: false,
      showSupportNotice: false,
      interpolation: { escapeValue: false },
      resources: {
        [this.#locale]: { translation: this.#messages }
      }
    });
  }

  t(key: string, options: TranslationOptions): string {
    if (!key || typeof key !== "string") {
      throw new Error("i18n.t requires a stable key.");
    }
    if (!options || typeof options.defaultValue !== "string") {
      throw new Error("i18n.t requires a string defaultValue.");
    }

    const { translatorComment: _, ...runtimeOptions } = options;
    if (!this.#instance.isInitialized) {
      return options.defaultValue.replace(/\{\{\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*\}\}/g, (placeholder, name: string) => {
        const value = options[name];
        return value === undefined || value === null ? placeholder : String(value);
      });
    }
    return this.#instance.t(key, runtimeOptions);
  }

  apply(root?: ParentNode): void {
    if (typeof document === "undefined") {
      return;
    }
    const target = root ?? document;
    target.querySelectorAll<HTMLElement>("[data-i18n]").forEach((element) => {
      const descriptor = element.dataset.i18n ?? "";
      const parsed = parseDescriptor(descriptor);
      const defaultValue = element.dataset.i18nDefaultValue ?? element.textContent ?? parsed.key;
      const value = this.t(parsed.key, { defaultValue });
      parsed.attributes.forEach((attribute) => {
        if (attribute === "text") {
          element.textContent = value;
        } else {
          element.setAttribute(attribute, value);
        }
      });
    });
    document.documentElement.lang = this.#locale;
  }
}

function parseDescriptor(descriptor: string): { attributes: string[]; key: string } {
  const attributes: string[] = [];
  let key = descriptor;
  while (key.startsWith("[")) {
    const closingBracket = key.indexOf("]");
    if (closingBracket <= 1) {
      break;
    }

    attributes.push(key.slice(1, closingBracket));
    key = key.slice(closingBracket + 1);
  }

  return key
    ? { attributes: attributes.length > 0 ? attributes : ["text"], key }
    : { attributes: ["text"], key: descriptor };
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === "object" && value !== null;
}

export const mytoolsI18n = new MyToolsI18n();
