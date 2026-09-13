import type { BrowserItem } from "./types.mjs";
export { isSubsequence } from "@qping/plugin-bus/search";
import { matchesSearchText, normalizeSearchText } from "@qping/plugin-bus/search";

const MaxFuzzyQueryLength = 12;
const Scores = { bookmark: 80, history: 50, exact: 100, prefix: 95, titleContains: 88,
  urlContains: 76, fuzzyOrMetadata: 62, bookmarkBonus: 4, historyCap: 70,
  historyBase: 40, historyVisitCap: 20 } as const;

export function itemMatches(item: BrowserItem, query: string): boolean {
  query = normalizeSearchText(query);
  if (!query) {
    return true;
  }
  const title = normalizeSearchText(item.title || "");
  const url = item.url || "";
  const folder = item.folderPath || "";
  const profile = item.profileName || "";
  if (title.includes(query) || url.toLowerCase().includes(query)) {
    return true;
  }
  if (folder.toLowerCase().includes(query) || profile.toLowerCase().includes(query)) {
    return true;
  }
  const terms = query.split(" ");
  if (terms.length > 1 && terms.every(term => title.includes(term))) return true;
  // Subsequence matching is useful for short abbreviations such as "gthb" -> "GitHub".
  // On long input it becomes both surprising and noisy because large page titles can contain
  // the requested characters far apart without representing a meaningful match.
  return query.length <= MaxFuzzyQueryLength
    && !/\s/.test(query)
    && matchesSearchText(query, title);
}

export function itemPriority(item: BrowserItem, query: string): number {
  query = normalizeSearchText(query);
  const title = normalizeSearchText(item.title || "");
  const url = (item.url || "").toLowerCase();
  let score: number = item.kind === "bookmark" ? Scores.bookmark : Scores.history;
  if (query) {
    if (title === query) {
      score = Scores.exact;
    } else if (title.startsWith(query) || url.startsWith(query)) {
      score = Scores.prefix;
    } else if (title.includes(query)) {
      score = Scores.titleContains;
    } else if (url.includes(query)) {
      score = Scores.urlContains;
    } else {
      score = Scores.fuzzyOrMetadata;
    }
    if (item.kind === "bookmark") {
      score += Scores.bookmarkBonus;
    }
  } else if (item.kind === "history") {
    score = Math.min(Scores.historyCap, Scores.historyBase + Math.min(item.visitCount, Scores.historyVisitCap));
  }
  return score;
}
