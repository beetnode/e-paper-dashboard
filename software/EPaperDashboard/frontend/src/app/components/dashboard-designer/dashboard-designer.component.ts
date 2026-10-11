import { Component, OnInit, OnDestroy, inject, signal, computed, HostListener, effect, viewChild, ElementRef } from '@angular/core';
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
import { WidgetPickerComponent, WidgetPickerDragStart } from '../widget-picker/widget-picker.component';
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
  WidgetDefinition,
  WIDGET_DEFINITIONS,
  getWidgetDefinition,
  getWidgetRenderType,
  widgetNeedsDataSource,
} from '../../models/widget-catalog';
import { rememberRecentWidget } from '../../models/widget-picker';
import {
  clampWhole,
  findFreeSlot,
  fitPositionToGrid,
  fitWidgetsToGrid,
  GRID_LIMITS,
  moveWidgetInPaintOrder,
} from '../../models/designer-layers';
import {
  CanvasZoom,
  parseCanvasZoom,
  resolveCanvasScale,
  stepCanvasZoom,
} from '../../models/designer-viewport';

const CANVAS_ZOOM_STORAGE_KEY = 'designer.canvasZoom';
/** Below this width the side panel moves into a drawer so the canvas keeps its space. */
const PANEL_DRAWER_MAX_WIDTH = 992;
/** Below this width the layers panel starts hidden so the canvas keeps its space. */
const LAYERS_PANEL_MIN_WIDTH = 1400;
const RECENT_WIDGETS_STORAGE_KEY = 'designer.recentWidgets';

type LayoutColorProperty = 'canvasBackgroundColor' | 'widgetBackgroundColor' | 'widgetBorderColor'
  | 'widgetTitleTextColor' | 'widgetTextColor' | 'iconColor';

