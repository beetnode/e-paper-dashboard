import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { map } from 'rxjs/operators';

@Injectable({
  providedIn: 'root'
})
export class HomeAssistantService {
  private readonly http = inject(HttpClient);

  /** Lists the dashboards of a Home Assistant connection, to pick the one a dashboard renders. */
  getConnectionDashboards(connectionId: string): Observable<any[]> {
    return this.http.get<{ data: any[] }>(`/api/homeassistant/connections/${encodeURIComponent(connectionId)}/dashboards`).pipe(
      map(response => response.data || [])
    );
  }
}
