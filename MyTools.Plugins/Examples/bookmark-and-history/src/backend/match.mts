import type { BrowserItem } from "./types.mjs";
export { isSubsequence } from "@qping/plugin-bus/search";
import { isSubsequence } from "@qping/plugin-bus/search";

const MaxSubsequenceQueryLength = 12;

export function itemMatches(item: BrowserItem, query: string): boolean {
  if (!query) {
    return true;
  }
  const title = item.title || "";
  const url = item.url || "";
  const folder = item.folderPath || "";
  const profile = item.profileName || "";
  if (title.toLowerCase().includes(query) || url.toLowerCase().includes(query)) {
    return true;
  }
  if (folder.toLowerCase().includes(query) || profile.toLowerCase().includes(query)) {
    return true;
  }
  // Subsequence matching is useful for short abbreviations such as "gthb" -> "GitHub".
  // On long input it becomes both surprising and noisy because large page titles can contain
  // the requested characters far apart without representing a meaningful match.
  return query.length <= MaxSubsequenceQueryLength
    && !/\s/.test(query)
    && isSubsequence(query, title);
}

export function itemPriority(item: BrowserItem, query: string): number {
  const title = (item.title || "").toLowerCase();
  const url = (item.url || "").toLowerCase();
  let score = item.kind === "bookmark" ? 80 : 50;
  if (query) {
    if (title === query) {
      score = 100;
    } else if (title.startsWith(query) || url.startsWith(query)) {
      score = 95;
    } else if (title.includes(query)) {
      score = 88;
    } else if (url.includes(query)) {
      score = 76;
    } else {
      score = 62;
    }
    if (item.kind === "bookmark") {
      score += 4;
    }
  } else if (item.kind === "history") {
    score = Math.min(70, 40 + Math.min(item.visitCount, 20));
  }
  return score;
}
