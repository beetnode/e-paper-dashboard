import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { WidgetConfig, ColorScheme, EntityState, WeatherForecastConfig, DashboardLayout, ForecastField, DEFAULT_FORECAST_FIELDS, getWeatherForecastDataKey } from '../../models/types';
import { resolveWidgetRenderContext } from './widget-render-context';

interface ForecastItem {
  id: number;
  time: string;
  condition: string;
  temp: string;
  tempHigh?: string;
  tempLow?: string;
  precip?: string;
  wind?: string;
}

@Component({
  selector: 'app-widget-weather-forecast',
  standalone: true,
  imports: [CommonModule],
  styleUrls: ['./weather-forecast-widget.component.scss'],
  template: `
    <div 
      class="weather-forecast-widget"
      [class.compact]="isCompactMode()"
      [class.tiny]="isTinyMode()"
      [class.height-1]="this.widget.position.h === 1"
      [class.height-2]="this.widget.position.h === 2"
      [class.height-3plus]="this.widget.position.h >= 3"
      [style.--titleFontSize]="getTitleFontSize() + 'px'"
      [style.--textFontSize]="getTextFontSize() + 'px'"
      [style.--titleFontWeight]="getTitleFontWeight()"
      [style.--textFontWeight]="getTextFontWeight()"
      [style.--smallFontSize]="getSmallFontSize() + 'px'"
      [style.--titleColor]="getTitleColor()"
      [style.--textColor]="getTextColor()"
      [style.--iconColor]="getIconColor()"
      [style.--rowGap]="getRowGap() + 'px'"
      [style.--widget-title-font-size]="getTitleFontSize() + 'px'"
      [style.--widget-title-font-weight]="getTitleFontWeight()"
      [style.--widget-title-color]="getTitleColor()"
      [style.color]="getTextColor()">
      
      @if (!isDataFetched()) {
        <div class="preview-state">
          <i class="fa fa-cloud-sun-rain"></i>
          <p>Forecast</p>
        </div>
      }
      
      @if (isDataFetched()) {
        @if (!isTinyMode() && widget.showTitle !== false) {
          <div class="widget-frame-title">
            {{ widget.titleOverride || 'Forecast' }}
          </div>
        }
        
        @if (getForecastItems().length > 0) {
          <div class="forecast-items" [class.hourly]="getForecastMode() === 'hourly'">
            @for (item of getForecastItems(); track item.id) {
              <div class="forecast-item">
                @if (isFieldVisible('time')) {
                  <div class="item-time">{{ item.time }}</div>
                }
                @if (isFieldVisible('condition') && !isTinyMode()) {
                  <div class="item-condition">{{ item.condition }}</div>
                }
                
                @if (isFieldVisible('tempHigh')) {
                  <div class="item-temp">{{ getForecastMode() === 'hourly' ? item.temp : item.tempHigh }}{{ getTemperatureUnit() }}</div>
                }
                @if (isFieldVisible('tempLow') && getForecastMode() !== 'hourly' && item.tempLow) {
                  <div class="item-temp item-temp-low">{{ item.tempLow }}{{ getTemperatureUnit() }}</div>
                }
                
                @if (isFieldVisible('precipitation') && item.precip !== undefined) {
                  <div class="item-precip">{{ item.precip }}%</div>
                }
                @if (isFieldVisible('wind') && item.wind !== undefined) {
                  <div class="item-wind">{{ item.wind }} {{ getWindSpeedUnit() }}</div>
                }
              </div>
            }
          </div>
        }
      }
    </div>
  `
})
export class WeatherForecastWidgetComponent {
  @Input() widget!: WidgetConfig;
  @Input() colorScheme!: ColorScheme;
  @Input() entityStates: Record<string, EntityState> | null = null;
  @Input() weatherForecastsByKey: Record<string, any[]> | null | undefined = null;
  @Input() designerSettings?: DashboardLayout;

  get config(): WeatherForecastConfig {
    return (this.widget?.config || {}) as WeatherForecastConfig;
  }

  getEntityState(entityId?: string) {
    if (!entityId || !this.entityStates) return null;
    return this.entityStates[entityId] ?? null;
  }

  /**
   * Checks if forecast data has been fetched for the configured entity.
   */
  isDataFetched(): boolean {
    const entityId = this.config.entityId;
    if (!entityId) return false;

    return this.getForecast().length > 0;
  }

  getWindSpeedUnit(): string {
    const state = this.getEntityState(this.config.entityId);
    return state?.attributes?.['wind_speed_unit'] || '';
  }

