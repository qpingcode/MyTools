import { createPlugin, type PluginSearchParams } from "@qping/plugin-bus/node";
import { mytoolsI18n } from "@qping/plugin-bus/i18n";

type Phrase = {
  trigger?: string;
  content?: string;
  timestamp?: string;
};

type OwnConfiguration = {
  values?: {
    Phrases?: Phrase[];
  };
};

const plugin = createPlugin();

function pasteAction() {
  return {
    id: "paste",
    title: mytoolsI18n.t("Plugin.Snippet.Action.Paste", { defaultValue: "Copy and paste" }),
    kind: "copyAndPaste",
    description: mytoolsI18n.t("Plugin.Snippet.Action.PasteDescription", {
      defaultValue: "Copy the phrase and paste it into the previously focused window",
    }),
  };
}

function preview(text: string): string {
  const compact = text.replace(/\s+/g, " ").trim();
  return compact.length > 80 ? `${compact.slice(0, 77)}...` : compact;
}

function priority(trigger: string, content: string, query: string): number {
  if (trigger.startsWith(query)) return 100;
  if (trigger.includes(query)) return 90;
  if (content.includes(query)) return 70;
  return 0;
}

function matches(phrase: Phrase, query: string, showAll: boolean): boolean {
  const trigger = (phrase.trigger || "").toLowerCase();
  const content = (phrase.content || "").toLowerCase();
  if (!query) return showAll;
  return trigger.includes(query) || content.includes(query);
}

async function loadPhrases(): Promise<Phrase[]> {
  const result = (await plugin.hostCall("configuration.readOwn")) as OwnConfiguration;
  const phrases = result?.values?.Phrases;
  return Array.isArray(phrases) ? phrases : [];
}

async function search(params: PluginSearchParams) {
  const query = (params.query || "").trim().toLowerCase();
  const showAll = params.mode === "plugin";
  if (!query && !showAll) {
    return { items: [] };
  }

  let phrases: Phrase[] = [];
  try {
    phrases = await loadPhrases();
  } catch {
    return { items: [] };
  }

  const items = phrases
    .filter((phrase) => matches(phrase, query, showAll))
    .map((phrase, index) => {
      const trigger = (phrase.trigger || "").trim();
      const content = phrase.content || "";
      const title = trigger || preview(content) || mytoolsI18n.t("Plugin.Snippet.Untitled", { defaultValue: "Untitled phrase" });
      return {
        id: `snippet:${index}:${trigger}`,
        title,
        subtitle: preview(content),
        priority: query ? priority(trigger.toLowerCase(), content.toLowerCase(), query) : 80,
        icon: { kind: "emoji", value: "💬" },
        copyText: content,
        actions: [pasteAction()],
      };
    })
    .filter((item) => (item.copyText || "").length > 0)
    .sort((left, right) => right.priority - left.priority);

  return { items };
}

plugin
  .initialize((params) => {
    mytoolsI18n.configure(params);
    return {};
  })
  .search(search)
  .start();
