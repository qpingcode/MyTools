/**
 * Returns whether every character in `pattern` occurs in `target` in the same order.
 * Matching is case-insensitive and characters do not need to be adjacent.
 *
 * @example isSubsequence("gthb", "GitHub") // true
 */
export function isSubsequence(pattern: string, target: string): boolean {
  if (!pattern) return true;
  if (!target) return false;

  const needle = pattern.toLowerCase();
  const haystack = target.toLowerCase();
  let patternIndex = 0;

  for (let targetIndex = 0; targetIndex < haystack.length && patternIndex < needle.length; targetIndex += 1) {
    if (haystack[targetIndex] === needle[patternIndex]) {
      patternIndex += 1;
    }
  }

  return patternIndex === needle.length;
}
