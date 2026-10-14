export interface User {
  id: string;
  username: string;
  nickname?: string;
  isSuperUser: boolean;
  /** An administrator issued a temporary password; the user must choose a new one before doing anything else. */
  mustChangePassword?: boolean;
  /** False when the deployment signs everyone in as one shared user (the Home Assistant add-on). */
  userManagementEnabled?: boolean;
  /** The deployment provides a Home Assistant connection of its own (the add-on's server). */
  hasDefaultHomeAssistantConnection?: boolean;
}

export interface LoginRequest {
  username: string;
  password: string;
}

export interface RegisterRequest {
  username: string;
  password: string;
}

export interface SetupRequest extends RegisterRequest {
  /** One-time code the server writes to its log until the first administrator exists. */
  setupCode: string;
}

export type DashboardOrientation = 'Landscape' | 'Portrait';

export interface DashboardSizePreset {
  width: number;
  height: number;
  label: string;
}

export const DASHBOARD_SIZE_PRESETS: DashboardSizePreset[] = [
  { width: 800, height: 480, label: '7.5" E-Paper (800×480)' }
];

export const DEFAULT_DASHBOARD_SIZE = DASHBOARD_SIZE_PRESETS[0];

export function isDashboardSizeValid(width: number, height: number): boolean {
  const w = Math.max(width, height);
  const h = Math.min(width, height);
  return DASHBOARD_SIZE_PRESETS.some(s => s.width === w && s.height === h);
}

export interface Dashboard {
  id: string;
  userId: string;
  name: string;
  description: string;
  path?: string;
  /** Named Home Assistant connection used by 'HomeAssistant' rendering. */
  homeAssistantConnectionId?: string | null;
  updateTimes?: string[];
  layoutConfig?: DashboardLayout;
  renderingMode?: RenderingMode;
  orientation?: DashboardOrientation;
  screenWidth: number;
  screenHeight: number;
  isAiEnabled?: boolean;
  aiPrompt?: string;
  aiLeadTimeMinutes?: number;
  lastAiGenerationTime?: string;
  lastAiGenerationError?: string;
  aiConfig?: AiConfig;
  effectiveAiConfigMode?: AiConnectionMode;
}

export type RenderingMode = 'Custom' | 'HomeAssistant';

export interface CreateDashboardRequest {
  name: string;
  description?: string;
  orientation?: DashboardOrientation;
  screenWidth?: number;
  screenHeight?: number;
  renderingMode?: RenderingMode;
  path?: string;
  homeAssistantConnectionId?: string;
}

export interface UpdateDashboardRequest {
  name?: string;
  description?: string;
  path?: string;
  /** Empty string removes the connection. */
  homeAssistantConnectionId?: string;
  updateTimes?: string[];
  layoutConfig?: DashboardLayout;
  renderingMode?: RenderingMode;
  orientation?: DashboardOrientation;
  screenWidth?: number;
  screenHeight?: number;
  isAiEnabled?: boolean;
  aiPrompt?: string;
  aiLeadTimeMinutes?: number;
  aiConfig?: AiConfig;
}


// Dashboard Designer Types
export type WidgetType =
  | 'header'
  | 'markdown'
  | 'calendar'
  | 'weather'
  | 'weather-forecast'
  | 'graph'
  | 'todo'
  | 'app-icon'
  | 'image'
  | 'version'
  | 'rss-feed'
  | 'ai-content'
  | (string & {});

export interface WidgetPosition {
  x: number;
  y: number;
  w: number;
  h: number;
  /** Legacy derived values retained when reading older saved layouts. */
  pixelX?: number;
  pixelY?: number;
  pixelWidth?: number;
  pixelHeight?: number;
}


export interface AppIconConfig {
  size?: number;
  dithering?: boolean;   // when true, renderer applies dithering to map icon shades to the palette
}

export interface ImageConfig {
  imageUrl: string;
  fit?: 'contain' | 'cover' | 'fill';  // legacy, kept for backward compat
  zoom?: number;      // 1 = fit container, >1 = zoom in (default 1)
  offsetX?: number;   // -1 to +1, horizontal pan (default 0 = center)
  offsetY?: number;   // -1 to +1, vertical pan (default 0 = center)
  dithering?: boolean; // when true, renderer applies dithering to map image colors to the palette
}

export interface VersionConfig {
  // No configuration needed - displays the app version
}

