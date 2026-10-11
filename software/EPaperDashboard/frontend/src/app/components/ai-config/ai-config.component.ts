import { Component, signal, computed, inject, OnInit } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { RouterModule } from '@angular/router';
import { AiService } from '../../services/ai.service';
import { ToastService } from '../../services/toast.service';
import { AiConfig, AiConnectionMode } from '../../models/types';
import { HasUnsavedChanges } from '../../guards/unsaved-changes.guard';

@Component({
  selector: 'app-ai-config',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule],
  templateUrl: './ai-config.component.html'
})
export class AiConfigComponent implements OnInit, HasUnsavedChanges {
  private readonly aiService = inject(AiService);
  private readonly toastService = inject(ToastService);

  private static readonly DEFAULT_CONFIG: AiConfig = { connectionMode: 'None' };
  readonly aiConfig = signal<AiConfig>(AiConfigComponent.DEFAULT_CONFIG);
  private originalAiConfig = JSON.stringify(AiConfigComponent.DEFAULT_CONFIG);
  readonly isLoading = signal(false);
  readonly loadError = signal('');
  readonly isSaving = signal(false);
  readonly availableModels = signal<{ id: string }[]>([]);
  readonly isLoadingModels = signal(false);
  readonly showApiKey = signal(false);
  readonly connectionStatus = signal<'success' | 'error' | null>(null);
  readonly connectionMessage = signal('');
  readonly isDirty = computed(() => JSON.stringify(this.aiConfig()) !== this.originalAiConfig);
  readonly isConfigValid = computed(() => {
    const config = this.aiConfig();
    if (config.connectionMode !== 'Direct') return true;
    return !!config.directEndpoint?.trim() && !!config.directModel?.trim();
  });

  ngOnInit(): void {
    this.loadConfig();
  }

  loadConfig(): void {
    this.isLoading.set(true);
    this.loadError.set('');
    this.aiService.getGlobalConfig().subscribe({
      next: (config) => {
        this.aiConfig.set(config);
        this.originalAiConfig = JSON.stringify(config);
        this.isLoading.set(false);
        if (config.connectionMode === 'Direct' && config.directEndpoint) {
          this.fetchModels();
        }
      },
      error: (err) => {
        this.loadError.set(err.error?.message || 'Check the server connection and try again.');
        this.isLoading.set(false);
      }
    });
  }

  onConnectionModeChange(mode: AiConnectionMode): void {
    this.aiConfig.update(c => ({ ...c, connectionMode: mode }));
    if (mode === 'Direct') {
      const endpoint = this.aiConfig().directEndpoint;
      if (endpoint) {
        this.fetchModels();
      }
    }
  }

  updateField(field: keyof AiConfig, value: string): void {
    this.aiConfig.update(c => ({ ...c, [field]: value }));
    if (field === 'directEndpoint' || field === 'directApiKey') {
      this.connectionStatus.set(null);
      this.availableModels.set([]);
    }
  }

  toggleApiKeyVisibility(): void {
    this.showApiKey.update(value => !value);
  }

  fetchModels(): void {
    const config = this.aiConfig();
    if (!config.directEndpoint) {
      return;
    }
    this.isLoadingModels.set(true);
    this.aiService.getAvailableModels(config.directEndpoint, config.directApiKey ?? undefined).subscribe({
      next: (models) => {
        this.availableModels.set(models);
        this.connectionStatus.set('success');
        this.connectionMessage.set(models.length > 0 ? `Connected · ${models.length} models available` : 'Connected · enter a model name manually');
        this.isLoadingModels.set(false);
      },
      error: () => {
        this.availableModels.set([]);
        this.connectionStatus.set('error');
        this.connectionMessage.set('Connection failed. Check the endpoint and credentials.');
        this.isLoadingModels.set(false);
      }
    });
  }

  save(): void {
    if (!this.isConfigValid()) return;
    this.toastService.clear();
    this.isSaving.set(true);
    this.aiService.updateGlobalConfig(this.aiConfig()).subscribe({
      next: (config) => {
        this.aiConfig.set(config);
        this.originalAiConfig = JSON.stringify(config);
        this.toastService.success('AI provider saved.');
        this.isSaving.set(false);
      },
      error: (err) => {
        this.toastService.error(err.error?.message || 'Failed to save AI provider.');
        this.isSaving.set(false);
      }
    });
  }

  discardChanges(): void {
    this.aiConfig.set(JSON.parse(this.originalAiConfig));
    this.connectionStatus.set(null);
  }

  hasUnsavedChanges(): boolean {
    return this.isDirty();
  }
}
