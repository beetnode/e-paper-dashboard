import { Component, OnInit, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { ReactiveFormsModule, FormBuilder, FormGroup, Validators } from '@angular/forms';
import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { ToastService } from '../../services/toast.service';
import { DialogService } from '../../services/dialog.service';

@Component({
  selector: 'app-users-management',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './users-management.component.html',
})
export class UsersManagementComponent implements OnInit {
  // Signals
  readonly users = signal<any[]>([]);
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
      password: ['', [Validators.required, Validators.minLength(6)]]
    });
  }

  ngOnInit(): void {
    this.loadUsers();
  }

  loadUsers(): void {
    this.loading.set(true);
    this.loadError.set('');
    this.http.get<any[]>('/api/users/all').subscribe({
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
          response.message || `User "${formValue.username}" created successfully`
        );
        this.addUserForm.reset();
        this.showAddForm.set(false);
        this.isAddingUser.set(false);
        this.loadUsers();
      },
      error: (error: HttpErrorResponse) => {
        this.handleError(error, `Failed to create user "${formValue.username}"`);
        this.isAddingUser.set(false);
      }
    });
  }

  deleteUser(userId: string, username: string): void {
    this.dialogService.confirm({
      title: 'Delete user',
      message: `Are you sure you want to delete user "${username}"? This action cannot be undone and all their dashboards will be deleted.`,
      confirmLabel: 'Delete user',
      cancelLabel: 'Keep user',
      isDangerous: true,
      onConfirm: () => this.performDelete(userId, username),
      onCancel: () => {
        this.toastService.info('User deletion cancelled');
      }
    });
  }

  private performDelete(userId: string, username: string): Promise<void> {
    const deletingState = this.isDeletingUser();
    this.isDeletingUser.set({ ...deletingState, [userId]: true });

    return new Promise((resolve) => {
      this.http.delete(`/api/users/${userId}`).subscribe({
        next: (response: any) => {
          this.toastService.success(
            response.message || `User "${username}" deleted successfully`
          );
          const state = this.isDeletingUser();
          this.isDeletingUser.set({ ...state, [userId]: false });
          this.loadUsers();
          resolve();
        },
        error: (error: HttpErrorResponse) => {
          this.handleError(error, `Failed to delete user "${username}"`);
          const state = this.isDeletingUser();
          this.isDeletingUser.set({ ...state, [userId]: false });
          resolve();
        }
      });
    });
  }

  getFilteredUsers(): any[] {
    return this.users();
  }

  toggleAddForm(): void {
    this.showAddForm.update(v => !v);

    if (!this.showAddForm()) {
      this.addUserForm.reset();
    }
  }

  showError(controlName: string, errorName?: string): boolean {
    const control = this.addUserForm.get(controlName);
    if (!control || !(control.touched || control.dirty) || !control.invalid) return false;
    return errorName ? control.hasError(errorName) : true;
  }

  private handleError(error: HttpErrorResponse, defaultMessage: string): void {
    let message = defaultMessage;
    let details = '';

    if (error.error?.message) {
      message = error.error.message;
    } else if (error.status === 401) {
      message = 'Unauthorized';
      details = 'You are not authorized to perform this action';
    } else if (error.status === 403) {
      message = 'Access Denied';
      details = 'You do not have permission to perform this action';
    } else if (error.status === 400) {
      message = 'Invalid Request';
      details = error.error?.message || 'Please check your input and try again';
    } else if (error.status === 409) {
      message = 'User Already Exists';
      details = error.error?.message || 'A user with this name already exists';
    } else if (error.status === 0) {
      message = 'Connection Error';
      details = 'Unable to reach the server. Please check your network connection.';
    } else if (error.status >= 500) {
      message = 'Server Error';
      details = 'An unexpected error occurred. Please try again later.';
    }

    this.toastService.error(details ? `${message}: ${details}` : message);
  }

  private getErrorMessage(error: HttpErrorResponse, fallback: string): string {
    if (error.error?.message) return error.error.message;
    if (error.status === 401) return 'You are not authorized to view users.';
    if (error.status === 403) return 'You do not have permission to view users.';
    if (error.status === 0) return 'Unable to reach the server.';
    return fallback;
  }
}
