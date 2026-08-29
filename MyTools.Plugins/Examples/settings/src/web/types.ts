export type Option = { value: string; label: string };

export type SettingSchemaProperty = {
    key: string;
    type: string;
    title: string;
    uiHint?: string;
    defaultValue?: string;
    hidden?: boolean;
    showInTable?: boolean;
    visibility?: string;
};

export type SettingSchema = {
    properties: SettingSchemaProperty[];
};

export type Setting = {
    key: string;
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
    settings: Setting[];
};

export type Config = {
    categories: Category[];
    supportedLocales: Option[];
    supportedThemes: Option[];
    supportedUpdateChannels: Option[];
    supportedLogLevels: Option[];
};

export type KeymapPlugin = {
    pluginId: string;
    overrideKey: string;
    location: string;
    name: string;
    defaultHotKey: string;
    currentHotKey: string;
    defaultKeywords: string[];
    currentKeywords: string[];
    isEnabled: boolean;
    defaultIncludeInGlobalResults: boolean;
    includeInGlobalResults: boolean;
    isNodePlugin: boolean;
    isDevelopment: boolean;
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
