import {
    RequestPanelId,
    ResponseBodyViewKind,
    ResponsePanelId,
} from './workspaceTypes.js';
import {emptyWorkspace, type ApiRequest} from '../../../shared/model.js';
import {validateWorkspace} from '../../../shared/workspaceValidation.js';

export const WorkspaceViewStateVersion = 1;
const DetachedRequestValidationCollectionIdPrefix = 'api-tester:view-state-owner';

export interface WorkspaceViewState {
    version: typeof WorkspaceViewStateVersion;
    openRequestIds: string[];
    detachedRequests: ApiRequest[];
    activeRequestId: string;
    expandedCollectionIds: string[];
    requestPanels: Record<string, RequestPanelId>;
    responsePanels: Record<string, ResponsePanelId>;
    responseBodyViews: Record<string, ResponseBodyViewKind>;
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
        responseBodyViews: {},
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
            responseBodyViews: enumRecord(value.responseBodyViews, Object.values(ResponseBodyViewKind)),
        };
    } catch {
        return emptyWorkspaceViewState();
    }
}
