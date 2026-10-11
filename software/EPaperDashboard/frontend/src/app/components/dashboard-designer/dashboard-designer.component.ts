import { Component, OnInit, OnDestroy, inject, signal, computed, HostListener, effect } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { A11yModule } from '@angular/cdk/a11y';
import { ActivatedRoute, Router, RouterModule } from '@angular/router';
import { HttpClient } from '@angular/common/http';
import { Subscription } from 'rxjs';
import type { DataSourceStatus } from '../../services/dashboard-preview-data.service';
import { DashboardService } from '../../services/dashboard.service';
import { ToastService } from '../../services/toast.service';
import { DataProviderService, DataSource } from '../../services/data-provider.service';
import { AiService } from '../../services/ai.service';
import {
  DashboardRenderPreviewService,
  WidgetRenderGeometry,
} from '../../services/dashboard-render-preview.service';
import { DialogService } from '../../services/dialog.service';
import { HasUnsavedChanges } from '../../guards/unsaved-changes.guard';
import { WidgetConfigComponent } from '../widget-config/widget-config.component';
import { RenderedPreviewModalComponent } from '../rendered-preview-modal/rendered-preview-modal.component';
import { WidgetElementOverlayComponent } from '../widget-element-overlay/widget-element-overlay.component';
import {
  applyEditableElementChange,
  EditableElementChange,
} from '../../models/widget-element-layout';
import {
  Dashboard,
  DashboardLayout,
  DashboardOrientation,
  WidgetConfig,
  WidgetColorOverrides,
  WidgetType,
  ColorScheme,
  DEFAULT_COLOR_SCHEMES,
  WidgetPosition,
  DEFAULT_DASHBOARD_SIZE,
  DashboardSizePreset,
  DASHBOARD_SIZE_PRESETS,
  AiConfig,
  AiDataSummary,
} from '../../models/types';
import {
  createDefaultWidgetConfig,
  getWidgetDefaultSize,
  WidgetCategory,
  WidgetDefinition,
  WIDGET_DEFINITIONS,
  getWidgetRenderType,
} from '../../models/widget-catalog';

@Component({
  selector: 'app-dashboard-designer',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, A11yModule, WidgetConfigComponent, RenderedPreviewModalComponent, WidgetElementOverlayComponent],
  templateUrl: './dashboard-designer.component.html',
  styleUrls: ['./dashboard-designer.component.scss']
})
export class DashboardDesignerComponent implements OnInit, OnDestroy, HasUnsavedChanges {
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly http = inject(HttpClient);
  private readonly dashboardService = inject(DashboardService);
  private readonly toastService = inject(ToastService);
  private readonly dataProviderService = inject(DataProviderService);
  private readonly renderPreviewService = inject(DashboardRenderPreviewService);
  private readonly aiService = inject(AiService);
  private readonly dialogService = inject(DialogService);
  private renderPreviewSubscription?: Subscription;

  // Dashboard data
  dashboardId: string = '';
  dashboard = signal<Dashboard | null>(null);
  orientation = signal<DashboardOrientation>('Landscape');
  sizePresets: DashboardSizePreset[] = DASHBOARD_SIZE_PRESETS;
  selectedSizeIndex = 0;
  layout = signal<DashboardLayout>({
    width: DEFAULT_DASHBOARD_SIZE.width,
    height: DEFAULT_DASHBOARD_SIZE.height,
    gridCols: 12,
    gridRows: 8,
    colorScheme: DEFAULT_COLOR_SCHEMES[0],
    widgets: [],
    canvasPadding: 16,
    widgetGap: 4,
    widgetBorder: 3,
    widgetPadding: 4,
    titleFontSize: 16,
    textFontSize: 14,
    titleFontWeight: 700,
    textFontWeight: 400
  });

  // UI State
  colorSchemes = DEFAULT_COLOR_SCHEMES;
  availableWidgets = WIDGET_DEFINITIONS;
  readonly widgetCategories: ReadonlyArray<{ type: WidgetCategory; label: string }> = [
    { type: 'content', label: 'Content' },
    { type: 'data', label: 'Data' },
    { type: 'home-assistant', label: 'Home Assistant' },
    { type: 'google', label: 'Google' },
    { type: 'chart', label: 'Charts' },
    { type: 'asset', label: 'Assets' },
    { type: 'system', label: 'System' },
  ];

  get allWidgetCategories(): ReadonlyArray<{ type: WidgetCategory; label: string }> {
    const builtIn = new Set(this.widgetCategories.map(category => category.type));
    const extra = new Map(this.availableWidgets.filter(widget => !builtIn.has(widget.category))
      .map(widget => [widget.category, { type: widget.category, label: widget.categoryLabel ?? widget.category }]));
    return [...this.widgetCategories, ...extra.values()];
  }

  widgetsInCategory(category: WidgetCategory): readonly WidgetDefinition[] {
    return this.availableWidgets.filter(widget => widget.category === category);
  }

  selectedWidget = signal<WidgetConfig | null>(null);
  ghost = signal<{ id: string; position: WidgetPosition } | null>(null);
  isLoading = signal(false);
  loadError = signal('');
  availableEntities = signal<DataSource[]>([]);
  entitiesLoading = signal(false);
  activeTab = signal<'dashboard' | 'widgets' | 'properties'>('dashboard');
  renderedCanvasImageUrl = signal('');
  renderedCanvasLoading = signal(false);
  renderedCanvasError = signal('');
  renderedCanvasAt = signal<string | null>(null);
  renderedWidgetGeometry = signal<Record<string, WidgetRenderGeometry>>({});
  renderedSourceStatuses = signal<Record<string, DataSourceStatus>>({});
  elementEditingWidgetId = signal<string | null>(null);
  toolboxCollapsed = signal(false); // Widget toolbox left panel collapsed
  colorSchemeCollapsed = signal(false); // Color scheme section expanded by default
  colorOverridesCollapsed = signal(true); // Layout color overrides collapsed by default
  layoutCollapsed = signal(false); // Layout section expanded by default
  fontsCollapsed = signal(true); // Fonts section collapsed by default
  aiSectionCollapsed = signal(false); // AI section expanded by default when AI enabled
  widgetColorOverridesCollapsed = signal(true); // Widget color overrides collapsed by default
  showPreviewModal = signal(false);
  previewLoading = signal(false);
  previewError = signal('');
  previewImageUrl = signal('');
  // Mobile responsive state
  isMobile = signal(false);
  mobileWidgetDrawerOpen = signal(false);
  mobilePropertiesOpen = signal(false);
  mobileOverflowOpen = signal(false);
  mobileSelectionToolbar = signal(false);
  longPressActive = signal(false);
  mobileDragging = signal(false);
  private mobileBreakpoint = 768;
  private isTouchDevice = false;
  private resizeTimer: any = null;
  private renderPreviewTimer: ReturnType<typeof setTimeout> | null = null;
  private renderPreviewRevision = 0;
  private renderedLayoutSignature = '';
  private forceRefreshOnNextRender = false;
  private viewportWidth = signal(typeof window !== 'undefined' ? window.innerWidth : 1024);
  private swipeStartY = 0;
  private swipeStartX = 0;

  // AI state
  isGeneratingAi = signal(false);
  aiGeneratedWidgets = signal<WidgetConfig[]>([]);
  aiLastGenerated = signal<string | null>(null);
  aiLastError = signal<string | null>(null);
  aiDataSummary = signal<AiDataSummary | null>(null);
  aiPromptTokenEstimate = signal<number | null>(null);

  // AI settings (editable in the AI tab)
  aiEnabled = signal(false);
  aiPrompt = signal('');
  aiLeadTimeMinutes = signal(5);
  aiConfigMode = signal<string>('None');

  // Dirty tracking
  private savedSnapshot = signal('');
  readonly isDirty = computed(() => {
    const current = this.computeCurrentSnapshot();
    return current !== this.savedSnapshot();
  });

  // Tab navigation
  tabOrder: Array<'dashboard' | 'widgets' | 'properties'> = ['dashboard', 'widgets', 'properties'];

  // Drag state
  private dragStartPos = { x: 0, y: 0 };
  private dragStartWidget = { x: 0, y: 0, w: 0, h: 0 };
  private previewObjectUrl: string | null = null;
  private longPressTimer: any = null;
  private readonly LONG_PRESS_MS = 300;
  private pointerMoved = false;

  canvasScale = computed(() => {
    if (!this.isMobile()) return 1;
    const availableWidth = this.viewportWidth() - 16;
    const canvasWidth = this.layout().width;
    if (canvasWidth <= availableWidth) return 1;
    return Math.max(0.2, availableWidth / canvasWidth);
  });

