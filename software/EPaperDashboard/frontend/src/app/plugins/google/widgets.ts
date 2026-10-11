import type { ProviderWidgetDefinition } from '../provider-widget-definition';
export const GOOGLE_WIDGETS: readonly ProviderWidgetDefinition[] = [
  { type: 'google.calendar', renderAs: 'calendar', label: 'Google Calendar', category: 'google', pluginId: 'google' },
  { type: 'google.tasks', renderAs: 'todo', label: 'Google Tasks', category: 'google', pluginId: 'google' },
];
