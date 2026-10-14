import { HasUnsavedChanges } from '../../guards/unsaved-changes.guard';

/** A settings section that keeps draft edits until the page-level Save or Discard. */
export interface EditableSettingsSection extends HasUnsavedChanges {
  isSaving(): boolean;
  canSave(): boolean;
  save(): void;
  discardChanges(): void;
}
