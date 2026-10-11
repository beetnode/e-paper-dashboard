import { Component, ElementRef, OnDestroy, computed, inject, input, output, signal, viewChild } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { IconCatalogService } from '../../services/icon-catalog.service';
import { iconName, searchIcons } from '../../models/icon-search';

let iconPickerInstance = 0;

/**
 * Searchable icon chooser for widget settings. Shows the current icon (or the default) and
 * emits values in the stored "fa-name" form; an empty string restores the default.
 */
@Component({
  selector: 'app-icon-picker',
  standalone: true,
  templateUrl: './icon-picker.component.html',
  styleUrls: ['./icon-picker.component.scss'],
})
export class IconPickerComponent implements OnDestroy {
  private readonly host = inject(ElementRef<HTMLElement>);
  private readonly catalog = inject(IconCatalogService);
  private readonly onDocumentPointerDown = (event: Event) => {
    if (!this.host.nativeElement.contains(event.target as Node)) this.close();
  };

  readonly value = input<string | null | undefined>('');
  /** Icon used when no value is set, e.g. the item's built-in icon. */
  readonly defaultIcon = input<string | null | undefined>('');
  readonly ariaLabel = input('Icon');
  readonly valueChange = output<string>();

  readonly panelId = `icon-picker-${++iconPickerInstance}`;
  readonly isOpen = signal(false);
  readonly query = signal('');
  private readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('search');
  private readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');

  private readonly names = toSignal(this.catalog.names(), { initialValue: [] as string[] });
  readonly results = computed(() => searchIcons(this.names(), this.query()));
  readonly current = computed(() => iconName(this.value()));
  readonly fallback = computed(() => iconName(this.defaultIcon()));
  readonly shown = computed(() => this.current() || this.fallback());

  toggle(): void {
    if (this.isOpen()) this.close();
    else this.open();
  }

  choose(name: string): void {
    this.valueChange.emit(name ? `fa-${name}` : '');
    this.close(true);
  }

  onKeydown(event: KeyboardEvent): void {
    if (event.key === 'Escape' && this.isOpen()) {
      event.preventDefault();
      event.stopPropagation();
      this.close(true);
    }
  }

  onSearchKeydown(event: KeyboardEvent): void {
    if (event.key !== 'Enter') return;
    event.preventDefault();
    const first = this.results()[0];
    if (first) this.choose(first);
  }

  ngOnDestroy(): void {
    document.removeEventListener('pointerdown', this.onDocumentPointerDown, true);
  }

  private open(): void {
    this.query.set('');
    this.isOpen.set(true);
    document.addEventListener('pointerdown', this.onDocumentPointerDown, true);
    setTimeout(() => this.searchInput()?.nativeElement.focus());
  }

  private close(restoreFocus = false): void {
    if (!this.isOpen()) return;
    this.isOpen.set(false);
    document.removeEventListener('pointerdown', this.onDocumentPointerDown, true);
    if (restoreFocus) setTimeout(() => this.trigger()?.nativeElement.focus());
  }
}
