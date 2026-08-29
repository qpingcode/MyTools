import fs from "node:fs";
import path from "node:path";
import { cachedAsync, fileSignature } from "./cache.mjs";
import { rowNumber, rowText, readSqliteQuery } from "./sqlite.mjs";
import type { BrowserItem, BrowserProfile } from "./types.mjs";

export type FirefoxIniProfile = {
  name: string;
  path: string;
  isRelative: boolean;
};

export function defaultFirefoxRoot(): string {
  const appData = process.env.APPDATA || "";
  return path.join(appData, "Mozilla", "Firefox");
}

export function parseFirefoxProfilesIni(text: string): FirefoxIniProfile[] {
  const profiles: FirefoxIniProfile[] = [];
  let current: { name?: string; path?: string; isRelative?: boolean } | null = null;
  const flush = () => {
    if (current?.path) {
      profiles.push({
        name: current.name || path.basename(current.path),
        path: current.path.replaceAll("/", path.sep),
        isRelative: current.isRelative !== false,
      });
    }
    current = null;
  };

  for (const rawLine of text.split(/\r?\n/)) {
    const line = rawLine.trim();
    if (!line || line.startsWith("#") || line.startsWith(";")) {
      continue;
    }
    if (line.startsWith("[") && line.endsWith("]")) {
      flush();
      current = /^\[profile\d+\]$/i.test(line) ? {} : null;
      continue;
    }
    if (!current) {
      continue;
    }
    const separator = line.indexOf("=");
    if (separator < 0) {
      continue;
    }
    const key = line.slice(0, separator).trim().toLowerCase();
    const value = line.slice(separator + 1).trim();
    if (key === "name") {
      current.name = value;
    } else if (key === "path") {
      current.path = value;
    } else if (key === "isrelative") {
      current.isRelative = value !== "0";
    }
  }
  flush();
  return profiles;
}

export function resolveFirefoxProfiles(configuredDir: string, profileFilter: string): BrowserProfile[] {
  const root = configuredDir || defaultFirefoxRoot();
  if (!root || !fs.existsSync(root)) {
    return [];
  }

  const profiles: BrowserProfile[] = [];
  const placesPath = path.join(root, "places.sqlite");
  if (fs.existsSync(placesPath)) {
    profiles.push({
      browser: "firefox",
      id: path.basename(root),
      name: path.basename(root),
      directory: root,
    });
  } else {
    const iniPath = findProfilesIni(root);
    if (iniPath) {
      const iniDir = path.dirname(iniPath);
      let parsed: FirefoxIniProfile[] = [];
      try {
        parsed = parseFirefoxProfilesIni(fs.readFileSync(iniPath, "utf8"));
      } catch {
        parsed = [];
      }
      for (const item of parsed) {
        const directory = item.isRelative ? path.join(iniDir, item.path) : item.path;
        if (!fs.existsSync(path.join(directory, "places.sqlite"))) {
          continue;
        }
        profiles.push({
          browser: "firefox",
          id: item.path,
          name: item.name,
          directory,
        });
      }
    } else {
      profiles.push(...scanFirefoxProfileFolders(root));
    }
  }

  const filter = profileFilter.trim().toLowerCase();
  if (!filter) {
    return profiles;
  }
  return profiles.filter((profile) =>
    profile.id.toLowerCase() === filter
    || profile.name.toLowerCase() === filter
    || path.basename(profile.directory).toLowerCase() === filter
    || profile.directory.toLowerCase() === filter
  );
}

function findProfilesIni(root: string): string | null {
  const direct = path.join(root, "profiles.ini");
  if (fs.existsSync(direct)) {
    return direct;
  }
  const parent = path.join(path.dirname(root), "profiles.ini");
  if (path.basename(root).toLowerCase() === "profiles" && fs.existsSync(parent)) {
    return parent;
  }
  return null;
}

function scanFirefoxProfileFolders(root: string): BrowserProfile[] {
  let entries: fs.Dirent[] = [];
  try {
    entries = fs.readdirSync(root, { withFileTypes: true });
  } catch {
    return [];
  }
  const profiles: BrowserProfile[] = [];
  for (const entry of entries) {
    if (!entry.isDirectory()) {
      continue;
    }
    const directory = path.join(root, entry.name);
    if (!fs.existsSync(path.join(directory, "places.sqlite"))) {
      continue;
    }
    profiles.push({
      browser: "firefox",
      id: entry.name,
      name: entry.name,
      directory,
    });
  }
  return profiles;
}

