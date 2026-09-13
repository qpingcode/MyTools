import { matchesSearchText, normalizeSearchText } from "@qping/plugin-bus/search";

export type Phrase = {
  trigger?: string;
  content?: string;
  timestamp?: string;
};

const Scores = { titlePrefix: 100, titleContains: 90, titleFuzzy: 80, contentContains: 70, unmatched: 0 } as const;

export function priority(title: string, content: string, query: string): number {
  title = normalizeSearchText(title);
  query = normalizeSearchText(query);
  content = content.toLowerCase();
  if (!query) return Scores.titleFuzzy;
  if (title.startsWith(query)) return Scores.titlePrefix;
  if (title.includes(query)) return Scores.titleContains;
  if (matchesSearchText(query, title)) return Scores.titleFuzzy;
  if (content.includes(query)) return Scores.contentContains;
  return Scores.unmatched;
}

export function matches(phrase: Phrase, query: string, showAll: boolean, title = phrase.trigger || ""): boolean {
  query = normalizeSearchText(query);
  const content = (phrase.content || "").toLowerCase();
  if (!query) return showAll;
  return matchesSearchText(query, title)
    || content.includes(query);
}

