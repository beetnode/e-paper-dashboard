import { Routes } from '@angular/router';
import { authGuard } from './guards/auth.guard';
import { superUserGuard } from './guards/superuser.guard';
import { unsavedChangesGuard } from './guards/unsaved-changes.guard';
import { systemAdminGuard } from './guards/system-admin.guard';

export const routes: Routes = [
  { path: 'connections', canActivate: [authGuard], loadComponent: () => import('./components/provider-connections/provider-connections.component').then(m => m.ProviderConnectionsComponent) },
  { path: '', redirectTo: '/home', pathMatch: 'full' },
  {
    path: 'home',
    loadComponent: () => import('./components/home/home.component').then(m => m.HomeComponent)
  },
  {
    path: 'login',
    loadComponent: () => import('./components/login/login.component').then(m => m.LoginComponent)
  },
  {
    path: 'setup',
    data: { setup: true },
    loadComponent: () => import('./components/register/register.component').then(m => m.RegisterComponent)
  },
  {
    path: 'rendering',
    canActivate: [authGuard],
    loadComponent: () => import('./components/rendering-settings/rendering-settings.component').then(m => m.RenderingSettingsComponent)
  },
  {
    path: 'register',
    loadComponent: () => import('./components/register/register.component').then(m => m.RegisterComponent)
  },
  {
    path: 'dashboards/create',
    canActivate: [authGuard],
    loadComponent: () => import('./components/dashboard-create/dashboard-create.component').then(m => m.DashboardCreateComponent)
  },
  {
    path: 'dashboards/:id/edit',
    canActivate: [authGuard],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./components/dashboard-edit/dashboard-edit.component').then(m => m.DashboardEditComponent)
  },
  {
    path: 'dashboards/:id/designer',
    canActivate: [authGuard],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./components/dashboard-designer/dashboard-designer.component').then(m => m.DashboardDesignerComponent)
  },
  {
    path: 'dashboards',
    canActivate: [authGuard],
    loadComponent: () => import('./components/dashboard-list/dashboard-list.component').then(m => m.DashboardListComponent)
  },
  {
    path: 'devices',
    canActivate: [authGuard],
    loadComponent: () => import('./components/device-list/device-list.component').then(m => m.DeviceListComponent)
  },
  {
    path: 'users/profile',
    canActivate: [authGuard],
    loadComponent: () => import('./components/profile/profile.component').then(m => m.ProfileComponent)
  },
  {
    path: 'ai/config',
    canActivate: [authGuard],
    canDeactivate: [unsavedChangesGuard],
    loadComponent: () => import('./components/ai-config/ai-config.component').then(m => m.AiConfigComponent)
  },
  {
    path: 'users/manage',
    canActivate: [authGuard, superUserGuard],
    loadComponent: () => import('./components/users-management/users-management.component').then(m => m.UsersManagementComponent)
  },
  {
    path: 'system',
    canActivate: [authGuard, systemAdminGuard],
    loadComponent: () => import('./components/system-settings/system-settings.component').then(m => m.SystemSettingsComponent)
  },
  {
    path: 'privacy',
    loadComponent: () => import('./components/privacy/privacy.component').then(m => m.PrivacyComponent)
  },
  { path: '**', redirectTo: '/home' }
];