@Component({
  selector: 'app-dashboard-designer',
  standalone: true,
  imports: [CommonModule, FormsModule, RouterModule, A11yModule, WidgetConfigComponent, RenderedPreviewModalComponent, WidgetElementOverlayComponent, WidgetPickerComponent],
  host: { '[class.designer-mobile]': 'isMobile()' },
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
  selectedWidget = signal<WidgetConfig | null>(null);
  ghost = signal<{ id: string; position: WidgetPosition } | null>(null);
  isLoading = signal(false);
  loadError = signal('');
  availableEntities = signal<DataSource[]>([]);
  entitiesLoading = signal(false);
  renderedCanvasImageUrl = signal('');
  renderedCanvasLoading = signal(false);
  renderedCanvasError = signal('');
  renderedCanvasAt = signal<string | null>(null);
  renderedWidgetGeometry = signal<Record<string, WidgetRenderGeometry>>({});
  renderedSourceStatuses = signal<Record<string, DataSourceStatus>>({});
  elementEditingWidgetId = signal<string | null>(null);
  /** Desktop layers column visibility; tablets and phones show layers in a drawer instead. */
  layersOpen = signal(true);
  layersDrawerOpen = signal(false);
  inspectorDrawerOpen = signal(false);
  pickerOpen = signal(false);
  /** Where the desktop picker popover is shown: under the toolbar button or beside the layers panel. */
  pickerAnchor = signal<'toolbar' | 'layers'>('toolbar');
  pickerPosition = signal({ top: 0, left: 0 });
  hoveredWidgetId = signal<string | null>(null);
  layerMenuWidgetId = signal<string | null>(null);
  recentWidgetTypes = signal<WidgetType[]>(DashboardDesignerComponent.readRecentWidgets());
  /** Open state of designer-owned inspector sections, kept while switching selection. */
  private readonly sectionOpenState = signal<Record<string, boolean>>({});
  showPreviewModal = signal(false);
  previewLoading = signal(false);
  previewError = signal('');
  previewImageUrl = signal('');
  // Mobile responsive state
  isMobile = signal(false);
  mobileOverflowOpen = signal(false);
  mobileSelectionToolbar = signal(false);
  longPressActive = signal(false);
  private mobileBreakpoint = 768;
  private isTouchDevice = false;
  private resizeTimer: any = null;
  private renderPreviewTimer: ReturnType<typeof setTimeout> | null = null;
  private renderPreviewRevision = 0;
  /** Signature of the layout behind the current preview image, and of the last requested render. */
  private renderedLayoutSignature = signal('');
  private requestedLayoutSignature = '';
  private forceRefreshOnNextRender = false;
  private viewportWidth = signal(typeof window !== 'undefined' ? window.innerWidth : 1280);
  /** Tablet widths: properties and settings open in the drawer instead of a fixed column. */
  panelAsDrawer = computed(() => !this.isMobile() && this.viewportWidth() < PANEL_DRAWER_MAX_WIDTH);
  usesDrawer = computed(() => this.isMobile() || this.panelAsDrawer());
  private canvasViewportSize = signal({ width: 0, height: 0 });
  private canvasViewportObserver?: ResizeObserver;
  private readonly canvasViewport = viewChild<ElementRef<HTMLElement>>('canvasViewport');
  canvasZoom = signal<CanvasZoom>(DashboardDesignerComponent.readStoredZoom());
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

  /** Every inspector section is open by default except colors, which are rarely changed. */
  isSectionOpen(key: string): boolean {
    return this.sectionOpenState()[key] ?? !key.endsWith('colors');
  }

  onSectionToggle(key: string, event: Event): void {
    const open = (event.target as HTMLDetailsElement).open;
    if (open !== this.isSectionOpen(key)) this.sectionOpenState.update(state => ({ ...state, [key]: open }));
  }

  /** Merges config values for the selected widget, e.g. layout-editor snap and guides. */
  updateSelectedWidgetConfig(patch: Record<string, unknown>): void {
    const widget = this.selectedWidget();
    if (!widget) return;
    this.onWidgetConfigChanged({ ...widget, config: { ...widget.config, ...patch } as WidgetConfig['config'] });
  }

  readonly layoutColorFields: ReadonlyArray<{ key: LayoutColorProperty; label: string }> = [
    { key: 'canvasBackgroundColor', label: 'Canvas background' },
    { key: 'widgetBackgroundColor', label: 'Widget background' },
    { key: 'widgetBorderColor', label: 'Widget border' },
    { key: 'widgetTitleTextColor', label: 'Title text' },
    { key: 'widgetTextColor', label: 'Text' },
    { key: 'iconColor', label: 'Icons' },
  ];
  readonly widgetColorFields: ReadonlyArray<{ key: keyof WidgetColorOverrides & LayoutColorProperty; label: string }> =
    this.layoutColorFields.filter((field): field is { key: keyof WidgetColorOverrides & LayoutColorProperty; label: string } =>
      field.key !== 'canvasBackgroundColor');


  // Drag state
  private dragStartPos = { x: 0, y: 0 };
  private dragStartWidget = { x: 0, y: 0, w: 0, h: 0 };
  private previewObjectUrl: string | null = null;
  private longPressTimer: any = null;
  private readonly LONG_PRESS_MS = 300;
  private pointerMoved = false;

  /** Effective canvas scale. On phones the page scrolls vertically, so fit uses width only. */
  canvasScale = computed(() => {
    const { width, height } = this.canvasViewportSize();
    const layout = this.layout();
    return resolveCanvasScale(
      this.canvasZoom(),
      { width, height: this.isMobile() ? 0 : height },
      { width: layout.width, height: layout.height },
    );
  });
  canvasZoomLabel = computed(() => `${Math.round(this.canvasScale() * 100)}%`);

  constructor() {
    // Scroll lock when mobile overlays are open
    effect(() => {
      const lock = this.usesDrawer() && (this.pickerOpen() || this.inspectorDrawerOpen() || this.layersDrawerOpen());
      document.body.style.overflow = lock ? 'hidden' : '';
    });

    // Track the visible canvas area so fit-to-view follows panel, window and orientation changes.
    effect(onCleanup => {
      const element = this.canvasViewport()?.nativeElement;
      if (!element || typeof ResizeObserver === 'undefined') return;
      const observer = new ResizeObserver(entries => {
        const box = entries[entries.length - 1].contentRect;
        this.canvasViewportSize.set({ width: Math.floor(box.width), height: Math.floor(box.height) });
      });
      observer.observe(element);
      this.canvasViewportObserver = observer;
      onCleanup(() => observer.disconnect());
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
      const wasWide = this.viewportWidth() >= LAYERS_PANEL_MIN_WIDTH;
      this.viewportWidth.set(window.innerWidth);
      const isWide = this.viewportWidth() >= LAYERS_PANEL_MIN_WIDTH;
      if (wasWide !== isWide) this.layersOpen.set(isWide);
      // A popover beside the layers panel would be left at a stale position.
      if (this.pickerAnchor() === 'layers') this.pickerOpen.set(false);
      this.isMobile.set(this.checkIsMobile());
      if (!this.usesDrawer()) {
        this.inspectorDrawerOpen.set(false);
        this.layersDrawerOpen.set(false);
      }
    }, 100);
  }

  @HostListener('document:click', ['$event'])
  onDocumentClick(event: Event): void {
    const target = event.target as HTMLElement;
    if (this.mobileOverflowOpen() && !target.closest('.mobile-overflow-menu')) {
      this.mobileOverflowOpen.set(false);
    }
    if (this.layerMenuWidgetId() && !target.closest('.layer-menu')) {
      this.layerMenuWidgetId.set(null);
    }
    if (this.pickerOpen() && !this.usesDrawer() && !target.closest('.picker-popover, [data-opens-picker]')) {
      this.pickerOpen.set(false);
    }
  }

  @HostListener('document:keydown.escape')
  closeOpenDesignerOverlay(): void {
    if (this.mobileOverflowOpen()) this.mobileOverflowOpen.set(false);
    else if (this.pickerOpen()) this.pickerOpen.set(false);
    else if (this.layerMenuWidgetId()) this.layerMenuWidgetId.set(null);
    else if (this.inspectorDrawerOpen()) this.inspectorDrawerOpen.set(false);
    else if (this.layersDrawerOpen()) this.layersDrawerOpen.set(false);
    else if (this.elementEditingWidgetId()) this.elementEditingWidgetId.set(null);
    else if (this.selectedWidget() && !DashboardDesignerComponent.isEditableTarget(document.activeElement)) {
      this.clearSelection();
    }
  }

  /** `A` opens the widget picker, like the Add widget button. */
  @HostListener('document:keydown', ['$event'])
  onDocumentKeydown(event: KeyboardEvent): void {
    if (event.key.toLowerCase() !== 'a' || event.ctrlKey || event.metaKey || event.altKey || event.repeat) return;
    if (DashboardDesignerComponent.isEditableTarget(event.target) || this.isLoading() || !this.dashboard()) return;
    event.preventDefault();
    this.openPicker();
  }

  private static isEditableTarget(target: EventTarget | Element | null): boolean {
    const element = target as HTMLElement | null;
    return !!element?.closest?.('input, textarea, select, [contenteditable="true"]');
  }

  ngOnInit(): void {
    this.isTouchDevice = window.matchMedia('(pointer: coarse)').matches;
    this.viewportWidth.set(window.innerWidth);
    this.layersOpen.set(window.innerWidth >= LAYERS_PANEL_MIN_WIDTH);
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
    this.canvasViewportObserver?.disconnect();
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

  private selectWidget(widget: WidgetConfig): void {
    this.selectedWidget.set(widget);
  }

  /** Returns the inspector to dashboard settings. */
  clearSelection(): void {
    this.selectedWidget.set(null);
    this.elementEditingWidgetId.set(null);
    this.mobileSelectionToolbar.set(false);
  }

  /** Layers are listed top-most first, the reverse of paint order. */
  readonly layerWidgets = computed(() => [...this.layout().widgets].reverse());

  widgetIcon(widget: WidgetConfig): string {
    try {
      return getWidgetDefinition(widget.type).icon;
    } catch {
      return 'fa-square';
    }
  }

  widgetDisplayName(widget: WidgetConfig): string {
    if (widget.titleOverride) return widget.titleOverride;
    try {
      return getWidgetDefinition(widget.type).label;
    } catch {
      return widget.type;
    }
  }

  onLayerSelect(widget: WidgetConfig): void {
    this.selectWidget(widget);
    if (this.usesDrawer()) this.layersDrawerOpen.set(false);
    if (this.isMobile()) this.mobileSelectionToolbar.set(true);
  }

  toggleLayerMenu(widget: WidgetConfig, event: Event): void {
    event.stopPropagation();
    this.layerMenuWidgetId.set(this.layerMenuWidgetId() === widget.id ? null : widget.id);
  }

  duplicateWidget(widget: WidgetConfig): void {
    this.layerMenuWidgetId.set(null);
    const layout = this.layout();
    const slot = findFreeSlot(layout.widgets, layout.gridCols, layout.gridRows, widget.position.w, widget.position.h);
    if (!slot) {
      this.toastService.show('No free space of the same size for a copy of this widget', 'error');
      return;
    }
    const copy: WidgetConfig = { ...structuredClone(widget), id: this.generateId(), position: slot };
    this.layout.update(value => ({ ...value, widgets: [...value.widgets, copy] }));
    this.selectWidget(copy);
  }

  moveWidgetLayer(widget: WidgetConfig, direction: 'forward' | 'backward'): void {
    this.layerMenuWidgetId.set(null);
    this.layout.update(value => {
      const widgets = moveWidgetInPaintOrder(value.widgets, widget.id, direction);
      return widgets === value.widgets ? value : { ...value, widgets: [...widgets] };
    });
  }

  canMoveWidgetLayer(widget: WidgetConfig, direction: 'forward' | 'backward'): boolean {
    const widgets = this.layout().widgets;
    const index = widgets.findIndex(item => item.id === widget.id);
    return direction === 'forward' ? index < widgets.length - 1 : index > 0;
  }

  openPicker(): void {
    this.mobileOverflowOpen.set(false);
    this.layerMenuWidgetId.set(null);
    this.pickerAnchor.set('toolbar');
    this.pickerOpen.set(true);
  }

  togglePicker(): void {
    if (this.pickerOpen() && this.pickerAnchor() === 'toolbar') this.pickerOpen.set(false);
    else this.openPicker();
  }

  /**
   * Opens the picker next to the layers panel, aligned with the button that opened it and kept
   * inside the window. Drawers (tablets, phones) use the bottom sheet instead.
   */
  openPickerBesideLayers(event: Event): void {
    const panel = (event.currentTarget as HTMLElement).closest('.designer-panel-left');
    if (this.usesDrawer() || !panel) {
      this.openPicker();
      return;
    }
    const margin = 8;
    const panelRect = panel.getBoundingClientRect();
    const triggerRect = (event.currentTarget as HTMLElement).getBoundingClientRect();
    const popoverHeight = Math.min(window.innerHeight * 0.7, 544);
    const top = Math.max(margin, Math.min(triggerRect.top, window.innerHeight - popoverHeight - margin));
    this.openPicker();
    this.pickerAnchor.set('layers');
    this.pickerPosition.set({ top, left: panelRect.right + margin });
  }

  openInspector(): void {
    this.mobileSelectionToolbar.set(false);
    this.layersDrawerOpen.set(false);
    this.inspectorDrawerOpen.set(true);
  }

  toggleLayers(): void {
    if (this.usesDrawer()) {
      this.inspectorDrawerOpen.set(false);
      this.layersDrawerOpen.set(!this.layersDrawerOpen());
    } else {
      this.layersOpen.set(!this.layersOpen());
    }
  }

  private rememberWidgetUse(type: WidgetType): void {
    const recent = rememberRecentWidget(this.recentWidgetTypes(), type);
    this.recentWidgetTypes.set(recent);
    try {
      localStorage.setItem(RECENT_WIDGETS_STORAGE_KEY, JSON.stringify(recent));
    } catch {
      // Recent widgets are a convenience; ignore unavailable storage.
    }
  }

  private static readRecentWidgets(): WidgetType[] {
    try {
      const value = JSON.parse(localStorage.getItem(RECENT_WIDGETS_STORAGE_KEY) ?? '[]');
      return Array.isArray(value) ? value.filter((item): item is WidgetType => typeof item === 'string') : [];
    } catch {
      return [];
    }
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
    this.layerMenuWidgetId.set(null);
    if (this.selectedWidget()?.id === widget.id) this.clearSelection();

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

  /**
   * Mouse press on a picker item: dragging onto the canvas places the widget there, while a
   * press without movement places it in the first free slot.
   */
  onPickerDragStart({ event, widget }: WidgetPickerDragStart): void {
    this.pickerOpen.set(false);
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

    const movePreview = (e: PointerEvent) => {
      preview.style.left = e.clientX + 8 + 'px';
      preview.style.top = e.clientY + 8 + 'px';
    };
    movePreview(event);
    let moved = false;

    const onPointerMove = (e: PointerEvent) => {
      movePreview(e);
      if (Math.abs(e.clientX - event.clientX) > 4 || Math.abs(e.clientY - event.clientY) > 4) moved = true;

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
      preview.remove();

      const g = this.ghost();
      this.ghost.set(null);
      if (!moved) {
        this.addWidgetToFirstSlot(widget);
      } else if (g) {
        const newWidget: WidgetConfig = {
          id: this.generateId(),
          type: widget.type,
          position: { ...g.position },
          config: createDefaultWidgetConfig(widget.type)
        };
        this.layout.update(l => ({ ...l, widgets: [...l.widgets, newWidget] }));
        this.selectWidget(newWidget);
        this.rememberWidgetUse(widget.type);
      }
    };

    this.trackPointer(event, onPointerMove, onPointerUp);
  }

  /** Adds a widget at the first free grid position (picker click, keyboard and touch). */
  addWidgetToFirstSlot(widget: WidgetDefinition): void {
    const layout = this.layout();
    const { w, h } = getWidgetDefaultSize(widget.type, layout.gridCols, layout.gridRows);
    const position = findFreeSlot(layout.widgets, layout.gridCols, layout.gridRows, w, h);
    this.pickerOpen.set(false);
    if (!position) {
      this.toastService.show('No space available on the canvas for a new widget', 'error');
      return;
    }

    const newWidget: WidgetConfig = {
      id: this.generateId(),
      type: widget.type,
      position,
      config: createDefaultWidgetConfig(widget.type)
    };
    this.layout.update(l => ({ ...l, widgets: [...l.widgets, newWidget] }));
    this.selectWidget(newWidget);
    this.rememberWidgetUse(widget.type);
    // New widgets usually need a data source, so show their settings straight away on small screens.
    if (this.usesDrawer()) this.openInspector();
  }

  onWidgetPointerDown(event: PointerEvent, widget: WidgetConfig): void {
    if (event.button !== 0) return;
    event.stopPropagation();
    if (this.elementEditingWidgetId() === widget.id) return;
    if (this.elementEditingWidgetId()) this.elementEditingWidgetId.set(null);
    this.selectWidget(widget);

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

    // On touch, an already selected widget drags immediately (its overlay disables panning);
    // other widgets need a long press so a swipe across the canvas still scrolls the page.
    const isTouch = event.pointerType === 'touch';
    if (isTouch && target.closest('.sel-overlay')) {
      this.startDrag(event, widget);
      return;
    }
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
      this.selectWidget(widget);
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
  }

  /** Renderer geometry for the selection, only while the preview matches the current layout. */
  readonly selectedWidgetGeometry = computed<WidgetRenderGeometry | undefined>(() => {
    if (this.renderedCanvasLoading() || this.renderedLayoutSignature() !== this.renderLayoutSignature()) return undefined;
    const selected = this.selectedWidget();
    return selected ? this.renderedWidgetGeometry()[selected.id] : undefined;
  });

  selectedWidgetSupportsElementEditing(): boolean {
    const selected = this.selectedWidget();
    return !!selected && !!this.renderedWidgetGeometry()[selected.id]?.editable;
  }


  /** Readable name for a source in diagnostics; opaque connection references are never shown. */
  private sourceLabel(sourceId: string): string {
    const source = this.availableEntities().find(entity => entity.entityId === sourceId);
    if (source) return source.connectionName ? `${source.connectionName} · ${source.friendlyName || sourceId}` : source.friendlyName || sourceId;
    return sourceId.startsWith('source:') ? 'Unavailable source' : sourceId;
  }
  readonly selectedWidgetSourceStatuses = computed(() => this.buildSelectedWidgetSourceStatuses());

  private buildSelectedWidgetSourceStatuses(): Array<{ key: string; label: string; status: DataSourceStatus }> {
    const widget = this.selectedWidget();
    if (!widget) return [];
    const config = widget.config as any;
    const entityId = config?.entityId as string | undefined;
    const sources: Array<{ key: string; label: string }> = [{ key: `widget:${widget.id}`, label: 'Widget data' }];

    switch (getWidgetRenderType(widget.type)) {
      case 'header':
        for (const badge of config?.badges ?? []) {
          if (badge.entityId) sources.push({ key: `entity:${badge.entityId}`, label: this.sourceLabel(badge.entityId) });
        }
        break;
      case 'weather':
        if (entityId) sources.push({ key: `entity:${entityId}`, label: this.sourceLabel(entityId) });
        break;
      case 'weather-forecast':
        if (entityId) {
          const mode = config?.forecastMode === 'hourly' ? 'hourly' : 'daily';
          sources.push({ key: `entity:${entityId}`, label: this.sourceLabel(entityId) });
          sources.push({ key: `forecast:${entityId}:${mode}`, label: `${mode} forecast` });
        }
        break;
      case 'todo':
        if (entityId) sources.push({ key: `todo:${entityId}`, label: this.sourceLabel(entityId) });
        break;
      case 'calendar':
        if (entityId) sources.push({ key: `calendar:${entityId}`, label: this.sourceLabel(entityId) });
        break;
      case 'rss-feed':
        if (entityId) sources.push({ key: `rss:${entityId}`, label: this.sourceLabel(entityId) });
        break;
      case 'graph':
        for (const series of config?.series ?? []) {
          if (series.entityId) sources.push({ key: `history:${series.entityId}`, label: series.label || this.sourceLabel(series.entityId) });
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

  /**
   * Follows the pointer that started a gesture until it is released or cancelled. While the
   * gesture runs, touch scrolling is suppressed so the browser does not pan instead of dragging.
   */
  private trackPointer(start: PointerEvent, onMove: (event: PointerEvent) => void, onEnd: () => void): void {
    const pointerId = start.pointerId;
    const blockScroll = (event: TouchEvent) => event.preventDefault();
    const move = (event: PointerEvent) => {
      if (event.pointerId === pointerId) onMove(event);
    };
    const end = (event: PointerEvent) => {
      if (event.pointerId !== pointerId) return;
      document.removeEventListener('pointermove', move);
      document.removeEventListener('pointerup', end);
      document.removeEventListener('pointercancel', end);
      document.removeEventListener('touchmove', blockScroll);
      onEnd();
    };
    document.addEventListener('pointermove', move);
    document.addEventListener('pointerup', end);
    document.addEventListener('pointercancel', end);
    document.addEventListener('touchmove', blockScroll, { passive: false });
  }

  private startDrag(event: PointerEvent, widget: WidgetConfig): void {
    let isDragging = true;
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

    const onPointerMove = (e: PointerEvent) => {
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
    };

    this.trackPointer(event, onPointerMove, onPointerUp);
  }

  private startResize(event: PointerEvent, widget: WidgetConfig, direction: 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw'): void {
    event.stopPropagation();
    let isResizing = true;
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

    const onPointerMove = (e: PointerEvent) => {
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
    };

    this.trackPointer(event, onPointerMove, onPointerUp);
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
    this.clearSelection();
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
    const previousOrientation = this.orientation();
    if (newOrientation === previousOrientation) return;
    this.orientation.set(newOrientation);

    const size = this.sizePresets[this.selectedSizeIndex] ?? DEFAULT_DASHBOARD_SIZE;
    const isPortrait = newOrientation === 'Portrait';
    const previous = this.layout();
    this.layout.set({
      ...previous,
      width: isPortrait ? size.height : size.width,
      height: isPortrait ? size.width : size.height,
    });
    // Rotating swaps the grid; widgets are fitted into it like any other grid change.
    this.applyGridSize(previous.gridRows, previous.gridCols, () => {
      this.orientation.set(previousOrientation);
      this.layout.set(previous);
    });
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

  /**
   * Number inputs commit on change (blur, Enter, spinner) rather than per keystroke, so typing
   * "16" never applies an intermediate 1. The clamped value is written back to the field.
   */
  private readNumberInput(event: Event, min: number, max: number, fallback: number): number {
    const input = event.target as HTMLInputElement;
    const value = clampWhole(input.value, min, max, fallback);
    input.value = String(value);
    return value;
  }

  updateLayoutGridCols(event: Event): void {
    const layout = this.layout();
    this.applyGridSize(this.readNumberInput(event, GRID_LIMITS.min, GRID_LIMITS.max, layout.gridCols), layout.gridRows);
  }

  updateLayoutGridRows(event: Event): void {
    const layout = this.layout();
    this.applyGridSize(layout.gridCols, this.readNumberInput(event, GRID_LIMITS.min, GRID_LIMITS.max, layout.gridRows));
  }

  /** Changes the grid and fits widgets back inside it, offering undo when any had to move. */
  private applyGridSize(gridCols: number, gridRows: number, undo?: () => void): void {
    const previous = this.layout();
    if (gridCols === previous.gridCols && gridRows === previous.gridRows) return;
    const fitted = fitWidgetsToGrid(previous.widgets, gridCols, gridRows);
    this.layout.set({ ...previous, gridCols, gridRows, widgets: fitted.widgets });
    this.refreshSelectedWidget();
    if (fitted.changed === 0) return;
    this.toastService.showWithAction(
      fitted.changed === 1 ? '1 widget was moved or resized to fit the grid' : `${fitted.changed} widgets were moved or resized to fit the grid`,
      'Undo',
      () => {
        if (undo) undo();
        else this.layout.set(previous);
        this.refreshSelectedWidget();
      },
      'info',
      6000,
    );
  }

  /** Re-reads the selected widget from the layout after a bulk change. */
  private refreshSelectedWidget(): void {
    const selected = this.selectedWidget();
    if (!selected) return;
    const current = this.layout().widgets.find(widget => widget.id === selected.id) ?? null;
    if (current !== selected) this.selectedWidget.set(current);
  }

  /** Position and size fields in the inspector; columns and rows are shown 1-based. */
  updateSelectedWidgetPosition(field: 'x' | 'y' | 'w' | 'h', event: Event): void {
    const widget = this.selectedWidget();
    if (!widget) return;
    const layout = this.layout();
    const isColumnAxis = field === 'x' || field === 'w';
    const limit = isColumnAxis ? layout.gridCols : layout.gridRows;
    const oneBased = field === 'x' || field === 'y';
    const current = widget.position[field] + (oneBased ? 1 : 0);
    const value = this.readNumberInput(event, 1, limit, current) - (oneBased ? 1 : 0);
    const position = fitPositionToGrid({ ...widget.position, [field]: value }, layout.gridCols, layout.gridRows);
    (event.target as HTMLInputElement).value = String(position[field] + (oneBased ? 1 : 0));
    const updated = { ...widget, position };
    this.layout.update(value => ({ ...value, widgets: value.widgets.map(item => item.id === widget.id ? updated : item) }));
    this.selectedWidget.set(updated);
  }

  updateCanvasPadding(event: Event): void {
    const canvasPadding = this.readNumberInput(event, 0, 128, this.layout().canvasPadding ?? 0);
    this.layout.update(layout => ({ ...layout, canvasPadding }));
  }

  updateWidgetGap(event: Event): void {
    const widgetGap = this.readNumberInput(event, 0, 64, this.layout().widgetGap ?? 0);
    this.layout.update(layout => ({ ...layout, widgetGap }));
  }

  updateWidgetBorder(event: Event): void {
    const widgetBorder = this.readNumberInput(event, 0, 20, this.layout().widgetBorder ?? 0);
    this.layout.update(layout => ({ ...layout, widgetBorder }));
  }

  updateWidgetPadding(event: Event): void {
    const widgetPadding = this.readNumberInput(event, 0, 32, this.layout().widgetPadding ?? 0);
    this.layout.update(layout => ({ ...layout, widgetPadding }));
  }

  updateTitleFontSize(event: Event): void {
    const titleFontSize = this.readNumberInput(event, 8, 72, this.layout().titleFontSize ?? 16);
    this.layout.update(layout => ({ ...layout, titleFontSize }));
  }

  updateTextFontSize(event: Event): void {
    const textFontSize = this.readNumberInput(event, 6, 48, this.layout().textFontSize ?? 14);
    this.layout.update(layout => ({ ...layout, textFontSize }));
  }

  updateTitleFontWeight(fontWeight: number | string): void {
    const weight = typeof fontWeight === 'string' ? parseInt(fontWeight, 10) : fontWeight;
    this.layout.update(layout => ({ ...layout, titleFontWeight: this.normalizeFontWeight(weight) }));
  }

  updateTextFontWeight(fontWeight: number | string): void {
    const weight = typeof fontWeight === 'string' ? parseInt(fontWeight, 10) : fontWeight;
    this.layout.update(layout => ({ ...layout, textFontWeight: this.normalizeFontWeight(weight) }));
  }

  updateLayoutColor(property: LayoutColorProperty, color: string): void {
    this.layout.update(layout => ({
      ...layout,
      colorScheme: { ...layout.colorScheme, [property]: color }
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
  isLayoutColorOverridden(prop: LayoutColorProperty): boolean {
    return this.layout().colorScheme[prop] !== this.getBaseScheme()[prop];
  }

  // Helper method to check if layout has any color overrides vs. the selected base scheme
  hasLayoutColorOverrides(): boolean {
    return this.layoutColorFields.some(field => this.isLayoutColorOverridden(field.key));
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
    return this.widgetColorFields.some(field => this.isWidgetColorOverridden(widget, field.key));
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
    const refreshData = this.forceRefreshOnNextRender;
    const renderLayout = this.renderLayout();
    const renderLayoutSignature = this.renderLayoutSignature();
    // Selecting, saving or no-op drags re-emit an identical layout; the preview already shows it.
    if (!refreshData && renderLayoutSignature === this.requestedLayoutSignature) return;

    const revision = ++this.renderPreviewRevision;
    this.requestedLayoutSignature = renderLayoutSignature;
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
          this.renderedLayoutSignature.set(renderLayoutSignature);
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
          this.requestedLayoutSignature = '';
        },
      });
  }

  /** Complete transient layout sent to the renderer, including generated widgets. */
  private readonly renderLayout = computed<DashboardLayout>(() => {
    const layout = this.layout();
    const generatedWidgets = this.aiEnabled() ? this.aiGeneratedWidgets() : [];
    return generatedWidgets.length === 0
      ? layout
      : { ...layout, widgets: [...layout.widgets, ...generatedWidgets] };
  });
  private readonly renderLayoutSignature = computed(() => JSON.stringify(this.renderLayout()));

  // Template styles are memoised so change detection during drags does not rebuild them.
  readonly canvasStyle = computed(() => this.buildCanvasStyle());
  readonly canvasScaleStyle = computed(() => this.buildCanvasScaleStyle());
  readonly gridOverlayStyle = computed(() => this.buildGridOverlayStyle());
  readonly selectionOverlayStyle = computed(() => this.buildSelectionOverlayStyle());
  readonly widgetStyles = computed(() => {
    const styles: Record<string, Record<string, unknown>> = {};
    for (const widget of [...this.layout().widgets, ...this.aiGeneratedWidgets()]) {
      styles[widget.id] = this.buildWidgetStyle(widget);
    }
    return styles;
  });

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

  private buildCanvasStyle(): any {
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
      transform: this.canvasScale() === 1 ? 'none' : `scale(${this.canvasScale()})`,
      transformOrigin: '0 0',
    };
  }

  private buildGridOverlayStyle(): any {
    const layout = this.layout();
    const padding = layout.canvasPadding ?? 0;
    const gap = layout.widgetGap ?? 0;
    const cols = Math.max(1, layout.gridCols);
    const rows = Math.max(1, layout.gridRows);
    // The overlay lives inside the scaled canvas, so it is laid out in unscaled device pixels.
    const totalWidth = layout.width;
    const totalHeight = layout.height;

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

  private buildWidgetStyle(widget: WidgetConfig): any {
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

  private buildSelectionOverlayStyle(): any {
    const widget = this.selectedWidget();
    if (!widget) return { display: 'none' };
    const editingElements = this.elementEditingWidgetId() === widget.id;
    return {
      ...this.widgetRect(widget),
      pointerEvents: editingElements ? 'none' : 'auto',
      zIndex: 100,
    };
  }

  /** Outline for the widget hovered in the layers list. */
  readonly hoverOverlayStyle = computed(() => {
    const id = this.hoveredWidgetId();
    const widget = id && id !== this.selectedWidget()?.id ? this.layout().widgets.find(item => item.id === id) : undefined;
    return widget ? { ...this.widgetRect(widget), pointerEvents: 'none', zIndex: 95 } : null;
  });

  /** Widget rectangle in unscaled canvas pixels, preferring renderer-resolved bounds. */
  private widgetRect(widget: WidgetConfig): Record<string, string> {
    const layout = this.layout();
    const padding = layout.canvasPadding ?? 0;
    const gap     = layout.widgetGap ?? 0;
    const cols    = layout.gridCols;
    const rows    = layout.gridRows;

    // Laid out in unscaled device pixels inside the (possibly scaled) canvas.
    const totalW = layout.width;
    const totalH = layout.height;

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

    return {
      position: 'absolute',
      left:   `${left}px`,
      top:    `${top}px`,
      width:  `${width}px`,
      height: `${height}px`,
      boxSizing: 'border-box',
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



  /** The wrapper occupies the scaled footprint so scrolling and centring see the visual size. */
  private buildCanvasScaleStyle(): any {
    const scale = this.canvasScale();
    const layout = this.layout();
    return {
      width: `${Math.round(layout.width * scale)}px`,
      height: `${Math.round(layout.height * scale)}px`,
    };
  }

  zoomCanvas(direction: 1 | -1): void {
    this.setCanvasZoom(stepCanvasZoom(this.canvasScale(), direction));
  }

  setCanvasZoom(zoom: CanvasZoom): void {
    this.canvasZoom.set(zoom);
    try {
      localStorage.setItem(CANVAS_ZOOM_STORAGE_KEY, String(zoom));
    } catch {
      // Storage may be unavailable (private mode); zoom still applies for this session.
    }
  }

  private static readStoredZoom(): CanvasZoom {
    try {
      return parseCanvasZoom(localStorage.getItem(CANVAS_ZOOM_STORAGE_KEY));
    } catch {
      return 'fit';
    }
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


  onCanvasPointerDown(event: PointerEvent): void {
    const target = event.target as HTMLElement;
    if (target.classList.contains('dashboard-canvas') || target.classList.contains('grid-overlay')) {
      this.clearSelection();
    }
  }

  onBottomSheetTouchStart(event: TouchEvent): void {
    this.swipeStartY = event.touches[0].clientY;
  }

  onBottomSheetTouchEnd(event: TouchEvent): void {
    const deltaY = event.changedTouches[0].clientY - this.swipeStartY;
    if (deltaY > 80) {
      this.pickerOpen.set(false);
    }
  }

  onDrawerTouchStart(event: TouchEvent): void {
    this.swipeStartX = event.touches[0].clientX;
  }

  /** Swiping a drawer back towards its edge closes it. */
  onDrawerTouchEnd(event: TouchEvent, side: 'left' | 'right'): void {
    const deltaX = event.changedTouches[0].clientX - this.swipeStartX;
    if (side === 'right' && deltaX > 80) this.inspectorDrawerOpen.set(false);
    if (side === 'left' && deltaX < -80) this.layersDrawerOpen.set(false);
  }

  needsSource(widget: WidgetConfig): boolean {
    return widgetNeedsDataSource(widget);
  }

  readonly selectedWidgetNeedsSource = computed(() => {
    const widget = this.selectedWidget();
    return !!widget && widgetNeedsDataSource(widget);
  });
}
