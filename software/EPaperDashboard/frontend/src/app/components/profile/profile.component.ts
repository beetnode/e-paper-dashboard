import { Component, computed, signal, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { AuthService } from '../../services/auth.service';
import { DialogService } from '../../services/dialog.service';
import { ToastService } from '../../services/toast.service';
import { HttpClient } from '@angular/common/http';

interface ChangeNicknameRequest {
  newNickname: string;
}

interface ChangePasswordRequest {
  currentPassword: string;
  newPassword: string;
  confirmNewPassword: string;
}

@Component({
  selector: 'app-profile',
  templateUrl: './profile.component.html',
  standalone: true,
  imports: [CommonModule, FormsModule]
})
export class ProfileComponent {
  private readonly http = inject(HttpClient);
  private readonly authService = inject(AuthService);
  private readonly router = inject(Router);
  private readonly dialogService = inject(DialogService);
  private readonly toastService = inject(ToastService);

  readonly newNickname = signal('');
  readonly currentPassword = signal('');
  readonly newPassword = signal('');
  readonly confirmNewPassword = signal('');
  readonly showPasswords = signal(false);
  readonly isChangingNickname = signal(false);
  readonly isChangingPassword = signal(false);
  readonly isDeletingProfile = signal(false);
  readonly isUserManagementDisabled = this.authService.isUserManagementDisabled;
  readonly mustChangePassword = this.authService.mustChangePassword;
  readonly canChangePassword = computed(() =>
    !!this.currentPassword() &&
    this.newPassword().length >= 6 &&
    this.newPassword() === this.confirmNewPassword()
  );

  get currentUser() {
    return this.authService.currentUser();
  }

  changeNickname(): void {
    this.toastService.clear();
    this.isChangingNickname.set(true);

    const request: ChangeNicknameRequest = {
      newNickname: this.newNickname()
    };

    this.http.post('/api/users/change-nickname', request).subscribe({
      next: () => {
        const message = this.newNickname().trim()
          ? 'Display name saved.'
          : 'Display name cleared.';
        this.toastService.success(message);
        this.newNickname.set('');
        this.isChangingNickname.set(false);
        // Refresh user data
        this.authService.getCurrentUser().subscribe();
      },
      error: (err) => {
        this.toastService.httpError(err, 'Could not save the display name.');
        this.isChangingNickname.set(false);
      }
    });
  }

  changePassword(): void {
    this.toastService.clear();

    if (!this.currentPassword() || !this.newPassword() || !this.confirmNewPassword()) {
      this.toastService.error('Fill in all password fields.');
      return;
    }

    if (this.newPassword() !== this.confirmNewPassword()) {
      this.toastService.error('The new passwords do not match.');
      return;
    }

    this.isChangingPassword.set(true);

    const request: ChangePasswordRequest = {
      currentPassword: this.currentPassword(),
      newPassword: this.newPassword(),
      confirmNewPassword: this.confirmNewPassword()
    };

    this.http.post('/api/users/change-password', request).subscribe({
      next: () => {
        const wasRequired = this.mustChangePassword();
        this.toastService.success('Password changed. Your other sessions were signed out.');
        this.clearPasswordFields();
        this.isChangingPassword.set(false);
        this.authService.getCurrentUser().subscribe(() => {
          if (wasRequired) {
            this.router.navigate(['/dashboards']);
          }
        });
      },
      error: (err) => {
        this.toastService.httpError(err, 'Could not change the password.');
        this.isChangingPassword.set(false);
      }
    });
  }

  async deleteProfile(): Promise<void> {
    await this.dialogService.confirm({
      title: 'Delete profile?',
      message: 'Are you sure you want to delete your profile? This action cannot be undone and you will be logged out.',
      confirmLabel: 'Delete profile',
      isDangerous: true,
      onConfirm: async () => {
        this.isDeletingProfile.set(true);
        try {
          await firstValueFrom(this.http.delete('/api/users/delete-profile'));
          this.toastService.success('Profile deleted.');
          await firstValueFrom(this.authService.logout());
          this.router.navigate(['/home']);
        } catch (err: any) {
          this.toastService.httpError(err, 'Could not delete the profile.');
          this.isDeletingProfile.set(false);
        }
      }
    });
  }

  private clearPasswordFields(): void {
    this.currentPassword.set('');
    this.newPassword.set('');
    this.confirmNewPassword.set('');
  }
}
