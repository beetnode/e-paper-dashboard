import type { DataSource } from '../services/data-provider.service';
import { PROVIDER_WIDGETS } from '../plugins/provider-widgets';
import type { WidgetDefinition } from './widget-definition';
export type { WidgetCategory, WidgetDefinition } from './widget-definition';
import {
  DEFAULT_CALENDAR_EVENT_ITEMS,
  DEFAULT_FORECAST_FIELDS,
  DEFAULT_WEATHER_ITEMS,
  WidgetConfig,
  WidgetType,
} from './types';

const graphDomains = new Set([
  'sensor', 'binary_sensor', 'input_number', 'number', 'counter', 'climate',
  'light', 'cover', 'fan', 'humidifier', 'water_heater', 'weather', 'person',
  'device_tracker', 'sun', 'zone',
]);

const domainIs = (...domains: string[]) => (entity: DataSource) => domains.includes(entity.domain.toLowerCase());

const COMMON_WIDGET_DEFINITIONS: readonly WidgetDefinition[] = [
  {
    type: 'header', capability: 'state', label: 'Header', icon: 'fa-heading', category: 'content',
    defaultSize: { w: 12, h: 1 }, minSize: { w: 3, h: 1 }, supportsTitle: false,
    createDefaultConfig: () => ({ title: 'New Header', badges: [] }),
    acceptsEntity: () => true,
  },
  {
    type: 'markdown', label: 'Markdown', icon: 'fa-align-left', category: 'content',
    defaultSize: { w: 4, h: 2 }, minSize: { w: 2, h: 1 }, supportsTitle: false,
    createDefaultConfig: () => ({ content: '# Markdown Content' }),
  },
  {
    type: 'calendar', capability: 'calendar', label: 'Calendar', icon: 'fa-calendar', category: 'data',
    defaultSize: { w: 6, h: 3 }, minSize: { w: 3, h: 2 }, supportsTitle: true,
    createDefaultConfig: () => ({
      entityId: '', maxEvents: 7, items: DEFAULT_CALENDAR_EVENT_ITEMS.map(item => ({ ...item })),
    }),
    acceptsEntity: domainIs('calendar'),
  },
  {
    type: 'weather', capability: 'weather', label: 'Weather', icon: 'fa-cloud-sun', category: 'data',
    defaultSize: { w: 4, h: 2 }, minSize: { w: 2, h: 1 }, supportsTitle: true,
    createDefaultConfig: () => ({ entityId: '', items: DEFAULT_WEATHER_ITEMS.map(item => ({ ...item })) }),
    acceptsEntity: domainIs('weather'),
  },
  {
    type: 'weather-forecast', capability: 'forecast', label: 'Weather Forecast', previewLabel: 'Forecast', icon: 'fa-cloud-sun-rain', category: 'data',
    defaultSize: { w: 6, h: 3 }, minSize: { w: 2, h: 1 }, supportsTitle: true,
    createDefaultConfig: () => ({ entityId: '', forecastMode: 'daily', visibleFields: [...DEFAULT_FORECAST_FIELDS] }),
    acceptsEntity: domainIs('weather'),
  },
  {
    type: 'graph', capability: 'history', label: 'Graph', icon: 'fa-chart-line', category: 'chart',
    defaultSize: { w: 6, h: 3 }, minSize: { w: 3, h: 2 }, supportsTitle: true,
    createDefaultConfig: () => ({ series: [], period: '24h', plotType: 'line', lineWidth: 2 }),
    acceptsEntity: entity => {
      const numericState = entity.state != null && entity.state.trim() !== '' && Number.isFinite(Number(entity.state));
      return numericState || graphDomains.has(entity.domain.toLowerCase());
    },
  },
  {
    type: 'todo', capability: 'todo', label: 'Todo List', previewLabel: 'Tasks', icon: 'fa-list-check', category: 'data',
    defaultSize: { w: 5, h: 3 }, minSize: { w: 2, h: 2 }, supportsTitle: true,
    createDefaultConfig: () => ({ entityId: '' }),
    acceptsEntity: domainIs('todo'),
  },
  {
    type: 'rss-feed', capability: 'feed', label: 'RSS Feed', icon: 'fa-rss', category: 'data',
    defaultSize: { w: 4, h: 4 }, minSize: { w: 3, h: 3 }, supportsTitle: true,
    createDefaultConfig: () => ({ entityId: '', title: 'Topic of the day' }),
    acceptsEntity: domainIs('event', 'sensor'),
  },
  {
    type: 'app-icon', label: 'App Icon', icon: 'fa-rocket', category: 'asset',
    defaultSize: { w: 2, h: 2 }, minSize: { w: 1, h: 1 }, supportsTitle: false,
    createDefaultConfig: () => ({ size: 48 }),
  },
  {
    type: 'image', label: 'Image', icon: 'fa-image', category: 'asset',
    defaultSize: { w: 4, h: 3 }, minSize: { w: 2, h: 2 }, supportsTitle: true,
    createDefaultConfig: () => ({ imageUrl: '', fit: 'contain' }),
  },
  {
    type: 'version', label: 'Version', icon: 'fa-code-branch', category: 'system',
    defaultSize: { w: 2, h: 1 }, minSize: { w: 1, h: 1 }, supportsTitle: false,
    createDefaultConfig: () => ({}),
  },
  {
    type: 'ai-content', label: 'AI Content', icon: 'fa-wand-magic-sparkles', category: 'content',
    defaultSize: { w: 4, h: 3 }, minSize: { w: 2, h: 2 }, supportsTitle: true,
    createDefaultConfig: () => ({ prompt: '', dataScope: 'dashboard' }),
  },
];

export const WIDGET_DEFINITIONS: readonly WidgetDefinition[] = [
  ...COMMON_WIDGET_DEFINITIONS,
  ...PROVIDER_WIDGETS.map(widget => {
    if (widget.renderAs) {
      const base = COMMON_WIDGET_DEFINITIONS.find(definition => definition.type === widget.renderAs);
      if (!base) throw new Error(`Unknown base widget: ${widget.renderAs}`);
      return { ...base, ...widget } as WidgetDefinition;
    }
    if (!widget.createDefaultConfig || !widget.defaultSize || !widget.minSize || !widget.icon)
      throw new Error(`Incomplete plugin widget: ${widget.type}`);
    return widget as WidgetDefinition;
  }),
];

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

export function isEntityCompatible(type: WidgetType, entity: DataSource): boolean {
  const definition = getWidgetDefinition(type);
  if (definition.pluginId && entity.pluginId !== definition.pluginId) return false;
  if (definition.capability && entity.capabilities) return entity.capabilities.includes(definition.capability);
  return definition.acceptsEntity?.(entity) ?? true;
}
