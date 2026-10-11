import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { map } from 'rxjs/operators';

export interface DataSource {
  entityId: string;
  friendlyName: string;
  domain: string;
  deviceClass?: string | null;
  unitOfMeasurement?: string | null;
  icon?: string | null;
  state?: string | null;
  supportedFeatures?: number | null;
  capabilities?: string[];
  pluginId?: string;
  connectionId?: string;
  connectionName?: string;
}
export interface ProviderManifest {
  id: string;
  name: string;
  requiresOAuth: boolean;
  credentials: { key: string; label: string; secret: boolean }[];
}
export interface ProviderConnection {
  id: string;
  name: string;
  pluginId: string;
  connected: boolean;
  credentials: Record<string, string>;
}
@Injectable({ providedIn: 'root' })
export class DataProviderService {
  private readonly http = inject(HttpClient);
  plugins() { return this.http.get<{ data: ProviderManifest[] }>('/api/providers').pipe(map(r => r.data)); }
  connections() { return this.http.get<{ data: ProviderConnection[] }>('/api/providers/connections').pipe(map(r => r.data)); }
  sources(dashboardId: string) { return this.http.get<{ data: DataSource[]; errors: string[] }>(`/api/dashboards/${dashboardId}/sources`); }
  getEntities(dashboardId: string) { return this.sources(dashboardId).pipe(map(r => r.data)); }
  save(id: string | null, body: { name: string; pluginId: string; credentials: Record<string, string> }) {
    return id ? this.http.put(`/api/providers/connections/${id}`, body) : this.http.post('/api/providers/connections', body);
  }
  remove(id: string) { return this.http.delete(`/api/providers/connections/${id}`); }
  authorize(id: string) { return this.http.post<{ authUrl: string }>(`/api/providers/connections/${id}/authorize`, {}); }
}
