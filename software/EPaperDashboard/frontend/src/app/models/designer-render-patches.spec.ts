import { describe, expect, it } from 'vitest';
import {
  buildRenderPatches,
  gridRect,
  isWidgetRenderCurrent,
  RenderedSnapshot,
} from './designer-render-patches';
import { DashboardLayout, DEFAULT_COLOR_SCHEMES, WidgetConfig } from './types';

function widget(id: string, x: number, y: number, w = 2, h = 2): WidgetConfig {
  return { id, type: 'markdown', position: { x, y, w, h }, config: {} } as WidgetConfig;
}

function layout(widgets: WidgetConfig[], overrides: Partial<DashboardLayout> = {}): DashboardLayout {
  return {
    width: 400,
    height: 200,
    gridCols: 4,
    gridRows: 2,
    canvasPadding: 0,
    widgetGap: 0,
    colorScheme: { ...DEFAULT_COLOR_SCHEMES[0], canvasBackgroundColor: '#eeeeee', widgetBackgroundColor: '#ffffff' },
    widgets,
    ...overrides,
  };
}

function snapshot(rendered: DashboardLayout): RenderedSnapshot {
  return {
    layout: rendered,
    bounds: Object.fromEntries(rendered.widgets.map(item => [item.id, gridRect(item.position, rendered)])),
  };
}

describe('gridRect', () => {
  it('accounts for canvas padding and widget gaps', () => {
    const value = layout([], { width: 420, height: 220, canvasPadding: 10, widgetGap: 0 });
    expect(gridRect({ x: 1, y: 1, w: 2, h: 1 }, value)).toEqual({ x: 110, y: 110, width: 200, height: 100 });

    const gapped = layout([], { width: 430, height: 200, widgetGap: 10 });
    expect(gridRect({ x: 1, y: 0, w: 2, h: 1 }, gapped)).toEqual({ x: 110, y: 0, width: 210, height: 95 });
  });
});

describe('isWidgetRenderCurrent', () => {
  it('is current only while the widget keeps its rendered cells', () => {
    const rendered = layout([widget('a', 0, 0)]);
    expect(isWidgetRenderCurrent(rendered, layout([widget('a', 0, 0)]), widget('a', 0, 0))).toBe(true);
    expect(isWidgetRenderCurrent(rendered, layout([widget('a', 1, 0)]), widget('a', 1, 0))).toBe(false);
    expect(isWidgetRenderCurrent(rendered, layout([widget('b', 0, 0)]), widget('b', 0, 0))).toBe(false);
  });

  it('is never current after the grid changed', () => {
    const rendered = layout([widget('a', 0, 0)]);
    expect(isWidgetRenderCurrent(rendered, layout([widget('a', 0, 0)], { gridCols: 8 }), widget('a', 0, 0))).toBe(false);
  });
});

describe('buildRenderPatches', () => {
  it('patches nothing while the image matches the layout', () => {
    const current = layout([widget('a', 0, 0)]);
    expect(buildRenderPatches(snapshot(current), current)).toEqual([]);
  });

  it('hides the vacated area and paints the rendered pixels at the new cells', () => {
    const rendered = layout([widget('a', 0, 0)]);
    const patches = buildRenderPatches(snapshot(rendered), layout([widget('a', 2, 0)]));

    expect(patches).toEqual([
      { key: 'vacated-a', rect: { x: 0, y: 0, width: 200, height: 200 }, color: '#eeeeee' },
      { key: 'target-a', rect: { x: 200, y: 0, width: 200, height: 200 }, color: '#ffffff' },
      { key: 'moved-a', rect: { x: 200, y: 0, width: 200, height: 200 }, crop: { x: 0, y: 0 } },
    ]);
  });

  it('never crops beyond the rendered widget when it grew', () => {
    const rendered = layout([widget('a', 0, 0, 1, 1)]);
    const patches = buildRenderPatches(snapshot(rendered), layout([widget('a', 0, 0, 2, 2)]));
    expect(patches.find(patch => patch.key === 'moved-a')?.rect).toEqual({ x: 0, y: 0, width: 100, height: 100 });
    expect(patches.find(patch => patch.key === 'target-a')?.rect).toEqual({ x: 0, y: 0, width: 200, height: 200 });
  });

  it('hides removed widgets', () => {
    const rendered = layout([widget('a', 0, 0), widget('b', 2, 0)]);
    const patches = buildRenderPatches(snapshot(rendered), layout([widget('a', 0, 0)]));
    expect(patches.map(patch => patch.key)).toEqual(['vacated-b']);
  });

  it('paints every crop above every fill so swapped widgets stay visible', () => {
    const rendered = layout([widget('a', 0, 0), widget('b', 2, 0)]);
    const patches = buildRenderPatches(snapshot(rendered), layout([widget('a', 2, 0), widget('b', 0, 0)]));
    const firstCrop = patches.findIndex(patch => patch.crop);
    expect(patches.slice(firstCrop).every(patch => patch.crop)).toBe(true);
    expect(patches.filter(patch => patch.crop)).toHaveLength(2);
  });

  it('patches nothing after the grid changed', () => {
    const rendered = layout([widget('a', 0, 0)]);
    expect(buildRenderPatches(snapshot(rendered), layout([widget('a', 1, 0)], { widgetGap: 4 }))).toEqual([]);
  });
});
