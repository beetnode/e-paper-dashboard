import { describe, expect, it } from 'vitest';
import type { WidgetConfig } from './types';
import { findFreeSlot, moveWidgetInPaintOrder } from './designer-layers';

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
