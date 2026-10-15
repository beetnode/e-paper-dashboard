import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Observable } from 'rxjs';
import { ToastService } from '../../services/toast.service';
import { DialogService } from '../../services/dialog.service';
import { AuthService } from '../../services/auth.service';
import { ClipboardService } from '../../services/clipboard.service';

export interface ManagedUser {
  id: string;
  username: string;
  nickname?: string;
  isSuperUser: boolean;
  isSuspended: boolean;
  mustChangePassword: boolean;
  renderingAllowed: boolean;
  createdAt?: string;
}

interface TemporaryPassword {
  username: string;
  password: string;
}

@Component({
  selector: 'app-users-management',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './users-management.component.html',
})
export class UsersManagementComponent implements OnInit {
  // Signals
  private readonly authService = inject(AuthService);
  private readonly clipboardService = inject(ClipboardService);

  readonly users = signal<ManagedUser[]>([]);
  readonly busyUsers = signal<Record<string, boolean>>({});
  readonly temporaryPassword = signal<TemporaryPassword | null>(null);
  readonly loading = signal(false);
  readonly loadError = signal('');
  readonly showAddForm = signal(false);
  readonly isAddingUser = signal(false);
  readonly isDeletingUser = signal<{ [key: string]: boolean }>({});

  addUserForm: FormGroup;

