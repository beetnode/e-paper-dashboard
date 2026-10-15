import {
  ChangeDetectorRef,
  Component,
  ElementRef,
  EventEmitter,
  Input,
  OnChanges,
  OnDestroy,
  Output,
  SimpleChanges,
} from '@angular/core';
import { CommonModule } from '@angular/common';
import {
  EditableWidgetElementGeometry,
  RenderRectangle,
  WidgetRenderGeometry,
} from '../../services/dashboard-render-preview.service';
import { EditableElementChange } from '../../models/widget-element-layout';

type ResizeHandle = 'n' | 's' | 'e' | 'w' | 'ne' | 'nw' | 'se' | 'sw';
type Guide = { orientation: 'horizontal' | 'vertical'; position: number };

interface PointerInteraction {
  type: 'drag' | 'resize';
  element: EditableWidgetElementGeometry;
  handle?: ResizeHandle;
  pointerId: number;
  startClientX: number;
  startClientY: number;
  startPosition: RenderRectangle;
  captureTarget: HTMLElement | null;
}

@Component({
  selector: 'app-widget-element-overlay',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './widget-element-overlay.component.html',
  styleUrls: ['./widget-element-overlay.component.scss'],
})
export class WidgetElementOverlayComponent implements OnChanges, OnDestroy {
  @Input({ required: true }) geometry!: WidgetRenderGeometry;
  @Input() snapStep = 2;
  @Input() showGuides = true;
  @Input() previewImageUrl = '';
  @Input() previewWidth = 0;
  @Input() previewHeight = 0;
  /** Widget background, painted over element pixels the image still shows at a previous spot. */
  @Input() maskColor = '';
  @Output() elementChange = new EventEmitter<EditableElementChange>();

  readonly handles: readonly ResizeHandle[] = ['n', 's', 'e', 'w', 'nw', 'ne', 'sw', 'se'];
  selectedId: string | null = null;
  guides: Guide[] = [];

  private positions = new Map<string, RenderRectangle>();
  private interaction: PointerInteraction | null = null;
  private readonly pointerMove = (event: PointerEvent) => this.onPointerMove(event);
  private readonly pointerUp = (event: PointerEvent) => this.onPointerUp(event);
  private readonly keyDown = (event: KeyboardEvent) => {
    if (event.key === 'Escape') this.cancelInteraction();
  };
  private readonly windowBlur = () => this.cancelInteraction();

  constructor(
    private readonly host: ElementRef<HTMLElement>,
    private readonly cdr: ChangeDetectorRef,
  ) {}

  ngOnChanges(changes: SimpleChanges): void {
    if (changes['geometry'] && !this.interaction) {
      this.positions = new Map(
        (this.geometry?.elements ?? []).map(element => [element.id, { ...element.position }]),
      );
      if (this.selectedId && !this.positions.has(this.selectedId)) this.selectedId = null;
    }
  }

  ngOnDestroy(): void {
    this.detachListeners();
  }

  elementStyle(element: EditableWidgetElementGeometry): Record<string, string> {
    const position = this.getPosition(element);
    const content = this.geometry.contentBounds;
    const widget = this.geometry.bounds;
    return {
      left: `${content.x - widget.x + position.x / 100 * content.width}px`,
      top: `${content.y - widget.y + position.y / 100 * content.height}px`,
      width: `${position.width / 100 * content.width}px`,
      height: `${position.height / 100 * content.height}px`,
    };
  }

  guideStyle(guide: Guide): Record<string, string> {
    const content = this.geometry.contentBounds;
    const widget = this.geometry.bounds;
    if (guide.orientation === 'vertical') {
      return {
        left: `${content.x - widget.x + guide.position / 100 * content.width}px`,
        top: `${content.y - widget.y}px`,
        height: `${content.height}px`,
      };
    }
    return {
      left: `${content.x - widget.x}px`,
      top: `${content.y - widget.y + guide.position / 100 * content.height}px`,
      width: `${content.width}px`,
    };
  }

  elementLabel(element: EditableWidgetElementGeometry): string {
    if (element.label) return element.label;
    if (element.kind === 'title') return 'Title';
    if (element.kind === 'badge') return `Badge ${(element.index ?? 0) + 1}`;
    if (element.kind === 'weather-item') return `Weather item ${(element.index ?? 0) + 1}`;
    return element.kind;
  }

  isInteracting(element: EditableWidgetElementGeometry): boolean {
    return this.interaction?.element.id === element.id;
  }

  /** True while the element sits somewhere other than where the preview image drew it. */
  isMoved(element: EditableWidgetElementGeometry): boolean {
    return this.hasPositionChanged(element.position, this.getPosition(element));
  }

  /**
   * Elements whose rendered pixels are hidden until the next render. Once any element has moved,
   * every element is drawn from the image at its current spot, so hiding a vacated area never
   * erases a neighbour that overlaps it.
   */
  get movedElements(): EditableWidgetElementGeometry[] {
    return this.previewImageUrl ? this.geometry.elements.filter(element => this.isMoved(element)) : [];
  }

