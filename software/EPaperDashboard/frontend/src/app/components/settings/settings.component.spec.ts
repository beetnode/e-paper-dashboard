import { TestBed } from '@angular/core/testing';
import { Component, signal } from '@angular/core';
import { SettingsComponent } from './settings.component';
import { AuthService } from '../../services/auth.service';
import { AiConfigComponent } from '../ai-config/ai-config.component';
import { RenderingSettingsComponent } from '../rendering-settings/rendering-settings.component';
import { SystemSettingsComponent } from '../system-settings/system-settings.component';
import { provideRouter } from '@angular/router';

@Component({ selector: 'app-ai-config', template: '' })
class AiConfigStub {
  readonly dirty = signal(false);
  readonly valid = signal(true);
  readonly saved = signal(0);
  readonly discarded = signal(0);
  hasUnsavedChanges(): boolean { return this.dirty(); }
  isSaving(): boolean { return false; }
  canSave(): boolean { return this.valid(); }
  save(): void { this.saved.update(count => count + 1); }
  discardChanges(): void { this.discarded.update(count => count + 1); }
}
@Component({ selector: 'app-rendering-settings', template: '' })
class RenderingSettingsStub {}
@Component({ selector: 'app-system-settings', template: '' })
class SystemSettingsStub {}

describe('SettingsComponent', () => {
  function render(user: { isSuperUser: boolean } | null, userManagementDisabled = false) {
    TestBed.configureTestingModule({ imports: [SettingsComponent], providers: [
      provideRouter([]),
      { provide: AuthService, useValue: { currentUser: signal(user), isUserManagementDisabled: () => userManagementDisabled } }
    ] });
    TestBed.overrideComponent(SettingsComponent, {
      remove: { imports: [AiConfigComponent, RenderingSettingsComponent, SystemSettingsComponent] },
      add: { imports: [AiConfigStub, RenderingSettingsStub, SystemSettingsStub] }
    });
    const fixture = TestBed.createComponent(SettingsComponent);
    fixture.detectChanges();
    return fixture;
  }

  function sections(user: { isSuperUser: boolean } | null, userManagementDisabled = false): string[] {
    const element = render(user, userManagementDisabled).nativeElement as HTMLElement;
    return Array.from(element.querySelectorAll('.settings-sections > *')).map(section => section.id);
  }

  it('shows only personal sections to regular users', () => {
    expect(sections({ isSuperUser: false })).toEqual(['ai', 'rendering']);
  });

  it('adds system and users sections for super users', () => {
    expect(sections({ isSuperUser: true })).toEqual(['ai', 'rendering', 'system', 'users']);
  });

  it('links users to their own page', () => {
    const element = render({ isSuperUser: true }).nativeElement as HTMLElement;
    expect(element.querySelector('#users a')?.getAttribute('href')).toBe('/users/manage');
  });

  it('hides the users section when user management is disabled', () => {
    expect(sections({ isSuperUser: true }, true)).toEqual(['ai', 'rendering', 'system']);
  });

  function aiConfig(fixture: ReturnType<typeof render>): AiConfigStub {
    return fixture.debugElement.query(element => element.componentInstance instanceof AiConfigStub).componentInstance;
  }

  function button(fixture: ReturnType<typeof render>, label: string): HTMLButtonElement | null {
    return (fixture.nativeElement as HTMLElement).querySelector(`.settings-header button[aria-label="${label}"]`);
  }

  it('reports unsaved section changes so leaving the page asks first', () => {
    const fixture = render({ isSuperUser: false });
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(false);
    aiConfig(fixture).dirty.set(true);
    expect(fixture.componentInstance.hasUnsavedChanges()).toBe(true);
  });

  it('shows save and discard only while a section has unsaved changes', () => {
    const fixture = render({ isSuperUser: false });
    expect(button(fixture, 'Save')).toBeNull();
    aiConfig(fixture).dirty.set(true);
    fixture.detectChanges();
    expect(button(fixture, 'Save')).not.toBeNull();
    expect(button(fixture, 'Discard unsaved changes')).not.toBeNull();
  });

  it('saves and discards the sections with unsaved changes', () => {
    const fixture = render({ isSuperUser: false });
    const section = aiConfig(fixture);
    section.dirty.set(true);
    fixture.detectChanges();
    button(fixture, 'Save')!.click();
    expect(section.saved()).toBe(1);
    button(fixture, 'Discard unsaved changes')!.click();
    expect(section.discarded()).toBe(1);
  });

  it('blocks saving while a section is invalid', () => {
    const fixture = render({ isSuperUser: false });
    const section = aiConfig(fixture);
    section.dirty.set(true);
    section.valid.set(false);
    fixture.detectChanges();
    expect(button(fixture, 'Save')!.disabled).toBe(true);
    fixture.componentInstance.save();
    expect(section.saved()).toBe(0);
  });
});
