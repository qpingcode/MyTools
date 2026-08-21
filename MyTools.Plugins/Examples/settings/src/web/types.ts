export type Option = { value: string; label: string };

export type SettingSchemaProperty = {
    key: string;
    type: string;
    title: string;
    uiHint?: string;
    defaultValue?: string;
    hidden?: boolean;
    table?: boolean;
};

export type SettingSchema = {
    properties: SettingSchemaProperty[];
};

export type Setting = {
    fullPath: string;
    title: string;
    description?: string;
    valueType: string;
    currentValue?: string;
    defaultValue?: string;
    requiresRestart: boolean;
    uiHint?: string;
    visibility?: string;
    schema?: SettingSchema;
};

export type Category = {
    key: string;
    name: string;
    description?: string;
    icon?: string;
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

export type KeymapDirty = {
    hotKey?: string | null;
    keywords?: string[];
    isEnabled?: boolean;
    includeInGlobalResults?: boolean;
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
    actionType: string;
    hotKey?: string | null;
    mouseButton?: string | null;
    processNames: string[];
    isEnabled: boolean;
};

export type SidebarItem =
    | { type: "group"; label: string }
    | { type: "category"; key: string; name: string; selectable: boolean; icon: string };
