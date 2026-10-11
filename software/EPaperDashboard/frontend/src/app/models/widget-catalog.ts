import type { DataSource } from '../services/data-provider.service';
import { PROVIDER_WIDGETS } from '../plugins/provider-widgets';
import type { ProviderWidgetDefinition } from '../plugins/provider-widget-definition';
import type { WidgetDefinition } from './widget-definition';
export type { WidgetCategory, WidgetDefinition } from './widget-definition';
import {
  DEFAULT_CALENDAR_EVENT_ITEMS,
  DEFAULT_FORECAST_FIELDS,
  DEFAULT_WEATHER_ITEMS,
  WidgetConfig,
  WidgetType,
} from './types';

const COMMON_WIDGET_DEFINITIONS: readonly WidgetDefinition[] = [
  {
    type: 'header', capability: 'state', label: 'Header', description: 'Title bar with optional status badges', icon: 'fa-heading', category: 'content',
    defaultSize: { w: 12, h: 1 }, minSize: { w: 3, h: 1 }, supportsTitle: false,
    createDefaultConfig: () => ({ title: 'New Header', badges: [] }),
  },
  {
    type: 'markdown', label: 'Markdown', description: 'Formatted text, lists and headings', icon: 'fa-align-left', category: 'content',
    defaultSize: { w: 4, h: 2 }, minSize: { w: 2, h: 1 }, supportsTitle: false,
    createDefaultConfig: () => ({ content: '# Markdown Content' }),
  },
  {
    type: 'calendar', capability: 'calendar', label: 'Calendar', description: 'Upcoming events from a calendar', icon: 'fa-calendar', category: 'data',
    defaultSize: { w: 6, h: 3 }, minSize: { w: 3, h: 2 }, supportsTitle: true,
    createDefaultConfig: () => ({
      entityId: '', maxEvents: 7, items: DEFAULT_CALENDAR_EVENT_ITEMS.map(item => ({ ...item })),
    }),
  },
  {
    type: 'weather', capability: 'weather', label: 'Weather', description: 'Current conditions and readings', icon: 'fa-cloud-sun', category: 'data',
    defaultSize: { w: 4, h: 2 }, minSize: { w: 2, h: 1 }, supportsTitle: true,
    createDefaultConfig: () => ({ entityId: '', items: DEFAULT_WEATHER_ITEMS.map(item => ({ ...item })) }),
  },
  {
    type: 'weather-forecast', capability: 'forecast', label: 'Weather Forecast', description: 'Daily or hourly forecast', previewLabel: 'Forecast', icon: 'fa-cloud-sun-rain', category: 'data',
    defaultSize: { w: 6, h: 3 }, minSize: { w: 2, h: 1 }, supportsTitle: true,
    createDefaultConfig: () => ({ entityId: '', forecastMode: 'daily', visibleFields: [...DEFAULT_FORECAST_FIELDS] }),
  },
  {
    type: 'graph', capability: 'history', label: 'Graph', description: 'Line or bar chart of sensor history', icon: 'fa-chart-line', category: 'chart',
    defaultSize: { w: 6, h: 3 }, minSize: { w: 3, h: 2 }, supportsTitle: true,
    createDefaultConfig: () => ({ series: [], period: '24h', plotType: 'line', lineWidth: 2 }),
  },
  {
    type: 'todo', capability: 'todo', label: 'Todo List', description: 'Open items from a task list', previewLabel: 'Tasks', icon: 'fa-list-check', category: 'data',
    defaultSize: { w: 5, h: 3 }, minSize: { w: 2, h: 2 }, supportsTitle: true,
    createDefaultConfig: () => ({ entityId: '' }),
  },
  {
    type: 'rss-feed', capability: 'feed', label: 'RSS Feed', description: 'Latest headlines from a feed', icon: 'fa-rss', category: 'data',
    defaultSize: { w: 4, h: 4 }, minSize: { w: 3, h: 3 }, supportsTitle: true,
    createDefaultConfig: () => ({ entityId: '', title: 'Topic of the day' }),
  },
  {
    type: 'app-icon', label: 'App Icon', description: 'Single icon or logo', icon: 'fa-rocket', category: 'asset',
    defaultSize: { w: 2, h: 2 }, minSize: { w: 1, h: 1 }, supportsTitle: false,
    createDefaultConfig: () => ({ size: 48 }),
  },
  {
    type: 'image', label: 'Image', description: 'Picture from a URL or upload', icon: 'fa-image', category: 'asset',
    defaultSize: { w: 4, h: 3 }, minSize: { w: 2, h: 2 }, supportsTitle: true,
    createDefaultConfig: () => ({ imageUrl: '', fit: 'contain' }),
  },
  {
    type: 'version', label: 'Version', description: 'App version label', icon: 'fa-code-branch', category: 'system',
    defaultSize: { w: 2, h: 1 }, minSize: { w: 1, h: 1 }, supportsTitle: false,
    createDefaultConfig: () => ({}),
  },
  {
    type: 'ai-content', label: 'AI Content', description: 'Text generated from your prompt and data', icon: 'fa-wand-magic-sparkles', category: 'content',
    defaultSize: { w: 4, h: 3 }, minSize: { w: 2, h: 2 }, supportsTitle: true,
    createDefaultConfig: () => ({ prompt: '', dataScope: 'dashboard' }),
  },
];

/** Adds plugin widgets: variants inherit a common widget (`renderAs`), bespoke ones must be complete. */
export function buildWidgetDefinitions(
  common: readonly WidgetDefinition[], providers: readonly ProviderWidgetDefinition[]): readonly WidgetDefinition[] {
  return [
    ...common,
    ...providers.map(widget => {
      if (widget.renderAs) {
        const base = common.find(definition => definition.type === widget.renderAs);
        if (!base) throw new Error(`Unknown base widget: ${widget.renderAs}`);
        return { ...base, ...widget } as WidgetDefinition;
      }
      if (!widget.createDefaultConfig || !widget.defaultSize || !widget.minSize || !widget.icon)
        throw new Error(`Incomplete plugin widget: ${widget.type}`);
      return widget as WidgetDefinition;
    }),
  ];
}

export const WIDGET_DEFINITIONS = buildWidgetDefinitions(COMMON_WIDGET_DEFINITIONS, PROVIDER_WIDGETS);

export function getWidgetRenderType(type: WidgetType): WidgetType { return getWidgetDefinition(type).renderAs ?? type; }

const widgetDefinitionsByType = new Map(WIDGET_DEFINITIONS.map(definition => [definition.type, definition]));

export function getWidgetDefinition(type: WidgetType): WidgetDefinition {
  const definition = widgetDefinitionsByType.get(type);
  if (!definition) throw new Error(`Unknown widget type: ${type}`);
  return definition;
}

export function createDefaultWidgetConfig(type: WidgetType): WidgetConfig['config'] {
  return getWidgetDefinition(type).createDefaultConfig();
}

export function getWidgetDefaultSize(type: WidgetType, gridCols: number, gridRows: number) {
  const size = getWidgetDefinition(type).defaultSize;
  return { w: Math.min(size.w, gridCols), h: Math.min(size.h, gridRows) };
}

/** Compatibility comes only from plugin-declared capabilities, never from provider-specific source details. */
export function isEntityCompatible(type: WidgetType, entity: DataSource): boolean {
  const definition = getWidgetDefinition(type);
  if (definition.pluginId && entity.pluginId !== definition.pluginId) return false;
  if (definition.capability && !entity.capabilities?.includes(definition.capability)) return false;
  return definition.acceptsEntity?.(entity) ?? true;
}
