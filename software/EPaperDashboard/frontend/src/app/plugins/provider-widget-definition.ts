import type { WidgetDefinition } from '../models/widget-definition';
export type ProviderWidgetDefinition =
  | (WidgetDefinition & { pluginId: string })
  | (Pick<WidgetDefinition, 'type' | 'label' | 'category'> & { pluginId: string; renderAs: string } & Partial<WidgetDefinition>);
