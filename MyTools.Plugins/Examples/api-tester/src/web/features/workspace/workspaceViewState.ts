import {
    RequestPanelId,
    ResponseBodyFormat,
    ResponsePanelId,
} from './workspaceTypes.js';
import {emptyWorkspace, type ApiRequest} from '../../../shared/model.js';
import {validateWorkspace} from '../../../shared/workspaceValidation.js';

export const WorkspaceViewStateVersion = 1;
const DetachedRequestValidationCollectionIdPrefix = 'api-tester:view-state-owner';
const LegacyFormattedResponseBodyView = 'formatted';
const LegacyTreeResponseBodyView = 'tree';

export interface WorkspaceViewState {
    version: typeof WorkspaceViewStateVersion;
    openRequestIds: string[];
    detachedRequests: ApiRequest[];
    activeRequestId: string;
    expandedCollectionIds: string[];
    requestPanels: Record<string, RequestPanelId>;
    responsePanels: Record<string, ResponsePanelId>;
    responseBodyFormats: Record<string, ResponseBodyFormat>;
    responseBodyPreviews: Record<string, boolean>;
}

export function emptyWorkspaceViewState(): WorkspaceViewState {
    return {
        version: WorkspaceViewStateVersion,
        openRequestIds: [],
        detachedRequests: [],
        activeRequestId: '',
        expandedCollectionIds: [],
        requestPanels: {},
        responsePanels: {},
        responseBodyFormats: {},
        responseBodyPreviews: {},
    };
}

function detachedRequests(value: unknown): ApiRequest[] {
    if (!Array.isArray(value)) return [];
    const requestIds = new Set(value.flatMap(item =>
        item && typeof item === 'object' && typeof item.id === 'string' ? [item.id] : [],
    ));
    let ownerId = DetachedRequestValidationCollectionIdPrefix;
    while (requestIds.has(ownerId)) ownerId += '-owner';
    const candidate = emptyWorkspace();
    candidate.collections = [{id: ownerId, name: '', requests: value as ApiRequest[]}];
    try {
        validateWorkspace(candidate);
        return candidate.collections[0].requests;
    } catch {
        return [];
    }
}

function stringList(value: unknown): string[] {
    return Array.isArray(value)
        ? [...new Set(value.filter((item): item is string => typeof item === 'string'))]
        : [];
}

function enumRecord<T extends string>(value: unknown, allowed: readonly T[]): Record<string, T> {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
    const entries = Object.entries(value).filter(
        (entry): entry is [string, T] => typeof entry[1] === 'string' && allowed.includes(entry[1] as T),
    );
    return Object.fromEntries(entries);
}

function booleanRecord(value: unknown): Record<string, boolean> {
    if (!value || typeof value !== 'object' || Array.isArray(value)) return {};
    return Object.fromEntries(Object.entries(value).filter(
        (entry): entry is [string, boolean] => typeof entry[1] === 'boolean',
    ));
}

function responseBodyFormats(value: Record<string, unknown>): Record<string, ResponseBodyFormat> {
    const current = enumRecord(value.responseBodyFormats, Object.values(ResponseBodyFormat));
    if (Object.keys(current).length || !value.responseBodyViews || typeof value.responseBodyViews !== 'object') return current;
    const migrated: Record<string, ResponseBodyFormat> = {};
    for (const [requestId, legacy] of Object.entries(value.responseBodyViews)) {
        if (legacy === ResponseBodyFormat.Raw) migrated[requestId] = ResponseBodyFormat.Raw;
        if (legacy === LegacyFormattedResponseBodyView || legacy === LegacyTreeResponseBodyView) {
            migrated[requestId] = ResponseBodyFormat.Json;
        }
    }
    return migrated;
}

export function parseWorkspaceViewState(source: unknown): WorkspaceViewState {
    if (!source) return emptyWorkspaceViewState();
    try {
        const value = (typeof source === 'string' ? JSON.parse(source) : source) as Record<string, unknown>;
        if (!value || value.version !== WorkspaceViewStateVersion) return emptyWorkspaceViewState();
        return {
            version: WorkspaceViewStateVersion,
            openRequestIds: stringList(value.openRequestIds),
            detachedRequests: detachedRequests(value.detachedRequests),
            activeRequestId: typeof value.activeRequestId === 'string' ? value.activeRequestId : '',
            expandedCollectionIds: stringList(value.expandedCollectionIds),
            requestPanels: enumRecord(value.requestPanels, Object.values(RequestPanelId)),
            responsePanels: enumRecord(value.responsePanels, Object.values(ResponsePanelId)),
            responseBodyFormats: responseBodyFormats(value),
            responseBodyPreviews: booleanRecord(value.responseBodyPreviews),
        };
    } catch {
        return emptyWorkspaceViewState();
    }
}
