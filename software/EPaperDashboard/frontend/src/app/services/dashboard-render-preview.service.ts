import { inject, Injectable } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import { DashboardLayout } from '../models/types';
import { resolveApiUrl } from '../pipes/resolve-url.pipe';
import type { DataSourceStatus } from './dashboard-preview-data.service';

export interface RenderRectangle {
  x: number;
  y: number;
  width: number;
  height: number;
}

export interface WidgetRenderGeometry {
  id: string;
  type: string;
  bounds: RenderRectangle;
  contentBounds: RenderRectangle;
  editable: boolean;
  elements: EditableWidgetElementGeometry[];
}

export interface EditableWidgetElementGeometry {
  id: string;
  kind: string;
  index: number | null;
  bounds: RenderRectangle;
  position: RenderRectangle;
  layoutBinding: EditableElementLayoutBinding | null;
  label: string | null;
  movable: boolean;
  resizable: boolean;
}

export interface EditableElementLayoutBinding {
  xPath: string;
  yPath: string;
  widthPath: string;
  heightPath: string;
  seedConfig?: Record<string, unknown> | null;
}

export interface DashboardRenderPreview {
  revision: number;
  width: number;
  height: number;
  imageUrl: string;
  renderedAt: string;
  widgets: WidgetRenderGeometry[];
  sourceStatuses?: Record<string, DataSourceStatus>;
}

@Injectable({ providedIn: 'root' })
export class DashboardRenderPreviewService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);

  render(
    dashboardId: string,
    layout: DashboardLayout,
    revision: number,
    refreshData = false,
  ): Observable<DashboardRenderPreview> {
    return this.http.post<DashboardRenderPreview>(
      `/api/dashboards/${dashboardId}/designer-preview`,
      { layout, revision, refreshData },
    );
  }

  /** The image is loaded by `<img>`, not HttpClient, so it must carry the ingress base path itself. */
  toImageUrl(preview: DashboardRenderPreview): string {
    return resolveApiUrl(this.document, preview.imageUrl);
  }
}
