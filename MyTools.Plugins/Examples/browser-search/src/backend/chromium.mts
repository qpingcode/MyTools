import fs from "node:fs";
import path from "node:path";
import { cached, cachedAsync, fileSignature } from "./cache.mjs";
import { rowNumber, rowText, readSqliteQuery } from "./sqlite.mjs";
import type { BrowserItem, BrowserKind, BrowserProfile } from "./types.mjs";

const SKIP_PROFILE_DIRS = new Set([
  "system profile",
  "guest profile",
  "crashpad",
  "grshadercache",
  "shadercache",
  "browsermetrics",
  "safe browsing",
  "swreporter",
  "wakewords",
  "optimizationguideondevicemodel",
]);

type BookmarkNode = {
  type?: string;
  name?: string;
  url?: string;
  children?: BookmarkNode[];
};

export function defaultChromiumUserDataDir(browser: "chrome" | "edge"): string {
  const localAppData = process.env.LOCALAPPDATA || "";
  if (browser === "edge") {
    return path.join(localAppData, "Microsoft", "Edge", "User Data");
  }
  return path.join(localAppData, "Google", "Chrome", "User Data");
}

export function readLocalStateProfileNames(userDataDir: string): Record<string, string> {
  const localStatePath = path.join(userDataDir, "Local State");
  if (!fs.existsSync(localStatePath)) {
    return {};
  }
  try {
    const json = JSON.parse(fs.readFileSync(localStatePath, "utf8"));
    const cache = json?.profile?.info_cache;
    if (!cache || typeof cache !== "object") {
      return {};
    }
    const names: Record<string, string> = {};
    for (const [folder, info] of Object.entries(cache)) {
      const name = info && typeof info === "object" ? String((info as { name?: string }).name || "").trim() : "";
      names[folder] = name || folder;
    }
    return names;
  } catch {
    return {};
  }
}

export function isChromiumProfileDirectory(directory: string): boolean {
  return fs.existsSync(path.join(directory, "Bookmarks"))
    || fs.existsSync(path.join(directory, "History"))
    || fs.existsSync(path.join(directory, "Preferences"));
}

export function resolveChromiumProfiles(
  browser: "chrome" | "edge",
  configuredDir: string,
  profileFilter: string,
): BrowserProfile[] {
  const fallback = defaultChromiumUserDataDir(browser);
  const root = configuredDir || fallback;
  if (!root || !fs.existsSync(root)) {
    return [];
  }

  const profiles: BrowserProfile[] = [];
  if (isChromiumProfileDirectory(root)) {
    profiles.push({
      browser,
      id: path.basename(root),
      name: path.basename(root),
      directory: root,
    });
  } else {
    const names = readLocalStateProfileNames(root);
    let entries: fs.Dirent[] = [];
    try {
      entries = fs.readdirSync(root, { withFileTypes: true });
    } catch {
      return [];
    }
    for (const entry of entries) {
      if (!entry.isDirectory()) {
        continue;
      }
      const folder = entry.name;
      if (SKIP_PROFILE_DIRS.has(folder.toLowerCase())) {
        continue;
      }
      const directory = path.join(root, folder);
      if (!isChromiumProfileDirectory(directory)) {
        continue;
      }
      profiles.push({
        browser,
        id: folder,
        name: names[folder] || folder,
        directory,
      });
    }
  }

  const filter = profileFilter.trim().toLowerCase();
  if (!filter) {
    return profiles;
  }
  return profiles.filter((profile) =>
    profile.id.toLowerCase() === filter
    || profile.name.toLowerCase() === filter
    || profile.directory.toLowerCase() === filter
  );
}

export function parseChromiumBookmarksJson(json: string, browser: BrowserKind, profileName: string): BrowserItem[] {
  let root: { roots?: Record<string, BookmarkNode> };
  try {
    root = JSON.parse(json);
  } catch {
    return [];
  }
  const roots = root.roots;
  if (!roots || typeof roots !== "object") {
    return [];
  }

  const results: BrowserItem[] = [];
  for (const [key, node] of Object.entries(roots)) {
    collectBookmarks(node?.children, rootLabel(key), browser, profileName, results);
  }
  return results;
}