export interface WidgetColorOverrides {
  widgetBackgroundColor?: string;
  widgetBorderColor?: string;
  widgetTitleTextColor?: string;
  widgetTextColor?: string;
  iconColor?: string;
}

export interface WidgetConfig {
  id: string;
  type: WidgetType;
  position: WidgetPosition;
  config:
  | HeaderConfig
  | MarkdownConfig
  | CalendarConfig
  | WeatherConfig
  | WeatherForecastConfig
  | GraphConfig
  | TodoConfig
  | AppIconConfig
  | ImageConfig
  | VersionConfig
  | RssFeedConfig
  | AiContentConfig;
  colorOverrides?: WidgetColorOverrides;
  titleOverride?: string;
  showTitle?: boolean;
  /** Designer-only layer name; not rendered. */
  name?: string;
  /** Per-widget font sizes in px that replace the dashboard defaults. */
  fontOverrides?: WidgetFontOverrides;
}

export interface WidgetFontOverrides {
  titleFontSize?: number;
  textFontSize?: number;
}

export interface HeaderConfig {
  title: string;
  badges?: BadgeConfig[];
  iconSize?: number;
  iconPosition?: 'left' | 'right';
  dithering?: boolean;   // when true, renderer applies dithering to the header icon
  /** Title element position/size as % of the header widget bounds (set by the visual editor) */
  titleX?: number;
  titleY?: number;
  titleW?: number;
  titleH?: number;
  /** Layout editor settings – stored with the widget so they persist across sessions */
  snapStep?: number;      // 0 = off, or 1 / 2 / 5 (%)
  showGuides?: boolean;
}

export interface BadgeConfig {
  /** Stable identity used by the native editor when badges are reordered. */
  id?: string;
  entityId?: string;
  icon?: string;
  /** Position and size as % of the header widget bounds (set by the visual editor) */
  x?: number;
  y?: number;
  w?: number;
  h?: number;
}

export interface MarkdownConfig {
  content: string;
}

export interface AiContentConfig {
  prompt: string;
  /** Controls whether smart-home data is included with the generation request. */
  dataScope?: 'none' | 'dashboard';
}

export type CalendarEventItemType = 'datetime' | 'title' | 'location' | 'description';

export interface CalendarEventItemConfig {
  type: CalendarEventItemType;
  visible?: boolean;
  /** Font Awesome icon class (e.g. 'fa-clock'). Each item type has a default. */
  icon?: string;
}

/** Default icon for each calendar event item type */
export function defaultCalendarEventItemIcon(type: CalendarEventItemType): string {
  switch (type) {
    case 'datetime':    return 'fa-clock';
    case 'title':       return 'fa-heading';
    case 'location':    return 'fa-location-dot';
    case 'description': return 'fa-align-left';
    default:            return '';
  }
}

export const DEFAULT_CALENDAR_EVENT_ITEMS: CalendarEventItemConfig[] = [
  { type: 'datetime',    visible: true },
  { type: 'title',       visible: true },
  { type: 'location',    visible: false, icon: 'fa-location-dot' },
  { type: 'description', visible: false, icon: 'fa-align-left' },
];

export interface CalendarConfig {
  entityId: string;
  maxEvents: number;
  items?: CalendarEventItemConfig[];
  /** Gap between event entries in px (default 0) */
  eventGap?: number;
}

export type ForecastMode = 'hourly' | 'daily' | 'weekly';

export type ForecastField = 'time' | 'condition' | 'tempHigh' | 'tempLow' | 'precipitation' | 'wind';

export const ALL_FORECAST_FIELDS: ForecastField[] = ['time', 'condition', 'tempHigh', 'tempLow', 'precipitation', 'wind'];

export const DEFAULT_FORECAST_FIELDS: ForecastField[] = ['time', 'condition', 'tempHigh', 'tempLow'];

export const FORECAST_FIELD_LABELS: Record<ForecastField, string> = {
  time: 'Time',
  condition: 'Condition',
  tempHigh: 'Temperature High',
  tempLow: 'Temperature Low',
  precipitation: 'Precipitation %',
  wind: 'Wind Speed',
};

export interface WeatherForecastConfig {
  entityId: string;
  forecastMode?: ForecastMode; // 'hourly', 'daily', 'weekly' - defaults to 'daily'
  maxItems?: number; // Max forecast items to display (auto if not specified)
  visibleFields?: ForecastField[]; // Which fields to show in each column (defaults to DEFAULT_FORECAST_FIELDS)
  rowGap?: number; // Gap between rows in each column in px (default 0)
}

