import type { ProviderWidgetDefinition } from '../provider-widget-definition';
export const HOME_ASSISTANT_WIDGETS: readonly ProviderWidgetDefinition[] = [
  { type: 'home-assistant.weather', renderAs: 'weather', label: 'HA Weather', category: 'home-assistant', pluginId: 'home-assistant' },
  { type: 'home-assistant.forecast', renderAs: 'weather-forecast', label: 'HA Forecast', category: 'home-assistant', pluginId: 'home-assistant' },
];
