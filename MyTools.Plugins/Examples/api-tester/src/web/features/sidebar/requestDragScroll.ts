export const RequestDragScrollEdgePx = 36;
export const RequestDragScrollMaxPx = 14;

export function requestDragScrollDelta(
    pointerX: number,
    pointerY: number,
    bounds: {top: number; bottom: number; left: number; right: number},
): number {
    if (pointerX < bounds.left || pointerX > bounds.right) return 0;
    const height = bounds.bottom - bounds.top;
    if (height <= 0) return 0;
    const edge = Math.min(RequestDragScrollEdgePx, height / 2);
    const topZone = bounds.top + edge;
    const bottomZone = bounds.bottom - edge;
    if (pointerY < topZone) {
        return -RequestDragScrollMaxPx * Math.min(1, (topZone - pointerY) / edge);
    }
    if (pointerY > bottomZone) {
        return RequestDragScrollMaxPx * Math.min(1, (pointerY - bottomZone) / edge);
    }
    return 0;
}
