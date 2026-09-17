export const DocumentTabRevealEdgePx = 36;
export const DocumentTabRevealIntervalMs = 160;
export const DocumentTabRevealPrevious = -1;
export const DocumentTabRevealNext = 1;
export const DocumentTabRevealNone = 0;

export function documentTabRevealDirection(
    pointerX: number,
    pointerY: number,
    bounds: {top: number; bottom: number; left: number; right: number},
): number {
    if (pointerY < bounds.top || pointerY > bounds.bottom) return DocumentTabRevealNone;
    const width = bounds.right - bounds.left;
    if (width <= 0) return DocumentTabRevealNone;
    const edge = Math.min(DocumentTabRevealEdgePx, width / 2);
    if (pointerX < bounds.left + edge) return DocumentTabRevealPrevious;
    if (pointerX > bounds.right - edge) return DocumentTabRevealNext;
    return DocumentTabRevealNone;
}