export function getWeatherForecastDataKey(entityId: string, forecastMode?: string): string {
  const forecastType = forecastMode === 'hourly' ? 'hourly' : 'daily';
  return `${entityId}\u0000${forecastType}`;
}

export type WeatherItemType = 'title' | 'temperature' | 'condition' | 'pressure' | 'attribute';

export interface WeatherItemConfig {
  /** Stable identity used by the native editor when items are reordered. */
  id?: string;
  type: WeatherItemType;
  visible?: boolean;
  /** Font Awesome icon class (e.g. 'fa-temperature-half'). Each item type has a default. */
  icon?: string;
  /** For 'attribute' type – which HA attribute key to display (e.g. 'humidity', 'wind_speed') */
  attributeKey?: string;
  /** Optional display label (e.g. "Humidity") */
  label?: string;
  /** Position and size as % of the weather widget bounds (set by the visual editor) */
  x?: number;
  y?: number;
  w?: number;
  h?: number;
}

/** Default icon for each weather item type */
export function defaultWeatherItemIcon(type: WeatherItemType, attributeKey?: string): string {
  switch (type) {
    case 'temperature': return 'fa-temperature-half';
    case 'condition':   return 'fa-cloud-sun';
    case 'pressure':    return 'fa-gauge';
    case 'attribute':
      switch (attributeKey) {
        case 'humidity':       return 'fa-droplet';
        case 'wind_speed':     return 'fa-wind';
        case 'wind_bearing':   return 'fa-compass';
        case 'visibility':     return 'fa-eye';
        case 'dew_point':      return 'fa-temperature-low';
        case 'cloud_coverage': return 'fa-cloud';
        case 'uv_index':       return 'fa-sun';
        default:               return 'fa-circle-info';
      }
    default: return '';
  }
}

export const DEFAULT_WEATHER_ITEMS: WeatherItemConfig[] = [
  { id: 'weather-title', type: 'title', visible: true, x: 0, y: 0, w: 100, h: 20 },
  { id: 'weather-temperature', type: 'temperature', visible: true, x: 0, y: 22, w: 50, h: 20, icon: 'fa-temperature-half' },
  { id: 'weather-condition', type: 'condition', visible: true, x: 50, y: 22, w: 50, h: 20, icon: 'fa-cloud-sun' },
  { id: 'weather-pressure', type: 'pressure', visible: true, x: 0, y: 44, w: 50, h: 20, icon: 'fa-gauge' },
  { id: 'weather-humidity', type: 'attribute', visible: true, x: 50, y: 44, w: 50, h: 20, attributeKey: 'humidity', label: 'Humidity', icon: 'fa-droplet' },
];

export interface WeatherConfig {
  entityId: string;
  items?: WeatherItemConfig[];
  /** Layout editor settings – stored with the widget so they persist across sessions */
  snapStep?: number;      // 0 = off, or 1 / 2 / 5 (%)
  showGuides?: boolean;
}

export interface GraphSeriesConfig {
  entityId: string;
  label?: string;
  color?: string;
}

export interface GraphConfig {
  series: GraphSeriesConfig[];
  period: '1h' | '6h' | '24h' | '7d' | '30d';
  plotType?: 'line' | 'bar';
  lineWidth?: number;
  barWidth?: number;
}

export interface TodoConfig {
  entityId: string;
  showCompleted?: boolean;
  maxItems?: number;
  pendingIcon?: string;
  completedIcon?: string;
}

export interface RssFeedConfig {
  entityId: string;
  title?: string;
}

export interface DashboardLayout {
  width: number;
  height: number;
  gridCols: number;
  gridRows: number;
  colorScheme: ColorScheme;
  widgets: WidgetConfig[];
  canvasPadding?: number;
  widgetGap?: number;
  widgetBorder?: number;
  widgetPadding?: number;
  titleFontSize?: number;
  textFontSize?: number;
  titleFontWeight?: number;
  textFontWeight?: number;
}

export interface ColorScheme {
  name: string;
  variant?: 'light' | 'dark';
  palette: string[]; // Allowed colors only (e.g., ['#000000', '#ff0000', '#ffffff'])
  background: string;
  canvasBackgroundColor: string;
  widgetBackgroundColor: string;
  widgetBorderColor: string;
  widgetTitleTextColor: string;
  widgetTextColor: string;
  iconColor: string;
  foreground: string;
  accent: string;
  text: string;
}

