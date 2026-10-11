import { CommonModule } from '@angular/common';
import { Component, OnDestroy, OnInit, computed, inject, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { DialogService } from '../../services/dialog.service';
import {
  PlaywrightComponentService,
  PlaywrightComponentStatus
} from '../../services/playwright-component.service';
import { ToastService } from '../../services/toast.service';

@Component({
  selector: 'app-system-settings',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './system-settings.component.html'
})
export class SystemSettingsComponent implements OnInit, OnDestroy {
  private readonly componentService = inject(PlaywrightComponentService);
  private readonly dialogService = inject(DialogService);
  private readonly toastService = inject(ToastService);
  private pollTimer?: ReturnType<typeof setTimeout>;

  readonly status = signal<PlaywrightComponentStatus | null>(null);
  readonly isLoading = signal(true);
  readonly isUninstalling = signal(false);
  readonly isTesting = signal(false);
  readonly progress = computed(() => {
    const value = this.status();
    if (!value?.totalBytes || value.totalBytes <= 0) return undefined;
    return Math.min(100, Math.round((value.bytesDownloaded / value.totalBytes) * 100));
  });

  ngOnInit(): void {
    this.refreshStatus();
  }

  ngOnDestroy(): void {
    if (this.pollTimer) clearTimeout(this.pollTimer);
  }

  install(): void {
    this.toastService.clear();
    this.componentService.install().subscribe({
      next: status => {
        this.status.set(status);
        this.schedulePoll();
      },
      error: err => this.toastService.error(err.error?.message || 'Could not start component installation.')
    });
  }

  async uninstall(): Promise<void> {
    await this.dialogService.confirm({
      title: 'Remove rendering component?',
      message: 'This removes the shared component for every user on this server. An enabled user can prepare it again on their next render.',
      confirmLabel: 'Remove component',
      isDangerous: true,
      onConfirm: async () => {
        this.isUninstalling.set(true);
        try {
          const status = await firstValueFrom(this.componentService.uninstall());
          this.status.set(status);
          this.toastService.success('Rendering component removed.');
        } catch (err: any) {
          this.toastService.error(err.error?.message || 'Could not remove the rendering component.');
        } finally {
          this.isUninstalling.set(false);
        }
      }
    });
  }

  test(): void {
    this.isTesting.set(true);
    this.componentService.test().subscribe({
      next: response => {
        this.toastService.success(response.message);
        this.isTesting.set(false);
      },
      error: err => {
        this.toastService.error(err.error?.message || 'Rendering component test failed.');
        this.isTesting.set(false);
      }
    });
  }

  formatBytes(bytes?: number): string {
    if (!bytes) return '0 MB';
    return `${(bytes / 1024 / 1024).toFixed(1)} MB`;
  }

  private refreshStatus(): void {
    this.componentService.getStatus().subscribe({
      next: status => {
        const previousState = this.status()?.state;
        this.status.set(status);
        this.isLoading.set(false);

        if (status.state === 'Installing') {
          this.schedulePoll();
        } else if (previousState === 'Installing' && status.state === 'Installed' && status.ready) {
          this.toastService.success('Rendering component installed and ready.');
        } else if (previousState === 'Installing' && status.state === 'Failed') {
          this.toastService.error(status.error || 'Rendering component installation failed.');
        }
        if (status.state === 'Installed' && !status.ready) this.schedulePoll();
      },
      error: () => {
        this.isLoading.set(false);
        this.toastService.error('Could not load component status.');
        if (this.status()?.state === 'Installing') this.schedulePoll();
      }
    });
  }

  private schedulePoll(): void {
    if (this.pollTimer) clearTimeout(this.pollTimer);
    this.pollTimer = setTimeout(() => this.refreshStatus(), 1000);
  }
}
