import { Component, EventEmitter, Input, Output } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { WidgetConfig } from '../../models/types';

@Component({
  selector: 'app-widget-title-config',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="mb-3">
      <label class="form-label fw-semibold" [for]="'widgetTitle-' + widget.id">Widget title</label>
      <div class="form-check form-switch mb-2">
        <input class="form-check-input" type="checkbox" [id]="'showTitle-' + widget.id"
          [checked]="widget.showTitle !== false" (change)="setVisible($event)" />
        <label class="form-check-label" [for]="'showTitle-' + widget.id">Show title</label>
      </div>
      <input type="text" class="form-control" [id]="'widgetTitle-' + widget.id" [ngModel]="widget.titleOverride ?? ''"
        (ngModelChange)="setTitle($event)" placeholder="Override widget title" />
      <small class="form-text text-muted">Leave empty to use the default title</small>
    </div>
  `,
})
export class WidgetTitleConfigComponent {
  @Input({ required: true }) widget!: WidgetConfig;
  @Output() widgetChange = new EventEmitter<WidgetConfig>();

  setVisible(event: Event): void {
    const showTitle = (event.target as HTMLInputElement).checked;
    this.widgetChange.emit({ ...this.widget, showTitle });
  }

  setTitle(titleOverride: string): void {
    this.widgetChange.emit({ ...this.widget, titleOverride: titleOverride || undefined });
  }
}
