import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { DashboardLayout, EntityState } from '../models/types';
import { TodoItem } from './todo.service';

export interface CalendarEventData {
  uid: string;
  summary: string;
  description?: string;
  location?: string;
  start: string;
  end?: string;
  allDay: boolean;
  recurrenceRule?: string;
}

export interface WeatherForecastData {
  datetime?: string;
  condition?: string;
  temperature?: number;
  templow?: number;
  precipitation_probability?: number;
  wind_speed?: number;
  [key: string]: unknown;
}

export interface RssFeedEntryData {
  title: string;
  link: string;
  published?: string;
  summary?: string;
}

export interface HistoryStateData {
  state: string;
  numericValue: number;
  lastChanged: string;
  attributes: Record<string, unknown>;
}

export interface DashboardPreviewData {
  widgetData?: Record<string, unknown>;
  entityStates: Record<string, EntityState>;
  todoItems: Record<string, TodoItem[]>;
  calendarEvents: Record<string, CalendarEventData[]>;
  weatherForecasts: Record<string, WeatherForecastData[]>;
  rssFeedEntries: Record<string, RssFeedEntryData[]>;
  historyData: Record<string, HistoryStateData[]>;
  generatedContent: Record<string, string>;
  sourceStatuses: Record<string, DataSourceStatus>;
  appVersion: string;
  fetchedAt: string;
}

export interface DataSourceStatus {
  state: 'ready' | 'empty' | 'error';
  error?: string;
  fetchedAt: string;
  fromCache: boolean;
}

@Injectable({ providedIn: 'root' })
export class DashboardPreviewDataService {
  private readonly http = inject(HttpClient);

  resolve(dashboardId: string, layout: DashboardLayout): Observable<DashboardPreviewData> {
    return this.http.post<DashboardPreviewData>(`/api/dashboards/${dashboardId}/preview-data`, layout);
  }
}
