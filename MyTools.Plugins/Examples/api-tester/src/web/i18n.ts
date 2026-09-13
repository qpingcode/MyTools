import {createWebBusClient} from '@qping/plugin-bus/web';

export const bus = createWebBusClient();
export const text = {
    Name: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Name', {
            defaultValue: 'API Tester',
            ...values,
        }),
    Subtitle: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Subtitle', {
            defaultValue: 'Debug HTTP requests and run simple API tests',
            ...values,
        }),
    SelectFile: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.SelectFile', {
            defaultValue: 'Select a file',
            ...values,
        }),
    Collections: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Collections', {
            defaultValue: 'Collections',
            ...values,
        }),
    Environments: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Environments', {
            defaultValue: 'Environments',
            ...values,
        }),
    NewCollection: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.NewCollection', {
            defaultValue: 'New collection',
            ...values,
        }),
    NewRequest: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.NewRequest', {
            defaultValue: 'New request',
            ...values,
        }),
    Rename: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Rename', {
            defaultValue: 'Rename',
            ...values,
        }),
    Delete: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Delete', {
            defaultValue: 'Delete',
            ...values,
        }),
    Duplicate: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Duplicate', {
            defaultValue: 'Duplicate',
            ...values,
        }),
    Up: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Up', {defaultValue: 'Move up', ...values}),
    Down: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Down', {
            defaultValue: 'Move down',
            ...values,
        }),
    Search: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Search', {
            defaultValue: 'Search name or URL',
            ...values,
        }),
    NoEnvironment: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.NoEnvironment', {
            defaultValue: 'No environment',
            ...values,
        }),
    NewEnvironment: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.NewEnvironment', {
            defaultValue: 'New environment',
            ...values,
        }),
    EditEnvironment: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.EditEnvironment', {
            defaultValue: 'Edit environment',
            ...values,
        }),
    ClearCookies: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ClearCookies', {
            defaultValue: 'Clear cookies',
            ...values,
        }),
    Cookies: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Cookies', {
            defaultValue: 'Cookies',
            ...values,
        }),
    CookiesHint: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CookiesHint', {
            defaultValue:
                'Cookies are kept for this environment until you clear them or close the plugin.',
            ...values,
        }),
    CookiesEmpty: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CookiesEmpty', {
            defaultValue: 'No cookies stored for the current environment.',
            ...values,
        }),
    CookiePath: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CookiePath', {
            defaultValue: 'Path',
            ...values,
        }),
    CookieExpires: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CookieExpires', {
            defaultValue: 'Expires',
            ...values,
        }),
    CookieSession: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CookieSession', {
            defaultValue: 'Session',
            ...values,
        }),
    CookieHttpOnly: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CookieHttpOnly', {
            defaultValue: 'HttpOnly',
            ...values,
        }),
    CookieSecure: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CookieSecure', {
            defaultValue: 'Secure',
            ...values,
        }),
    CookieYes: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CookieYes', {
            defaultValue: 'Yes',
            ...values,
        }),
    CookieNo: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CookieNo', {
            defaultValue: 'No',
            ...values,
        }),
    Defaults: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Defaults', {
            defaultValue: 'Default execution settings',
            ...values,
        }),
    Save: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Save', {defaultValue: 'Save', ...values}),
    Cancel: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Cancel', {
            defaultValue: 'Cancel',
            ...values,
        }),
    Discard: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Discard', {
            defaultValue: 'Discard',
            ...values,
        }),
    Close: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Close', {defaultValue: 'Close', ...values}),
    Send: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Send', {defaultValue: 'Send', ...values}),
    Run: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Run', {
            defaultValue: 'Run collection / selected requests',
            ...values,
        }),
    StopOnFailure: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.StopOnFailure', {
            defaultValue: 'Stop on failure',
            ...values,
        }),
    Method: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Method', {
            defaultValue: 'Method',
            ...values,
        }),
    Url: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Url', {defaultValue: 'URL', ...values}),
    Params: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Params', {
            defaultValue: 'Parameters',
            ...values,
        }),
    Headers: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Headers', {
            defaultValue: 'Headers',
            ...values,
        }),
    Authentication: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Authentication', {
            defaultValue: 'Authentication',
            ...values,
        }),
    Body: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Body', {
            defaultValue: 'Body',
            ...values,
        }),
    Assertions: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Assertions', {
            defaultValue: 'Assertions',
            ...values,
        }),
    Extractions: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Extractions', {
            defaultValue: 'JSON variable extraction',
            ...values,
        }),
    Settings: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Settings', {
            defaultValue: 'Settings',
            ...values,
        }),
    RequestSections: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.RequestSections', {
            defaultValue: 'Request',
            ...values,
        }),
    Enabled: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Enabled', {
            defaultValue: 'Enabled',
            ...values,
        }),
    Key: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Key', {defaultValue: 'Name', ...values}),
    Value: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Value', {defaultValue: 'Value', ...values}),
    Add: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Add', {defaultValue: 'Add', ...values}),
    NoEquals: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.NoEquals', {
            defaultValue: 'No equals sign',
            ...values,
        }),
    File: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.File', {defaultValue: 'File', ...values}),
    ContentType: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ContentType', {
            defaultValue: 'Content-Type',
            ...values,
        }),
    None: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.None', {defaultValue: 'None', ...values}),
    Json: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Json', {defaultValue: 'JSON', ...values}),
    Text: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Text', {
            defaultValue: 'Raw text',
            ...values,
        }),
    Form: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Form', {
            defaultValue: 'URL-encoded form',
            ...values,
        }),
    Multipart: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Multipart', {
            defaultValue: 'Multipart form',
            ...values,
        }),
    Binary: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Binary', {
            defaultValue: 'Binary file',
            ...values,
        }),
    Basic: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Basic', {
            defaultValue: 'Basic Auth',
            ...values,
        }),
    Bearer: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Bearer', {
            defaultValue: 'Bearer Token',
            ...values,
        }),
    ApiKey: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ApiKey', {
            defaultValue: 'API Key',
            ...values,
        }),
    Username: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Username', {
            defaultValue: 'Username',
            ...values,
        }),
    Password: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Password', {
            defaultValue: 'Password',
            ...values,
        }),
    Token: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Token', {defaultValue: 'Token', ...values}),
    Location: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Location', {
            defaultValue: 'Location',
            ...values,
        }),
    HeaderLocation: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.HeaderLocation', {
            defaultValue: 'Header',
            ...values,
        }),
    QueryLocation: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.QueryLocation', {
            defaultValue: 'Query parameter',
            ...values,
        }),
    Format: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Format', {
            defaultValue: 'Format JSON',
            ...values,
        }),
    Timeout: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Timeout', {
            defaultValue: 'Timeout (milliseconds)',
            ...values,
        }),
    Follow: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Follow', {
            defaultValue: 'Follow redirects',
            ...values,
        }),
    VerifyTls: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.VerifyTls', {
            defaultValue: 'Verify TLS certificate',
            ...values,
        }),
    Override: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Override', {
            defaultValue: 'Override defaults for this request',
            ...values,
        }),
    Path: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Path', {
            defaultValue: 'JSON Pointer / header name',
            ...values,
        }),
    Expected: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Expected', {
            defaultValue: 'Expected value (JSON for JSON equality)',
            ...values,
        }),
    Actual: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Actual', {
            defaultValue: 'Actual',
            ...values,
        }),
    VariableName: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.VariableName', {
            defaultValue: 'Target variable name',
            ...values,
        }),
    Status: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Status', {
            defaultValue: 'Status code',
            ...values,
        }),
    Time: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Time', {
            defaultValue: 'Response time',
            ...values,
        }),
    HeaderExists: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.HeaderExists', {
            defaultValue: 'Header exists',
            ...values,
        }),
    Contains: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Contains', {
            defaultValue: 'Text contains',
            ...values,
        }),
    JsonExists: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.JsonExists', {
            defaultValue: 'JSON field exists',
            ...values,
        }),
    JsonValue: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.JsonValue', {
            defaultValue: 'JSON value equals',
            ...values,
        }),
    JsonType: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.JsonType', {
            defaultValue: 'JSON type',
            ...values,
        }),
    Response: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Response', {
            defaultValue: 'Response',
            ...values,
        }),
    Raw: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Raw', {defaultValue: 'Raw', ...values}),
    Formatted: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Formatted', {
            defaultValue: 'Formatted JSON',
            ...values,
        }),
    Copy: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Copy', {
            defaultValue: 'Copy body',
            ...values,
        }),
    CopyHeaders: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CopyHeaders', {
            defaultValue: 'Copy headers',
            ...values,
        }),
    SaveBody: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.SaveBody', {
            defaultValue: 'Save body to file',
            ...values,
        }),
    RequestHeaders: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.RequestHeaders', {
            defaultValue: 'Effective request headers',
            ...values,
        }),
    ResponseHeaders: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ResponseHeaders', {
            defaultValue: 'Response headers',
            ...values,
        }),
    Results: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Results', {
            defaultValue: 'Run results',
            ...values,
        }),
    Complete: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Complete', {
            defaultValue: 'Complete',
            ...values,
        }),
    Failed: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Failed', {
            defaultValue: 'Failed',
            ...values,
        }),
    Cancelled: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Cancelled', {
            defaultValue: 'Cancelled',
            ...values,
        }),
    Skipped: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Skipped', {
            defaultValue: 'Skipped',
            ...values,
        }),
    Passed: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Passed', {
            defaultValue: 'Passed',
            ...values,
        }),
    TestFailed: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.TestFailed', {
            defaultValue: 'Test failed',
            ...values,
        }),
    Untested: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Untested', {
            defaultValue: 'Untested',
            ...values,
        }),
    NotApplicable: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.NotApplicable', {
            defaultValue: 'Not applicable',
            ...values,
        }),
    Ready: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Ready', {
            defaultValue: 'Create or select a request to begin',
            ...values,
        }),
    Running: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Running', {
            defaultValue: 'Running: {{name}}',
            ...values,
        }),
    Progress: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Progress', {
            defaultValue: '{{done}} / {{total}} requests · {{elapsed}} ms',
            ...values,
        }),
    Summary: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Summary', {
            defaultValue:
                'HTTP {{status}} · {{elapsed}} ms · {{size}} bytes · {{time}}',
            ...values,
        }),
    PreviewLimit: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.PreviewLimit', {
            defaultValue:
                'Preview limited to 1 MiB; complete body remains available while cached.',
            ...values,
        }),
    BinaryResponse: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.BinaryResponse', {
            defaultValue: 'Binary response. Save the body to inspect it.',
            ...values,
        }),
    ContentTypeWarning: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ContentTypeWarning', {
            defaultValue: 'Content-Type differs from the body type.',
            ...values,
        }),
    Unsaved: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Unsaved', {
            defaultValue: 'Save changes to {{name}}?',
            ...values,
        }),
    DeleteCollection: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.DeleteCollection', {
            defaultValue: 'Delete this collection and its {{count}} requests?',
            ...values,
        }),
    DeleteItem: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.DeleteItem', {
            defaultValue: 'Delete {{name}}?',
            ...values,
        }),
    ReplaceParams: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ReplaceParams', {
            defaultValue:
                'Editing the URL will remove disabled parameters. Continue?',
            ...values,
        }),
    NamePrompt: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.NamePrompt', {
            defaultValue: 'Enter a name',
            ...values,
        }),
    TabLimit: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.TabLimit', {
            defaultValue: 'Close a tab before opening another (maximum 12).',
            ...values,
        }),
    SaveTarget: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.SaveTarget', {
            defaultValue: 'Choose a collection to save this request',
            ...values,
        }),
    ConfigurationError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ConfigurationError', {
            defaultValue: 'Invalid request configuration',
            ...values,
        }),
    VariableError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.VariableError', {
            defaultValue: 'Undefined variable',
            ...values,
        }),
    FileError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.FileError', {
            defaultValue: 'File missing or unreadable',
            ...values,
        }),
    DnsError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.DnsError', {
            defaultValue: 'DNS lookup failed',
            ...values,
        }),
    ConnectionError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ConnectionError', {
            defaultValue: 'Connection or response reception failed',
            ...values,
        }),
    TlsError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.TlsError', {
            defaultValue: 'TLS certificate validation failed',
            ...values,
        }),
    TimeoutError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.TimeoutError', {
            defaultValue: 'Request timed out',
            ...values,
        }),
    CancelledError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CancelledError', {
            defaultValue: 'Request cancelled',
            ...values,
        }),
    DecodeError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.DecodeError', {
            defaultValue: 'Response decoding failed',
            ...values,
        }),
    BodyLimitError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.BodyLimitError', {
            defaultValue: 'Response body exceeds 20 MiB',
            ...values,
        }),
    RedirectLimitError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.RedirectLimitError', {
            defaultValue: 'More than 10 redirects',
            ...values,
        }),
    ExtractionError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ExtractionError', {
            defaultValue: 'JSON variable extraction failed',
            ...values,
        }),
    StorageError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.StorageError', {
            defaultValue: 'Could not load or save workspace',
            ...values,
        }),
    CacheError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CacheError', {
            defaultValue: 'Response cache expired or too many active runs',
            ...values,
        }),
    TransportError: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.TransportError', {
            defaultValue: 'Could not communicate with the plugin',
            ...values,
        }),
    BadJson: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.BadJson', {
            defaultValue: 'Invalid JSON',
            ...values,
        }),
    Pass: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Pass', {defaultValue: 'Pass', ...values}),
    Fail: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Fail', {defaultValue: 'Fail', ...values}),
    Saved: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Saved', {defaultValue: 'Saved', ...values}),
    NoResults: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.NoResults', {
            defaultValue: 'No response yet',
            ...values,
        }),
    SaveAll: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.SaveAll', {
            defaultValue: 'Save all and close',
            ...values,
        }),
    Exit: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Exit', {
            defaultValue: 'Close workspace',
            ...values,
        }),
    UnsavedAll: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.UnsavedAll', {
            defaultValue: 'There are unsaved requests. Choose how to close.',
            ...values,
        }),
    CloseWorkspace: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.CloseWorkspace', {
            defaultValue: 'Close workspace',
            ...values,
        }),
    FileFilter: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.FileFilter', {
            defaultValue: 'All files (*.*)|*.*',
            ...values,
        }),
    FileInfo: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.FileInfo', {
            defaultValue: '{{name}} · {{size}} bytes',
            ...values,
        }),
    Continue: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Continue', {
            defaultValue: 'Continue',
            ...values,
        }),
    Missing: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.Missing', {
            defaultValue: 'Missing field',
            ...values,
        }),
    ResizeSidebar: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ResizeSidebar', {
            defaultValue: 'Drag to resize sidebar; double-click to reset',
            ...values,
        }),
    ResizeRequestResponse: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ResizeRequestResponse', {
            defaultValue: 'Drag to resize request and response panes; double-click to reset',
            ...values,
        }),
    VariableNotInEnvironment: () =>
        bus.i18n.t('Plugin.ApiTester.VariableNotInEnvironment', {
            defaultValue: 'Variable is not defined or enabled in the current environment',
        }),
    VariableEmptyValue: () =>
        bus.i18n.t('Plugin.ApiTester.VariableEmptyValue', {
            defaultValue: '(empty value)',
        }),
    ResponseBody: (values: Record<string, string | number> = {}) =>
        bus.i18n.t('Plugin.ApiTester.ResponseBody', {
            defaultValue: 'Body',
            ...values,
        }),
};