  constructor(
    private http: HttpClient,
    private toastService: ToastService,
    private dialogService: DialogService,
    private fb: FormBuilder
  ) {
    this.addUserForm = this.fb.group({
      username: ['', [Validators.required, Validators.minLength(3)]],
      password: ['', [Validators.required, Validators.minLength(6)]],
      mustChangePassword: [true]
    });
  }

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.http.get<ManagedUser[]>('/api/users/all').subscribe({
      next: (data) => {
        this.users.set(data || []);
        this.loading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.loadError.set(this.getErrorMessage(error, 'Check the server connection and try again.'));
        this.loading.set(false);
      }
    });
  }

  addUser(): void {
    if (this.addUserForm.invalid) {
      this.addUserForm.markAllAsTouched();
      return;
    }

    const formValue = {
      ...this.addUserForm.value,
      username: this.addUserForm.value.username.trim()
    };
    if (formValue.username.length < 3) {
      this.addUserForm.get('username')?.setErrors({ minlength: true });
      this.addUserForm.get('username')?.markAsTouched();
      return;
    }
    this.isAddingUser.set(true);
    this.http.post('/api/users/add', formValue).subscribe({
      next: (response: any) => {
        this.toastService.success(
          response.message || `User ${formValue.username} added.`
        );
        this.addUserForm.reset({ mustChangePassword: true });
        this.showAddForm.set(false);
        this.isAddingUser.set(false);
        this.loadUsers();
      },
      error: (error: HttpErrorResponse) => {
        this.toastService.httpError(error, `Could not add user ${formValue.username}.`);
        this.isAddingUser.set(false);
      }
    });
  }

  deleteUser(userId: string, username: string): void {
    this.dialogService.confirm({
      title: 'Delete user',
      message: `Are you sure you want to delete user "${username}"? This action cannot be undone. Their dashboards, connections and paired devices will be deleted.`,
      confirmLabel: 'Delete user',
      cancelLabel: 'Keep user',
      isDangerous: true,
      onConfirm: () => this.performDelete(userId, username),
    });
  }

  private performDelete(userId: string, username: string): Promise<void> {
    const deletingState = this.isDeletingUser();
    this.isDeletingUser.set({ ...deletingState, [userId]: true });

    return new Promise((resolve) => {
      this.http.delete(`/api/users/${userId}`).subscribe({
        next: (response: any) => {
          this.toastService.success(
            response.message || `User ${username} deleted.`
          );
          const state = this.isDeletingUser();
          this.isDeletingUser.set({ ...state, [userId]: false });
          this.loadUsers();
          resolve();
        },
        error: (error: HttpErrorResponse) => {
          this.toastService.httpError(error, `Could not delete user ${username}.`);
          const state = this.isDeletingUser();
          this.isDeletingUser.set({ ...state, [userId]: false });
          resolve();
        }
      });
    });
  }

  getFilteredUsers(): ManagedUser[] {
    return this.users();
  }

  isCurrentUser(user: ManagedUser): boolean {
    return this.authService.currentUser()?.id === user.id;
  }

  isBusy(user: ManagedUser): boolean {
    return !!this.busyUsers()[user.id] || !!this.isDeletingUser()[user.id];
  }

  setRole(user: ManagedUser, isSuperUser: boolean): void {
    if (isSuperUser) {
      this.dialogService.confirm({
        title: 'Make administrator',
        message: `"${user.username}" will be able to manage users and system components.`,
        confirmLabel: 'Make administrator',
        cancelLabel: 'Cancel',
        onConfirm: () => this.runAction(user, this.http.put(`/api/users/${user.id}/role`, { isSuperUser }), 'Could not change the role.'),
      });
      return;
    }
    this.runAction(user, this.http.put(`/api/users/${user.id}/role`, { isSuperUser }), 'Could not change the role.');
  }

  setSuspended(user: ManagedUser, suspended: boolean): void {
    if (suspended) {
      this.dialogService.confirm({
        title: 'Suspend user',
        message: `"${user.username}" will be signed out and their devices will stop updating until you reinstate the account.`,
        confirmLabel: 'Suspend user',
        cancelLabel: 'Cancel',
        isDangerous: true,
        onConfirm: () => this.runAction(user, this.http.put(`/api/users/${user.id}/suspension`, { suspended }), 'Could not suspend the user.'),
      });
      return;
    }
    this.runAction(user, this.http.put(`/api/users/${user.id}/suspension`, { suspended }), 'Could not reinstate the user.');
  }

  setRenderingAllowed(user: ManagedUser, allowed: boolean): void {
    this.runAction(user, this.http.put(`/api/users/${user.id}/rendering`, { allowed }),
      allowed ? 'Could not allow rendering.' : 'Could not turn off rendering.');
  }

  resetPassword(user: ManagedUser): void {
    this.dialogService.confirm({
      title: 'Reset password',
      message: `"${user.username}" will be signed out and must choose a new password after signing in with a temporary one.`,
      confirmLabel: 'Reset password',
      cancelLabel: 'Cancel',
      isDangerous: true,
      onConfirm: () => this.runAction(
        user,
        this.http.post<{ temporaryPassword: string }>(`/api/users/${user.id}/reset-password`, {}),
        'Could not reset the password.',
        response => this.temporaryPassword.set({ username: user.username, password: response.temporaryPassword })),
    });
  }

  async copyTemporaryPassword(): Promise<void> {
    const temporary = this.temporaryPassword();
    if (temporary && await this.clipboardService.copy(temporary.password)) {
      this.toastService.success('Temporary password copied.');
    }
  }

  dismissTemporaryPassword(): void {
    this.temporaryPassword.set(null);
  }

  private runAction<T>(user: ManagedUser, request: Observable<T>, failure: string, onSuccess?: (response: T) => void): Promise<void> {
    this.busyUsers.update(state => ({ ...state, [user.id]: true }));
    return new Promise(resolve => {
      request.subscribe({
        next: (response: any) => {
          onSuccess?.(response);
          if (!onSuccess && response?.message) {
            this.toastService.success(response.message);
          }
          this.busyUsers.update(state => ({ ...state, [user.id]: false }));
          this.loadUsers();
          resolve();
        },
        error: (error: HttpErrorResponse) => {
          this.toastService.httpError(error, failure);
          this.busyUsers.update(state => ({ ...state, [user.id]: false }));
          resolve();
        }
      });
    });
  }

  toggleAddForm(): void {
    this.showAddForm.update(v => !v);

    if (!this.showAddForm()) {
      this.addUserForm.reset({ mustChangePassword: true });
    }
  }

  showError(controlName: string, errorName?: string): boolean {
    const control = this.addUserForm.get(controlName);
    if (!control || !(control.touched || control.dirty) || !control.invalid) return false;
    return errorName ? control.hasError(errorName) : true;
  }

  private getErrorMessage(error: HttpErrorResponse, fallback: string): string {
    if (error.error?.message) return error.error.message;
    if (error.status === 401) return 'You are not authorized to view users.';
    if (error.status === 403) return 'You do not have permission to view users.';
    if (error.status === 0) return 'Unable to reach the server.';
    return fallback;
  }
}
