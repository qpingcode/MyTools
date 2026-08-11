import type { MyToolsEvents } from "./contracts.js";

export const MyToolsEventSubjects = {
  host: {
    initialize: "mytools.host.initialize",
    search: "mytools.host.search",
    key: "mytools.host.key",
    languageChanged: "mytools.host.language-changed",
    themeChanged: "mytools.host.theme-changed",
  },
} as const satisfies MyToolsEvents;
