export enum ListDropKind {
    Before = 'before',
    After = 'after',
}

export function listInsertionIndex(
    ids: readonly string[],
    targetId: string | undefined,
    kind: ListDropKind,
): number {
    if (!targetId) return ids.length;
    const index = ids.findIndex(id => id === targetId);
    if (index < 0) return ids.length;
    return kind === ListDropKind.After ? index + 1 : index;
}

export function listRelocation(
    ids: readonly string[],
    itemId: string,
    targetIndex: number,
): {fromIndex: number; insertAt: number} | undefined {
    const fromIndex = ids.findIndex(id => id === itemId);
    if (fromIndex < 0) return;
    let insertAt = Math.max(0, Math.min(targetIndex, ids.length));
    if (insertAt === fromIndex || insertAt === fromIndex + 1) return;
    if (fromIndex < insertAt) insertAt -= 1;
    return {fromIndex, insertAt};
}

export function placeListItem(ids: string[], itemId: string, targetIndex: number): boolean {
    const relocation = listRelocation(ids, itemId, targetIndex);
    if (!relocation) return false;
    const [id] = ids.splice(relocation.fromIndex, 1);
    ids.splice(relocation.insertAt, 0, id);
    return true;
}
