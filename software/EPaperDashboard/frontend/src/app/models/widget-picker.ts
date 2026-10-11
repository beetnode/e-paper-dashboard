import type { WidgetCategory, WidgetDefinition } from './widget-definition';
import type { WidgetType } from './types';

export interface WidgetCategoryGroup {
  type: WidgetCategory;
  label: string;
  widgets: readonly WidgetDefinition[];
}

export const RECENT_WIDGET_LIMIT = 4;

const BUILT_IN_CATEGORIES: ReadonlyArray<{ type: WidgetCategory; label: string }> = [
  { type: 'content', label: 'Content' },
  { type: 'data', label: 'Data' },
  { type: 'chart', label: 'Charts' },
  { type: 'asset', label: 'Assets' },
  { type: 'system', label: 'System' },
];

/** Every whitespace-separated term must appear in the widget's name, description or category. */
export function filterWidgetDefinitions(
  definitions: readonly WidgetDefinition[], query: string): readonly WidgetDefinition[] {
  const terms = query.trim().toLowerCase().split(/\s+/).filter(Boolean);
  if (terms.length === 0) return definitions;
  return definitions.filter(definition => {
    const haystack = [
      definition.label, definition.previewLabel, definition.description,
      definition.categoryLabel, definition.category, definition.type,
    ].filter(Boolean).join(' ').toLowerCase();
    return terms.every(term => haystack.includes(term));
  });
}

/** Built-in categories first in a fixed order, then plugin categories; empty groups are dropped. */
export function groupWidgetDefinitions(definitions: readonly WidgetDefinition[]): WidgetCategoryGroup[] {
  const categories = [...BUILT_IN_CATEGORIES];
  for (const definition of definitions) {
    if (!categories.some(category => category.type === definition.category)) {
      categories.push({ type: definition.category, label: definition.categoryLabel ?? definition.category });
    }
  }
  return categories
    .map(category => ({ ...category, widgets: definitions.filter(widget => widget.category === category.type) }))
    .filter(group => group.widgets.length > 0);
}

/** Most recent first, without duplicates, capped at {@link RECENT_WIDGET_LIMIT}. */
export function rememberRecentWidget(recent: readonly WidgetType[], type: WidgetType): WidgetType[] {
  return [type, ...recent.filter(item => item !== type)].slice(0, RECENT_WIDGET_LIMIT);
}

/** Resolves stored recent types, silently dropping widgets that no longer exist. */
export function resolveRecentWidgets(
  recent: readonly WidgetType[], definitions: readonly WidgetDefinition[]): WidgetDefinition[] {
  return recent.flatMap(type => definitions.filter(definition => definition.type === type));
}
