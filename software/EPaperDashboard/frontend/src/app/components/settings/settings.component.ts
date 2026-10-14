import { Component, computed, inject, viewChild } from '@angular/core';
import { AuthService } from '../../services/auth.service';
import { HasUnsavedChanges } from '../../guards/unsaved-changes.guard';
import { EditableSettingsSection } from './editable-settings-section';
import { AiConfigComponent } from '../ai-config/ai-config.component';
import { RenderingSettingsComponent } from '../rendering-settings/rendering-settings.component';
import { SystemSettingsComponent } from '../system-settings/system-settings.component';
import { RouterLink } from '@angular/router';

/** All settings on one page, one section after another. */
@Component({
  selector: 'app-settings',
  standalone: true,
  imports: [RouterLink, AiConfigComponent, RenderingSettingsComponent, SystemSettingsComponent],
  template: `
    <div class="app-page">
      <div class="app-page-header settings-header">
        <div>
          <h1 class="app-page-title">Settings</h1>
        </div>
        <div class="app-page-actions">
          <span class="visually-hidden" aria-live="polite">{{ isDirty() ? 'Unsaved changes' : '' }}</span>
          @if (isDirty() || isSaving()) {
            <button type="button" class="btn btn-primary" (click)="save()" [disabled]="isSaving() || !canSave()" aria-label="Save">
              @if (isSaving()) {
                <span class="spinner-border spinner-border-sm" role="status" aria-hidden="true"></span>
              } @else {
                <i class="fa-solid fa-floppy-disk" aria-hidden="true"></i>
              }
              <span class="d-none d-sm-inline">Save</span>
            </button>
            <button type="button" class="btn btn-outline-secondary" (click)="discardChanges()" [disabled]="isSaving()" title="Discard unsaved changes" aria-label="Discard unsaved changes">
              <i class="fa-solid fa-rotate-left" aria-hidden="true"></i><span class="d-none d-sm-inline">Discard</span>
            </button>
          }
        </div>
      </div>
      <div class="settings-sections">
        <app-ai-config #aiConfig id="ai" />
        <app-rendering-settings id="rendering" />
        @if (showSystem()) {
          <app-system-settings id="system" />
        }
        @if (showUsers()) {
          <section id="users" class="app-section-card" aria-labelledby="usersTitle">
            <div class="app-section-header mb-0">
              <div>
                <h2 id="usersTitle" class="app-section-title">Users</h2>
                <p class="app-section-description">Manage who can access and configure izBoard.</p>
              </div>
              <a class="btn btn-outline-primary" routerLink="/users/manage">
                <i class="fa-solid fa-users" aria-hidden="true"></i><span class="d-none d-sm-inline"> Manage users</span><span class="visually-hidden d-sm-none">Manage users</span>
              </a>
            </div>
          </section>
        }
      </div>
    </div>
  `,
  styles: `
    /* Keeps Save and Discard in reach while scrolling through the sections. */
    .settings-header {
      position: sticky;
      top: 0;
      z-index: 10;
      align-items: center;
      margin-inline: -0.5rem;
      padding: 0.5rem;
      background: var(--bs-body-bg);
    }

    .settings-sections {
      display: flex;
      flex-direction: column;
      gap: 2.5rem;
    }

    /* Each section was a full page on its own; inside Settings it is one block of the page. */
    .settings-sections ::ng-deep > * > .app-page {
      max-width: none;
      padding-block: 0;
    }

    .settings-sections ::ng-deep > * > .app-page > .app-page-header {
      margin-bottom: 1rem;
    }

    .settings-sections ::ng-deep > * > .app-page > .app-page-header .app-page-title {
      font-size: clamp(1.2rem, 2.2vw, 1.4rem);
    }
  `
})
export class SettingsComponent implements HasUnsavedChanges {
  private readonly authService = inject(AuthService);
  private readonly aiConfig = viewChild<EditableSettingsSection>('aiConfig');
  /** Sections holding draft edits; Rendering and System apply their actions immediately. */
  private readonly editableSections = computed(() => [this.aiConfig()].filter(section => !!section));
  private readonly dirtySections = computed(() => this.editableSections().filter(section => section.hasUnsavedChanges()));

  protected readonly isDirty = computed(() => this.dirtySections().length > 0);
  protected readonly isSaving = computed(() => this.editableSections().some(section => section.isSaving()));
  protected readonly canSave = computed(() => this.dirtySections().every(section => section.canSave()));

  protected readonly showSystem = computed(() => this.authService.currentUser()?.isSuperUser === true);
  protected readonly showUsers = computed(() => this.showSystem() && !this.authService.isUserManagementDisabled());

  hasUnsavedChanges(): boolean {
    return this.isDirty();
  }

  save(): void {
    if (!this.canSave()) return;
    this.dirtySections().forEach(section => section.save());
  }

  discardChanges(): void {
    this.dirtySections().forEach(section => section.discardChanges());
  }
}
