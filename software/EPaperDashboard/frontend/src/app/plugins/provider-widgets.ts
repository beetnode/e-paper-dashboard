import type { ProviderWidgetDefinition } from './provider-widget-definition';
// Register plugin-specific widget modules here, alongside their backend DI registration. Prefer common widgets:
// they already accept data from every plugin that declares the matching capability.
export const PROVIDER_WIDGETS: readonly ProviderWidgetDefinition[] = [];