  getTemperatureUnit(): string {
    const state = this.getEntityState(this.config.entityId);
    return state?.attributes?.['temperature_unit'] || '°C';
  }

  getForecastMode(): 'hourly' | 'daily' | 'weekly' {
    return this.config.forecastMode || 'daily';
  }

  getVisibleFields(): ForecastField[] {
    const fields = this.config.visibleFields || DEFAULT_FORECAST_FIELDS;
    // Backward compat: migrate old 'temperature' field to 'tempHigh' + 'tempLow'
    if ((fields as string[]).includes('temperature')) {
      const migrated = fields.filter(f => f !== ('temperature' as any)) as ForecastField[];
      if (!migrated.includes('tempHigh')) migrated.push('tempHigh');
      if (!migrated.includes('tempLow')) migrated.push('tempLow');
      return migrated;
    }
    return fields;
  }

  isFieldVisible(field: ForecastField): boolean {
    return this.getVisibleFields().includes(field);
  }

  getForecastItems(): ForecastItem[] {
    let forecast = this.getForecast();
    const maxItems = this.getMaxForecastItems();
    const mode = this.getForecastMode();

    // For hourly mode, filter to show at least 1 hour apart
    if (mode === 'hourly') {
      forecast = this.filterHourlyForecast(forecast);
    }

    const items = forecast.slice(0, maxItems).map((item, idx) => ({
      id: idx,
      time: this.formatTime(item.datetime, mode, idx, forecast),
      condition: this.formatCondition(item.condition),
      temp: this.roundTemp(item.temperature),
      tempHigh: this.roundTemp(item.temperature),
      tempLow: this.roundTemp(item.templow),
      precip: item.precipitation_probability !== undefined && item.precipitation_probability !== null
        ? String(Math.round(Number(item.precipitation_probability)))
        : undefined,
      wind: item.wind_speed !== undefined && item.wind_speed !== null
        ? this.roundWind(item.wind_speed)
        : undefined
    }));

    return items;
  }

  private getForecast(): any[] {
    const entityId = this.config.entityId;
    if (!entityId) return [];

    const key = getWeatherForecastDataKey(entityId, this.getForecastMode());
    if (this.weatherForecastsByKey && key in this.weatherForecastsByKey) {
      return this.weatherForecastsByKey[key] || [];
    }

    const state = this.getEntityState(entityId);
    return (state?.attributes?.['forecast'] as any[] | undefined) || [];
  }

  private filterHourlyForecast(forecast: any[]): any[] {
    if (forecast.length === 0) return forecast;
    
    const filtered = [forecast[0]];
    
    for (let i = 1; i < forecast.length; i++) {
      const lastDate = new Date(filtered[filtered.length - 1].datetime);
      const currentDate = new Date(forecast[i].datetime);
      
      // Calculate difference in minutes
      const diffMinutes = (currentDate.getTime() - lastDate.getTime()) / (1000 * 60);
      
      // Only include if at least 60 minutes apart
      if (diffMinutes >= 60) {
        filtered.push(forecast[i]);
      }
    }
    
    return filtered;
  }

  private getMaxForecastItems(): number {
    if (this.config.maxItems !== undefined && this.config.maxItems > 0) {
      return this.config.maxItems;
    }

    const widgetWidth = this.widget?.position?.w ?? 1;
    const widgetHeight = this.widget?.position?.h ?? 1;
    const mode = this.getForecastMode();

    // Minimal mode (1x1): no items
    if (this.isMinimalMode()) return 0;
    
    // Tiny vertical (any width, height = 1): limit items due to space
    if (widgetHeight === 1) {
      if (mode === 'hourly') return Math.min(4, widgetWidth * 2);
      if (mode === 'daily') return Math.min(2, widgetWidth);
      if (mode === 'weekly') return 1;
      return 2;
    }

    // Compact height (height = 2): moderate items
    if (widgetHeight === 2) {
      if (mode === 'hourly') {
        if (widgetWidth === 1) return 3;
        if (widgetWidth === 2) return 5;
        return 7;
      }
      if (mode === 'daily') {
        if (widgetWidth === 1) return 2;
        if (widgetWidth === 2) return 3;
        return 4;
      }
      if (mode === 'weekly') {
        if (widgetWidth === 1) return 1;
        if (widgetWidth === 2) return 2;
        return 3;
      }
    }

    // Normal height (height >= 3): full items
    if (mode === 'hourly') {
      if (widgetWidth === 1) return 4;
      if (widgetWidth === 2) return 6;
      return 8;
    }

    if (mode === 'daily') {
      if (widgetWidth === 1) return 2;
      if (widgetWidth === 2) return 4;
      return 5;
    }

    if (mode === 'weekly') {
      if (widgetWidth === 1) return 1;
      if (widgetWidth === 2) return 2;
      return 4;
    }

    return 3;
  }

