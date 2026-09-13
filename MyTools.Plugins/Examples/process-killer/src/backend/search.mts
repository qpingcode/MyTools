import { matchesSearchText } from "@qping/plugin-bus/search";

const DecimalRadix = 10;

export function matches(processInfo: { name: string; id: number; port: number }, query: string): boolean {
  if (!query) return true;
  var asInt = Number.parseInt(query, DecimalRadix);
  if (String(asInt) === query && (processInfo.id === asInt || processInfo.port === asInt)) {
    return true;
  }
  return matchesSearchText(query, processInfo.name);
}

