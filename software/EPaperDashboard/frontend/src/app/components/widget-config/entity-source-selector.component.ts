import { Component, EventEmitter, Input, Output, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';

export interface EntitySourceOption {
  entity_id: string;
  friendly_name?: string;
  domain?: string;
  device_class?: string;
  unit_of_measurement?: string;
  state?: string;
  connectionName?: string;
}

export interface SourceGroup<T> {
  name: string;
  sources: T[];
}

/** Groups sources by connection, keeping the order in which connections first appear. */
export function groupByConnection<T extends { connectionName?: string }>(sources: T[]): SourceGroup<T>[] {
  const groups = new Map<string, T[]>();
  for (const source of sources) {
    const name = source.connectionName || 'Other sources';
    groups.set(name, [...(groups.get(name) ?? []), source]);
  }
  return [...groups].map(([name, grouped]) => ({ name, sources: grouped }));
}

@Component({
  selector: 'app-entity-source-selector',
  standalone: true,
  imports: [FormsModule],
  template: `
    <div class="mb-3">
      <label class="form-label fw-semibold" for="entitySourceSelect">Data source</label>
      @if (loading) {
        <div class="text-muted small" role="status"><i class="fa fa-spinner fa-spin me-1" aria-hidden="true"></i>Loading sources…</div>
      } @else if (entities.length === 0) {
        <div class="alert alert-warning mb-0">
          <i class="fa fa-exclamation-triangle"></i> No compatible sources available. Add a connection from Connections.
        </div>
      } @else {
        @if (entities.length > 8) {
          <input class="form-control mb-2" type="search" placeholder="Filter sources…" aria-label="Filter available data sources"
            [ngModel]="query()" (ngModelChange)="query.set($event)" />
        }
        <select id="entitySourceSelect" class="form-select" [ngModel]="value" (ngModelChange)="valueChange.emit($event)">
          <option value="">Select source…</option>
          @for (group of groupedEntities(); track group.name) {
            <optgroup [label]="group.name">
              @for (entity of group.sources; track entity.entity_id) {
                <option [value]="entity.entity_id">{{ formatEntity(entity, false) }}</option>
              }
            </optgroup>
          }
        </select>
        @if (selectedEntity(); as selected) {
          <div class="d-flex align-items-center flex-wrap gap-2 mt-2 small">
            <span class="app-status-chip app-status-chip-success"><i class="fa-solid fa-circle-check" aria-hidden="true"></i> Available</span>
            @if (selected.state != null && selected.state !== '') {
              <span class="text-muted">Current value: <strong class="text-body">{{ selected.state }}{{ selected.unit_of_measurement ? ' ' + selected.unit_of_measurement : '' }}</strong></span>
            }
          </div>
        }
      }
    </div>
    <hr class="my-2" />
  `,
})
export class EntitySourceSelectorComponent {
  @Input() value = '';
  @Input() entities: EntitySourceOption[] = [];
  @Input() loading = false;
  @Output() valueChange = new EventEmitter<string>();

  readonly query = signal('');

  selectedEntity(): EntitySourceOption | undefined {
    return this.entities.find(entity => entity.entity_id === this.value);
  }

  filteredEntities(): EntitySourceOption[] {
    const query = this.query().trim().toLowerCase();
    if (!query) return this.entities;
    return this.entities.filter(entity => this.formatEntity(entity).toLowerCase().includes(query));
  }

  groupedEntities(): SourceGroup<EntitySourceOption>[] {
    return groupByConnection(this.filteredEntities());
  }

  formatEntity(entity: EntitySourceOption, withConnection = true): string {
    const label = entity.friendly_name || entity.entity_id;
    const name = withConnection && entity.connectionName ? `${entity.connectionName} · ${label}` : label;
    const details = [entity.domain, entity.device_class, entity.unit_of_measurement]
      .filter(Boolean)
      .join(', ');
    const state = entity.state != null && entity.state !== '' ? ` · ${entity.state}` : '';
    return `${name}${details ? ` (${details})` : ''}${state}`;
  }
}