export async function loadFirefoxItems(
  profiles: BrowserProfile[],
  searchBookmarks: boolean,
  searchHistory: boolean,
): Promise<BrowserItem[]> {
  const items: BrowserItem[] = [];
  for (const profile of profiles) {
    const placesPath = path.join(profile.directory, "places.sqlite");
    if (!fs.existsSync(placesPath)) {
      continue;
    }
    try {
      items.push(...await cachedAsync(
        `firefox:${placesPath}:${searchBookmarks}:${searchHistory}`,
        fileSignature([placesPath]),
        () => loadFirefoxPlaces(placesPath, profile, searchBookmarks, searchHistory),
      ));
    } catch (error) {
      const message = error instanceof Error ? error.message : String(error);
      console.error(`[bookmark-and-history] firefox places failed for ${placesPath}: ${message}`);
    }
  }
  return items;
}

async function loadFirefoxPlaces(
  placesPath: string,
  profile: BrowserProfile,
  searchBookmarks: boolean,
  searchHistory: boolean,
): Promise<BrowserItem[]> {
  try {
    return await loadFirefoxPlacesCore(placesPath, profile, searchBookmarks, searchHistory);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`[bookmark-and-history] firefox places failed for ${placesPath}: ${message}`);
    return [];
  }
}

async function loadFirefoxPlacesCore(
  placesPath: string,
  profile: BrowserProfile,
  searchBookmarks: boolean,
  searchHistory: boolean,
): Promise<BrowserItem[]> {
  const items: BrowserItem[] = [];
    const [places, bookmarks] = await Promise.all([
      readSqliteQuery(
        placesPath,
        "SELECT id, url, title, visit_count, hidden, CAST(last_visit_date AS TEXT) AS last_visit_date FROM moz_places",
      ),
      searchBookmarks
        ? readSqliteQuery(placesPath, "SELECT id, type, fk, parent, title FROM moz_bookmarks")
        : Promise.resolve([]),
    ]);
    const placesById = new Map<number, { url: string; title: string; visitCount: number; lastVisit: number; hidden: number }>();
    for (const row of places) {
      const id = rowNumber(row, "id");
      const url = rowText(row, "url").trim();
      if (!id || !url) {
        continue;
      }
      placesById.set(id, {
        url,
        title: rowText(row, "title").trim() || url,
        visitCount: rowNumber(row, "visit_count"),
        lastVisit: rowNumber(row, "last_visit_date"),
        hidden: rowNumber(row, "hidden"),
      });
    }

    if (searchBookmarks) {
      const folders = new Map<number, { title: string; parent: number }>();
      for (const row of bookmarks) {
        if (rowNumber(row, "type") !== 2) {
          continue;
        }
        folders.set(rowNumber(row, "id"), {
          title: rowText(row, "title").trim(),
          parent: rowNumber(row, "parent"),
        });
      }
      for (const row of bookmarks) {
        if (rowNumber(row, "type") !== 1) {
          continue;
        }
        const place = placesById.get(rowNumber(row, "fk"));
        if (!place) {
          continue;
        }
        const title = rowText(row, "title").trim() || place.title;
        items.push({
          browser: "firefox",
          kind: "bookmark",
          title,
          url: place.url,
          folderPath: firefoxFolderPath(folders, rowNumber(row, "parent")),
          profileName: profile.name,
          visitCount: place.visitCount,
          lastVisit: place.lastVisit,
        });
      }
    }

    if (searchHistory) {
      for (const place of placesById.values()) {
        if (place.hidden !== 0 || place.url.startsWith("place:") || place.url.startsWith("about:")) {
          continue;
        }
        items.push({
          browser: "firefox",
          kind: "history",
          title: place.title,
          url: place.url,
          folderPath: "",
          profileName: profile.name,
          visitCount: place.visitCount,
          lastVisit: place.lastVisit,
        });
      }
    }
  return items;
}

function firefoxFolderPath(folders: Map<number, { title: string; parent: number }>, parentId: number): string {
  const parts: string[] = [];
  let current = parentId;
  const seen = new Set<number>();
  while (current && !seen.has(current)) {
    seen.add(current);
    const folder = folders.get(current);
    if (!folder) {
      break;
    }
    if (folder.title && !/^menu$|^toolbar$|^tags$|^unfiled$|^mobile$/i.test(folder.title)) {
      parts.push(folder.title);
    } else if (folder.title) {
      parts.push(folder.title);
    }
    current = folder.parent;
  }
  return parts.reverse().join("/");
}
