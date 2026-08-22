import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import test from "node:test";
import {
  asBool,
  isSubsequence,
  itemMatches,
  parseChromiumBookmarksJson,
  parseFirefoxProfilesIni,
  parseSettings,
  readSqliteQuery,
  readSqliteTable,
  readSqliteTableFromBuffer,
  resolveChromiumProfiles,
  resolveFirefoxProfiles,
} from "../dist/backend/index.mjs";

const here = path.dirname(fileURLToPath(import.meta.url));

test("parseChromiumBookmarksJson flattens nested folders", () => {
  const json = fs.readFileSync(path.join(here, "fixtures", "bookmarks.json"), "utf8");
  const items = parseChromiumBookmarksJson(json, "chrome", "Person 1");
  assert.deepEqual(items.map((item) => item.title), ["GitLab", "SharpLab", "GitHub"]);
  assert.equal(items.find((item) => item.title === "SharpLab")?.folderPath, "Bar/Dev");
  assert.equal(items[0].browser, "chrome");
  assert.equal(items[0].kind, "bookmark");
});

test("itemMatches title, url, folder, and subsequence", () => {
  const item = {
    browser: "chrome",
    kind: "bookmark",
    title: "GitHub",
    url: "https://github.com/qping",
    folderPath: "Bar/Dev",
    profileName: "Work",
    visitCount: 0,
    lastVisit: 0,
  };
  assert.equal(itemMatches(item, "git"), true);
  assert.equal(itemMatches(item, "qping"), true);
  assert.equal(itemMatches(item, "dev"), true);
  assert.equal(itemMatches(item, "gthb"), true);
  assert.equal(itemMatches(item, "zzzz"), false);
  assert.equal(isSubsequence("gthb", "GitHub"), true);

  const longQuery = "abcdefghijklmnop";
  const longTitleItem = {
    ...item,
    title: "a-b-c-d-e-f-g-h-i-j-k-l-m-n-o-p",
    url: "https://example.com/",
    folderPath: "",
    profileName: "",
  };
  assert.equal(isSubsequence(longQuery, longTitleItem.title), true);
  assert.equal(itemMatches(longTitleItem, longQuery), false);
});

test("parseSettings reads host configuration.readOwn values", () => {
  const settings = parseSettings({
    ChromeEnabled: true,
    EdgeEnabled: "False",
    FirefoxEnabled: 0,
    SearchBookmarks: "true",
    SearchHistory: false,
    ChromeUserDataDir: " D:\\ChromeUserData ",
    ChromeProfile: "Profile 1",
  });
  assert.equal(settings.chromeEnabled, true);
  assert.equal(settings.edgeEnabled, false);
  assert.equal(settings.firefoxEnabled, false);
  assert.equal(settings.searchBookmarks, true);
  assert.equal(settings.searchHistory, false);
  assert.equal(settings.chromeUserDataDir, "D:\\ChromeUserData");
  assert.equal(settings.chromeProfile, "Profile 1");
  assert.equal(asBool("True", false), true);
});

test("resolveChromiumProfiles scans User Data and filters by person name", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "mt-chrome-"));
  fs.writeFileSync(path.join(root, "Local State"), JSON.stringify({
    profile: {
      info_cache: {
        Default: { name: "Person 1" },
        "Profile 1": { name: "Work" },
      },
    },
  }));
  fs.mkdirSync(path.join(root, "Default"));
  fs.writeFileSync(path.join(root, "Default", "Bookmarks"), "{}");
  fs.mkdirSync(path.join(root, "Profile 1"));
  fs.writeFileSync(path.join(root, "Profile 1", "History"), "");
  fs.mkdirSync(path.join(root, "Crashpad"));

  const all = resolveChromiumProfiles("chrome", root, "");
  assert.equal(all.length, 2);
  assert.equal(all.find((item) => item.id === "Profile 1")?.name, "Work");

  const work = resolveChromiumProfiles("edge", root, "Work");
  assert.equal(work.length, 1);
  assert.equal(work[0].id, "Profile 1");
});

