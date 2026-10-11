import { createDefaultWidgetConfig, getWidgetDefaultSize, getWidgetDefinition, isEntityCompatible, WIDGET_DEFINITIONS } from './widget-catalog';

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
    expect(isEntityCompatible('calendar', { entityId: 'calendar.home', friendlyName: 'Home', domain: 'calendar' })).toBe(true);
    expect(isEntityCompatible('calendar', { entityId: 'sensor.temp', friendlyName: 'Temp', domain: 'sensor' })).toBe(false);
  });
});

describe('provider widget registration', () => {
  const calendar = (pluginId: string) => ({ entityId: 'source:connection:calendar', friendlyName: 'Calendar', domain: 'calendar', pluginId, capabilities: ['calendar'] });
  it('allows common calendars to consume either plugin while keeping plugin widgets scoped', () => {
    expect(isEntityCompatible('calendar', calendar('google'))).toBe(true);
    expect(isEntityCompatible('calendar', calendar('home-assistant'))).toBe(true);
    expect(isEntityCompatible('google.calendar', calendar('google'))).toBe(true);
    expect(isEntityCompatible('google.calendar', calendar('home-assistant'))).toBe(false);
  });
  it('uses explicit capabilities rather than guessing from source ID or domain', () => {
    expect(isEntityCompatible('calendar', { ...calendar('example'), domain: 'custom-domain' })).toBe(true);
    expect(isEntityCompatible('calendar', { ...calendar('example'), capabilities: ['todo'] })).toBe(false);
    expect(isEntityCompatible('weather', { ...calendar('home-assistant'), capabilities: ['state'] })).toBe(false);
    expect(isEntityCompatible('weather', { ...calendar('example'), capabilities: ['weather', 'state'] })).toBe(true);
    expect(isEntityCompatible('header', calendar('google'))).toBe(false);
  });
  it('registers independent plugin widget defaults and their shared renderer', () => {
    expect(getWidgetDefinition('google.tasks').renderAs).toBe('todo');
    const first = createDefaultWidgetConfig('google.calendar') as any;
    const second = createDefaultWidgetConfig('calendar') as any;
    first.items[0].visible = false;
    expect(second.items[0].visible).toBe(true);
  });
});