  showsImage(element: EditableWidgetElementGeometry, anyMoved: boolean): boolean {
    return !!this.previewImageUrl && (anyMoved || this.isInteracting(element));
  }

  vacatedStyle(element: EditableWidgetElementGeometry): Record<string, string> {
    const widget = this.geometry.bounds;
    return {
      left: `${element.bounds.x - widget.x}px`,
      top: `${element.bounds.y - widget.y}px`,
      width: `${element.bounds.width}px`,
      height: `${element.bounds.height}px`,
      backgroundColor: this.maskColor || 'transparent',
    };
  }

  ghostStyle(element: EditableWidgetElementGeometry): Record<string, string> {
    return {
      backgroundImage: this.previewImageUrl ? `url("${this.previewImageUrl}")` : 'none',
      backgroundSize: `${this.previewWidth}px ${this.previewHeight}px`,
      backgroundPosition: `${-element.bounds.x}px ${-element.bounds.y}px`,
    };
  }

  onElementPointerDown(event: PointerEvent, element: EditableWidgetElementGeometry): void {
    if (event.button !== 0 || !element.movable) return;
    event.preventDefault();
    event.stopPropagation();
    this.selectedId = element.id;
    this.beginInteraction(event, element, 'drag');
  }

  onHandlePointerDown(
    event: PointerEvent,
    element: EditableWidgetElementGeometry,
    handle: ResizeHandle,
  ): void {
    if (!element.resizable) return;
    event.preventDefault();
    event.stopPropagation();
    this.selectedId = element.id;
    this.beginInteraction(event, element, 'resize', handle);
  }

  onElementKeyDown(event: KeyboardEvent, element: EditableWidgetElementGeometry): void {
    if (!element.movable || !['ArrowLeft', 'ArrowRight', 'ArrowUp', 'ArrowDown'].includes(event.key)) return;
    event.preventDefault();
    event.stopPropagation();
    this.selectedId = element.id;
    const current = this.getPosition(element);
    const amount = event.altKey ? 0.5 : Math.max(this.snapStep, 1) * (event.shiftKey ? 5 : 1);
    const next = {
      ...current,
      x: Math.max(0, Math.min(100 - current.width,
        current.x + (event.key === 'ArrowLeft' ? -amount : event.key === 'ArrowRight' ? amount : 0))),
      y: Math.max(0, Math.min(100 - current.height,
        current.y + (event.key === 'ArrowUp' ? -amount : event.key === 'ArrowDown' ? amount : 0))),
    };
    this.positions.set(element.id, next);
    this.elementChange.emit({ element, position: next });
    this.cdr.markForCheck();
  }

  private beginInteraction(
    event: PointerEvent,
    element: EditableWidgetElementGeometry,
    type: 'drag' | 'resize',
    handle?: ResizeHandle,
  ): void {
    this.interaction = {
      type,
      element,
      handle,
      pointerId: event.pointerId,
      startClientX: event.clientX,
      startClientY: event.clientY,
      startPosition: { ...this.getPosition(element) },
      captureTarget: event.currentTarget instanceof HTMLElement ? event.currentTarget : null,
    };
    this.interaction.captureTarget?.setPointerCapture?.(event.pointerId);
    document.addEventListener('pointermove', this.pointerMove);
    document.addEventListener('pointerup', this.pointerUp);
    document.addEventListener('pointercancel', this.pointerUp);
    document.addEventListener('keydown', this.keyDown);
    window.addEventListener('blur', this.windowBlur);
    this.cdr.markForCheck();
  }

  private onPointerMove(event: PointerEvent): void {
    const interaction = this.interaction;
    if (!interaction || interaction.pointerId !== event.pointerId) return;
    event.preventDefault();

    const hostRect = this.host.nativeElement.getBoundingClientRect();
    const contentBounds = this.geometry.contentBounds;
    if (hostRect.width <= 0 || hostRect.height <= 0 ||
        contentBounds.width <= 0 || contentBounds.height <= 0) return;
    const scaleX = this.geometry.bounds.width / hostRect.width;
    const scaleY = this.geometry.bounds.height / hostRect.height;
    const dx = (event.clientX - interaction.startClientX) * scaleX
      / contentBounds.width * 100;
    const dy = (event.clientY - interaction.startClientY) * scaleY
      / contentBounds.height * 100;
    const start = interaction.startPosition;

    const next = interaction.type === 'drag'
      ? this.movePosition(interaction.element.id, start, dx, dy)
      : this.resizePosition(start, dx, dy, interaction.handle!);
    this.positions.set(interaction.element.id, next);
    this.cdr.detectChanges();
  }

