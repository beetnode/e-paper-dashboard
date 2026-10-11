import { Component, input, output, signal, computed, ElementRef, inject, OnDestroy, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';

let searchableSelectInstance = 0;

export interface SelectOption {
  value: string;
  label: string;
  /** Optional heading; consecutive options with the same group are listed under it. */
  group?: string;
}

@Component({
  selector: 'app-searchable-select',
  standalone: true,
  imports: [CommonModule],
  host: { '[class.ss-md]': "size() === 'md'" },
  styles: [`
    :host {
      position: relative;
      display: inline-block;
    }

    .ss-trigger {
      width: 100%;
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 0.5rem;
      cursor: pointer;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      padding: 0.25rem 0.5rem;
      font-size: 0.85rem;
      line-height: 1.5;
      border: 1px solid var(--bs-border-color);
      border-radius: 0.375rem;
      background-color: var(--bs-body-bg);
      color: var(--bs-body-color);
      transition: border-color 0.15s ease;
      text-align: left;
    }

    .ss-trigger:hover {
      border-color: var(--bs-primary);
    }

    .ss-trigger:disabled {
      cursor: not-allowed;
      opacity: 0.65;
    }

    .ss-trigger-text {
      overflow: hidden;
      text-overflow: ellipsis;
    }

    /* Full-size variant, matching .form-select in forms. */
    :host(.ss-md) .ss-trigger {
      min-height: var(--app-control-min-height, 2.75rem);
      padding: 0.375rem 0.75rem;
      font-size: 1rem;
      border-radius: var(--bs-border-radius);
    }

    :host(.ss-md) .ss-search,
    :host(.ss-md) .ss-option {
      padding: 0.5rem 0.75rem;
      font-size: 1rem;
    }

    .ss-group {
      padding: 0.5rem 0.5rem 0.25rem;
      color: var(--bs-secondary-color);
      font-size: 0.75rem;
      font-weight: 600;
      letter-spacing: 0.03em;
      text-transform: uppercase;
    }

    .ss-chevron {
      flex-shrink: 0;
      font-size: 0.65rem;
      transition: transform 0.15s ease;
    }

    .ss-chevron.open {
      transform: rotate(180deg);
    }

    .ss-dropdown {
      position: absolute;
      top: 100%;
      left: 0;
      z-index: 1050;
      min-width: 100%;
      max-height: 280px;
      margin-top: 2px;
      background: var(--bs-body-bg);
      border: 1px solid var(--bs-border-color);
      border-radius: 0.375rem;
      box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
      display: flex;
      flex-direction: column;
      overflow: hidden;
    }

    .ss-search {
      padding: 0.375rem 0.5rem;
      border: none;
      border-bottom: 1px solid var(--bs-border-color);
      outline: none;
      font-size: 0.85rem;
      background: var(--bs-body-bg);
      color: var(--bs-body-color);
    }

    .ss-search::placeholder {
      color: var(--bs-secondary-color);
    }

    .ss-options {
      overflow-y: auto;
      max-height: 230px;
    }

    .ss-option {
      display: block;
      width: 100%;
      border: 0;
      background: transparent;
      text-align: left;
      padding: 0.375rem 0.5rem;
      cursor: pointer;
      font-size: 0.85rem;
      white-space: nowrap;
      overflow: hidden;
      text-overflow: ellipsis;
      color: var(--bs-body-color);
    }

    .ss-option:hover {
      background: var(--bs-secondary-bg);
    }

    .ss-option.active {
      box-shadow: inset 3px 0 0 var(--bs-primary);
      background: var(--bs-secondary-bg);
    }

    .ss-option.selected {
      background: var(--bs-primary);
      color: white;
    }

    .ss-option.selected:hover {
      background: var(--bs-primary);
      opacity: 0.9;
    }

    .ss-empty {
      padding: 0.5rem;
      text-align: center;
      font-size: 0.85rem;
      color: var(--bs-secondary-color);
    }
  `],
  template: `
    <button #trigger type="button" class="ss-trigger" [attr.id]="inputId() || null"
      role="combobox"
      [disabled]="disabled()"
      [attr.aria-label]="ariaLabel()"
      [attr.aria-expanded]="isOpen()"
      [attr.aria-controls]="listboxId"
      aria-haspopup="listbox"
      (click)="toggle($event)"
      (keydown)="onTriggerKeydown($event)">
      <span class="ss-trigger-text" [class.text-muted]="!value()">
        {{ selectedLabel() }}
      </span>
      <i class="fa-solid fa-chevron-down ss-chevron" [class.open]="isOpen()" aria-hidden="true"></i>
    </button>
    @if (isOpen()) {
      <div class="ss-dropdown" (click)="$event.stopPropagation()">
        <input
          #searchInput
          class="ss-search"
          type="text"
          role="combobox"
          aria-autocomplete="list"
          [attr.aria-label]="searchPlaceholder()"
          [attr.aria-expanded]="true"
          [attr.aria-controls]="listboxId"
          [attr.aria-activedescendant]="activeOptionId()"
          [placeholder]="searchPlaceholder()"
          [value]="searchTerm()"
          (input)="onSearchInput($event)"
          (keydown)="onSearchKeydown($event)">
        <div class="ss-options" [id]="listboxId" role="listbox" [attr.aria-label]="ariaLabel()">
          @if (showEmptyOption()) {
            <button type="button" class="ss-option"
              [id]="optionId(0)"
              role="option"
              [class.selected]="!value()"
              [class.active]="activeIndex() === 0"
              [attr.aria-selected]="!value()"
              (mouseenter)="activeIndex.set(0)"
              (click)="select('')">
              {{ emptyLabel() }}
            </button>
          }
          @for (option of filteredOptions(); track option.value; let i = $index) {
            @if (option.group && option.group !== filteredOptions()[i - 1]?.group) {
              <div class="ss-group" role="presentation">{{ option.group }}</div>
            }
            <button type="button" class="ss-option"
              [id]="optionId(i + (showEmptyOption() ? 1 : 0))"
              role="option"
              [class.selected]="option.value === value()"
              [class.active]="activeIndex() === i + (showEmptyOption() ? 1 : 0)"
              [attr.aria-selected]="option.value === value()"
              (mouseenter)="activeIndex.set(i + (showEmptyOption() ? 1 : 0))"
              (click)="select(option.value)">
              {{ option.label }}
            </button>
          }
          @if (filteredOptions().length === 0 && searchTerm()) {
            <div class="ss-empty">No matches found</div>
          }
        </div>
      </div>
    }
  `
})
export class SearchableSelectComponent implements OnDestroy {
  private readonly elementRef = inject(ElementRef);
  private onDocumentClickBound = this.onDocumentClick.bind(this);
  private readonly instanceId = ++searchableSelectInstance;
  readonly listboxId = `searchable-select-listbox-${this.instanceId}`;
  readonly trigger = viewChild<ElementRef<HTMLButtonElement>>('trigger');
  readonly searchInput = viewChild<ElementRef<HTMLInputElement>>('searchInput');

  readonly options = input<SelectOption[]>([]);
  readonly value = input<string>('');
  readonly emptyLabel = input<string>('— None —');
  readonly showEmptyOption = input<boolean>(true);
  readonly searchPlaceholder = input<string>('Search…');
  readonly ariaLabel = input<string>('Select an option');
  readonly disabled = input<boolean>(false);
  /** 'md' matches full-size form controls; 'sm' keeps the compact look used in tables. */
  readonly size = input<'sm' | 'md'>('sm');
  /** Id for the trigger so a <label for> can point at it. */
  readonly inputId = input<string>('');
  readonly selectionChange = output<string>();

  readonly isOpen = signal(false);
  readonly searchTerm = signal('');
  readonly activeIndex = signal(0);

  readonly selectedLabel = computed(() => {
    const val = this.value();
    if (!val) return this.emptyLabel();
    const option = this.options().find(o => o.value === val);
    return option?.label ?? val;
  });

  readonly filteredOptions = computed(() => {
    const term = this.searchTerm().toLowerCase().trim();
    const opts = this.options();
    if (!term) return opts;
    return opts.filter(o => o.label.toLowerCase().includes(term) || !!o.group?.toLowerCase().includes(term));
  });

  readonly activeValues = computed(() => [
    ...(this.showEmptyOption() ? [''] : []),
    ...this.filteredOptions().map(option => option.value)
  ]);

  readonly activeOptionId = computed(() => this.isOpen() ? this.optionId(this.activeIndex()) : null);

  onSearchInput(event: Event): void {
    const value = (event.target as HTMLInputElement).value;
    this.searchTerm.set(value);
    this.activeIndex.set(0);
  }

  toggle(event: MouseEvent): void {
    event.stopPropagation();
    if (this.isOpen()) {
      this.close();
    } else {
      this.open();
    }
  }

  onTriggerKeydown(event: KeyboardEvent): void {
    if (this.disabled()) return;
    if (event.key === 'ArrowDown' || event.key === 'ArrowUp' || event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      if (!this.isOpen()) this.open();
    }
  }

  onSearchKeydown(event: KeyboardEvent): void {
    const values = this.activeValues();
    if (event.key === 'Escape') {
      event.preventDefault();
      this.close(true);
      return;
    }
    if (event.key === 'ArrowDown') {
      event.preventDefault();
      this.activeIndex.update(index => Math.min(index + 1, Math.max(values.length - 1, 0)));
    } else if (event.key === 'ArrowUp') {
      event.preventDefault();
      this.activeIndex.update(index => Math.max(index - 1, 0));
    } else if (event.key === 'Home') {
      event.preventDefault();
      this.activeIndex.set(0);
    } else if (event.key === 'End') {
      event.preventDefault();
      this.activeIndex.set(Math.max(values.length - 1, 0));
    } else if (event.key === 'Enter' && values.length > 0) {
      event.preventDefault();
      this.select(values[this.activeIndex()]);
    }
  }

  select(value: string): void {
    this.selectionChange.emit(value);
    this.close(true);
  }

  optionId(index: number): string {
    return `${this.listboxId}-option-${index}`;
  }

  private open(): void {
    this.searchTerm.set('');
    const currentIndex = this.activeValues().findIndex(optionValue => optionValue === this.value());
    this.activeIndex.set(Math.max(currentIndex, 0));
    this.isOpen.set(true);
    document.addEventListener('click', this.onDocumentClickBound, true);
    setTimeout(() => {
      this.searchInput()?.nativeElement.focus();
    });
  }

  private close(restoreFocus = false): void {
    this.isOpen.set(false);
    this.searchTerm.set('');
    document.removeEventListener('click', this.onDocumentClickBound, true);
    if (restoreFocus) setTimeout(() => this.trigger()?.nativeElement.focus());
  }

  private onDocumentClick(event: MouseEvent): void {
    if (!this.elementRef.nativeElement.contains(event.target as Node)) {
      this.close();
    }
  }

  ngOnDestroy(): void {
    document.removeEventListener('click', this.onDocumentClickBound, true);
  }
}
