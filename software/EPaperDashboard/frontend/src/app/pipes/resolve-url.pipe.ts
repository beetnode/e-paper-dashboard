import { Pipe, PipeTransform, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';

/**
 * Resolves an absolute API URL against the document's `<base href>`; see {@link ResolveUrlPipe}.
 * Use it for URLs the browser loads directly (e.g. `new Image()`), which skip the HTTP interceptor.
 */
export function resolveApiUrl(document: Document, url: string | undefined | null): string {
  if (!url || !url.startsWith('/api/')) {
    return url ?? '';
  }

  const baseElement = document.querySelector('base');
  const baseHref = baseElement?.getAttribute('href') || '/';

  if (baseHref === '/') {
    return url;
  }

  // baseHref ends with '/', url starts with '/' → drop the leading slash
  return baseHref + url.substring(1);
}

/**
 * Resolves an absolute API URL (e.g. `/api/dashboards/…/images/…`) against the
 * document's `<base href>`.
 *
 * In normal mode (`<base href="/">`), the URL is returned unchanged.
 * In Home Assistant add-on / ingress mode the base href is something like
 * `/api/hassio_ingress/TOKEN/`, so the pipe prepends that prefix so the
 * browser's request goes through the HA ingress proxy.
 *
 * Usage:  `<img [src]="someUrl | resolveUrl" />`
 */
@Pipe({
  name: 'resolveUrl',
  standalone: true,
  pure: true,
})
export class ResolveUrlPipe implements PipeTransform {
  private readonly document = inject(DOCUMENT);

  transform(url: string | undefined | null): string {
    return resolveApiUrl(this.document, url);
  }
}
