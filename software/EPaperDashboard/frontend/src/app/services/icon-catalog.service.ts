import { inject, Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable, catchError, of, shareReplay } from 'rxjs';

/**
 * Names of the Font Awesome solid icons the server renderer can draw. The list is generated at
 * build time next to the renderer's icon paths, so the picker never offers an unsupported icon.
 */
@Injectable({ providedIn: 'root' })
export class IconCatalogService {
  private readonly http = inject(HttpClient);
  private names$?: Observable<string[]>;

  names(): Observable<string[]> {
    this.names$ ??= this.http.get<string[]>('fa-icon-names.json').pipe(
      catchError(() => of([])),
      shareReplay(1),
    );
    return this.names$;
  }
}
