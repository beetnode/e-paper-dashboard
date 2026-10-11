import { Component, OnInit, inject, signal, computed, effect } from '@angular/core';
import { RouterOutlet, RouterModule, Router, NavigationEnd, ActivatedRouteSnapshot } from '@angular/router';
import { toSignal } from '@angular/core/rxjs-interop';
import { filter, map } from 'rxjs';
import { CommonModule } from '@angular/common';
import { AuthService } from './services/auth.service';
import { VersionService } from './services/version.service';
import { ToastContainerComponent } from './components/toast-container/toast-container.component';
import { ConfirmDialogComponent } from './components/confirm-dialog/confirm-dialog.component';

interface SettingsLink {
  path: string;
  label: string;
  icon: string;
}

@Component({
  selector: 'app-root',
  imports: [CommonModule, RouterOutlet, RouterModule, ToastContainerComponent, ConfirmDialogComponent],
  templateUrl: './app.html',
  styleUrl: './app.scss'
})
export class App implements OnInit {
  protected readonly authService = inject(AuthService);
  protected readonly versionService = inject(VersionService);
  private readonly router = inject(Router);
  protected readonly currentYear = new Date().getFullYear();

  private readonly theme = signal<string>(this.getInitialTheme());
  readonly isDark = computed(() => this.theme() === 'dark');

  private readonly navigationEnd = this.router.events.pipe(filter(event => event instanceof NavigationEnd));

  /** Routes flagged with `data: { workspace: true }` use the full window width and height. */
  protected readonly isWorkspace = toSignal(
    this.navigationEnd.pipe(map(() => App.isWorkspaceRoute(this.router.routerState.snapshot.root))),
    { initialValue: false },
  );

  private readonly currentUrl = toSignal(this.navigationEnd.pipe(map(event => event.urlAfterRedirects)), { initialValue: '' });

  /** Setup pages visited rarely, grouped under one Settings menu. */
  protected readonly settingsLinks = computed<SettingsLink[]>(() => {
    const user = this.authService.currentUser();
    const links: SettingsLink[] = [
      { path: '/ai/config', label: 'AI provider', icon: 'fa-wand-magic-sparkles' },
      { path: '/rendering', label: 'Rendering', icon: 'fa-image' },
    ];
    if (user?.isSuperUser) links.push({ path: '/system', label: 'System', icon: 'fa-gears' });
    if (user?.isSuperUser && !this.authService.isUserManagementDisabled()) {
      links.push({ path: '/users/manage', label: 'Users', icon: 'fa-users' });
    }
    return links;
  });

  protected readonly isSettingsRoute = computed(() =>
    this.settingsLinks().some(link => this.currentUrl().startsWith(link.path)));

  constructor() {
    effect(() => {
      const currentTheme = this.theme();
      document.documentElement.setAttribute('data-bs-theme', currentTheme);
      localStorage.setItem('theme', currentTheme);
    });
  }

  ngOnInit(): void {
    this.authService.checkAuth();
    this.authService.getSetupStatus().subscribe({
      next: status => {
        if (status.setupRequired) this.router.navigate(['/setup']);
      }
    });
  }

  logout(): void {
    this.authService.logout().subscribe({
      next: () => {
        this.router.navigate(['/login']);
      }
    });
  }

  setTheme(checked: boolean): void {
    this.theme.set(checked ? 'dark' : 'light');
  }

  closeMobileNavigation(): void {
    const navigation = document.getElementById('navbarNav');
    if (!navigation?.classList.contains('show')) return;

    document.querySelector<HTMLButtonElement>('.navbar-toggler')?.click();
  }

  private static isWorkspaceRoute(route: ActivatedRouteSnapshot): boolean {
    let current: ActivatedRouteSnapshot | null = route;
    while (current?.firstChild) current = current.firstChild;
    return current?.data?.['workspace'] === true;
  }

  private getInitialTheme(): string {
    const stored = localStorage.getItem('theme');
    return stored || (document.documentElement.getAttribute('data-bs-theme') || 'light');
  }
}
