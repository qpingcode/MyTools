import type {
    ApiRequest,
    RequestResult,
    Environment,
    Settings,
} from '../../../shared/model.js';

export interface Tab {
    request: ApiRequest;
    collectionId: string;
    baseline: string;
    runId: string;
    result?: RequestResult;
    running: boolean;
}

export enum RequestPanelId {
    Params = 'Params',
    Headers = 'Headers',
    Authentication = 'Authentication',
    Body = 'Body',
    Scripts = 'Scripts',
    Settings = 'Settings',
}

export enum ResponsePanelId {
    Body = 'Body',
    ResponseHeaders = 'ResponseHeaders',
    RequestHeaders = 'RequestHeaders',
    TestResults = 'TestResults',
    ScriptConsole = 'ScriptConsole',
}

export enum ResponseBodyFormat {
    Auto = 'auto',
    Json = 'json',
    Xml = 'xml',
    Html = 'html',
    JavaScript = 'javascript',
    Raw = 'raw',
}

export enum DialogKind {
    Name = 'name',
    Confirm = 'confirm',
    Unsaved = 'unsaved',
    SaveFailed = 'saveFailed',
    Collection = 'collection',
    Environment = 'environment',
    Defaults = 'defaults',
}

export enum Choice {
    Save = 'save',
    Discard = 'discard',
    Cancel = 'cancel',
}

export interface Modal {
    kind: DialogKind;
    title: () => string;
    message?: () => string;
    value: string;
    environment?: Environment;
    settings?: Settings;
    finish: (value: unknown) => void;
}
