import { Component, ElementRef, Input, Output, EventEmitter, HostListener, Injector, OnInit, Signal, effect, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { A11yModule } from '@angular/cdk/a11y';

@Component({
  selector: 'app-rendered-preview-modal',
  standalone: true,
  imports: [CommonModule, A11yModule],
  template: `
    @if (isOpen()) {
      <div class="preview-backdrop position-fixed top-0 start-0 w-100 h-100">
        <div class="preview-dialog position-absolute top-50 start-50 translate-middle rounded"
          role="dialog" aria-modal="true" aria-labelledby="renderedPreviewTitle"
          cdkTrapFocus [cdkTrapFocusAutoCapture]="true">
          <!-- Modal Header -->
          <div class="preview-header d-flex justify-content-between align-items-center p-3">
            <h2 id="renderedPreviewTitle" class="h5 mb-0">{{ title }}</h2>
            <div class="d-flex gap-2 align-items-center">
              <button type="button" class="btn btn-ghost app-icon-button" aria-label="Reload preview" title="Reload" (click)="onReloadClick()">
                <i class="fa-solid fa-arrows-rotate" aria-hidden="true"></i>
              </button>
              <button #closeButton type="button" class="btn-close" aria-label="Close" (click)="onCloseClick()"></button>
            </div>
          </div>
          <!-- Modal Body -->
          <div class="preview-body flex-grow-1 overflow-auto p-3 d-flex justify-content-center align-items-flex-start">
            @if (isLoading()) {
              <div class="spinner-border text-primary" role="status">
                <span class="visually-hidden">Rendering preview</span>
              </div>
            } @else if (error()) {
              <div class="alert alert-danger mb-0" role="alert">{{ error() }}</div>
            } @else if (imageUrl()) {
              <img class="preview-image" [src]="imageUrl()" [alt]="title" />
            }
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .preview-backdrop {
      z-index: 1050;
      overflow: hidden;
      padding: 1rem;
      background: rgba(0, 0, 0, 0.55);
      backdrop-filter: blur(2px);
    }
    .preview-dialog {
      display: flex;
      flex-direction: column;
      width: min(90vw, 900px);
      height: min(90dvh, 600px);
      color: var(--bs-body-color);
      background: var(--bs-body-bg);
      border: 1px solid var(--bs-border-color);
      box-shadow: 0 1rem 3rem rgba(0, 0, 0, 0.3);
    }
    .preview-header {
      border-bottom: 1px solid var(--bs-border-color);
    }
    .preview-body {
      background-color: var(--bs-secondary-bg);
    }
    .preview-image {
      max-width: 100%;
      height: auto;
      object-fit: contain;
    }
  `]
})
export class RenderedPreviewModalComponent implements OnInit {
  private readonly injector = inject(Injector);
  private readonly closeButton = viewChild<ElementRef<HTMLButtonElement>>('closeButton');
  private previouslyFocusedElement: HTMLElement | null = null;
  private wasOpen = false;
  @Input() title = 'Preview';
  @Input() isOpen!: Signal<boolean>;
  @Input() isLoading!: Signal<boolean>;
  @Input() error!: Signal<string>;
  @Input() imageUrl!: Signal<string>;
  @Output() close = new EventEmitter<void>();
  @Output() reload = new EventEmitter<void>();

  ngOnInit(): void {
    effect(() => {
      const isOpen = this.isOpen?.() ?? false;
      if (isOpen && !this.wasOpen) {
        this.previouslyFocusedElement = document.activeElement as HTMLElement | null;
        setTimeout(() => this.closeButton()?.nativeElement.focus());
      } else if (!isOpen && this.wasOpen && this.previouslyFocusedElement) {
        const element = this.previouslyFocusedElement;
        this.previouslyFocusedElement = null;
        setTimeout(() => element.focus());
      }
      this.wasOpen = isOpen;
    }, { injector: this.injector });
  }

  onCloseClick(): void {
    this.close.emit();
  }

  onReloadClick(): void {
    this.reload.emit();
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.isOpen()) this.onCloseClick();
  }
}
