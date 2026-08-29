export type BrowserKind = "chrome" | "edge" | "firefox";
export type ItemKind = "bookmark" | "history";

export type BrowserItem = {
  browser: BrowserKind;
  kind: ItemKind;
  title: string;
  url: string;
  folderPath: string;
  profileName: string;
  visitCount: number;
  lastVisit: number;
};

export type BrowserProfile = {
  browser: BrowserKind;
  id: string;
  name: string;
  directory: string;
};

export type PluginSettings = {
  chromeEnabled: boolean;
  edgeEnabled: boolean;
  firefoxEnabled: boolean;
  searchBookmarks: boolean;
  searchHistory: boolean;
  chromeUserDataDir: string;
  chromeProfile: string;
  edgeUserDataDir: string;
  edgeProfile: string;
  firefoxProfilesDir: string;
  firefoxProfile: string;
};