test("resolveChromiumProfiles treats a profile folder as a single user", () => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "mt-chrome-profile-"));
  fs.writeFileSync(path.join(root, "Bookmarks"), "{}");
  const profiles = resolveChromiumProfiles("chrome", root, "");
  assert.equal(profiles.length, 1);
  assert.equal(profiles[0].directory, root);
});

test("parseFirefoxProfilesIni and resolveFirefoxProfiles", () => {
  const firefoxRoot = fs.mkdtempSync(path.join(os.tmpdir(), "mt-ff-"));
  const profileDir = path.join(firefoxRoot, "Profiles", "abcd.default");
  fs.mkdirSync(profileDir, { recursive: true });
  fs.writeFileSync(path.join(profileDir, "places.sqlite"), "");
  fs.writeFileSync(path.join(firefoxRoot, "profiles.ini"), `
[General]
StartWithLastProfile=1

[Profile0]
Name=default
IsRelative=1
Path=Profiles/abcd.default
Default=1

[Profile1]
Name=missing
IsRelative=1
Path=Profiles/gone
`);

  const parsed = parseFirefoxProfilesIni(fs.readFileSync(path.join(firefoxRoot, "profiles.ini"), "utf8"));
  assert.equal(parsed.length, 2);
  assert.equal(parsed[0].name, "default");

  const resolved = resolveFirefoxProfiles(firefoxRoot, "default");
  assert.equal(resolved.length, 1);
  assert.equal(resolved[0].directory, profileDir);
});

test("readSqliteTableFromBuffer reads a node:sqlite created table", async () => {
  let DatabaseSync;
  try {
    ({ DatabaseSync } = await import("node:sqlite"));
  } catch {
    return;
  }
  const file = path.join(os.tmpdir(), `mt-sqlite-${Date.now()}.sqlite`);
  const db = new DatabaseSync(file);
  db.exec(`CREATE TABLE urls (
    id INTEGER PRIMARY KEY,
    url TEXT,
    title TEXT,
    visit_count INTEGER,
    hidden INTEGER
  );`);
  db.prepare("INSERT INTO urls (url, title, visit_count, hidden) VALUES (?, ?, ?, ?)").run(
    "https://github.com/",
    "GitHub",
    4,
    0,
  );
  db.close();

  const rows = readSqliteTableFromBuffer(fs.readFileSync(file), "urls");
  assert.equal(rows.length, 1);
  assert.equal(rows[0].url, "https://github.com/");
  assert.equal(rows[0].title, "GitHub");
  assert.equal(rows[0].visit_count, 4);

  const liveRows = await readSqliteTable(file, "urls");
  assert.equal(liveRows.length, 1);
  assert.equal(liveRows[0].url, "https://github.com/");
  fs.unlinkSync(file);
});

test("readSqliteQuery casts Chrome-sized timestamps to text", async () => {
  let DatabaseSync;
  try {
    ({ DatabaseSync } = await import("node:sqlite"));
  } catch {
    return;
  }
  const file = path.join(os.tmpdir(), `mt-sqlite-ts-${Date.now()}.sqlite`);
  const db = new DatabaseSync(file);
  db.exec("CREATE TABLE urls (url TEXT, title TEXT, visit_count INTEGER, hidden INTEGER, last_visit_time INTEGER);");
  db.exec("INSERT INTO urls (url, title, visit_count, hidden, last_visit_time) VALUES ('https://github.com/', 'GitHub', 1, 0, 13428031999593552);");
  db.close();

  const rows = await readSqliteQuery(
    file,
    "SELECT url, title, visit_count, hidden, CAST(last_visit_time AS TEXT) AS last_visit_time FROM urls",
  );
  assert.equal(rows.length, 1);
  assert.equal(rows[0].url, "https://github.com/");
  assert.equal(String(rows[0].last_visit_time), "13428031999593552");
  fs.unlinkSync(file);
});