  private onPointerUp(event: PointerEvent): void {
    const interaction = this.interaction;
    if (!interaction || interaction.pointerId !== event.pointerId) return;
    const position = { ...this.getPosition(interaction.element) };
    if (interaction.captureTarget?.hasPointerCapture?.(interaction.pointerId)) {
      interaction.captureTarget.releasePointerCapture(interaction.pointerId);
    }
    this.interaction = null;
    this.guides = [];
    this.detachListeners();
    if (this.hasPositionChanged(interaction.startPosition, position)) {
      this.elementChange.emit({ element: interaction.element, position });
    }
    this.cdr.markForCheck();
  }

  private movePosition(
    id: string,
    start: RenderRectangle,
    dx: number,
    dy: number,
  ): RenderRectangle {
    let x = this.snap(start.x + dx);
    let y = this.snap(start.y + dy);
    this.guides = [];

    if (this.showGuides) {
      const snapped = this.snapToElements(id, x, y, start.width, start.height);
      x = snapped.x;
      y = snapped.y;
    }
    return {
      ...start,
      x: Math.max(0, Math.min(100 - start.width, x)),
      y: Math.max(0, Math.min(100 - start.height, y)),
    };
  }

  private resizePosition(
    start: RenderRectangle,
    dx: number,
    dy: number,
    handle: ResizeHandle,
  ): RenderRectangle {
    const minimum = Math.max(this.snapStep, 4);
    let { x, y, width, height } = start;
    if (handle.includes('e')) width = Math.max(minimum, start.width + dx);
    if (handle.includes('s')) height = Math.max(minimum, start.height + dy);
    if (handle.includes('w')) {
      width = Math.max(minimum, start.width - dx);
      x = start.x + start.width - width;
    }
    if (handle.includes('n')) {
      height = Math.max(minimum, start.height - dy);
      y = start.y + start.height - height;
    }

    x = Math.max(0, this.snap(x));
    y = Math.max(0, this.snap(y));
    width = Math.min(100 - x, Math.max(minimum, this.snap(width)));
    height = Math.min(100 - y, Math.max(minimum, this.snap(height)));
    this.guides = [];
    return { x, y, width, height };
  }

  private snapToElements(
    id: string,
    x: number,
    y: number,
    width: number,
    height: number,
  ): { x: number; y: number } {
    const threshold = Math.max(this.snapStep, 2);
    let bestX: { distance: number; value: number; guide: number } | null = null;
    let bestY: { distance: number; value: number; guide: number } | null = null;

    for (const element of this.geometry.elements) {
      if (element.id === id) continue;
      const other = this.getPosition(element);
      const movingX = [x, x + width / 2, x + width];
      const otherX = [other.x, other.x + other.width / 2, other.x + other.width];
      for (const source of movingX) {
        for (const target of otherX) {
          if (Math.abs(source - target) < threshold) {
            const distance = Math.abs(source - target);
            if (!bestX || distance < bestX.distance) {
              bestX = { distance, value: x + target - source, guide: target };
            }
          }
        }
      }
      const movingY = [y, y + height / 2, y + height];
      const otherY = [other.y, other.y + other.height / 2, other.y + other.height];
      for (const source of movingY) {
        for (const target of otherY) {
          if (Math.abs(source - target) < threshold) {
            const distance = Math.abs(source - target);
            if (!bestY || distance < bestY.distance) {
              bestY = { distance, value: y + target - source, guide: target };
            }
          }
        }
      }
    }

    this.guides = [];
    if (bestX) this.guides.push({ orientation: 'vertical', position: bestX.guide });
    if (bestY) this.guides.push({ orientation: 'horizontal', position: bestY.guide });
    return { x: bestX?.value ?? x, y: bestY?.value ?? y };
  }

  private snap(value: number): number {
    return this.snapStep > 0 ? Math.round(value / this.snapStep) * this.snapStep : value;
  }

  private getPosition(element: EditableWidgetElementGeometry): RenderRectangle {
    return this.positions.get(element.id) ?? element.position;
  }

  private hasPositionChanged(first: RenderRectangle, second: RenderRectangle): boolean {
    return first.x !== second.x
      || first.y !== second.y
      || first.width !== second.width
      || first.height !== second.height;
  }

  private detachListeners(): void {
    document.removeEventListener('pointermove', this.pointerMove);
    document.removeEventListener('pointerup', this.pointerUp);
    document.removeEventListener('pointercancel', this.pointerUp);
    document.removeEventListener('keydown', this.keyDown);
    window.removeEventListener('blur', this.windowBlur);
  }

  private cancelInteraction(): void {
    const interaction = this.interaction;
    if (!interaction) return;
    this.positions.set(interaction.element.id, { ...interaction.startPosition });
    if (interaction.captureTarget?.hasPointerCapture?.(interaction.pointerId)) {
      interaction.captureTarget.releasePointerCapture(interaction.pointerId);
    }
    this.interaction = null;
    this.guides = [];
    this.detachListeners();
    this.cdr.markForCheck();
  }
}
