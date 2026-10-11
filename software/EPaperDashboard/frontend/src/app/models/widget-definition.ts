import type { Type } from '@angular/core';
import type { DataSource } from '../services/data-provider.service';
import type { WidgetConfig, WidgetType } from './types';

/** Core categories; plugins add their own and name them with `categoryLabel`. */
export type WidgetCategory = 'content' | 'data' | 'chart' | 'asset' | 'system' | (string & {});

export interface WidgetDefinition {
  type: WidgetType;
  renderAs?: WidgetType;
  pluginId?: string;
  capability?: string;
  categoryLabel?: string;
  /** Optional plugin components; inputs follow the contract in data-provider-plugins.md. */
  previewComponent?: Type<unknown>;
  configComponent?: Type<unknown>;
  label: string;
  /** One-line summary shown in the widget picker. */
  description?: string;
  previewLabel?: string;
  icon: string;
  category: WidgetCategory;
  defaultSize: { w: number; h: number };
  minSize: { w: number; h: number };
  supportsTitle: boolean;
  createDefaultConfig: () => WidgetConfig['config'];
  /** Optional extra filter for plugin widgets, applied after the capability check. */
  acceptsEntity?: (entity: DataSource) => boolean;
}

