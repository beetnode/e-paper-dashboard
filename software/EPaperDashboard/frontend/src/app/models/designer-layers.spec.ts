import { describe, expect, it } from 'vitest';
import type { WidgetConfig } from './types';
import { clampWhole, findFreeSlot, fitPositionToGrid, fitWidgetsToGrid, moveWidgetInPaintOrder } from './designer-layers';

function widget(id: string, x: number, y: number, w: number, h: number): WidgetConfig {
  return { id, type: 'markdown', position: { x, y, w, h }, config: {} } as WidgetConfig;
}

describe('findFreeSlot', () => {
  it('returns the first top-left position that fits', () => {
    const widgets = [widget('a', 0, 0, 2, 2)];
    expect(findFreeSlot(widgets, 4, 4, 2, 2)).toEqual({ x: 2, y: 0, w: 2, h: 2 });
  });

  it('returns null when nothing fits', () => {
    expect(findFreeSlot([widget('a', 0, 0, 4, 4)], 4, 4, 1, 1)).toBeNull();
  });
});

describe('moveWidgetInPaintOrder', () => {
  const widgets = [widget('a', 0, 0, 1, 1), widget('b', 1, 0, 1, 1), widget('c', 2, 0, 1, 1)];

  it('swaps with the neighbour in the requested direction', () => {
    expect(moveWidgetInPaintOrder(widgets, 'a', 'forward').map(item => item.id)).toEqual(['b', 'a', 'c']);
    expect(moveWidgetInPaintOrder(widgets, 'c', 'backward').map(item => item.id)).toEqual(['a', 'c', 'b']);
  });

  it('returns the original array at the ends or for unknown ids', () => {
    expect(moveWidgetInPaintOrder(widgets, 'c', 'forward')).toBe(widgets);
    expect(moveWidgetInPaintOrder(widgets, 'a', 'backward')).toBe(widgets);
    expect(moveWidgetInPaintOrder(widgets, 'z', 'forward')).toBe(widgets);
  });
});

describe('clampWhole', () => {
  it('rounds, clamps and falls back for empty or invalid input', () => {
    expect(clampWhole(4.6, 1, 10, 3)).toBe(5);
    expect(clampWhole(0, 1, 10, 3)).toBe(1);
    expect(clampWhole(500, 1, 10, 3)).toBe(10);
    expect(clampWhole('', 1, 10, 3)).toBe(3);
    expect(clampWhole('abc', 1, 10, 3)).toBe(3);
    expect(clampWhole(null, 1, 10, 3)).toBe(3);
  });
});

describe('fitWidgetsToGrid', () => {
  it('moves widgets back inside and shrinks those larger than the grid', () => {
    const widgets = [widget('a', 10, 0, 2, 2), widget('b', 0, 0, 12, 1), widget('c', 0, 0, 1, 1)];
    const result = fitWidgetsToGrid(widgets, 8, 8);
    expect(result.changed).toBe(2);
    expect(result.widgets[0].position).toEqual({ x: 6, y: 0, w: 2, h: 2 });
    expect(result.widgets[1].position).toEqual({ x: 0, y: 0, w: 8, h: 1 });
    expect(result.widgets[2]).toBe(widgets[2]);
  });
});

describe('fitPositionToGrid', () => {
  it('keeps at least one cell', () => {
    expect(fitPositionToGrid({ x: -2, y: 9, w: 0, h: 3 }, 4, 4)).toEqual({ x: 0, y: 1, w: 1, h: 3 });
  });
});
