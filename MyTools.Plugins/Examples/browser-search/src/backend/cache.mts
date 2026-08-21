import fs from "node:fs";

type CacheEntry<T> = {
  signature: string;
  value: T;
};

const memory = new Map<string, CacheEntry<unknown>>();

export function fileSignature(paths: string[]): string {
  return paths.map((filePath) => {
    try {
      const stat = fs.statSync(filePath);
      return `${filePath}:${stat.mtimeMs}:${stat.size}`;
    } catch {
      return `${filePath}:missing`;
    }
  }).join("|");
}

export function cached<T>(key: string, signature: string, load: () => T): T {
  const existing = memory.get(key) as CacheEntry<T> | undefined;
  if (existing && existing.signature === signature) {
    return existing.value;
  }
  const value = load();
  memory.set(key, { signature, value });
  return value;
}

export async function cachedAsync<T>(key: string, signature: string, load: () => Promise<T>): Promise<T> {
  const existing = memory.get(key) as CacheEntry<T> | undefined;
  if (existing && existing.signature === signature) {
    return existing.value;
  }
  const value = await load();
  memory.set(key, { signature, value });
  return value;
}