export interface EntityState {
  entityId: string;
  state: string;
  attributes?: Record<string, any>;
}

export const DEFAULT_COLOR_SCHEMES: ColorScheme[] = [
  {
    name: 'E-Paper Light (Black/Red/White)',
    palette: ['#000000', '#ff0000', '#ffffff'],
    background: '#ffffff',
    canvasBackgroundColor: '#ffffff',
    widgetBackgroundColor: '#ffffff',
    widgetBorderColor: '#000000',
    widgetTitleTextColor: '#000000',
    widgetTextColor: '#000000',
    iconColor: '#ff0000',
    foreground: '#000000',
    accent: '#ff0000',
    text: '#000000'
  },
  {
    name: 'E-Paper Dark (Black/Red/White)',
    variant: 'dark',
    palette: ['#000000', '#ff0000', '#ffffff'],
    background: '#000000',
    canvasBackgroundColor: '#000000',
    widgetBackgroundColor: '#000000',
    widgetBorderColor: '#ffffff',
    widgetTitleTextColor: '#ffffff',
    widgetTextColor: '#ffffff',
    iconColor: '#ff0000',
    foreground: '#ffffff',
    accent: '#ff0000',
    text: '#ffffff'
  },
  {
    name: 'E-Paper Light (Black/White)',
    variant: 'light',
    palette: ['#000000', '#ffffff'],
    background: '#ffffff',
    canvasBackgroundColor: '#ffffff',
    widgetBackgroundColor: '#ffffff',
    widgetBorderColor: '#000000',
    widgetTitleTextColor: '#000000',
    widgetTextColor: '#000000',
    iconColor: '#000000',
    foreground: '#000000',
    accent: '#000000',
    text: '#000000'
  },
  {
    name: 'E-Paper Dark (Black/White)',
    variant: 'dark',
    palette: ['#000000', '#ffffff'],
    background: '#000000',
    canvasBackgroundColor: '#000000',
    widgetBackgroundColor: '#000000',
    widgetBorderColor: '#ffffff',
    widgetTitleTextColor: '#ffffff',
    widgetTextColor: '#ffffff',
    iconColor: '#ffffff',
    foreground: '#ffffff',
    accent: '#ffffff',
    text: '#ffffff'
  },
  {
    name: 'E-Paper Light (Yellow/Black/White)',
    variant: 'light',
    palette: ['#000000', '#ffff00', '#ffffff'],
    background: '#ffffff',
    canvasBackgroundColor: '#ffffff',
    widgetBackgroundColor: '#ffffff',
    widgetBorderColor: '#000000',
    widgetTitleTextColor: '#000000',
    widgetTextColor: '#000000',
    iconColor: '#ffff00',
    foreground: '#000000',
    accent: '#ffff00',
    text: '#000000'
  },
  {
    name: 'E-Paper Dark (Yellow/Black/White)',
    variant: 'dark',
    palette: ['#000000', '#ffff00', '#ffffff'],
    background: '#000000',
    canvasBackgroundColor: '#000000',
    widgetBackgroundColor: '#000000',
    widgetBorderColor: '#ffffff',
    widgetTitleTextColor: '#ffffff',
    widgetTextColor: '#ffffff',
    iconColor: '#ffff00',
    foreground: '#ffffff',
    accent: '#ffff00',
    text: '#ffffff'
  }
];

// AI Configuration Types
export type AiConnectionMode = 'None' | 'HomeAssistant' | 'Direct';

export interface AiConfig {
  connectionMode: AiConnectionMode;
  directEndpoint?: string;
  directApiKey?: string;
  directModel?: string;
  homeAssistantAgentId?: string;
  /** Named Home Assistant connection for conversation agents; empty uses the add-on's default connection. */
  homeAssistantConnectionId?: string;
}

export interface AiGenerationResult {
  widgets: WidgetConfig[];
  generatedAt?: string;
  isAiEnabled?: boolean;
  prompt?: string;
  lastError?: string;
  dataSummary?: AiDataSummary;
  promptTokenEstimate?: number;
}

export interface AiDataSummary {
  entityStates: number;
  todoLists: string[];
  calendars: string[];
  weatherEntities: string[];
  rssFeeds: string[];
}

export interface ConversationAgent {
  id: string;
  name: string;
}
