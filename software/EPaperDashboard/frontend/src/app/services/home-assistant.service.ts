
import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

export interface HassEntity {
  entityId: string;
  friendlyName: string;
  domain: string;
  deviceClass?: string | null;
  unitOfMeasurement?: string | null;
  icon?: string | null;
  state?: string | null;
  supportedFeatures?: number | null;
}

@Injectable({
  providedIn: 'root'
})
export class HomeAssistantService {
  private readonly http = inject(HttpClient);

  startAuth(host: string, dashboardId: string): Observable<{ authUrl: string; state: string }> {
    return this.http.post<{ authUrl: string; state: string }>('/api/homeassistant/start-auth', {
      host: host,
      dashboardId: dashboardId
    });
  }

  getDashboards(dashboardId: string): Observable<any[]> {
    return this.http.get<{ data: any[] }>(`/api/dashboards/${dashboardId}/homeassistant/dashboards`).pipe(
      map(response => response.data || [])
    );
  }

  getConnectionDashboards(connectionId: string): Observable<any[]> {
    return this.http.get<{ data: any[] }>(`/api/homeassistant/connections/${encodeURIComponent(connectionId)}/dashboards`).pipe(
      map(response => response.data || [])
    );
  }

  getEntities(dashboardId: string): Observable<HassEntity[]> {
    return this.http.get<{ data: HassEntity[] }>(`/api/dashboards/${dashboardId}/homeassistant/designer/entity-metadata`).pipe(
      map(response => response.data || [])
    );
  }
}