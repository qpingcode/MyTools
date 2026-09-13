import { pinyin } from "pinyin-pro";

/**
 * Returns whether every character in `pattern` occurs in `target` in the same order.
 * Matching is case-insensitive and characters do not need to be adjacent.
 *
 * @example isSubsequence("gthb", "GitHub") // true
 */
export function isSubsequence(pattern: string, target: string): boolean {
  if (!pattern) return true;
  if (!target) return false;

  const needle = normalizeSearchText(pattern);
  const haystack = normalizeSearchText(target);
  let patternIndex = 0;

  for (let targetIndex = 0; targetIndex < haystack.length && patternIndex < needle.length; targetIndex += 1) {
    if (haystack[targetIndex] === needle[patternIndex]) {
      patternIndex += 1;
    }
  }

  return patternIndex === needle.length;
}

export function normalizeSearchText(text: string): string {
  return text.normalize(searchNormalizationForm).toLowerCase().replace(/\s+/gu, normalizedWordSeparator).trim();
}

const titleVariantCacheLimit = 4096;
const searchNormalizationForm = "NFKC";
const normalizedWordSeparator = " ";
const pinyinOptions = { toneType: "none", type: "array" } as const;

const titleVariants = new Map<string, string[]>();

/** Compatible title recall: original text, multiword, pinyin and initials. */
export function matchesSearchText(query: string, title: string): boolean {
  const needle = normalizeSearchText(query);
  const target = normalizeSearchText(title);
  if (!needle) return true;
  if (isSubsequence(needle, target)) return true;
  const terms = needle.split(normalizedWordSeparator);
  if (terms.length > 1 && terms.every(term => target.includes(term))) return true;
  let variants = titleVariants.get(target);
  if (!variants) {
    if (titleVariants.size > titleVariantCacheLimit) titleVariants.clear();
    // Convert each character to agree with the host's character-based pinyin conversion.
    const syllables = Array.from(target, c => pinyin(c, pinyinOptions).join(""));
    variants = [syllables.join(""), syllables.map(s => s[0] ?? "").join(""),
      (target.match(/[\p{L}\p{N}]+/gu) ?? []).map(word => word[0]).join("")];
    titleVariants.set(target, variants);
  }
  return variants.some(variant => isSubsequence(needle, variant));
}
