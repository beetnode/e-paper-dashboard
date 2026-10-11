import { Component, Input } from '@angular/core';
import type { TodoItem } from '../../services/todo.service';
import type {
  CalendarEventData,
  DataSourceStatus,
  HistoryStateData,
  RssFeedEntryData,
  WeatherForecastData,
} from '../../services/dashboard-preview-data.service';
import { CommonModule } from '@angular/common';
import { AppIconWidgetComponent } from '../widgets/app-icon-widget.component';
import { ImageWidgetComponent } from '../widgets/image-widget.component';
import { HeaderWidgetComponent } from '../widgets/header-widget.component';
import { MarkdownWidgetComponent } from '../widgets/markdown-widget.component';
import { WeatherWidgetComponent } from '../widgets/weather-widget.component';
import { WeatherForecastWidgetComponent } from '../widgets/weather-forecast-widget.component';
import { GraphWidgetComponent } from '../widgets/graph-widget.component';
import { TodoWidgetComponent } from '../widgets/todo-widget.component';
import { CalendarWidgetComponent } from '../widgets/calendar-widget.component';
import { VersionWidgetComponent } from '../widgets/version-widget.component';
import { RssFeedWidgetComponent } from '../widgets/rss-feed-widget.component';
import { AiContentWidgetComponent } from '../widgets/ai-content-widget.component';
import {
  WidgetConfig,
  ColorScheme,
  HeaderConfig,
  MarkdownConfig,
  CalendarConfig,
  WeatherConfig,
  GraphConfig,
  GraphSeriesConfig,
  TodoConfig,
  EntityState,
  DashboardLayout,
  getWeatherForecastDataKey,
} from '../../models/types';
import { getWidgetDefinition, getWidgetRenderType } from '../../models/widget-catalog';

@Component({
  selector: 'app-widget-preview',
  standalone: true,
  imports: [
    CommonModule,
    AppIconWidgetComponent,
    ImageWidgetComponent,
    HeaderWidgetComponent,
    MarkdownWidgetComponent,
    WeatherWidgetComponent,
    WeatherForecastWidgetComponent,
    GraphWidgetComponent,
    TodoWidgetComponent,
    CalendarWidgetComponent,
    VersionWidgetComponent,
    RssFeedWidgetComponent,
    AiContentWidgetComponent
  ],
  template: `
    <div class="widget-preview">
      @if (!dataFetched || !hasDataForWidget()) {
        <div class="widget-preview-placeholder" [style.color]="colorScheme.text || 'currentColor'" [title]="getSourceError() || ''">
          <i class="fa {{ getWidgetIcon() }}" [style.color]="colorScheme.iconColor || colorScheme.accent || 'currentColor'"></i>
          <p>{{ getPlaceholderLabel() }}</p>
        </div>
      }
      @if (dataFetched && hasDataForWidget()) {
      @if (pluginPreviewComponent; as component) {
        <ng-container *ngComponentOutlet="component; inputs: pluginPreviewInputs" />
      } @else {
      @if (renderType === 'app-icon') {
        <app-widget-app-icon [widget]="widget" [colorScheme]="colorScheme"></app-widget-app-icon>
      }
      @if (renderType === 'image') {
        <app-widget-image [widget]="widget" [colorScheme]="colorScheme" [designerSettings]="designerSettings"></app-widget-image>
      }
      @if (renderType === 'header') {
        <app-widget-header [widget]="widget" [colorScheme]="colorScheme" [entityStates]="entityStates" [designerSettings]="designerSettings">
        </app-widget-header>
      }
      @if (renderType === 'markdown') {
        <app-widget-markdown [widget]="widget" [colorScheme]="colorScheme" [designerSettings]="designerSettings"></app-widget-markdown>
      }
      @if (renderType === 'weather') {
        <app-widget-weather [widget]="widget" [colorScheme]="colorScheme" [entityStates]="entityStates" [designerSettings]="designerSettings">
        </app-widget-weather>
      }
      @if (renderType === 'weather-forecast') {
        <app-widget-weather-forecast [widget]="widget" [colorScheme]="colorScheme" [entityStates]="entityStates" [weatherForecastsByKey]="weatherForecastsByKey" [designerSettings]="designerSettings"></app-widget-weather-forecast>
      }
      @if (renderType === 'graph') {
        <app-widget-graph [widget]="widget" [colorScheme]="colorScheme" [designerSettings]="designerSettings" [historyDataByEntityId]="historyDataByEntityId"></app-widget-graph>
      }
      @if (renderType === 'todo') {
        <app-widget-todo [widget]="widget" [colorScheme]="colorScheme" [entityStates]="entityStates" [todoItemsByEntityId]="todoItemsByEntityId" [designerSettings]="designerSettings"></app-widget-todo>
      }
      @if (renderType === 'calendar') {
        <app-widget-calendar [widget]="widget" [colorScheme]="colorScheme" [entityStates]="entityStates" [calendarEventsByEntityId]="calendarEventsByEntityId" [designerSettings]="designerSettings">
        </app-widget-calendar>
      }
      @if (renderType === 'version') {
        <app-widget-version [widget]="widget" [colorScheme]="colorScheme" [designerSettings]="designerSettings" [version]="appVersion"></app-widget-version>
      }
      @if (renderType === 'rss-feed') {
        <app-widget-rss-feed [widget]="widget" [colorScheme]="colorScheme" [designerSettings]="designerSettings" [rssFeedEntriesByEntityId]="rssFeedEntriesByEntityId"></app-widget-rss-feed>
      }
      @if (renderType === 'ai-content') {
        <app-widget-ai-content [widget]="widget" [colorScheme]="colorScheme" [designerSettings]="designerSettings" [dashboardId]="dashboardId" [generatedContent]="generatedContentByWidgetId?.[widget.id] || ''"></app-widget-ai-content>
      }
      }
      }
    </div>
  `,
  styleUrls: ['./widget-preview.component.scss']
})
export class WidgetPreviewComponent {
  @Input() todoItemsByEntityId?: Record<string, TodoItem[]>;
  @Input() calendarEventsByEntityId?: Record<string, CalendarEventData[]>;
  @Input() weatherForecastsByKey?: Record<string, WeatherForecastData[]>;
  @Input() rssFeedEntriesByEntityId?: Record<string, RssFeedEntryData[]>;
  @Input() historyDataByEntityId?: Record<string, HistoryStateData[]>;
  @Input() generatedContentByWidgetId?: Record<string, string>;
  @Input() sourceStatuses?: Record<string, DataSourceStatus>;
  @Input() widgetData?: Record<string, unknown>;
  get pluginPreviewComponent() { return getWidgetDefinition(this.widget.type).previewComponent; }
  get pluginPreviewInputs() { return {
    widget: this.widget, colorScheme: this.colorScheme, designerSettings: this.designerSettings,
    data: this.widgetData?.[this.widget.id],
  }; }
  @Input() appVersion = '';
  @Input() widget!: WidgetConfig;
  @Input() colorScheme!: ColorScheme;
  @Input() designerSettings?: DashboardLayout;
  @Input() entityStates: Record<string, EntityState> | null = null;
  @Input() dashboardId?: string;
  /** Whether live preview data has ever been fetched. When false, show icon+title placeholders. */
  @Input() dataFetched = true;
  get renderType() { return getWidgetRenderType(this.widget.type); }