  private formatTime(datetime: string, mode: string, idx: number, forecast: any[]): string {
    if (!datetime) return '';
    
    let date: Date;
    
    // Parse the datetime string carefully to handle timezone issues
    // Home Assistant typically returns ISO format like "2026-02-04T14:30:00" or with timezone
    if (datetime.includes('Z') || datetime.includes('+') || datetime.includes('-00:')) {
      // Has explicit timezone indicator, use as-is
      date = new Date(datetime);
    } else {
      // No timezone indicator - treat as local time by parsing components manually
      // This prevents JavaScript from interpreting it as UTC
      const match = datetime.match(/(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})/);
      if (match) {
        const [, year, month, day, hours, minutes, seconds] = match;
        date = new Date(
          parseInt(year),
          parseInt(month) - 1,
          parseInt(day),
          parseInt(hours),
          parseInt(minutes),
          parseInt(seconds)
        );
      } else {
        // Fallback to standard parsing
        date = new Date(datetime);
      }
    }
    
    if (mode === 'hourly') {
      const hours = String(date.getHours()).padStart(2, '0');
      const minutes = String(date.getMinutes()).padStart(2, '0');
      return `${hours}:${minutes}`;
    }
    
    if (mode === 'weekly') {
      const days = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'];
      return days[date.getDay()];
    }
    
    // For daily forecast, show just the day number
    return String(date.getDate());
  }

  private formatCondition(condition: string): string {
    if (!condition) return '';
    
    const conditionMap: Record<string, string> = {
      'clear-night': 'Clear',
      'cloudy': 'Cloudy',
      'fog': 'Fog',
      'hail': 'Hail',
      'lightning': 'Storm',
      'lightning-rainy': 'Stormy',
      'partlycloudy': 'Pt. Cloudy',
      'pouring': 'Pouring',
      'rainy': 'Rainy',
      'snowy': 'Snowy',
      'snowy-rainy': 'Snowy Rain',
      'sunny': 'Sunny',
      'windy': 'Windy',
      'windy-variant': 'Windy',
      'exceptional': 'Exceptional'
    };
    
    return conditionMap[condition.toLowerCase()] || condition;
  }

  private roundTemp(temp: any): string {
    if (temp === undefined || temp === null) return '';
    return String(Math.round(Number(temp)));
  }

  private roundWind(wind: any): string {
    if (wind === undefined || wind === null) return '';
    return String(Math.round(Number(wind) * 10) / 10);
  }

  getTitleFontSize(): number {
    return this.renderContext.titleFontSize;
  }

  getTextFontSize(): number {
    return this.renderContext.textFontSize;
  }

  getTitleFontWeight(): number {
    return this.renderContext.titleFontWeight;
  }

  getTextFontWeight(): number {
    return this.renderContext.textFontWeight;
  }

  getSmallFontSize(): number {
    return Math.round(this.renderContext.textFontSize * 0.75);
  }

  getRowGap(): number {
    return this.config.rowGap ?? 0;
  }

  getTitleColor(): string {
    return this.renderContext.titleColor;
  }

  getTextColor(): string {
    return this.renderContext.textColor;
  }

  getIconColor(): string {
    return this.renderContext.iconColor;
  }

  private get renderContext() {
    return resolveWidgetRenderContext(this.widget, this.colorScheme, this.designerSettings);
  }

  isTinyMode(): boolean {
    const widgetWidth = this.widget?.position?.w ?? 1;
    const widgetHeight = this.widget?.position?.h ?? 1;
    
    // Minimal content on very small widgets
    return widgetWidth <= 2 || widgetHeight === 1;
  }

  isCompactMode(): boolean {
    const widgetWidth = this.widget?.position?.w ?? 1;
    const widgetHeight = this.widget?.position?.h ?? 1;
    
    // Tight spacing but show most data
    return (widgetWidth < 2 && widgetHeight <= 2) || (widgetHeight === 2 && widgetWidth <= 2);
  }

  isMinimalMode(): boolean {
    const widgetWidth = this.widget?.position?.w ?? 1;
    const widgetHeight = this.widget?.position?.h ?? 1;
    
    return widgetWidth === 1 && widgetHeight === 1;
  }
}
