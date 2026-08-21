import type { BrowserItem } from "./types.mjs";

export function isSubsequence(pattern: string, target: string): boolean {
  if (!pattern) {
    return true;
  }
  if (!target) {
    return false;
  }
  let pi = 0;
  let ti = 0;
  const needle = pattern.toLowerCase();
  const haystack = target.toLowerCase();
  while (ti < haystack.length && pi < needle.length) {
    if (haystack[ti] === needle[pi]) {
      pi += 1;
    }
    ti += 1;
  }
  return pi === needle.length;
}

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
  return isSubsequence(query, title);
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
