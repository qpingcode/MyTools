import type {ApiRequest, Collection} from './model.js';

export const CollectionPathSeparator = ' / ';

export function newCollection(id: string, name: string, parentId?: string): Collection {
    return {id, name, ...(parentId ? {parentId} : {}), requests: []};
}

export function rootCollections(collections: Collection[]): Collection[] {
    return collections.filter(owner => !owner.parentId);
}

export function childCollections(collections: Collection[], parentId: string): Collection[] {
    return collections.filter(owner => owner.parentId === parentId);
}

export function collectionSubtreeIds(collections: Collection[], rootId: string): Set<string> {
    const ids = new Set([rootId]);
    let changed = true;
    while (changed) {
        changed = false;
        for (const candidate of collections) {
            if (!candidate.parentId || !ids.has(candidate.parentId) || ids.has(candidate.id)) continue;
            ids.add(candidate.id);
            changed = true;
        }
    }
    return ids;
}

export function flattenCollectionTree(collections: Collection[], parentId?: string): Collection[] {
    return childList(collections, parentId).flatMap(owner => [owner, ...flattenCollectionTree(collections, owner.id)]);
}

export function collectionPath(collections: Collection[], owner: Collection): string {
    const names = [owner.name];
    const seen = new Set([owner.id]);
    let current: Collection | undefined = owner;
    while (current.parentId) {
        const parent = collections.find(candidate => candidate.id === current!.parentId);
        if (!parent || seen.has(parent.id)) break;
        seen.add(parent.id);
        names.unshift(parent.name);
        current = parent;
    }
    return names.join(CollectionPathSeparator);
}

export function collectionSubtreeRequests(collections: Collection[], owner: Collection): ApiRequest[] {
    return [
        ...childCollections(collections, owner.id).flatMap(child => collectionSubtreeRequests(collections, child)),
        ...owner.requests,
    ];
}

export function collectionMatchesSearch(collections: Collection[], owner: Collection, search: string): boolean {
    const needle = search.trim().toLowerCase();
    if (!needle) return true;
    if (owner.name.toLowerCase().includes(needle)) return true;
    if (owner.requests.some(request => (request.name + ' ' + request.url).toLowerCase().includes(needle))) return true;
    return childCollections(collections, owner.id).some(child => collectionMatchesSearch(collections, child, search));
}

function childList(collections: Collection[], parentId?: string): Collection[] {
    return parentId === undefined ? rootCollections(collections) : childCollections(collections, parentId);
}
