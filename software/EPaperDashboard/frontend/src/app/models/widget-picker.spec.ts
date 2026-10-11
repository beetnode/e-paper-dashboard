import { describe, expect, it } from 'vitest';
import type { WidgetDefinition } from './widget-definition';
import {
  filterWidgetDefinitions,
  groupWidgetDefinitions,
  rememberRecentWidget,
  resolveRecentWidgets,
} from './widget-picker';

function definition(type: string, label: string, category: string, extra: Partial<WidgetDefinition> = {}): WidgetDefinition {
  return {
    type, label, category, icon: 'fa-x',
    defaultSize: { w: 1, h: 1 }, minSize: { w: 1, h: 1 }, supportsTitle: false,
    createDefaultConfig: () => ({}),
    ...extra,
  } as WidgetDefinition;
}

const weather = definition('weather', 'Weather', 'data', { description: 'Current conditions from a weather source' });
const markdown = definition('markdown', 'Markdown', 'content', { description: 'Formatted text' });
const plugin = definition('plugin-energy', 'Energy', 'energy', { categoryLabel: 'Energy meters' });

describe('filterWidgetDefinitions', () => {
  it('returns everything for an empty query', () => {
    expect(filterWidgetDefinitions([weather, markdown], '  ')).toEqual([weather, markdown]);
  });

  it('matches every term against label, description and category', () => {
    expect(filterWidgetDefinitions([weather, markdown], 'current weather')).toEqual([weather]);
    expect(filterWidgetDefinitions([weather, markdown], 'TEXT')).toEqual([markdown]);
    expect(filterWidgetDefinitions([weather, plugin], 'meters')).toEqual([plugin]);
    expect(filterWidgetDefinitions([weather, markdown], 'weather text')).toEqual([]);
  });
});

describe('groupWidgetDefinitions', () => {
  it('orders built-in categories first, appends plugin categories and drops empty ones', () => {
    const groups = groupWidgetDefinitions([plugin, weather, markdown]);
    expect(groups.map(group => group.label)).toEqual(['Content', 'Data', 'Energy meters']);
    expect(groups[1].widgets).toEqual([weather]);
  });
});

describe('recent widgets', () => {
  it('keeps the newest first without duplicates and caps the list', () => {
    expect(rememberRecentWidget(['a', 'b', 'c', 'd'], 'c')).toEqual(['c', 'a', 'b', 'd']);
    expect(rememberRecentWidget(['a', 'b', 'c', 'd'], 'e')).toEqual(['e', 'a', 'b', 'c']);
  });

  it('drops types that are no longer available', () => {
    expect(resolveRecentWidgets(['markdown', 'gone', 'weather'], [weather, markdown])).toEqual([markdown, weather]);
  });
});
