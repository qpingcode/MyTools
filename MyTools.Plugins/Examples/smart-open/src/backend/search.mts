import { matchesSearchText, normalizeSearchText } from "@qping/plugin-bus/search";

/** Match display titles with the host-compatible SDK while retaining subtitle recall. */
export function itemMatches(item: { title: string; subtitle: string }, query: string): boolean {
  return matchesSearchText(query, item.title)
    || normalizeSearchText(item.subtitle).includes(normalizeSearchText(query));
}
