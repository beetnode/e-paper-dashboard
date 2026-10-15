import { DashboardLayout, WidgetConfig, WidgetPosition } from './types';

/** Rectangle in unscaled canvas pixels. */
export interface CanvasRect {
  x: number;
  y: number;
  width: number;
  height: number;
}

/**
 * A layer painted over the last rendered image while a newer render is pending: either a plain
 * fill hiding stale pixels or a crop of the rendered image moved to where a widget now sits.
 */
export interface RenderPatch {
  key: string;
  rect: CanvasRect;
  color?: string;
  crop?: { x: number; y: number };
}

/** The image is a renderer snapshot of a previous layout: what it shows and where. */
export interface RenderedSnapshot {
  layout: DashboardLayout;
  bounds: Readonly<Record<string, CanvasRect | undefined>>;
}

/** True when both layouts place grid cells at the same pixels. */
export function sameCanvasGrid(first: DashboardLayout, second: DashboardLayout): boolean {
  return first.width === second.width
    && first.height === second.height
    && first.gridCols === second.gridCols
    && first.gridRows === second.gridRows
    && (first.canvasPadding ?? 0) === (second.canvasPadding ?? 0)
    && (first.widgetGap ?? 0) === (second.widgetGap ?? 0);
}

export function samePosition(first: WidgetPosition, second: WidgetPosition): boolean {
  return first.x === second.x && first.y === second.y && first.w === second.w && first.h === second.h;
}

/** True when the rendered image shows the widget in the cells it occupies now. */
export function isWidgetRenderCurrent(rendered: DashboardLayout, current: DashboardLayout, widget: WidgetConfig): boolean {
  if (!sameCanvasGrid(rendered, current)) return false;
  const renderedWidget = rendered.widgets.find(item => item.id === widget.id);
  return !!renderedWidget && samePosition(renderedWidget.position, widget.position);
}

/** Pixel rectangle the grid assigns to a position, matching the renderer's cell maths. */
export function gridRect(position: WidgetPosition, layout: DashboardLayout): CanvasRect {
  const padding = layout.canvasPadding ?? 0;
  const gap = layout.widgetGap ?? 0;
  const cols = Math.max(1, layout.gridCols);
  const rows = Math.max(1, layout.gridRows);
  const cellWidth = (layout.width - 2 * padding - gap * (cols - 1)) / cols;
  const cellHeight = (layout.height - 2 * padding - gap * (rows - 1)) / rows;
  return {
    x: padding + position.x * (cellWidth + gap),
    y: padding + position.y * (cellHeight + gap),
    width: position.w * cellWidth + (position.w - 1) * gap,
    height: position.h * cellHeight + (position.h - 1) * gap,
  };
}

export function widgetBackgroundColor(widget: WidgetConfig, layout: DashboardLayout): string {
  return widget.colorOverrides?.widgetBackgroundColor
    || layout.colorScheme.widgetBackgroundColor
    || layout.colorScheme.background;
}

/**
 * Patches that make the last rendered image show moved, resized and removed widgets where the
 * current layout has them, until the renderer catches up. Fills come first so crops paint on top.
 * Nothing is patched when the grid itself changed, since every widget would then be off.
 */
export function buildRenderPatches(snapshot: RenderedSnapshot, current: DashboardLayout): RenderPatch[] {
  if (!sameCanvasGrid(snapshot.layout, current)) return [];

  const currentWidgets = new Map(current.widgets.map(widget => [widget.id, widget]));
  const canvasColor = current.colorScheme.canvasBackgroundColor || current.colorScheme.background;
  const fills: RenderPatch[] = [];
  const crops: RenderPatch[] = [];

  for (const renderedWidget of snapshot.layout.widgets) {
    const bounds = snapshot.bounds[renderedWidget.id];
    if (!bounds) continue;
    const widget = currentWidgets.get(renderedWidget.id);
    if (widget && samePosition(widget.position, renderedWidget.position)) continue;

    fills.push({ key: `vacated-${renderedWidget.id}`, rect: bounds, color: canvasColor });
    if (!widget) continue;

    const target = gridRect(widget.position, current);
    fills.push({ key: `target-${widget.id}`, rect: target, color: widgetBackgroundColor(widget, current) });
    crops.push({
      key: `moved-${widget.id}`,
      rect: { x: target.x, y: target.y, width: Math.min(target.width, bounds.width), height: Math.min(target.height, bounds.height) },
      crop: { x: bounds.x, y: bounds.y },
    });
  }

  return [...fills, ...crops];
}