export function readChromiumBookmarksFile(filePath: string, browser: BrowserKind, profileName: string): BrowserItem[] {
  if (!fs.existsSync(filePath)) {
    return [];
  }
  try {
    return cached(`chromium-bookmarks:${filePath}:${browser}:${profileName}`, fileSignature([filePath]), () =>
      parseChromiumBookmarksJson(fs.readFileSync(filePath, "utf8"), browser, profileName));
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`[browser-search] chromium bookmarks failed for ${filePath}: ${message}`);
    return [];
  }
}

function rootLabel(rootName: string): string {
  switch (rootName) {
    case "bookmark_bar":
      return "Bar";
    case "other":
      return "Other";
    case "synced":
      return "Mobile";
    default:
      return rootName;
  }
}

function collectBookmarks(
  nodes: BookmarkNode[] | undefined,
  folderPath: string,
  browser: BrowserKind,
  profileName: string,
  results: BrowserItem[],
): void {
  if (!Array.isArray(nodes)) {
    return;
  }
  for (const child of nodes) {
    const type = String(child?.type || "");
    if (type.toLowerCase() === "url") {
      const title = String(child.name || "").trim();
      const url = String(child.url || "").trim();
      if (!title || !url) {
        continue;
      }
      results.push({
        browser,
        kind: "bookmark",
        title,
        url,
        folderPath,
        profileName,
        visitCount: 0,
        lastVisit: 0,
      });
      continue;
    }
    if (type.toLowerCase() === "folder") {
      const name = String(child.name || "").trim();
      const nextPath = name ? `${folderPath}/${name}` : folderPath;
      collectBookmarks(child.children, nextPath, browser, profileName, results);
    }
  }
}

export async function readChromiumHistoryFile(
  filePath: string,
  browser: BrowserKind,
  profileName: string,
): Promise<BrowserItem[]> {
  if (!fs.existsSync(filePath)) {
    return [];
  }
  return cachedAsync(
    `chromium-history:${filePath}:${browser}:${profileName}`,
    fileSignature([filePath]),
    async () => {
      try {
        const rows = await readSqliteQuery(
          filePath,
          "SELECT url, title, visit_count, hidden, CAST(last_visit_time AS TEXT) AS last_visit_time FROM urls",
        );
        const items: BrowserItem[] = [];
        for (const row of rows) {
          if (rowNumber(row, "hidden") !== 0) {
            continue;
          }
          const url = rowText(row, "url").trim();
          if (!url) {
            continue;
          }
          items.push({
            browser,
            kind: "history",
            title: rowText(row, "title").trim() || url,
            url,
            folderPath: "",
            profileName,
            visitCount: rowNumber(row, "visit_count"),
            lastVisit: rowNumber(row, "last_visit_time"),
          });
        }
        return items;
      } catch (error) {
        const message = error instanceof Error ? error.message : String(error);
        console.error(`[browser-search] chromium history failed for ${filePath}: ${message}`);
        return [];
      }
    },
  );
}

export async function loadChromiumItems(
  browser: "chrome" | "edge",
  profiles: BrowserProfile[],
  searchBookmarks: boolean,
  searchHistory: boolean,
): Promise<BrowserItem[]> {
  const items: BrowserItem[] = [];
  for (const profile of profiles) {
    try {
      if (searchBookmarks) {
        items.push(...readChromiumBookmarksFile(path.join(profile.directory, "Bookmarks"), browser, profile.name));
      }
      if (searchHistory) {
        items.push(...await readChromiumHistoryFile(path.join(profile.directory, "History"), browser, profile.name));
      }
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      console.error(`[browser-search] ${browser} profile ${profile.name} failed: ${message}`);
    }
  }
  return items;
}
