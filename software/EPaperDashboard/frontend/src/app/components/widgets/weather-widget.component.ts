import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  ColorScheme,
  DashboardLayout,
  DEFAULT_WEATHER_ITEMS,
  defaultWeatherItemIcon,
  EntityState,
  WeatherConfig,
  WeatherItemConfig,
  WidgetConfig,
} from '../../models/types';
import { resolveWidgetRenderContext } from './widget-render-context';

interface VisibleWeatherItem {
  config: WeatherItemConfig;
  index: number;
}

@Component({
  selector: 'app-widget-weather',
  standalone: true,
  imports: [CommonModule],
  styleUrls: ['./weather-widget.component.scss'],
  template: `
    <div class="weather-widget"
         [style.--titleFontSize]="getTitleFontSize() + 'px'"
         [style.--textFontSize]="getTextFontSize() + 'px'"
         [style.--titleFontWeight]="getTitleFontWeight()"
         [style.--textFontWeight]="getTextFontWeight()"
         [style.--titleColor]="getTitleColor()"
         [style.--textColor]="getTextColor()"
         [style.--iconColor]="getIconColor()"
         [style.color]="getTextColor()">
      @if (!isDataFetched()) {
        <div class="preview-state">
          <i class="fa fa-cloud-sun"></i>
          <p>Weather</p>
        </div>
      } @else {
        @for (item of visibleItems(); track item.index) {
          <div class="ww-item"
               [style.left.%]="position(item.config).x"
               [style.top.%]="position(item.config).y"
               [style.width.%]="position(item.config).w"
               [style.height.%]="position(item.config).h">
            @switch (item.config.type) {
              @case ('title') {
                <span class="ww-title"
                      [style.fontSize.px]="getTitleFontSize()"
                      [style.fontWeight]="getTitleFontWeight()"
                      [style.color]="getTitleColor()">
                  {{ widget.titleOverride || 'Weather' }}
                </span>
              }
              @case ('temperature') {
                <span class="ww-value ww-with-icon"
                      [style.fontSize.px]="getTextFontSize()"
                      [style.fontWeight]="getTextFontWeight()"
                      [style.color]="getTextColor()">
                  <i class="fa {{ resolveIcon(item.config) }}" [style.color]="getIconColor()"></i>
                  {{ getAttr('temperature') }}°
                </span>
              }
              @case ('condition') {
                <span class="ww-value ww-with-icon"
                      [style.fontSize.px]="getTextFontSize()"
                      [style.fontWeight]="getTextFontWeight()"
                      [style.color]="getTextColor()">
                  <i class="fa {{ resolveIcon(item.config) }}" [style.color]="getIconColor()"></i>
                  {{ getState() }}
                </span>
              }
              @case ('pressure') {
                <span class="ww-value ww-with-icon"
                      [style.fontSize.px]="getTextFontSize()"
                      [style.fontWeight]="getTextFontWeight()"
                      [style.color]="getTextColor()">
                  <i class="fa {{ resolveIcon(item.config) }}" [style.color]="getIconColor()"></i>
                  {{ getAttr('pressure') }}
                </span>
              }
              @case ('attribute') {
                <span class="ww-value ww-with-icon"
                      [style.fontSize.px]="getTextFontSize()"
                      [style.fontWeight]="getTextFontWeight()"
                      [style.color]="getTextColor()">
                  <i class="fa {{ resolveIcon(item.config) }}" [style.color]="getIconColor()"></i>
                  {{ getAttr(item.config.attributeKey) }}{{ attributeSuffix(item.config) }}
                </span>
              }
            }
          </div>
        }
      }
    </div>
  `,
})
export class WeatherWidgetComponent {
  @Input() widget!: WidgetConfig;
  @Input() colorScheme!: ColorScheme;
  @Input() entityStates: Record<string, EntityState> | null = null;
  @Input() designerSettings?: DashboardLayout;

  get config(): WeatherConfig {
    return (this.widget?.config ?? {}) as WeatherConfig;
  }

  private get items(): WeatherItemConfig[] {
    return this.config.items?.length ? this.config.items : DEFAULT_WEATHER_ITEMS;
  }

  visibleItems(): VisibleWeatherItem[] {
    return this.items
      .map((config, index) => ({ config, index }))
      .filter(({ config }) => config.visible !== false &&
        (config.type !== 'title' || this.widget.showTitle !== false));
  }

  position(item: WeatherItemConfig) {
    const fallback = this.config.items?.length
      ? undefined
      : DEFAULT_WEATHER_ITEMS.find(candidate => candidate.type === item.type);
    return {
      x: item.x ?? fallback?.x ?? 0,
      y: item.y ?? fallback?.y ?? 0,
      w: item.w ?? fallback?.w ?? 100,
      h: item.h ?? fallback?.h ?? 20,
    };
  }

  isDataFetched(): boolean {
    const state = this.getEntityState(this.config.entityId);
    return state?.attributes?.['temperature'] !== undefined &&
      state.attributes['temperature'] !== null;
  }

  getEntityState(entityId?: string) {
    if (!entityId || !this.entityStates) return null;
    return this.entityStates[entityId] ?? null;
  }

  getState(): string {
    return this.getEntityState(this.config.entityId)?.state ?? '';
  }

  getAttr(key?: string): string {
    if (!key) return '';
    const value = this.getEntityState(this.config.entityId)?.attributes?.[key];
    return value === undefined || value === null ? '' : String(value);
  }

  attributeSuffix(item: WeatherItemConfig): string {
    return this.getAttr(item.attributeKey) && item.attributeKey === 'humidity' ? '%' : '';
  }

  resolveIcon(item: WeatherItemConfig): string {
    return item.icon || defaultWeatherItemIcon(item.type, item.attributeKey);
  }

  getTextFontSize(): number { return this.renderContext.textFontSize; }
  getTitleFontSize(): number { return this.renderContext.titleFontSize; }
  getTitleFontWeight(): number { return this.renderContext.titleFontWeight; }
  getTextFontWeight(): number { return this.renderContext.textFontWeight; }
  getTitleColor(): string { return this.renderContext.titleColor; }
  getTextColor(): string { return this.renderContext.textColor; }
  getIconColor(): string { return this.renderContext.iconColor; }

  private get renderContext() {
    return resolveWidgetRenderContext(this.widget, this.colorScheme, this.designerSettings);
  }
}
