import { Component, EventEmitter, Input, Output } from '@angular/core';
import { SearchableSelectComponent, SelectOption } from '../searchable-select/searchable-select.component';

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
  imports: [SearchableSelectComponent],
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
        <app-searchable-select class="d-block" size="md" inputId="entitySourceSelect"
          [options]="options()" [value]="value" emptyLabel="Select source…"
          searchPlaceholder="Search sources…" ariaLabel="Data source"
          (selectionChange)="valueChange.emit($event)" />
        @if (selectedEntity(); as selected) {
          @if (selected.state != null && selected.state !== '') {
            <div class="mt-2 small text-muted">Current value: <strong class="text-body">{{ selected.state }}{{ selected.unit_of_measurement ? ' ' + selected.unit_of_measurement : '' }}</strong></div>
          }
        }
      }
    </div>
  `,
})
export class EntitySourceSelectorComponent {
  @Input() value = '';
  @Input() entities: EntitySourceOption[] = [];
  @Input() loading = false;
  @Output() valueChange = new EventEmitter<string>();

  selectedEntity(): EntitySourceOption | undefined {
    return this.entities.find(entity => entity.entity_id === this.value);
  }

  /** Options grouped by connection for the searchable picker. */
  options(): SelectOption[] {
    return groupByConnection(this.entities).flatMap(group => group.sources.map(entity => ({
      value: entity.entity_id,
      label: this.formatEntity(entity, false),
      group: group.name,
    })));
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
