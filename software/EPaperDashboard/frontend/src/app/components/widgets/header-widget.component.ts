import {
  ChangeDetectorRef,
  Component,
  Input,
  OnChanges,
  OnInit,
  SimpleChanges,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import { DomSanitizer, SafeHtml } from '@angular/platform-browser';
import {
  BadgeConfig,
  ColorScheme,
  DashboardLayout,
  EntityState,
  HeaderConfig,
  WidgetConfig,
} from '../../models/types';
import { resolveWidgetRenderContext } from './widget-render-context';

const svgTextCache: { text: string | null; pending: Promise<string | null> | null } = {
  text: null,
  pending: null,
};

interface VisibleBadge {
  badge: BadgeConfig;
  index: number;
}

@Component({
  selector: 'app-widget-header',
  standalone: true,
  imports: [CommonModule],
  styleUrls: ['./header-widget.component.scss'],
  template: `
    <div class="header-widget">
      @if (!isDataFetched()) {
        <div class="preview-state">
          <i class="fa fa-heading"></i>
          <p>{{ cfg.title || 'Header' }}</p>
        </div>
      } @else {
        @if (widget.showTitle !== false) {
          <div class="title-section"
               [style.left.%]="titlePosition.x"
               [style.top.%]="titlePosition.y"
               [style.width.%]="titlePosition.w"
               [style.height.%]="titlePosition.h"
               [style.color]="getTitleColor()">
            @if (isIconOnLeft() && inlineSvg) {
              <div class="header-icon"
                   [innerHTML]="inlineSvg"
                   [style.width.px]="cfg.iconSize ?? 32"
                   [style.height.px]="cfg.iconSize ?? 32"
                   [style.--accent-color]="getIconColor()"></div>
            }
            <div class="title"
                 [style.fontSize.px]="getTitleFontSize()"
                 [style.fontWeight]="getTitleFontWeight()">{{ cfg.title }}</div>
            @if (!isIconOnLeft() && inlineSvg) {
              <div class="header-icon"
                   [innerHTML]="inlineSvg"
                   [style.width.px]="cfg.iconSize ?? 32"
                   [style.height.px]="cfg.iconSize ?? 32"
                   [style.--accent-color]="getIconColor()"></div>
            }
          </div>
        }

        @for (entry of visibleBadgeEntries(); track entry.index) {
          <span class="hw-badge"
                [style.left.%]="badgePosition(entry).x"
                [style.top.%]="badgePosition(entry).y"
                [style.width.%]="badgePosition(entry).w"
                [style.height.%]="badgePosition(entry).h"
                [style.fontSize.px]="getTextFontSize()"
                [style.fontWeight]="getTextFontWeight()"
                [style.color]="getTextColor()">
            @if (entry.badge.icon) {
              <i class="fa {{ entry.badge.icon }}" [style.color]="getIconColor()"></i>
            }
            @if (entry.badge.entityId) {
              <span class="hw-badge-text">
                {{ getEntityState(entry.badge.entityId)?.state || '' }}
                @if (getEntityAttribute(entry.badge.entityId, 'unit_of_measurement')) {
                  {{ getEntityAttribute(entry.badge.entityId, 'unit_of_measurement') }}
                }
              </span>
            }
          </span>
        }
      }
    </div>
  `,
})
export class HeaderWidgetComponent implements OnInit, OnChanges {
  @Input() widget!: WidgetConfig;
  @Input() colorScheme!: ColorScheme;
  @Input() entityStates: Record<string, EntityState> | null = null;
  @Input() designerSettings?: DashboardLayout;

  inlineSvg: SafeHtml | null = null;

  get cfg(): HeaderConfig {
    return this.widget.config as HeaderConfig;
  }

  get titlePosition() {
    return {
      x: this.cfg.titleX ?? ((this.cfg.iconPosition ?? 'left') === 'right' ? 0 : 58),
      y: this.cfg.titleY ?? 0,
      w: this.cfg.titleW ?? 42,
      h: this.cfg.titleH ?? 50,
    };
  }

  constructor(
    private readonly sanitizer: DomSanitizer,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnInit(): void {
    this.loadSvg();
  }

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['colorScheme']) {
      this.applySvgColor();
    } else if (changes['widget']) {
      this.loadSvg();
    }
  }

  badgePosition(entry: VisibleBadge) {
    return {
      x: entry.badge.x ?? (entry.index % 4) * 22,
      y: entry.badge.y ?? Math.floor(entry.index / 4) * 30,
      w: entry.badge.w ?? 22,
      h: entry.badge.h ?? 30,
    };
  }

  visibleBadgeEntries(): VisibleBadge[] {
    return (this.cfg.badges ?? [])
      .map((badge, index) => ({ badge, index }))
      .filter(({ badge }) => !!badge && !!(badge.entityId?.trim() || badge.icon?.trim()));
  }

  isDataFetched(): boolean {
    const entityBadges = this.visibleBadgeEntries()
      .map(entry => entry.badge)
      .filter(badge => badge.entityId?.trim());
    if (entityBadges.length === 0) return true;
    return entityBadges.some(badge => !!this.getEntityState(badge.entityId));
  }

  getEntityState(entityId?: string) {
    if (!entityId || !this.entityStates) return null;
    return this.entityStates[entityId] ?? null;
  }

  getEntityAttribute(entityId?: string, attribute?: string) {
    const state = this.getEntityState(entityId);
    return state?.attributes && attribute ? (state.attributes[attribute] ?? null) : null;
  }

  isIconOnLeft(): boolean {
    return (this.cfg.iconPosition ?? 'left') === 'left';
  }

  getTitleFontSize(): number { return this.renderContext.titleFontSize; }
  getTextFontSize(): number { return this.renderContext.textFontSize; }
  getTitleFontWeight(): number { return this.renderContext.titleFontWeight; }
  getTextFontWeight(): number { return this.renderContext.textFontWeight; }
  getTitleColor(): string { return this.renderContext.titleColor; }
  getTextColor(): string { return this.renderContext.textColor; }
  getIconColor(): string { return this.renderContext.iconColor; }

  private get renderContext() {
    return resolveWidgetRenderContext(this.widget, this.colorScheme, this.designerSettings);
  }

  private async loadSvg(): Promise<void> {
    if (!svgTextCache.text) {
      if (!svgTextCache.pending) {
        svgTextCache.pending = fetch('/icon-tab-dynamic.svg')
          .then(response => response.ok ? response.text() : null)
          .catch(() => null)
          .then(text => {
            svgTextCache.text = text;
            svgTextCache.pending = null;
            return text;
          });
      }
      await svgTextCache.pending;
    }
    this.applySvgColor();
  }

  private applySvgColor(): void {
    const raw = svgTextCache.text;
    if (!raw) return;
    const patched = raw.replace(
      /--accent-color:\s*#[0-9a-fA-F]{3,8};/gi,
      `--accent-color: ${this.getIconColor()};`,
    );
    this.inlineSvg = this.sanitizer.bypassSecurityTrustHtml(patched);
    this.cdr.markForCheck();
  }
}
