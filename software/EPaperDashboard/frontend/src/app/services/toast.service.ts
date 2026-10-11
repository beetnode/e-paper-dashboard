import { Injectable, signal } from '@angular/core';

export interface Toast {
  id: string;
  message: string;
  type: 'success' | 'error' | 'warning' | 'info';
  duration?: number;
  actionLabel?: string;
  action?: () => void;
}

/**
 * Toast wording, used across the app:
 * - Full sentences in sentence case, ending with a period.
 * - Success names the thing and what happened: "Dashboard saved.", "Device removed."
 * - Failure says what could not be done: "Could not save the dashboard."
 *   A message from the server replaces that fallback.
 * - No toast for what the page already shows (inline errors, preview dialogs) or for cancelled actions.
 */
export function describeHttpError(error: unknown, fallback: string): string {
  const response = error as { status?: number; error?: unknown } | null;
  const body = response?.error as { message?: unknown; detail?: unknown } | string | null | undefined;
  if (typeof body === 'string' && body.trim() && !body.trimStart().startsWith('<')) return body.trim();
  if (body && typeof body === 'object') {
    if (typeof body.message === 'string' && body.message.trim()) return body.message.trim();
    if (typeof body.detail === 'string' && body.detail.trim()) return body.detail.trim();
  }
  if (response?.status === 0) return 'Could not reach the server. Check your connection.';
  return fallback;
}

/** Errors stay longer than other toasts, so there is time to read them. */
const ERROR_DURATION = 8000;

@Injectable({
  providedIn: 'root'
})
export class ToastService {
  readonly toasts = signal<Toast[]>([]);
  private toastIdCounter = 0;
  private readonly timers = new Map<string, ReturnType<typeof setTimeout>>();
  private readonly deadlines = new Map<string, number>();
  private readonly remainingDurations = new Map<string, number>();

  show(message: string, type: 'success' | 'error' | 'warning' | 'info' = 'info', duration = 5000, actionLabel?: string, action?: () => void): string {
    const id = `toast-${this.toastIdCounter++}`;
    const toast: Toast = { id, message, type, duration, actionLabel, action };

    this.toasts.set([toast]);

    if (duration > 0) {
      this.scheduleRemoval(id, duration);
    }

    return id;
  }

  showWithAction(message: string, actionLabel: string, action: () => void, type: 'success' | 'error' | 'warning' | 'info' = 'info', duration = 5000): string {
    return this.show(message, type, duration, actionLabel, action);
  }

  remove(id: string): void {
    const timer = this.timers.get(id);
    if (timer) clearTimeout(timer);
    this.timers.delete(id);
    this.deadlines.delete(id);
    this.remainingDurations.delete(id);
    const current = this.toasts();
    this.toasts.set(current.filter(t => t.id !== id));
  }

  clear(): void {
    for (const timer of this.timers.values()) clearTimeout(timer);
    this.timers.clear();
    this.deadlines.clear();
    this.remainingDurations.clear();
    this.toasts.set([]);
  }

  pause(id: string): void {
    const timer = this.timers.get(id);
    const deadline = this.deadlines.get(id);
    if (!timer || deadline == null) return;
    clearTimeout(timer);
    this.timers.delete(id);
    this.remainingDurations.set(id, Math.max(deadline - Date.now(), 250));
  }

  resume(id: string): void {
    const remaining = this.remainingDurations.get(id);
    if (remaining == null || !this.toasts().some(toast => toast.id === id)) return;
    this.remainingDurations.delete(id);
    this.scheduleRemoval(id, remaining);
  }

  private scheduleRemoval(id: string, duration: number): void {
    this.deadlines.set(id, Date.now() + duration);
    this.timers.set(id, setTimeout(() => this.remove(id), duration));
  }

  success(message: string, duration = 5000): string {
    return this.show(message, 'success', duration);
  }

  error(message: string, duration = ERROR_DURATION): string {
    return this.show(message, 'error', duration);
  }

  /** Shows the server's explanation of a failed request, or the fallback when it gives none. */
  httpError(error: unknown, fallback: string): string {
    return this.error(describeHttpError(error, fallback));
  }

  warning(message: string, duration = 5000): string {
    return this.show(message, 'warning', duration);
  }

  info(message: string, duration = 5000): string {
    return this.show(message, 'info', duration);
  }
}
