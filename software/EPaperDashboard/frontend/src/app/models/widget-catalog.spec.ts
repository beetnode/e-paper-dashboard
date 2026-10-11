import { buildWidgetDefinitions, createDefaultWidgetConfig, getWidgetDefaultSize, getWidgetDefinition, isEntityCompatible, WIDGET_DEFINITIONS } from './widget-catalog';

describe('widget catalog', () => {
  it('defines every widget type once', () => {
    expect(new Set(WIDGET_DEFINITIONS.map(item => item.type)).size).toBe(WIDGET_DEFINITIONS.length);
  });

  it('creates independent nested defaults', () => {
    const first = createDefaultWidgetConfig('weather') as any;
    const second = createDefaultWidgetConfig('weather') as any;
    first.items[0].visible = false;
    expect(second.items[0].visible).toBe(true);
  });

  it('provides widget-specific sizes and capabilities', () => {
    expect(getWidgetDefaultSize('header', 8, 12)).toEqual({ w: 8, h: 1 });
    expect(getWidgetDefinition('app-icon').supportsTitle).toBe(false);
    expect(isEntityCompatible('calendar', { entityId: 'calendar.home', friendlyName: 'Home', domain: 'calendar', capabilities: ['calendar'] })).toBe(true);
    // A provider-specific domain alone never makes a source compatible.
    expect(isEntityCompatible('calendar', { entityId: 'calendar.home', friendlyName: 'Home', domain: 'calendar' })).toBe(false);
    expect(isEntityCompatible('graph', { entityId: 'sensor.temp', friendlyName: 'Temp', domain: 'sensor', capabilities: ['state'] })).toBe(false);
    expect(isEntityCompatible('graph', { entityId: 'sensor.temp', friendlyName: 'Temp', domain: 'sensor', capabilities: ['state', 'history'] })).toBe(true);
  });
});

describe('provider widget registration', () => {
  const calendar = (pluginId: string) => ({ entityId: 'source:connection:calendar', friendlyName: 'Calendar', domain: 'calendar', pluginId, capabilities: ['calendar'] });
  it('lets common widgets consume sources from any plugin', () => {
    expect(isEntityCompatible('calendar', calendar('google'))).toBe(true);
    expect(isEntityCompatible('calendar', calendar('home-assistant'))).toBe(true);
    expect(isEntityCompatible('todo', { ...calendar('google'), capabilities: ['todo'] })).toBe(true);
  });
  it('uses explicit capabilities rather than guessing from source ID or domain', () => {
    expect(isEntityCompatible('calendar', { ...calendar('example'), domain: 'custom-domain' })).toBe(true);
    expect(isEntityCompatible('calendar', { ...calendar('example'), capabilities: ['todo'] })).toBe(false);
    expect(isEntityCompatible('weather', { ...calendar('home-assistant'), capabilities: ['state'] })).toBe(false);
    expect(isEntityCompatible('weather', { ...calendar('example'), capabilities: ['weather', 'state'] })).toBe(true);
    expect(isEntityCompatible('header', calendar('google'))).toBe(false);
  });
  it('builds plugin variants from a common widget and rejects incomplete bespoke widgets', () => {
    const variant = { type: 'example.calendar', renderAs: 'calendar', label: 'Example calendar', category: 'example', pluginId: 'example' };
    const definitions = buildWidgetDefinitions(WIDGET_DEFINITIONS, [variant]);
    const built = definitions.find(d => d.type === 'example.calendar')!;
    expect(built.capability).toBe('calendar');
    expect(built.pluginId).toBe('example');
    expect(built.createDefaultConfig()).toEqual(getWidgetDefinition('calendar').createDefaultConfig());
    expect(() => buildWidgetDefinitions(WIDGET_DEFINITIONS, [{ ...variant, renderAs: 'missing' }])).toThrow();
    expect(() => buildWidgetDefinitions(WIDGET_DEFINITIONS, [{ type: 'example.bespoke', label: 'Bespoke', category: 'example', pluginId: 'example' } as any])).toThrow();
  });
});
