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
