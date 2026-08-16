export type Option = { value: string; label: string };

export type Setting = {
    fullPath: string;
    title: string;
    description?: string;
    valueType: string;
    currentValue?: string;
    defaultValue?: string;
    requiresRestart: boolean;
};

export type Category = {
    key: string;
    name: string;
    description?: string;
    isSelectable: boolean;
    children: Category[];
    settings: Setting[];
};

export type Config = {
    categories: Category[];
    supportedLocales: Option[];
    supportedThemes: Option[];
    supportedLogLevels: Option[];
};

export type KeymapPlugin = {
    pluginId: string;
    name: string;
    defaultHotKey: string;
    currentHotKey: string;
    defaultKeywords: string[];
    currentKeywords: string[];
    isEnabled: boolean;
    defaultIncludeInGlobalResults: boolean;
    includeInGlobalResults: boolean;
    isNodePlugin: boolean;
};

export type KeymapConflict = {
    pluginId: string;
    field: string;
    value: string;
    conflictsWith: string;
};

export type GestureConfig = {
    id: string;
    directions: string[];
    actionName: string;
    actionType: string; // "hotkey" | "mouse"
    hotKey?: string | null;
    mouseButton?: string | null; // "XButton1" | "XButton2"
    processNames: string[];
    isEnabled: boolean;
};
