import { Injectable, inject } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, Subject, tap } from 'rxjs';
import { AiConfig, AiGenerationResult, ConversationAgent } from '../models/types';

@Injectable({
  providedIn: 'root'
})
export class AiService {
  private readonly http = inject(HttpClient);
  private readonly widgetContentGenerated = new Subject<{ dashboardId: string; widgetId: string; content: string }>();
  readonly widgetContentGenerated$ = this.widgetContentGenerated.asObservable();

  getGlobalConfig(): Observable<AiConfig> {
    return this.http.get<AiConfig>('/api/ai/config');
  }

  updateGlobalConfig(config: AiConfig): Observable<AiConfig> {
    return this.http.put<AiConfig>('/api/ai/config', config);
  }

  getConfig(dashboardId: string): Observable<AiConfig> {
    return this.http.get<AiConfig>(`/api/ai/dashboards/${dashboardId}/config`);
  }

  updateConfig(dashboardId: string, config: AiConfig): Observable<AiConfig> {
    return this.http.put<AiConfig>(`/api/ai/dashboards/${dashboardId}/config`, config);
  }

  getConversationAgents(dashboardId: string, connectionId?: string): Observable<ConversationAgent[]> {
    const params: Record<string, string> = connectionId ? { connectionId } : {};
    return this.http.get<ConversationAgent[]>(`/api/ai/dashboards/${dashboardId}/conversation-agents`, { params });
  }

  generateDashboard(dashboardId: string, prompt?: string): Observable<AiGenerationResult> {
    return this.http.post<AiGenerationResult>(`/api/ai/dashboards/${dashboardId}/generate`, { prompt: prompt ?? null });
  }

  getGeneratedWidgets(dashboardId: string): Observable<AiGenerationResult> {
    return this.http.get<AiGenerationResult>(`/api/ai/dashboards/${dashboardId}/generated`);
  }

  clearGeneratedWidgets(dashboardId: string): Observable<void> {
    return this.http.delete<void>(`/api/ai/dashboards/${dashboardId}/generated`);
  }

  generateWidgetContent(dashboardId: string, widgetId: string): Observable<{ content: string }> {
    return this.http.post<{ content: string }>(`/api/ai/dashboards/${dashboardId}/widgets/${widgetId}/generate-content`, {}).pipe(
      tap(result => this.widgetContentGenerated.next({ dashboardId, widgetId, content: result.content }))
    );
  }

  getWidgetContent(dashboardId: string, widgetId: string): Observable<{ content: string | null }> {
    return this.http.get<{ content: string | null }>(`/api/ai/dashboards/${dashboardId}/widgets/${widgetId}/content`);
  }

  getAvailableModels(endpoint: string, apiKey?: string): Observable<{ id: string }[]> {
    const params: Record<string, string> = { endpoint };
    if (apiKey) {
      params['apiKey'] = apiKey;
    }
    return this.http.get<{ id: string }[]>('/api/ai/models', { params });
  }
}
