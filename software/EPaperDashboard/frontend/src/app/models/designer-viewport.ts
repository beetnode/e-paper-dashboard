/** Zoom applied to the designer canvas: either fit to the visible area or a fixed factor. */
export type CanvasZoom = 'fit' | number;

export interface Size {
  width: number;
  height: number;
}

/** Fixed zoom factors offered by the zoom controls, in ascending order. */
export const CANVAS_ZOOM_STEPS: readonly number[] = [0.25, 0.33, 0.5, 0.67, 0.75, 1, 1.25, 1.5, 2];

const MIN_SCALE = 0.1;

/**
 * Largest scale (capped at 1:1 so device pixels stay honest) at which the canvas fits the
 * available area. A non-positive available height means height is unconstrained, which is
 * the case when the page itself scrolls vertically (phone layout).
 */
export function fitCanvasScale(available: Size, canvas: Size): number {
  if (canvas.width <= 0 || canvas.height <= 0 || available.width <= 0) return 1;
  const byWidth = available.width / canvas.width;
  const byHeight = available.height > 0 ? available.height / canvas.height : Number.POSITIVE_INFINITY;
  return Math.max(MIN_SCALE, Math.min(1, byWidth, byHeight));
}

export function resolveCanvasScale(zoom: CanvasZoom, available: Size, canvas: Size): number {
  return zoom === 'fit' ? fitCanvasScale(available, canvas) : zoom;
}

/** Next fixed zoom step strictly above (+1) or below (-1) the current effective scale. */
export function stepCanvasZoom(currentScale: number, direction: 1 | -1): number {
  const epsilon = 0.005;
  if (direction > 0) {
    return CANVAS_ZOOM_STEPS.find(step => step > currentScale + epsilon)
      ?? CANVAS_ZOOM_STEPS[CANVAS_ZOOM_STEPS.length - 1];
  }
  return [...CANVAS_ZOOM_STEPS].reverse().find(step => step < currentScale - epsilon)
    ?? CANVAS_ZOOM_STEPS[0];
}

/** Parses a persisted zoom value, falling back to fit for anything unrecognised. */
export function parseCanvasZoom(value: string | null | undefined): CanvasZoom {
  if (!value || value === 'fit') return 'fit';
  const parsed = Number(value);
  return CANVAS_ZOOM_STEPS.includes(parsed) ? parsed : 'fit';
}
