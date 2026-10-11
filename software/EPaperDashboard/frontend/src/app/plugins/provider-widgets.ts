import { HOME_ASSISTANT_WIDGETS } from './home-assistant/widgets';
import { GOOGLE_WIDGETS } from './google/widgets';
// Register built-in frontend plugin modules here, alongside their backend DI registration.
export const PROVIDER_WIDGETS = [...HOME_ASSISTANT_WIDGETS, ...GOOGLE_WIDGETS];
