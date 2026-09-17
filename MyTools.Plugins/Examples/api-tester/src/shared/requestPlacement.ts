import type {Collection} from './model.js';

export enum RequestDropKind {
    Before = 'before',
    After = 'after',
    Into = 'into',
}

export function requestInsertionIndex(
    owner: Collection,
    requestId: string | undefined,
    kind: RequestDropKind,
): number {
    if (!requestId || kind === RequestDropKind.Into) return owner.requests.length;
    const index = owner.requests.findIndex(request => request.id === requestId);
    if (index < 0) return owner.requests.length;
    return kind === RequestDropKind.After ? index + 1 : index;
}

export function requestRelocation(
    collections: Collection[],
    requestId: string,
    targetCollectionId: string,
    targetIndex: number,
): {source: Collection; target: Collection; fromIndex: number; insertAt: number} | undefined {
    const source = collections.find(owner => owner.requests.some(request => request.id === requestId));
    const target = collections.find(owner => owner.id === targetCollectionId);
    if (!source || !target) return;
    const fromIndex = source.requests.findIndex(request => request.id === requestId);
    if (fromIndex < 0) return;
    let insertAt = Math.max(0, Math.min(targetIndex, target.requests.length));
    if (source.id === target.id) {
        if (insertAt === fromIndex || insertAt === fromIndex + 1) return;
        if (fromIndex < insertAt) insertAt -= 1;
    }
    return {source, target, fromIndex, insertAt};
}

export function placeRequest(
    collections: Collection[],
    requestId: string,
    targetCollectionId: string,
    targetIndex: number,
): boolean {
    const relocation = requestRelocation(collections, requestId, targetCollectionId, targetIndex);
    if (!relocation) return false;
    const [request] = relocation.source.requests.splice(relocation.fromIndex, 1);
    relocation.target.requests.splice(relocation.insertAt, 0, request);
    return true;
}
