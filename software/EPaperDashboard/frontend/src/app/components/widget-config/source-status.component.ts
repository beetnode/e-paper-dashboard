import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { NgClass } from '@angular/common';
import type { DataSourceStatus } from '../../services/dashboard-preview-data.service';

export interface SourceStatusEntry {
  key: string;
  label: string;
  status: DataSourceStatus;
}

/**
 * Data health shown at the end of a widget's Data section: a warning when no source is chosen,
 * otherwise a status chip that expands into per-source details.
 */
@Component({
  selector: 'app-source-status',
  standalone: true,
  imports: [NgClass],
  template: `
    @if (missing) {
      <div class="alert alert-warning d-flex gap-2 align-items-start mb-0 mt-2 small" role="status">
        <i class="fa-solid fa-triangle-exclamation mt-1" aria-hidden="true"></i>
        <span>Choose a data source. The widget stays empty on the device until one is selected.</span>
      </div>
    } @else if (statuses.length > 0) {
      <div class="mt-2">
        <button type="button" class="app-status-chip border-0" [ngClass]="'app-status-chip-' + summary().tone"
          (click)="open.set(!open())" [attr.aria-expanded]="open()" title="Show data source details">
          {{ summary().label }}
          <i class="fa-solid fa-chevron-down" [class.fa-rotate-180]="open()" aria-hidden="true"></i>
        </button>
        @if (open()) {
          <ul class="list-unstyled small mt-2 mb-1">
            @for (source of statuses; track source.key) {
              <li class="d-flex justify-content-between align-items-center gap-2 py-1">
                <span class="text-truncate" [title]="source.label">{{ source.label }}</span>
                <span class="app-status-chip"
                  [ngClass]="source.status.state === 'ready' ? 'app-status-chip-success' : (source.status.state === 'error' ? 'app-status-chip-warning' : 'app-status-chip-muted')"
                  [title]="source.status.error || (source.status.fromCache ? 'Using recently cached data' : '')">
                  {{ source.status.state === 'ready' ? 'Ready' : (source.status.state === 'empty' ? 'No data' : 'Error') }}
                </span>
              </li>
            }
          </ul>
          <button type="button" class="btn btn-sm btn-link px-0" (click)="refresh.emit()" [disabled]="refreshing">
            Refresh source data
          </button>
        }
      </div>
    }
  `,
})
export class SourceStatusComponent {
  @Input() statuses: SourceStatusEntry[] = [];
  @Input() missing = false;
  @Input() refreshing = false;
  @Output() refresh = new EventEmitter<void>();

  readonly open = signal(false);

  summary(): { tone: 'success' | 'warning' | 'muted'; label: string } {
    const errors = this.statuses.filter(source => source.status.state === 'error').length;
    if (errors > 0) return { tone: 'warning', label: errors === 1 ? '1 source issue' : `${errors} source issues` };
    if (this.statuses.some(source => source.status.state === 'empty')) return { tone: 'muted', label: 'No data' };
    return { tone: 'success', label: 'Data ready' };
  }
}