  getWidgetIcon(): string {
    return getWidgetDefinition(this.widget.type).icon;
  }

  getWidgetLabel(): string {
    const definition = getWidgetDefinition(this.widget.type);
    return this.widget.titleOverride
      || definition.previewLabel
      || definition.label;
  }

  getPlaceholderLabel(): string {
    if (!this.dataFetched) return this.getWidgetLabel();
    const statuses = this.getWidgetSourceStatuses();
    if (statuses.some(status => status.state === 'error')) return 'Data unavailable';
    if (statuses.length > 0 && statuses.every(status => status.state === 'empty')) return 'No data';
    return this.getWidgetLabel();
  }

  getSourceError(): string | undefined {
    return this.getWidgetSourceStatuses().find(status => status.state === 'error')?.error;
  }

  private getWidgetSourceStatuses(): DataSourceStatus[] {
    if (!this.sourceStatuses) return [];
    const config = this.widget.config as any;
    const entityId = config?.entityId as string | undefined;
    const keys: string[] = [`widget:${this.widget.id}`];

    switch (this.renderType) {
      case 'header':
        keys.push(...((config?.badges ?? []) as any[])
          .map(badge => badge.entityId)
          .filter(Boolean)
          .map(id => `entity:${id}`));
        break;
      case 'weather':
        if (entityId) keys.push(`entity:${entityId}`);
        break;
      case 'weather-forecast':
        if (entityId) keys.push(`forecast:${entityId}:${config?.forecastMode === 'hourly' ? 'hourly' : 'daily'}`);
        break;
      case 'todo':
        if (entityId) keys.push(`todo:${entityId}`);
        break;
      case 'calendar':
        if (entityId) keys.push(`calendar:${entityId}`);
        break;
      case 'rss-feed':
        if (entityId) keys.push(`rss:${entityId}`);
        break;
      case 'graph':
        keys.push(...((config?.series ?? []) as GraphSeriesConfig[])
          .map(series => series.entityId)
          .filter(Boolean)
          .map(id => `history:${id}`));
        break;
    }

    return keys
      .map(key => this.sourceStatuses?.[key])
      .filter((status): status is DataSourceStatus => !!status);
  }

