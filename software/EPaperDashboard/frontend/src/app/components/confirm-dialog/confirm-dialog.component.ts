import { Component, ElementRef, HostListener, effect, inject, viewChild } from '@angular/core';
import { CommonModule } from '@angular/common';
import { A11yModule } from '@angular/cdk/a11y';
import { DialogService } from '../../services/dialog.service';

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [CommonModule, A11yModule],
  template: `
    @if (dialogService.isOpen()) {
      <div class="modal-backdrop position-fixed top-0 start-0 w-100 h-100 d-flex align-items-center justify-content-center" 
           (click)="onBackdropClick($event)">
        <div class="card confirm-dialog" role="alertdialog" aria-modal="true"
          aria-labelledby="confirmDialogTitle" aria-describedby="confirmDialogMessage"
          cdkTrapFocus [cdkTrapFocusAutoCapture]="true" (click)="$event.stopPropagation()">
          <div class="card-body">
            <h2 id="confirmDialogTitle" class="h5 card-title mb-3">{{ dialogService.title() }}</h2>
            <p id="confirmDialogMessage" class="card-text mb-4">{{ dialogService.message() }}</p>
            <div class="d-flex gap-2 justify-content-end">
              <button 
                #cancelButton
                type="button" 
                class="btn btn-outline-secondary"
                [disabled]="dialogService.isLoading()"
                (click)="cancel()"
              >
                {{ dialogService.cancelLabel() }}
              </button>
              <button 
                type="button" 
                [ngClass]="'btn btn-' + (dialogService.isDangerous() ? 'danger' : 'primary')"
                [disabled]="dialogService.isLoading()"
                (click)="confirm()"
              >
                @if (dialogService.isLoading()) {
                  <span class="spinner-border spinner-border-sm me-2" role="status" aria-hidden="true"></span>
                }
                {{ dialogService.confirmLabel() }}
              </button>
            </div>
          </div>
        </div>
      </div>
    }
  `,
  styles: [`
    .modal-backdrop {
      z-index: 2000;
      background-color: rgba(0, 0, 0, 0.5);
      backdrop-filter: blur(2px);
      padding: 1rem;
    }

    .confirm-dialog {
      width: min(100%, 400px);
      max-height: calc(100dvh - 2rem);
      overflow-y: auto;
    }
  `]
})
export class ConfirmDialogComponent {
  protected dialogService = inject(DialogService);
  private readonly cancelButton = viewChild<ElementRef<HTMLButtonElement>>('cancelButton');
  private previouslyFocusedElement: HTMLElement | null = null;
  private wasOpen = false;

  constructor() {
    effect(() => {
      const isOpen = this.dialogService.isOpen();
      if (isOpen && !this.wasOpen) {
        this.previouslyFocusedElement = document.activeElement as HTMLElement | null;
        setTimeout(() => this.cancelButton()?.nativeElement.focus());
      } else if (!isOpen && this.wasOpen && this.previouslyFocusedElement) {
        const element = this.previouslyFocusedElement;
        this.previouslyFocusedElement = null;
        setTimeout(() => element.focus());
      }
      this.wasOpen = isOpen;
    });
  }

  @HostListener('document:keydown.escape')
  onEscape(): void {
    if (this.dialogService.isOpen() && !this.dialogService.isLoading()) this.cancel();
  }

  async confirm(): Promise<void> {
    await this.dialogService.handleConfirm();
  }

  cancel(): void {
    this.dialogService.handleCancel();
  }

  onBackdropClick(event: MouseEvent): void {
    // Prevent closing when clicking outside (modal behavior)
    event.stopPropagation();
  }
}
