import { afterEach, describe, expect, it, vi } from 'vitest';
import { describeHttpError, ToastService } from './toast.service';

describe('ToastService', () => {
  afterEach(() => vi.useRealTimers());

  it('pauses and resumes automatic dismissal', () => {
    vi.useFakeTimers();
    const service = new ToastService();
    const id = service.info('Saved', 1000);

    vi.advanceTimersByTime(400);
    service.pause(id);
    vi.advanceTimersByTime(2000);
    expect(service.toasts()).toHaveLength(1);

    service.resume(id);
    vi.advanceTimersByTime(599);
    expect(service.toasts()).toHaveLength(1);
    vi.advanceTimersByTime(1);
    expect(service.toasts()).toHaveLength(0);
  });

  it('keeps errors on screen longer than other toasts', () => {
    vi.useFakeTimers();
    const service = new ToastService();
    service.error('Could not save the dashboard.');
    vi.advanceTimersByTime(5000);
    expect(service.toasts()).toHaveLength(1);
    vi.advanceTimersByTime(3000);
    expect(service.toasts()).toHaveLength(0);
  });
});

describe('describeHttpError', () => {
  const fallback = 'Could not save the dashboard.';

  it('prefers the server message, then its detail', () => {
    expect(describeHttpError({ status: 400, error: { message: 'Name is taken.' } }, fallback)).toBe('Name is taken.');
    expect(describeHttpError({ status: 400, error: { detail: 'Code expired.' } }, fallback)).toBe('Code expired.');
    expect(describeHttpError({ status: 400, error: 'Plain text reason' }, fallback)).toBe('Plain text reason');
  });

  it('explains an unreachable server', () => {
    expect(describeHttpError({ status: 0, error: new ProgressEvent('error') }, fallback)).toBe('Could not reach the server. Check your connection.');
  });

  it('falls back when the server gives no readable reason', () => {
    expect(describeHttpError({ status: 500, error: '<html>Bad gateway</html>' }, fallback)).toBe(fallback);
    expect(describeHttpError({ status: 500, error: null }, fallback)).toBe(fallback);
    expect(describeHttpError(undefined, fallback)).toBe(fallback);
  });
});