  asHeaderConfig(config: any): HeaderConfig {
    return config as HeaderConfig;
  }

  asMarkdownConfig(config: any): MarkdownConfig {
    return config as MarkdownConfig;
  }

  asCalendarConfig(config: any): CalendarConfig {
    return config as CalendarConfig;
  }

  asWeatherConfig(config: any): WeatherConfig {
    return config as WeatherConfig;
  }

  asGraphConfig(config: any): GraphConfig {
    return config as GraphConfig;
  }

  asTodoConfig(config: any): TodoConfig {
    return config as TodoConfig;
  }

  hasDataForWidget(): boolean {
    const type = this.renderType;
    const config = this.widget.config as any;
    const entityId = config?.entityId as string | undefined;

    if (!['header', 'weather', 'weather-forecast', 'graph', 'todo', 'calendar', 'rss-feed'].includes(type)) {
      return true;
    }

    if (type === 'header') {
      const badges = (config?.badges ?? []) as any[];
      const entityBadges = badges.filter((b: any) => b.entityId?.trim());
      if (entityBadges.length === 0) return true;
      return entityBadges.some((b: any) =>
        this.entityStates && this.entityStates[b.entityId]
      );
    }

    if (type === 'graph') {
      const series = (config?.series ?? []) as GraphSeriesConfig[];
      const entityIds = series.map(item => item.entityId).filter(Boolean);
      return entityIds.length > 0 && entityIds.some(entityId =>
        !!this.historyDataByEntityId && entityId in this.historyDataByEntityId);
    }

    if (!entityId) {
      return false;
    }

    if (type === 'todo') {
      return !!(this.todoItemsByEntityId && entityId in this.todoItemsByEntityId);
    }

    if (type === 'calendar') {
      return !!(this.calendarEventsByEntityId && entityId in this.calendarEventsByEntityId);
    }

    if (type === 'rss-feed') {
      return !!(this.rssFeedEntriesByEntityId && entityId in this.rssFeedEntriesByEntityId);
    }

    if (type === 'weather') {
      const state = this.entityStates?.[entityId];
      return !!(state?.attributes && state.attributes['temperature'] != null);
    }

    if (type === 'weather-forecast') {
      const key = getWeatherForecastDataKey(entityId, config?.forecastMode);
      return !!(this.weatherForecastsByKey && key in this.weatherForecastsByKey);
    }

    return !!(this.entityStates && this.entityStates[entityId]);
  }

  getEntityState(entityId?: string) {
    if (!entityId || !this.entityStates) return null;
    return this.entityStates[entityId] ?? null;
  }

  getForecastItems(entityId?: string) {
    const state = this.getEntityState(entityId);
    if (!state?.attributes?.['forecast']) return [];
    const forecast = state.attributes['forecast'] as any[];
    return forecast.slice(0, 2).map((item, idx) => ({ ...item, id: idx }));
  }

  getItemDate(item: any) {
    if (!item.datetime) return '';
    const dt = item.datetime as string;
    return dt.substring(5, 10);
  }

  getItemCondition(item: any) {
    return item.condition || '';
  }

  getItemTemp(item: any) {
    return item.temperature || '';
  }

  getTodoItems(entityId?: string): Array<{ id: string | number; complete: boolean; summary: string }> {
    if (this.todoItemsByEntityId && entityId && this.todoItemsByEntityId[entityId]) {
      // Map backend items to expected format for display
      const mapped = this.todoItemsByEntityId[entityId].map((item: any, idx: number) => ({
        ...item,
        id: item.uid || item.id || idx,
        // Home Assistant uses 'status' field: 'needs_action' (incomplete) or 'completed' (complete)
        complete: item.status === 'completed' || item.status === 'done' || item.complete === true || item.completed === true || false,
        summary: item.summary || item.title || ''
      }));
      mapped.sort((a, b) => {
        const ac = a.complete ? 1 : 0;
        const bc = b.complete ? 1 : 0;
        return ac - bc;
      });
      return mapped;
    }
    const state = this.getEntityState(entityId);
    if (!state?.attributes?.['todo_items']) return [];
    const items = state.attributes['todo_items'] as any[];
    return items.map((item: any, idx: number) => ({
      ...item,
      id: idx,
      complete: item.status === 'completed' || item.complete === true || false,
      summary: item.summary || ''
    }));
  }

  getPendingTodoCount(entityId?: string): number {
    const items = this.getTodoItems(entityId);
    return items.filter(i => !i.complete).length;
  }

  getTodoItemsLimited(entityId?: string, w = 2, h = 2): any[] {
    // Estimate how many items fit: roughly w * (h * 2)
    const max = Math.max(1, w * Math.max(1, h * 2));
    return this.getTodoItems(entityId).slice(0, max);
  }
}
