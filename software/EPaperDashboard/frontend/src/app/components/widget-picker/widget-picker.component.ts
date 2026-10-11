import { AfterViewInit, Component, ElementRef, computed, input, output, signal, viewChild } from '@angular/core';
import type { WidgetDefinition } from '../../models/widget-definition';
import type { WidgetType } from '../../models/types';
import {
  filterWidgetDefinitions,
  groupWidgetDefinitions,
  resolveRecentWidgets,
} from '../../models/widget-picker';

export interface WidgetPickerDragStart {
  event: PointerEvent;
  widget: WidgetDefinition;
}

/**
 * Searchable widget catalogue shared by the desktop popover and the phone bottom sheet.
 * Clicking (or Enter) picks a widget; a mouse drag is handed to the host to drop on the canvas.
 */
@Component({
  selector: 'app-widget-picker',
  standalone: true,
  templateUrl: './widget-picker.component.html',
  styleUrls: ['./widget-picker.component.scss'],
})
export class WidgetPickerComponent implements AfterViewInit {
  readonly definitions = input.required<readonly WidgetDefinition[]>();
  readonly recentTypes = input<readonly WidgetType[]>([]);
  readonly allowDrag = input(true);
  readonly pick = output<WidgetDefinition>();
  readonly dragStart = output<WidgetPickerDragStart>();

  readonly query = signal('');
  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('search');

  readonly filtered = computed(() => filterWidgetDefinitions(this.definitions(), this.query()));
  readonly groups = computed(() => groupWidgetDefinitions(this.filtered()));
  readonly recent = computed(() =>
    this.query().trim() ? [] : resolveRecentWidgets(this.recentTypes(), this.definitions()));

  ngAfterViewInit(): void {
    // Focus without scrolling so the bottom sheet animation is not interrupted.
    this.searchInput()?.nativeElement.focus({ preventScroll: true });
  }

  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter') return;
    const first = this.filtered()[0];
    if (!first) return;
    event.preventDefault();
    this.pick.emit(first);
  }

  /** Set when a mouse press was handed to the host as a drag, so the following click is ignored. */
  private pressHandledAsDrag = false;

  onItemPointerDown(event: PointerEvent, widget: WidgetDefinition): void {
    if (!this.allowDrag() || event.pointerType !== 'mouse' || event.button !== 0) return;
    event.preventDefault();
    this.pressHandledAsDrag = true;
    this.dragStart.emit({ event, widget });
  }

  /** Keyboard, touch and pen activation; mouse presses are placed by the host's drag handling. */
  onItemClick(widget: WidgetDefinition): void {
    if (this.pressHandledAsDrag) {
      this.pressHandledAsDrag = false;
      return;
    }
    this.pick.emit(widget);
  }
}