  constructor() {
    // Scroll lock when mobile overlays are open
    effect(() => {
      const lock = this.mobileWidgetDrawerOpen() || this.mobilePropertiesOpen();
      document.body.style.overflow = lock ? 'hidden' : '';
    });

    // The native renderer is the canonical visual source. Layout changes are rendered after a
    // short quiet period so controls remain immediate while obsolete HTTP requests are cancelled.
    effect(() => {
      this.layout();
      this.aiGeneratedWidgets();
      this.aiEnabled();
      const dashboard = this.dashboard();
      const loading = this.isLoading();
      if (!loading && dashboard && this.dashboardId) {
        this.scheduleRenderedCanvas();
      }
    });
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    if (this.resizeTimer) clearTimeout(this.resizeTimer);
    this.resizeTimer = setTimeout(() => {
      this.viewportWidth.set(window.innerWidth);
      this.isMobile.set(this.checkIsMobile());
    }, 100);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event): void {
    if (!this.mobileOverflowOpen()) return;
    const target = event.target as HTMLElement;
    if (!target.closest('.mobile-overflow-menu')) {
      this.mobileOverflowOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  closeOpenDesignerOverlay(): void {
    if (this.mobileOverflowOpen()) this.mobileOverflowOpen.set(false);
    else if (this.mobileWidgetDrawerOpen()) this.mobileWidgetDrawerOpen.set(false);
    else if (this.mobilePropertiesOpen()) this.mobilePropertiesOpen.set(false);
  }

  ngOnInit(): void {
    this.isTouchDevice = window.matchMedia('(pointer: coarse)').matches;
    this.viewportWidth.set(window.innerWidth);
    this.isMobile.set(this.checkIsMobile());
    this.dashboardId = this.route.snapshot.paramMap.get('id') || '';
    if (this.dashboardId) {
      this.isLoading.set(true);
      this.loadDashboard();
    } else {
      this.loadError.set('The dashboard address is incomplete. Return to the dashboard list and try again.');
      this.isLoading.set(false);
    }
  }

  ngOnDestroy(): void {
    this.renderPreviewSubscription?.unsubscribe();
    if (this.longPressTimer) clearTimeout(this.longPressTimer);
    if (this.resizeTimer) clearTimeout(this.resizeTimer);
    if (this.renderPreviewTimer) clearTimeout(this.renderPreviewTimer);
    document.body.style.overflow = '';
  }

  // Dashboard loading
  loadDashboard(): void {
    this.loadError.set('');
    this.dashboardService.getDashboard(this.dashboardId).subscribe({
      next: (dashboard) => {
        this.dashboard.set(dashboard);
        const storedOrientation = dashboard.orientation || 'Landscape';
        const screenW = dashboard.screenWidth || DEFAULT_DASHBOARD_SIZE.width;
        const screenH = dashboard.screenHeight || DEFAULT_DASHBOARD_SIZE.height;

        const sizeIdx = this.sizePresets.findIndex(s => s.width === screenW && s.height === screenH);
        this.selectedSizeIndex = sizeIdx >= 0 ? sizeIdx : 0;

        if (dashboard.layoutConfig) {
          const w = dashboard.layoutConfig.width ?? screenW;
          const h = dashboard.layoutConfig.height ?? screenH;
          const effectiveOrientation: DashboardOrientation = h > w ? 'Portrait' : 'Landscape';
          this.orientation.set(effectiveOrientation);
          this.layout.set(this.normalizeLayout(dashboard.layoutConfig, effectiveOrientation, screenW, screenH));
        } else {
          this.orientation.set(storedOrientation);
          const isPortrait = storedOrientation === 'Portrait';
          this.layout.update(l => ({
            ...l,
            width: isPortrait ? screenH : screenW,
            height: isPortrait ? screenW : screenH,
            gridCols: isPortrait ? 8 : 12,
            gridRows: isPortrait ? 12 : 8,
          }));
        }
        this.loadAvailableEntities();
        this.loadAiGeneratedWidgets();

        // Initialize AI settings from dashboard
        this.aiPrompt.set(dashboard.aiPrompt ?? '');
        this.aiLeadTimeMinutes.set(dashboard.aiLeadTimeMinutes ?? 5);

        // Resolve effective AI config mode from backend (already resolved server-side)
        const effectiveMode = dashboard.effectiveAiConfigMode ?? 'None';
        this.aiConfigMode.set(effectiveMode);
        this.aiEnabled.set(effectiveMode !== 'None' && (dashboard.isAiEnabled ?? false));

        this.markAsPristine();
        this.isLoading.set(false);
      },
      error: (err) => {
        this.dashboard.set(null);
        this.loadError.set(err.error?.message || 'Check the server connection and try again.');
        this.isLoading.set(false);
      }
    });
  }

  retryLoadDashboard(): void {
    if (!this.dashboardId) {
      this.router.navigate(['/dashboards']);
      return;
    }
    this.isLoading.set(true);
    this.loadDashboard();
  }

  sourceErrors = signal<string[]>([]);

  loadAvailableEntities(): void {
    if (!this.dashboardId) {
      return;
    }

    this.entitiesLoading.set(true);
    this.dataProviderService.sources(this.dashboardId).subscribe({
      next: (result) => {
        this.sourceErrors.set(result.errors);
        this.availableEntities.set(result.data);
        this.entitiesLoading.set(false);
        this.isLoading.set(false);
      },
      error: (err) => {
        this.availableEntities.set([]);
        this.entitiesLoading.set(false);
        this.isLoading.set(false);
      }
    });
  }

  onWidgetSelect(widget: WidgetConfig): void {
    this.selectedWidget.set(widget);
    this.activeTab.set('properties');
  }

  loadAiGeneratedWidgets(): void {
    const d = this.dashboard();
    if (!d?.isAiEnabled) return;

    this.aiService.getGeneratedWidgets(this.dashboardId).subscribe({
      next: (result) => {
        this.aiGeneratedWidgets.set(result.widgets || []);
        this.aiLastGenerated.set(
          result.generatedAt ? new Date(result.generatedAt).toLocaleString() : null
        );
        this.aiLastError.set(result.lastError ?? null);
      },
      error: () => {}
    });
  }

  generateAiContent(): void {
    if (this.isGeneratingAi()) {
      return;
    }
    this.isGeneratingAi.set(true);

    // Send the prompt directly — no need to save first
    this.aiService.generateDashboard(this.dashboardId, this.aiPrompt()).subscribe({
      next: (result) => {
        this.aiGeneratedWidgets.set(result.widgets || []);
        this.aiLastGenerated.set(
          result.generatedAt ? new Date(result.generatedAt).toLocaleString() : null
        );
        this.aiLastError.set(null);
        this.aiDataSummary.set(result.dataSummary ?? null);
        this.aiPromptTokenEstimate.set(result.promptTokenEstimate ?? null);
        this.toastService.success(`AI generated ${result.widgets?.length ?? 0} widgets`);
        this.isGeneratingAi.set(false);
      },
      error: (err) => {
        const errorMsg = err.error?.message || err.error || 'AI generation failed';
        this.aiLastError.set(errorMsg);
        this.toastService.error(errorMsg);
        this.isGeneratingAi.set(false);
      }
    });
  }

  deleteWidget(widget: WidgetConfig): void {
    const originalIndex = this.layout().widgets.findIndex(item => item.id === widget.id);
    if (originalIndex < 0) return;

    this.layout.update(layout => ({
      ...layout,
      widgets: layout.widgets.filter(w => w.id !== widget.id)
    }));
    if (this.selectedWidget()?.id === widget.id) {
      this.selectedWidget.set(null);
      this.elementEditingWidgetId.set(null);
    }

    this.toastService.showWithAction(
      'Widget removed from the layout',
      'Undo',
      () => {
        this.layout.update(layout => {
          const widgets = [...layout.widgets];
          widgets.splice(Math.min(originalIndex, widgets.length), 0, widget);
          return { ...layout, widgets };
        });
        this.selectedWidget.set(widget);
      },
      'info',
      5000
    );
  }

  onToolboxWidgetMouseDown(event: MouseEvent | PointerEvent, widget: WidgetDefinition): void {
    event.preventDefault();
    const layout = this.layout();
    const canvas = document.querySelector('.dashboard-canvas') as HTMLElement;
    if (!canvas) return;

    // Create a small preview element that follows the cursor
    const preview = document.createElement('div');
    preview.className = 'toolbox-drag-preview';
    preview.style.position = 'fixed';
    preview.style.pointerEvents = 'none';
    preview.style.zIndex = '9999';
    preview.style.opacity = '0.85';
    preview.innerHTML = `<i class='fa ${widget.icon}'></i> ${widget.label}`;
    document.body.appendChild(preview);

    const movePreview = (e: PointerEvent | MouseEvent) => {
      preview.style.left = e.clientX + 8 + 'px';
      preview.style.top = e.clientY + 8 + 'px';
    };
    movePreview(event);

    const onPointerMove = (e: PointerEvent | MouseEvent) => {
      movePreview(e);

      const rect = canvas.getBoundingClientRect();
      if (e.clientX >= rect.left && e.clientX <= rect.right && e.clientY >= rect.top && e.clientY <= rect.bottom) {
        const padding = layout.canvasPadding ?? 0;
        const gap = layout.widgetGap ?? 0;
        const cols = Math.max(1, layout.gridCols);
        const rows = Math.max(1, layout.gridRows);
        const innerWidth = Math.max(0, rect.width - padding * 2 - gap * (cols - 1));
        const innerHeight = Math.max(0, rect.height - padding * 2 - gap * (rows - 1));
        const cellWidth = innerWidth / cols;
        const cellHeight = innerHeight / rows;
        const slotWidth = cellWidth + gap;
        const slotHeight = cellHeight + gap;
        const relX = e.clientX - rect.left - padding;
        const relY = e.clientY - rect.top - padding;
        const x = Math.max(0, Math.min(layout.gridCols - 1, Math.floor(relX / slotWidth)));
        const y = Math.max(0, Math.min(layout.gridRows - 1, Math.floor(relY / slotHeight)));
        const defaultSize = getWidgetDefaultSize(widget.type, layout.gridCols, layout.gridRows);
        const w = Math.min(defaultSize.w, layout.gridCols - x);
        const h = Math.min(defaultSize.h, layout.gridRows - y);
        this.ghost.set({ id: 'toolbox-' + widget.type, position: { x, y, w, h } });
      } else {
        this.ghost.set(null);
      }
    };

    const onPointerUp = () => {
      document.removeEventListener('pointermove', onPointerMove);
      document.removeEventListener('pointerup', onPointerUp);
      document.removeEventListener('pointercancel', onPointerUp);
      document.removeEventListener('mousemove', onPointerMove);
      document.removeEventListener('mouseup', onPointerUp);
      preview.remove();

      const g = this.ghost();
      if (g) {
        const newWidget: WidgetConfig = {
          id: this.generateId(),
          type: widget.type,
          position: { ...g.position },
          config: createDefaultWidgetConfig(widget.type)
        };
        this.layout.update(l => ({ ...l, widgets: [...l.widgets, newWidget] }));
      }
      this.ghost.set(null);
    };

    document.addEventListener('pointermove', onPointerMove);
    document.addEventListener('pointerup', onPointerUp);
    document.addEventListener('pointercancel', onPointerUp);
    // Fallback for environments where pointer events aren't fully supported
    document.addEventListener('mousemove', onPointerMove);
    document.addEventListener('mouseup', onPointerUp);
  }

  onToolboxWidgetKeyDown(event: KeyboardEvent, widget: WidgetDefinition): void {
    if (event.key !== 'Enter' && event.key !== ' ') return;
    event.preventDefault();
    this.addWidgetToFirstSlot(widget);
  }

  /** Add a widget to the first available grid position (used on mobile). */
  addWidgetToFirstSlot(widget: WidgetDefinition): void {
    const layout = this.layout();
    const cols = layout.gridCols;
    const rows = layout.gridRows;
    const { w: defaultW, h: defaultH } = getWidgetDefaultSize(widget.type, cols, rows);

    // Build occupancy grid
    const occupied = new Set<string>();
    for (const w of layout.widgets) {
      for (let dx = 0; dx < w.position.w; dx++) {
        for (let dy = 0; dy < w.position.h; dy++) {
          occupied.add(`${w.position.x + dx},${w.position.y + dy}`);
        }
      }
    }

    // Find first position that fits
    let placed = false;
    for (let y = 0; y <= rows - defaultH && !placed; y++) {
      for (let x = 0; x <= cols - defaultW && !placed; x++) {
        let fits = true;
        for (let dx = 0; dx < defaultW && fits; dx++) {
          for (let dy = 0; dy < defaultH && fits; dy++) {
            if (occupied.has(`${x + dx},${y + dy}`)) fits = false;
          }
        }
        if (fits) {
          const newWidget: WidgetConfig = {
            id: this.generateId(),
            type: widget.type,
            position: { x, y, w: defaultW, h: defaultH },
            config: createDefaultWidgetConfig(widget.type)
          };
          this.layout.update(l => ({ ...l, widgets: [...l.widgets, newWidget] }));
          this.selectedWidget.set(newWidget);
          this.activeTab.set('properties');
          if (this.isMobile()) {
            this.mobileWidgetDrawerOpen.set(false);
            this.mobilePropertiesOpen.set(true);
          }
          placed = true;
        }
      }
    }

    if (!placed) {
      this.toastService.show('No space available on the canvas for a new widget', 'error');
    }
  }

  // Tab navigation
  switchTab(direction: 'left' | 'right'): void {
    const current = this.tabOrder.indexOf(this.activeTab());
    let newIdx;
    if (direction === 'left') {
      newIdx = (current - 1 + this.tabOrder.length) % this.tabOrder.length;
    } else {
      newIdx = (current + 1) % this.tabOrder.length;
    }
    this.activeTab.set(this.tabOrder[newIdx]);
    setTimeout(() => {
      const tabBar = document.querySelector('.custom-tab-bar') as HTMLElement;
      if (!tabBar) return;
      const tabBtns = Array.from(tabBar.querySelectorAll('.tab-btn')) as HTMLElement[];
      const activeBtn = tabBtns[newIdx];
      if (activeBtn) {
        activeBtn.focus();
        const barRect = tabBar.getBoundingClientRect();
        const btnRect = activeBtn.getBoundingClientRect();
        if (btnRect.left < barRect.left) {
          tabBar.scrollBy({ left: btnRect.left - barRect.left - 16, behavior: 'smooth' });
        } else if (btnRect.right > barRect.right) {
          tabBar.scrollBy({ left: btnRect.right - barRect.right + 16, behavior: 'smooth' });
        }
      }
    }, 0);
  }

  onDesignerTabKeydown(event: KeyboardEvent): void {
    const current = this.tabOrder.indexOf(this.activeTab());
    let newIndex = current;

    if (event.key === 'ArrowLeft') newIndex = (current - 1 + this.tabOrder.length) % this.tabOrder.length;
    else if (event.key === 'ArrowRight') newIndex = (current + 1) % this.tabOrder.length;
    else if (event.key === 'Home') newIndex = 0;
    else if (event.key === 'End') newIndex = this.tabOrder.length - 1;
    else return;

    event.preventDefault();
    this.activeTab.set(this.tabOrder[newIndex]);
    const tabList = (event.currentTarget as HTMLElement).closest('[role="tablist"]');
    const tabs = tabList?.querySelectorAll<HTMLElement>('[role="tab"]');
    setTimeout(() => tabs?.item(newIndex).focus());
  }


  onWidgetMouseDown(event: MouseEvent | PointerEvent, widget: WidgetConfig): void {
    event.stopPropagation();
    if (this.elementEditingWidgetId() === widget.id) return;
    if (this.elementEditingWidgetId()) this.elementEditingWidgetId.set(null);
    this.selectedWidget.set(widget);
    this.activeTab.set('properties');

    // On mobile, show floating toolbar instead of immediately opening drawer
    if (this.isMobile()) {
      this.mobileSelectionToolbar.set(true);
    }

    const target = event.target as HTMLElement;
    if (target.classList.contains('resize-handle')) {
      const dir = target.dataset['direction'];
      if (dir) {
        this.startResize(event, widget, dir as 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw');
        return;
      }
    }

    // On touch devices, use long-press to initiate drag
    const isTouch = 'pointerType' in event && (event as PointerEvent).pointerType === 'touch';
    if (isTouch) {
      this.pointerMoved = false;
      this.longPressActive.set(true);
      if (this.longPressTimer) clearTimeout(this.longPressTimer);

      const onTouchMove = (e: PointerEvent) => {
        const dx = Math.abs(e.clientX - event.clientX);
        const dy = Math.abs(e.clientY - event.clientY);
        if (dx > 5 || dy > 5) {
          this.pointerMoved = true;
          this.longPressActive.set(false);
          if (this.longPressTimer) { clearTimeout(this.longPressTimer); this.longPressTimer = null; }
          cleanup();
        }
      };
      const onTouchUp = () => {
        this.longPressActive.set(false);
        if (this.longPressTimer) { clearTimeout(this.longPressTimer); this.longPressTimer = null; }
        cleanup();
      };
      const cleanup = () => {
        document.removeEventListener('pointermove', onTouchMove);
        document.removeEventListener('pointerup', onTouchUp);
        document.removeEventListener('pointercancel', onTouchUp);
      };

      document.addEventListener('pointermove', onTouchMove);
      document.addEventListener('pointerup', onTouchUp);
      document.addEventListener('pointercancel', onTouchUp);

      this.longPressTimer = setTimeout(() => {
        cleanup();
        this.longPressActive.set(false);
        this.startDrag(event, widget);
      }, this.LONG_PRESS_MS);

      return;
    }

    this.startDrag(event, widget);
  }

  onWidgetKeyDown(event: KeyboardEvent, widget: WidgetConfig): void {
    if (event.key === 'Enter' || event.key === ' ') {
      event.preventDefault();
      this.onWidgetSelect(widget);
      return;
    }

    const direction = {
      ArrowLeft: { x: -1, y: 0 },
      ArrowRight: { x: 1, y: 0 },
      ArrowUp: { x: 0, y: -1 },
      ArrowDown: { x: 0, y: 1 },
    }[event.key];
    if (!direction) return;

    event.preventDefault();
    const layout = this.layout();
    const current = widget.position;
    const nextPosition = event.shiftKey
      ? {
          ...current,
          w: Math.max(1, Math.min(layout.gridCols - current.x, current.w + direction.x)),
          h: Math.max(1, Math.min(layout.gridRows - current.y, current.h + direction.y)),
        }
      : {
          ...current,
          x: Math.max(0, Math.min(layout.gridCols - current.w, current.x + direction.x)),
          y: Math.max(0, Math.min(layout.gridRows - current.h, current.y + direction.y)),
        };

    const updated = { ...widget, position: nextPosition };
    this.layout.update(value => ({
      ...value,
      widgets: value.widgets.map(item => item.id === widget.id ? updated : item),
    }));
    this.selectedWidget.set(updated);
    this.activeTab.set('properties');
  }

  getSelectedWidgetGeometry(): WidgetRenderGeometry | undefined {
    if (this.renderedCanvasLoading()
        || this.renderedLayoutSignature !== JSON.stringify(this.getCompleteRenderLayout())) return undefined;
    const selected = this.selectedWidget();
    return selected ? this.renderedWidgetGeometry()[selected.id] : undefined;
  }

  selectedWidgetSupportsElementEditing(): boolean {
    const selected = this.selectedWidget();
    return !!selected && !!this.renderedWidgetGeometry()[selected.id]?.editable;
  }

  selectedWidgetSourceStatuses(): Array<{ key: string; label: string; status: DataSourceStatus }> {
    const widget = this.selectedWidget();
    if (!widget) return [];
    const config = widget.config as any;
    const entityId = config?.entityId as string | undefined;
    const sources: Array<{ key: string; label: string }> = [{ key: `widget:${widget.id}`, label: 'Widget data' }];

    switch (getWidgetRenderType(widget.type)) {
      case 'header':
        for (const badge of config?.badges ?? []) {
          if (badge.entityId) sources.push({ key: `entity:${badge.entityId}`, label: badge.entityId });
        }
        break;
      case 'weather':
        if (entityId) sources.push({ key: `entity:${entityId}`, label: entityId });
        break;
      case 'weather-forecast':
        if (entityId) {
          const mode = config?.forecastMode === 'hourly' ? 'hourly' : 'daily';
          sources.push({ key: `entity:${entityId}`, label: entityId });
          sources.push({ key: `forecast:${entityId}:${mode}`, label: `${mode} forecast` });
        }
        break;
      case 'todo':
        if (entityId) sources.push({ key: `todo:${entityId}`, label: entityId });
        break;
      case 'calendar':
        if (entityId) sources.push({ key: `calendar:${entityId}`, label: entityId });
        break;
      case 'rss-feed':
        if (entityId) sources.push({ key: `rss:${entityId}`, label: entityId });
        break;
      case 'graph':
        for (const series of config?.series ?? []) {
          if (series.entityId) sources.push({ key: `history:${series.entityId}`, label: series.label || series.entityId });
        }
        break;
      case 'ai-content':
        sources.push({ key: `generated:${widget.id}`, label: 'Generated content' });
        break;
    }

    const statuses = this.renderedSourceStatuses();
    return sources
      .filter((source, index) => sources.findIndex(item => item.key === source.key) === index)
      .flatMap(source => statuses[source.key] ? [{ ...source, status: statuses[source.key] }] : []);
  }

  toggleElementEditing(widget: WidgetConfig): void {
    const editingId = this.elementEditingWidgetId();
    this.elementEditingWidgetId.set(editingId === widget.id ? null : widget.id);
  }

  getSelectedWidgetSnapStep(): number {
    const config = this.selectedWidget()?.config as { snapStep?: number } | undefined;
    return config?.snapStep ?? 2;
  }

  getSelectedWidgetShowGuides(): boolean {
    const config = this.selectedWidget()?.config as { showGuides?: boolean } | undefined;
    return config?.showGuides ?? true;
  }

  onEditableElementChange(change: EditableElementChange): void {
    const selected = this.selectedWidget();
    if (!selected) return;
    const updated = applyEditableElementChange(selected, change);
    if (updated === selected) return;

    this.layout.update(layout => ({
      ...layout,
      widgets: layout.widgets.map(widget => widget.id === updated.id ? updated : widget),
    }));
    this.selectedWidget.set(updated);
  }

  private startDrag(event: MouseEvent | PointerEvent, widget: WidgetConfig): void {
    let isDragging = true;
    this.mobileDragging.set(true);
    this.dragStartPos = { x: event.clientX, y: event.clientY };
    this.dragStartWidget = { ...widget.position };

    const canvas = document.querySelector('.dashboard-canvas') as HTMLElement;
    const rect = canvas.getBoundingClientRect();
    const layout = this.layout();
    const padding = layout.canvasPadding ?? 0;
    const gap = layout.widgetGap ?? 0;
    const cols = Math.max(1, layout.gridCols);
    const rows = Math.max(1, layout.gridRows);
    const innerWidth = Math.max(0, rect.width - padding * 2 - gap * (cols - 1));
    const innerHeight = Math.max(0, rect.height - padding * 2 - gap * (rows - 1));
    const cellWidth = innerWidth / cols;
    const cellHeight = innerHeight / rows;
    const slotWidth = cellWidth + gap;
    const slotHeight = cellHeight + gap;

    this.ghost.set({ id: widget.id, position: { ...widget.position } });

    const onPointerMove = (e: PointerEvent | MouseEvent) => {
      if (!isDragging) return;

      const deltaX = e.clientX - this.dragStartPos.x;
      const deltaY = e.clientY - this.dragStartPos.y;
      const gridDeltaX = Math.round(deltaX / slotWidth);
      const gridDeltaY = Math.round(deltaY / slotHeight);
      const newX = Math.max(0, Math.min(layout.gridCols - widget.position.w, this.dragStartWidget.x + gridDeltaX));
      const newY = Math.max(0, Math.min(layout.gridRows - widget.position.h, this.dragStartWidget.y + gridDeltaY));

      this.ghost.set({ id: widget.id, position: { ...widget.position, x: newX, y: newY } });
    };

    const onPointerUp = () => {
      isDragging = false;
      this.mobileDragging.set(false);
      const g = this.ghost();
      if (g) {
        this.layout.update(l => ({
          ...l,
          widgets: l.widgets.map(w => w.id === g.id ? { ...w, position: { ...g.position } } : w)
        }));
        const updated = this.layout().widgets.find(w => w.id === g.id);
        if (updated) this.selectedWidget.set(updated);
      }
      this.ghost.set(null);
      document.removeEventListener('pointermove', onPointerMove);
      document.removeEventListener('pointerup', onPointerUp);
      document.removeEventListener('pointercancel', onPointerUp);
      document.removeEventListener('mousemove', onPointerMove);
      document.removeEventListener('mouseup', onPointerUp);
    };

    document.addEventListener('pointermove', onPointerMove);
    document.addEventListener('pointerup', onPointerUp);
    document.addEventListener('pointercancel', onPointerUp);
    document.addEventListener('mousemove', onPointerMove);
    document.addEventListener('mouseup', onPointerUp);
  }

  private startResize(event: MouseEvent | PointerEvent, widget: WidgetConfig, direction: 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw'): void {
    event.stopPropagation();
    let isResizing = true;
    this.mobileDragging.set(true);
    this.dragStartPos = { x: event.clientX, y: event.clientY };
    this.dragStartWidget = { ...widget.position };
    const canvas = document.querySelector('.dashboard-canvas') as HTMLElement;
    const rect = canvas.getBoundingClientRect();
    const layout = this.layout();
    const padding = layout.canvasPadding ?? 0;
    const gap = layout.widgetGap ?? 0;
    const cols = Math.max(1, layout.gridCols);
    const rows = Math.max(1, layout.gridRows);
    const innerWidth = Math.max(0, rect.width - padding * 2 - gap * (cols - 1));
    const innerHeight = Math.max(0, rect.height - padding * 2 - gap * (rows - 1));
    const cellWidth = innerWidth / cols;
    const cellHeight = innerHeight / rows;
    const slotWidth = cellWidth + gap;
    const slotHeight = cellHeight + gap;

    this.ghost.set({ id: widget.id, position: { ...widget.position } });

    const onPointerMove = (e: PointerEvent | MouseEvent) => {
      if (!isResizing) return;
      const deltaX = e.clientX - this.dragStartPos.x;
      const deltaY = e.clientY - this.dragStartPos.y;
      const gridDeltaX = Math.round(deltaX / slotWidth);
      const gridDeltaY = Math.round(deltaY / slotHeight);
      let newX = this.dragStartWidget.x;
      let newY = this.dragStartWidget.y;
      let newW = this.dragStartWidget.w;
      let newH = this.dragStartWidget.h;

      if (direction.includes('e')) {
        newW = Math.max(1, Math.min(cols - this.dragStartWidget.x, this.dragStartWidget.w + gridDeltaX));
      }
      if (direction.includes('w')) {
        newX = this.dragStartWidget.x + gridDeltaX;
        newW = this.dragStartWidget.w - gridDeltaX;
        if (newX < 0) {
          newW += newX;
          newX = 0;
        }
        if (newW < 1) {
          const diff = 1 - newW;
          newW = 1;
          newX = Math.max(0, newX - diff);
        }
        if (newX + newW > cols) {
          newW = cols - newX;
        }
      }

      if (direction.includes('s')) {
        newH = Math.max(1, Math.min(rows - this.dragStartWidget.y, this.dragStartWidget.h + gridDeltaY));
      }
      if (direction.includes('n')) {
        newY = this.dragStartWidget.y + gridDeltaY;
        newH = this.dragStartWidget.h - gridDeltaY;
        if (newY < 0) {
          newH += newY;
          newY = 0;
        }
        if (newH < 1) {
          const diff = 1 - newH;
          newH = 1;
          newY = Math.max(0, newY - diff);
        }
        if (newY + newH > rows) {
          newH = rows - newY;
        }
      }

      newX = Math.max(0, Math.min(cols - 1, newX));
      newY = Math.max(0, Math.min(rows - 1, newY));
      newW = Math.max(1, Math.min(cols - newX, newW));
      newH = Math.max(1, Math.min(rows - newY, newH));

      this.ghost.set({ id: widget.id, position: { x: newX, y: newY, w: newW, h: newH } });
    };

    const onPointerUp = () => {
      isResizing = false;
      this.mobileDragging.set(false);
      const g = this.ghost();
      if (g) {
        this.layout.update(l => ({
          ...l,
          widgets: l.widgets.map(w => w.id === g.id ? { ...w, position: { ...g.position } } : w)
        }));
        const updated = this.layout().widgets.find(w => w.id === g.id);
        if (updated) this.selectedWidget.set(updated);
      }
      this.ghost.set(null);
      document.removeEventListener('pointermove', onPointerMove);
      document.removeEventListener('pointerup', onPointerUp);
      document.removeEventListener('pointercancel', onPointerUp);
      document.removeEventListener('mousemove', onPointerMove);
      document.removeEventListener('mouseup', onPointerUp);
    };

    document.addEventListener('pointermove', onPointerMove);
    document.addEventListener('pointerup', onPointerUp);
    document.addEventListener('pointercancel', onPointerUp);
    document.addEventListener('mousemove', onPointerMove);
    document.addEventListener('mouseup', onPointerUp);
  }


  saveDashboard(): void {
    if (!this.dashboard()) return;

    const layoutConfig = this.layout();
    const payload: any = { layoutConfig, orientation: this.orientation() };

    // Include AI settings in every save
    payload.isAiEnabled = this.aiEnabled();
    payload.aiPrompt = this.aiPrompt();
    payload.aiLeadTimeMinutes = this.aiLeadTimeMinutes();

    this.dashboardService.updateDashboard(this.dashboardId, payload).subscribe({
      next: () => {
        this.dashboard.update(d => d ? {
          ...d,
          isAiEnabled: payload.isAiEnabled,
          aiPrompt: payload.aiPrompt,
          aiLeadTimeMinutes: payload.aiLeadTimeMinutes,
        } : d);
        this.markAsPristine();
        this.toastService.show('Dashboard saved successfully', 'success');
      },
      error: (err) => {
        if (err.status === 401 || err.status === 403) {
          this.toastService.show('Authentication error. Please log in again.', 'error');
          this.router.navigate(['/login'], { queryParams: { returnUrl: this.router.url } });
        } else {
          this.toastService.show('Failed to save dashboard', 'error');
        }
      }
    });
  }

  goBack(): void {
    this.router.navigate(['/dashboards', this.dashboardId, 'edit']);
  }

  hasUnsavedChanges(): boolean {
    return this.isDirty();
  }

  discardChanges(): void {
    const snapshot = JSON.parse(this.savedSnapshot());
    this.layout.set(snapshot.layout);
    this.orientation.set(snapshot.orientation);
    this.aiEnabled.set(snapshot.aiEnabled);
    this.aiPrompt.set(snapshot.aiPrompt);
    this.aiLeadTimeMinutes.set(snapshot.aiLeadTimeMinutes);
    this.selectedWidget.set(null);
    this.elementEditingWidgetId.set(null);
  }

  private computeCurrentSnapshot(): string {
    const layout = this.layout();
    return JSON.stringify({
      layout,
      orientation: this.orientation(),
      aiEnabled: this.aiEnabled(),
      aiPrompt: this.aiPrompt(),
      aiLeadTimeMinutes: this.aiLeadTimeMinutes(),
    });
  }

  private markAsPristine(): void {
    this.savedSnapshot.set(this.computeCurrentSnapshot());
  }

  previewServerSideRendered(): void {
    if (!this.dashboard()) {
      this.toastService.show('No dashboard loaded', 'error');
      return;
    }

    this.openRenderedPreview();
  }

  openRenderedPreview(): void {
    this.showPreviewModal.set(true);
    this.previewLoading.set(true);
    this.previewError.set('');
    this.previewImageUrl.set('');

    if (this.previewObjectUrl) {
      try {
        URL.revokeObjectURL(this.previewObjectUrl);
      } catch (e) {}
      this.previewObjectUrl = null;
    }

    const url = `/api/dashboards/${this.dashboardId}/render-image?format=png&refresh=true`;

    this.http.post(url, this.layout(), {
      responseType: 'blob'
    }).subscribe({
      next: (blob) => {
        const imageUrl = URL.createObjectURL(blob);
        this.previewObjectUrl = imageUrl;
        this.previewImageUrl.set(imageUrl);
        this.previewLoading.set(false);
      },
      error: async (error) => {
        this.previewLoading.set(false);
        let errorMessage = 'Failed to load rendered preview';

        if (error.error instanceof Blob) {
          try {
            const text = await error.error.text();
            try {
              const json = JSON.parse(text);
              errorMessage = json.title || json.error || json.message || text;
            } catch (jsonError) {
              errorMessage = text || `HTTP Error ${error.status}`;
            }
          } catch (e) {
            errorMessage = `HTTP Error ${error.status}`;
          }
        } else if (error.status) {
          errorMessage = `HTTP Error ${error.status}`;
        }

        this.previewError.set(errorMessage);
        this.toastService.show(errorMessage, 'error');
      }
    });
  }

  updateColorScheme(scheme: ColorScheme): void {
    this.layout.update(layout => {
      // Strip widget color overrides that reference colors outside the new palette
      const widgets = layout.widgets.map(w => {
        if (!w.colorOverrides) return w;
        const cleaned: WidgetColorOverrides = {};
        let hasAny = false;
        for (const [key, val] of Object.entries(w.colorOverrides)) {
          if (val && scheme.palette.includes(val.toLowerCase())) {
            (cleaned as any)[key] = val;
            hasAny = true;
          }
        }
        // Also strip graph series colors outside the new palette
        let config = w.config;
        if (w.type === 'graph' && (config as any)?.series) {
          const series = ((config as any).series as any[]).map(s => {
            if (s.color && !scheme.palette.includes(s.color.toLowerCase())) {
              return { ...s, color: this.getDefaultGraphColor(scheme, 0) };
            }
            return s;
          });
          config = { ...(config as any), series };
        }
        return { ...w, colorOverrides: hasAny ? cleaned : undefined, config };
      });
      return { ...layout, colorScheme: scheme, widgets };
    });
    // Refresh selected widget reference if needed
    const sel = this.selectedWidget();
    if (sel) {
      const updated = this.layout().widgets.find(w => w.id === sel.id);
      if (updated) this.selectedWidget.set(updated);
    }
  }

  /** Reset the current color scheme to its base defaults and clear ALL widget color overrides. */
  resetAllColorOverrides(): void {
    const currentName = this.layout().colorScheme.name;
    const baseScheme = this.colorSchemes.find(cs => cs.name === currentName) || DEFAULT_COLOR_SCHEMES[0];
    this.layout.update(layout => ({
      ...layout,
      colorScheme: { ...baseScheme },
      widgets: layout.widgets.map(w => {
        const { colorOverrides, ...rest } = w;
        return rest as WidgetConfig;
      })
    }));
    // Refresh selected widget reference
    const sel = this.selectedWidget();
    if (sel) {
      const updated = this.layout().widgets.find(w => w.id === sel.id);
      if (updated) this.selectedWidget.set(updated);
    }
  }

  /** Reset only the layout-level color overrides back to the base scheme defaults. */
  resetLayoutColorOverrides(): void {
    const currentName = this.layout().colorScheme.name;
    const baseScheme = this.colorSchemes.find(cs => cs.name === currentName) || DEFAULT_COLOR_SCHEMES[0];
    this.layout.update(layout => ({ ...layout, colorScheme: { ...baseScheme } }));
  }

  setOrientation(newOrientation: DashboardOrientation): void {
    if (newOrientation === this.orientation()) return;
    this.orientation.set(newOrientation);

    const size = this.sizePresets[this.selectedSizeIndex] ?? DEFAULT_DASHBOARD_SIZE;
    const isPortrait = newOrientation === 'Portrait';

    this.layout.update(l => ({
      ...l,
      width: isPortrait ? size.height : size.width,
      height: isPortrait ? size.width : size.height,
      gridCols: l.gridRows,
      gridRows: l.gridCols,
    }));
  }

  onSizeChange(): void {
    const size = this.sizePresets[this.selectedSizeIndex] ?? DEFAULT_DASHBOARD_SIZE;
    const isPortrait = this.orientation() === 'Portrait';
    this.layout.update(l => ({
      ...l,
      width: isPortrait ? size.height : size.width,
      height: isPortrait ? size.width : size.height,
    }));
  }

  updateLayoutGridCols(gridCols: number): void {
    this.layout.update(layout => ({ ...layout, gridCols }));
  }

  updateLayoutGridRows(gridRows: number): void {
    this.layout.update(layout => ({ ...layout, gridRows }));
  }

  updateCanvasPadding(padding: number): void {
    this.layout.update(layout => ({ ...layout, canvasPadding: padding }));
  }

  updateWidgetGap(gap: number): void {
    this.layout.update(layout => ({ ...layout, widgetGap: gap }));
  }

  updateWidgetBorder(border: number): void {
    this.layout.update(layout => ({ ...layout, widgetBorder: border }));
  }

  updateWidgetPadding(padding: number): void {
    this.layout.update(layout => ({ ...layout, widgetPadding: padding }));
  }

  updateTitleFontSize(fontSize: number | string): void {
    const size = typeof fontSize === 'string' ? parseInt(fontSize, 10) : fontSize;
    this.layout.update(layout => ({ ...layout, titleFontSize: size }));
  }

  updateTextFontSize(fontSize: number | string): void {
    const size = typeof fontSize === 'string' ? parseInt(fontSize, 10) : fontSize;
    this.layout.update(layout => ({ ...layout, textFontSize: size }));
  }

  updateTitleFontWeight(fontWeight: number | string): void {
    const weight = typeof fontWeight === 'string' ? parseInt(fontWeight, 10) : fontWeight;
    this.layout.update(layout => ({ ...layout, titleFontWeight: this.normalizeFontWeight(weight) }));
  }

  updateTextFontWeight(fontWeight: number | string): void {
    const weight = typeof fontWeight === 'string' ? parseInt(fontWeight, 10) : fontWeight;
    this.layout.update(layout => ({ ...layout, textFontWeight: this.normalizeFontWeight(weight) }));
  }

  updateCanvasBackgroundColor(color: string): void {
    this.layout.update(layout => ({
      ...layout,
      colorScheme: { ...layout.colorScheme, canvasBackgroundColor: color }
    }));
  }

  updateWidgetBorderColor(color: string): void {
    this.layout.update(layout => ({
      ...layout,
      colorScheme: { ...layout.colorScheme, widgetBorderColor: color }
    }));
  }

  updateWidgetTitleTextColor(color: string): void {
    this.layout.update(layout => ({
      ...layout,
      colorScheme: { ...layout.colorScheme, widgetTitleTextColor: color }
    }));
  }

  updateWidgetTextColor(color: string): void {
    this.layout.update(layout => ({
      ...layout,
      colorScheme: { ...layout.colorScheme, widgetTextColor: color }
    }));
  }

  updateIconColor(color: string): void {
    this.layout.update(layout => ({
      ...layout,
      colorScheme: { ...layout.colorScheme, iconColor: color }
    }));
  }

  updateWidgetBackgroundColor(color: string): void {
    this.layout.update(layout => ({
      ...layout,
      colorScheme: { ...layout.colorScheme, widgetBackgroundColor: color }
    }));
  }

  updateWidgetColorOverride(widget: WidgetConfig, colorProperty: keyof WidgetColorOverrides, value: string): void {
    const updatedWidget: WidgetConfig = {
      ...widget,
      colorOverrides: {
        ...widget.colorOverrides,
        [colorProperty]: value || undefined
      }
    };

    this.layout.update(layout => ({
      ...layout,
      widgets: layout.widgets.map(w => w.id === widget.id ? updatedWidget : w)
    }));

    this.selectedWidget.set(updatedWidget);
  }

  clearWidgetColorOverride(widget: WidgetConfig, colorProperty: keyof WidgetColorOverrides): void {
    const overrides = { ...widget.colorOverrides };
    delete overrides[colorProperty];

    const updatedWidget: WidgetConfig = {
      ...widget,
      colorOverrides: Object.keys(overrides).length > 0 ? overrides : undefined
    };

    this.layout.update(layout => ({
      ...layout,
      widgets: layout.widgets.map(w => w.id === widget.id ? updatedWidget : w)
    }));

    this.selectedWidget.set(updatedWidget);
  }

  onWidgetConfigChanged(widget: WidgetConfig): void {
    // Update the layout signal with the new widget configuration
    this.layout.update(layout => ({
      ...layout,
      widgets: layout.widgets.map(w => w.id === widget.id ? { ...widget } : w)
    }));
    
    // Update selected widget if it's currently selected
    if (this.selectedWidget()?.id === widget.id) {
      this.selectedWidget.set({ ...widget });
    }
  }

  /** Find the base (default) scheme that matches the current scheme by name. */
  private getBaseScheme(): ColorScheme {
    const currentName = this.layout().colorScheme.name;
    return this.colorSchemes.find(cs => cs.name === currentName) || DEFAULT_COLOR_SCHEMES[0];
  }

  // Helper method to check if a specific layout color property is overridden vs. the base scheme
  isLayoutColorOverridden(prop: 'canvasBackgroundColor' | 'widgetBackgroundColor' | 'widgetBorderColor' | 'widgetTitleTextColor' | 'widgetTextColor' | 'iconColor'): boolean {
    return this.layout().colorScheme[prop] !== this.getBaseScheme()[prop];
  }

  // Helper method to check if layout has any color overrides vs. the selected base scheme
  hasLayoutColorOverrides(): boolean {
    return this.isLayoutColorOverridden('canvasBackgroundColor') ||
      this.isLayoutColorOverridden('widgetBackgroundColor') ||
      this.isLayoutColorOverridden('widgetBorderColor') ||
      this.isLayoutColorOverridden('widgetTitleTextColor') ||
      this.isLayoutColorOverridden('widgetTextColor') ||
      this.isLayoutColorOverridden('iconColor');
  }

  // Helper method to check if a specific widget color override differs from the current scheme
  isWidgetColorOverridden(widget: WidgetConfig | null, prop: keyof WidgetColorOverrides): boolean {
    if (!widget?.colorOverrides) return false;
    const val = widget.colorOverrides[prop];
    if (val == null || val === '') return false;
    const scheme = this.layout().colorScheme;
    const schemeDefaults: Record<string, string> = {
      widgetBackgroundColor: scheme.widgetBackgroundColor,
      widgetBorderColor: scheme.widgetBorderColor,
      widgetTitleTextColor: scheme.widgetTitleTextColor,
      widgetTextColor: scheme.widgetTextColor,
      iconColor: scheme.iconColor,
    };
    return val !== schemeDefaults[prop];
  }

  // Helper method to check if widget has any color overrides that actually differ from the current scheme
  hasWidgetColorOverrides(widget: WidgetConfig | null): boolean {
    if (!widget?.colorOverrides) return false;
    return this.isWidgetColorOverridden(widget, 'widgetBackgroundColor') ||
      this.isWidgetColorOverridden(widget, 'widgetBorderColor') ||
      this.isWidgetColorOverridden(widget, 'widgetTitleTextColor') ||
      this.isWidgetColorOverridden(widget, 'widgetTextColor') ||
      this.isWidgetColorOverridden(widget, 'iconColor');
  }


  /** Get a sensible default color for a graph series from the palette. */
  private getDefaultGraphColor(scheme: ColorScheme, index: number): string {
    const chartColors = scheme.palette.filter(
      c => c !== scheme.canvasBackgroundColor && c !== scheme.widgetBackgroundColor && c !== scheme.background
    );
    if (chartColors.length > 0) return chartColors[index % chartColors.length];
    return scheme.palette[index % scheme.palette.length] || '#000000';
  }

  getColorName(color: string): string {
    const colorMap: Record<string, string> = {
      '#000000': 'Black',
      '#ffffff': 'White',
      '#ff0000': 'Red',
      '#ffff00': 'Yellow'
    };
    return colorMap[color.toLowerCase()] || color;
  }

  refreshLivePreview(): void {
    if (this.dashboardId) this.scheduleRenderedCanvas(true, 0);
  }

  private scheduleRenderedCanvas(refreshData = false, delayMs = 300): void {
    if (!this.dashboardId || this.isLoading()) return;

    this.forceRefreshOnNextRender ||= refreshData;
    if (this.renderPreviewTimer) clearTimeout(this.renderPreviewTimer);
    this.renderPreviewTimer = setTimeout(() => {
      this.renderPreviewTimer = null;
      this.renderRenderedCanvas();
    }, delayMs);
  }

  private renderRenderedCanvas(): void {
    const revision = ++this.renderPreviewRevision;
    const refreshData = this.forceRefreshOnNextRender;
    const renderLayout = this.getCompleteRenderLayout();
    const renderLayoutSignature = JSON.stringify(renderLayout);
    this.forceRefreshOnNextRender = false;

    this.renderPreviewSubscription?.unsubscribe();
    this.renderedCanvasLoading.set(true);
    this.renderedCanvasError.set('');
    this.renderPreviewSubscription = this.renderPreviewService
      .render(this.dashboardId, renderLayout, revision, refreshData)
      .subscribe({
        next: preview => {
          if (preview.revision !== this.renderPreviewRevision) return;

          this.renderedCanvasImageUrl.set(this.renderPreviewService.toImageUrl(preview));
          this.renderedLayoutSignature = renderLayoutSignature;
          this.renderedCanvasAt.set(preview.renderedAt);
          this.renderedWidgetGeometry.set(Object.fromEntries(
            preview.widgets.map(widget => [widget.id, widget]),
          ));
          this.renderedSourceStatuses.set(preview.sourceStatuses ?? {});
          this.renderedCanvasLoading.set(false);
        },
        error: async error => {
          if (revision !== this.renderPreviewRevision) return;

          const message = await this.getRenderErrorMessage(error);
          if (revision !== this.renderPreviewRevision) return;

          this.renderedCanvasLoading.set(false);
          this.renderedCanvasError.set(message);
        },
      });
  }

  private getCompleteRenderLayout(): DashboardLayout {
    const layout = this.layout();
    const generatedWidgets = this.aiEnabled() ? this.aiGeneratedWidgets() : [];
    return generatedWidgets.length === 0
      ? layout
      : { ...layout, widgets: [...layout.widgets, ...generatedWidgets] };
  }

  private async getRenderErrorMessage(error: any): Promise<string> {
    if (error?.error instanceof Blob) {
      const text = await error.error.text();
      try {
        const value = JSON.parse(text);
        return value.title || value.error || value.message || text;
      } catch {
        return text || `HTTP Error ${error.status}`;
      }
    }

    return error?.error?.title
      || error?.error?.error
      || error?.error?.message
      || error?.message
      || 'Failed to render dashboard preview';
  }

  getCanvasStyle(): any {
    const layout = this.layout();
    return {
      width: `${layout.width}px`,
      minWidth: `${layout.width}px`,
      'min-width': `${layout.width}px`,
      height: `${layout.height}px`,
      minHeight: `${layout.height}px`,
      'min-height': `${layout.height}px`,
      backgroundColor: layout.colorScheme.canvasBackgroundColor || layout.colorScheme.background,
      color: layout.colorScheme.text,
      display: 'grid',
      gridTemplateColumns: `repeat(${layout.gridCols}, 1fr)`,
      gridTemplateRows: `repeat(${layout.gridRows}, 1fr)`,
      gap: `${layout.widgetGap ?? 0}px`,
      padding: `${layout.canvasPadding ?? 0}px`,
      '--widget-border': `${layout.widgetBorder ?? 3}px`,
      position: 'relative',
      boxSizing: 'border-box',
    };
  }

  getGridOverlayStyle(): any {
    const layout = this.layout();
    const padding = layout.canvasPadding ?? 0;
    const gap = layout.widgetGap ?? 0;
    const cols = Math.max(1, layout.gridCols);
    const rows = Math.max(1, layout.gridRows);
    const canvasEl = document.querySelector('.dashboard-canvas') as HTMLElement | null;
    const rect = canvasEl ? canvasEl.getBoundingClientRect() : null;
    const totalWidth = rect ? rect.width : layout.width;
    const totalHeight = rect ? rect.height : layout.height;

    const innerWidth = Math.max(0, totalWidth - padding * 2 - gap * (cols - 1));
    const innerHeight = Math.max(0, totalHeight - padding * 2 - gap * (rows - 1));

    const cellWidth = innerWidth / cols;
    const cellHeight = innerHeight / rows;
    const slotWidth = cellWidth + gap;
    const slotHeight = cellHeight + gap;
    const lineColor = 'rgba(0,0,0,0.06)';
    const offset = padding - gap / 2;

    return {
      position: 'absolute',
      top: '0',
      left: '0',
      right: '0',
      bottom: '0',
      pointerEvents: 'none',
      backgroundImage: `linear-gradient(to right, ${lineColor} 1px, transparent 1px), linear-gradient(to bottom, ${lineColor} 1px, transparent 1px)`,
      backgroundSize: `${slotWidth}px ${slotHeight}px, ${slotWidth}px ${slotHeight}px`,
      backgroundPosition: `${offset}px ${offset}px, ${offset}px ${offset}px`,
      zIndex: 1,
      opacity: 0.6
    };
  }

  getWidgetStyle(widget: WidgetConfig): any {
    const layout = this.layout();
    const borderColor = widget.colorOverrides?.widgetBorderColor || layout.colorScheme.widgetBorderColor || layout.colorScheme.foreground;
    const backgroundColor = widget.colorOverrides?.widgetBackgroundColor || layout.colorScheme.widgetBackgroundColor || layout.colorScheme.background;
    return {
      gridColumn: `${widget.position.x + 1} / span ${widget.position.w}`,
      gridRow: `${widget.position.y + 1} / span ${widget.position.h}`,
      backgroundColor: backgroundColor,
      border: `${layout.widgetBorder ?? 2}px solid ${borderColor}`,
      color: layout.colorScheme.text,
      padding: `${layout.widgetPadding ?? 0}px`,
      boxSizing: 'border-box',
      overflow: 'visible',
      cursor: 'grab',
      position: 'relative',
      userSelect: 'none'
    };
  }

  getSelectionOverlayStyle(): any {
    const widget = this.selectedWidget();
    if (!widget) return { display: 'none' };
    const layout = this.layout();
    const padding = layout.canvasPadding ?? 0;
    const gap     = layout.widgetGap ?? 0;
    const cols    = layout.gridCols;
    const rows    = layout.gridRows;

    // Use actual inner dimensions (clientWidth excludes the canvas CSS border)
    const canvasEl = document.querySelector('.dashboard-canvas') as HTMLElement | null;
    const totalW = canvasEl ? canvasEl.clientWidth  : layout.width;
    const totalH = canvasEl ? canvasEl.clientHeight : layout.height;

    const cellW = (totalW - 2 * padding - gap * (cols - 1)) / cols;
    const cellH = (totalH - 2 * padding - gap * (rows - 1)) / rows;

    // During drag/resize the live position is in ghost(), not layout()
    const ghost = this.ghost();
    const p = (ghost?.id === widget.id ? ghost.position : null) ?? widget.position;

    const resolvedBounds = ghost?.id === widget.id
      ? null
      : this.renderedWidgetGeometry()[widget.id]?.bounds;
    const left   = resolvedBounds?.x ?? padding + p.x * (cellW + gap);
    const top    = resolvedBounds?.y ?? padding + p.y * (cellH + gap);
    const width  = resolvedBounds?.width ?? p.w * cellW + (p.w - 1) * gap;
    const height = resolvedBounds?.height ?? p.h * cellH + (p.h - 1) * gap;
    const editingElements = this.elementEditingWidgetId() === widget.id;

    return {
      position: 'absolute',
      left:   `${left}px`,
      top:    `${top}px`,
      width:  `${width}px`,
      height: `${height}px`,
      boxSizing: 'border-box',
      pointerEvents: editingElements ? 'none' : 'auto',
      zIndex: 100,
    };
  }

  getGhostStyle(ghost: { id: string; position: WidgetPosition }): any {
    const layout = this.layout();
    const p = ghost.position;
    return {
      gridColumn: `${p.x + 1} / span ${p.w}`,
      gridRow: `${p.y + 1} / span ${p.h}`,
      backgroundColor: 'transparent',
      border: `${layout.widgetBorder ?? 2}px dashed ${layout.colorScheme.foreground}`,
      color: layout.colorScheme.text,
      padding: '8px',
      overflow: 'visible',
      cursor: 'grabbing',
      position: 'relative',
      userSelect: 'none',
      zIndex: 3,
      opacity: 0.7
    };
  }

  private generateId(): string {
    return `widget-${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
  }

  compareColorSchemes(a: ColorScheme, b: ColorScheme): boolean {
    return a?.name === b?.name;
  }

  private normalizeLayout(parsedLayout: any, orientation?: DashboardOrientation, screenWidth?: number, screenHeight?: number): DashboardLayout {
    const isPortrait = orientation === 'Portrait';
    const sw = screenWidth || DEFAULT_DASHBOARD_SIZE.width;
    const sh = screenHeight || DEFAULT_DASHBOARD_SIZE.height;
    const baseLayout: DashboardLayout = {
      width: isPortrait ? sh : sw,
      height: isPortrait ? sw : sh,
      gridCols: isPortrait ? 8 : 12,
      gridRows: isPortrait ? 12 : 8,
      colorScheme: DEFAULT_COLOR_SCHEMES[0],
      widgets: [],
      canvasPadding: 16,
      widgetGap: 4,
      widgetBorder: 3,
      widgetPadding: 4,
      titleFontSize: 16,
      textFontSize: 14,
      titleFontWeight: 700,
      textFontWeight: 400
    };

    const baseScheme = parsedLayout?.colorScheme?.name
      ? this.colorSchemes.find(cs => cs.name === parsedLayout.colorScheme.name) || DEFAULT_COLOR_SCHEMES[0]
      : DEFAULT_COLOR_SCHEMES[0];

    const mergedScheme: ColorScheme = {
      ...baseScheme,
      ...(parsedLayout?.colorScheme ?? {}),
      name: baseScheme.name,
      palette: Array.isArray(parsedLayout?.colorScheme?.palette) && parsedLayout.colorScheme.palette.length > 0
        ? parsedLayout.colorScheme.palette
        : baseScheme.palette
    };

    const widgets = Array.isArray(parsedLayout?.widgets)
      ? parsedLayout.widgets.map((widget: any) => this.normalizeWidget(widget))
      : [];

    return {
      width: typeof parsedLayout?.width === 'number' ? parsedLayout.width : baseLayout.width,
      height: typeof parsedLayout?.height === 'number' ? parsedLayout.height : baseLayout.height,
      gridCols: typeof parsedLayout?.gridCols === 'number' ? parsedLayout.gridCols : baseLayout.gridCols,
      gridRows: typeof parsedLayout?.gridRows === 'number' ? parsedLayout.gridRows : baseLayout.gridRows,
      colorScheme: mergedScheme,
      widgets: widgets,
      canvasPadding: typeof parsedLayout?.canvasPadding === 'number' ? parsedLayout.canvasPadding : baseLayout.canvasPadding,
      widgetGap: typeof parsedLayout?.widgetGap === 'number' ? parsedLayout.widgetGap : baseLayout.widgetGap,
      widgetBorder: typeof parsedLayout?.widgetBorder === 'number' ? parsedLayout.widgetBorder : baseLayout.widgetBorder,
      widgetPadding: typeof parsedLayout?.widgetPadding === 'number' ? parsedLayout.widgetPadding : baseLayout.widgetPadding,
      titleFontSize: typeof parsedLayout?.titleFontSize === 'number' ? parsedLayout.titleFontSize : baseLayout.titleFontSize,
      textFontSize: typeof parsedLayout?.textFontSize === 'number' ? parsedLayout.textFontSize : baseLayout.textFontSize,
      titleFontWeight: this.normalizeFontWeight(
        typeof parsedLayout?.titleFontWeight === 'number' ? parsedLayout.titleFontWeight : baseLayout.titleFontWeight),
      textFontWeight: this.normalizeFontWeight(
        typeof parsedLayout?.textFontWeight === 'number' ? parsedLayout.textFontWeight : baseLayout.textFontWeight)
    };
  }

  private normalizeFontWeight(weight: number): 400 | 700 {
    return weight >= 700 ? 700 : 400;
  }

  private normalizeWidget(widget: any): WidgetConfig {
    const type = widget?.type as WidgetType;
    const defaultConfig = createDefaultWidgetConfig(type);
    const config = {
      ...defaultConfig,
      ...(widget?.config ?? {})
    };

    const position = {
      x: typeof widget?.position?.x === 'number' ? widget.position.x : 0,
      y: typeof widget?.position?.y === 'number' ? widget.position.y : 0,
      w: typeof widget?.position?.w === 'number' ? widget.position.w : 2,
      h: typeof widget?.position?.h === 'number' ? widget.position.h : 2
    };

    return {
      id: widget?.id || this.generateId(),
      type: type,
      position,
      config,
      colorOverrides: widget?.colorOverrides,
      titleOverride: widget?.titleOverride,
      showTitle: widget?.showTitle !== false
    } as WidgetConfig;
  }

  // AI settings methods
  onAiEnabledChange(enabled: boolean): void {
    if (enabled && this.aiConfigMode() === 'None') {
      this.aiEnabled.set(false);
      return;
    }
    this.aiEnabled.set(enabled);
    if (!enabled) {
      this.aiGeneratedWidgets.set([]);
      this.aiLastGenerated.set(null);
    }
  }

  onAiPromptChange(value: string): void {
    this.aiPrompt.set(value);
  }

  onAiLeadTimeChange(value: string): void {
    this.aiLeadTimeMinutes.set(parseInt(value, 10) || 5);
  }



  getCanvasScaleStyle(): any {
    const scale = this.canvasScale();
    if (scale >= 1) return {};
    const w = this.layout().width;
    const h = this.layout().height;
    return {
      transform: `scale(${scale})`,
      'transform-origin': 'top left',
      width: `${w}px`,
      height: `${h}px`,
      'margin-right': `-${w * (1 - scale)}px`,
      'margin-bottom': `-${h * (1 - scale)}px`,
    };
  }

  /** Touch-primary devices use a wider breakpoint (1024) so landscape phones still get mobile UI. */
  private checkIsMobile(): boolean {
    const width = window.innerWidth;
    if (this.isTouchDevice) {
      // Use the smaller screen dimension to detect phones vs tablets
      const minDim = Math.min(screen.width, screen.height);
      // Phones typically have a shorter dimension under ~500px
      return minDim < 600 || width < this.mobileBreakpoint;
    }
    return width < this.mobileBreakpoint;
  }

  openMobileProperties(): void {
    this.mobileSelectionToolbar.set(false);
    this.mobilePropertiesOpen.set(true);
    this.activeTab.set('properties');
  }

  onCanvasMouseDown(event: MouseEvent): void {
    const target = event.target as HTMLElement;
    if (target.classList.contains('dashboard-canvas') || target.classList.contains('grid-overlay')) {
      this.selectedWidget.set(null);
      this.elementEditingWidgetId.set(null);
      this.mobileSelectionToolbar.set(false);
    }
  }

  onBottomSheetTouchStart(event: TouchEvent): void {
    this.swipeStartY = event.touches[0].clientY;
  }

  onBottomSheetTouchEnd(event: TouchEvent): void {
    const deltaY = event.changedTouches[0].clientY - this.swipeStartY;
    if (deltaY > 80) {
      this.mobileWidgetDrawerOpen.set(false);
    }
  }

  onDrawerTouchStart(event: TouchEvent): void {
    this.swipeStartX = event.touches[0].clientX;
  }

  onDrawerTouchEnd(event: TouchEvent): void {
    const deltaX = event.changedTouches[0].clientX - this.swipeStartX;
    if (deltaX > 80) {
      this.mobilePropertiesOpen.set(false);
    }
  }
}
