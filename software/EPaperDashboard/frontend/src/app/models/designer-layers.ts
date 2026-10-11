import type { WidgetConfig, WidgetPosition } from './types';

/** First top-left-most position where a w×h block fits without overlapping existing widgets. */
export function findFreeSlot(
  widgets: readonly WidgetConfig[], gridCols: number, gridRows: number, w: number, h: number,
): WidgetPosition | null {
  const occupied = new Set<string>();
  for (const widget of widgets) {
    for (let dx = 0; dx < widget.position.w; dx++) {
      for (let dy = 0; dy < widget.position.h; dy++) {
        occupied.add(`${widget.position.x + dx},${widget.position.y + dy}`);
      }
    }
  }

  for (let y = 0; y <= gridRows - h; y++) {
    for (let x = 0; x <= gridCols - w; x++) {
      let fits = true;
      for (let dx = 0; dx < w && fits; dx++) {
        for (let dy = 0; dy < h && fits; dy++) {
          if (occupied.has(`${x + dx},${y + dy}`)) fits = false;
        }
      }
      if (fits) return { x, y, w, h };
    }
  }
  return null;
}

/**
 * Moves a widget one step in paint order. Later widgets paint on top, so "forward" moves the
 * widget towards the end of the array. Returns the same array when the move is not possible.
 */
export function moveWidgetInPaintOrder(
  widgets: readonly WidgetConfig[], id: string, direction: 'forward' | 'backward',
): readonly WidgetConfig[] {
  const index = widgets.findIndex(widget => widget.id === id);
  const target = direction === 'forward' ? index + 1 : index - 1;
  if (index < 0 || target < 0 || target >= widgets.length) return widgets;
  const reordered = [...widgets];
  [reordered[index], reordered[target]] = [reordered[target], reordered[index]];
  return reordered;
}

/** Supported grid size; mirrors TransientLayoutValidator on the server. */
export const GRID_LIMITS = { min: 1, max: 100 } as const;

/** Whole number within [min, max]; empty or invalid input falls back. */
export function clampWhole(value: unknown, min: number, max: number, fallback: number): number {
  const number = typeof value === 'number' ? value : Number(value);
  if (value === '' || value === null || value === undefined || !Number.isFinite(number)) return fallback;
  return Math.min(max, Math.max(min, Math.round(number)));
}

/** Clamps a position so the widget keeps at least 1×1 and stays inside the grid. */
export function fitPositionToGrid(position: WidgetPosition, gridCols: number, gridRows: number): WidgetPosition {
  const w = Math.min(Math.max(1, position.w), gridCols);
  const h = Math.min(Math.max(1, position.h), gridRows);
  return {
    w,
    h,
    x: Math.min(Math.max(0, position.x), gridCols - w),
    y: Math.min(Math.max(0, position.y), gridRows - h),
  };
}

/** Moves or shrinks widgets that no longer fit after a grid change; reports how many changed. */
export function fitWidgetsToGrid(
  widgets: readonly WidgetConfig[], gridCols: number, gridRows: number,
): { widgets: WidgetConfig[]; changed: number } {
  let changed = 0;
  const fitted = widgets.map(widget => {
    const position = fitPositionToGrid(widget.position, gridCols, gridRows);
    const same = position.x === widget.position.x && position.y === widget.position.y
      && position.w === widget.position.w && position.h === widget.position.h;
    if (same) return widget;
    changed++;
    return { ...widget, position };
  });
  return { widgets: fitted, changed };
}
