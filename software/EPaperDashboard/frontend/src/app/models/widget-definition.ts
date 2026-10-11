import type { Type } from '@angular/core';
import type { DataSource } from '../services/data-provider.service';
import type { WidgetConfig, WidgetType } from './types';

export type WidgetCategory = 'content' | 'data' | 'home-assistant' | 'google' | 'chart' | 'asset' | 'system' | (string & {});

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
  previewLabel?: string;
  icon: string;
  category: WidgetCategory;
  defaultSize: { w: number; h: number };
  minSize: { w: number; h: number };
  supportsTitle: boolean;
  createDefaultConfig: () => WidgetConfig['config'];
  acceptsEntity?: (entity: DataSource) => boolean;
}

