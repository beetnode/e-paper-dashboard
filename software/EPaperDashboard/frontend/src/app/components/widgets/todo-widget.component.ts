import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import type { TodoItem } from '../../services/todo.service';
import { WidgetConfig, ColorScheme, EntityState, TodoConfig, DashboardLayout } from '../../models/types';
import { resolveWidgetRenderContext } from './widget-render-context';

@Component({
  selector: 'app-widget-todo',
  standalone: true,
  imports: [CommonModule],
  styleUrls: ['./todo-widget.component.scss'],
  template: `
    <div 
      class="todo-widget" 
      [style.--headerFontSize]="getHeaderFontSize() + 'px'"
      [style.--itemFontSize]="getItemFontSize() + 'px'"
      [style.--smallFontSize]="getSmallFontSize() + 'px'"
      [style.--headerFontWeight]="getHeaderFontWeight()"
      [style.--itemFontWeight]="getItemFontWeight()"
      [style.--iconColor]="getIconColor()"
      [style.--titleColor]="getTitleColor()"
      [style.--textColor]="getTextColor()"
      [style.--widget-title-font-size]="getHeaderFontSize() + 'px'"
      [style.--widget-title-font-weight]="getHeaderFontWeight()"
      [style.--widget-title-color]="getTitleColor()"
      [style.color]="getTextColor()">
      @if (!isDataFetched()) {
        <div class="preview-state">
          <i class="fa fa-list-check"></i>
          <p>Tasks</p>
        </div>
      }
      @if (isDataFetched()) {
        <div class="todo-content">
          @if (widget.position.w === 1 && widget.position.h === 1) {
            <div class="todo-count">
              <i class="fa fa-list-check"></i>
              <span>{{ getPendingTodoCount(config.entityId) }}</span>
              <small>Pending</small>
            </div>
          } @else {
            @if (widget.showTitle !== false) {
              <h4 class="widget-frame-title">{{ widget.titleOverride || getEntityState(config.entityId)?.attributes?.['friendly_name'] || 'Tasks' }}</h4>
            }
            @if (getVisibleTodoItems(config.entityId).length > 0) {
              <div class="todo-items">
                @for (item of getVisibleTodoItems(config.entityId); track trackByItemId($index, item)) {
                  <div class="todo-item">
                    @if (item.complete) {
                      <i class="fa {{ config.completedIcon || 'fa-check-circle' }}"></i>
                    } @else {
                      <i class="fa {{ config.pendingIcon || 'fa-circle' }}"></i>
                    }
                    <span [class.completed]="item.complete">{{ item.summary }}</span>
                  </div>
                }
              </div>
            }
          }
        </div>
      }
    </div>
  `
})
export class TodoWidgetComponent {
  @Input() widget!: WidgetConfig;
  @Input() colorScheme!: ColorScheme;
  @Input() entityStates: Record<string, EntityState> | null = null;
  @Input() todoItemsByEntityId?: Record<string, TodoItem[]>;
  @Input() designerSettings?: DashboardLayout;

  get config(): TodoConfig { return (this.widget?.config || {}) as TodoConfig; }

  getHeaderFontSize(): number {
    return this.renderContext.titleFontSize;
  }

  getItemFontSize(): number {
    return this.renderContext.textFontSize;
  }

  getHeaderFontWeight(): number {
    return this.renderContext.titleFontWeight;
  }

  getItemFontWeight(): number {
    return this.renderContext.textFontWeight;
  }

  getSmallFontSize(): number {
    return Math.round(this.renderContext.textFontSize * 0.75);
  }

  /**
   * Checks if todo data has been fetched for the configured entity.
   */
  isDataFetched(): boolean {
    const entityId = this.config.entityId;
    if (!entityId) return false;

    // Check if we have fetched todo items from the API
    if (this.todoItemsByEntityId && entityId in this.todoItemsByEntityId) {
      return true;
    }

    // Fallback: check if entity has todo items in attributes
    const state = this.getEntityState(entityId);
    if (!state || !state.attributes) return false;

    return !!state.attributes['todo_items'];
  }

  getEntityState(entityId?: string) {
    if (!entityId || !this.entityStates) return null;
    return this.entityStates[entityId] ?? null;
  }

  getTodoItems(entityId?: string): Array<{ id: string | number; complete: boolean; summary: string }> {
    if (this.todoItemsByEntityId && entityId && this.todoItemsByEntityId[entityId]) {
      let mapped = this.todoItemsByEntityId[entityId].map((item: any, idx: number) => ({
        ...item,
        id: item.uid || item.id || idx,
        // Home Assistant uses 'status' field: 'needs_action' (incomplete) or 'completed' (complete)
        complete: item.status === 'completed' || item.status === 'done' || item.complete === true || item.completed === true || false,
        summary: item.summary || item.title || ''
      }));

      // Filter out completed items if showCompleted is false
      if (this.config.showCompleted === false) {
        mapped = mapped.filter(item => !item.complete);
      }

      // Sort to show incomplete items first
      mapped.sort((a, b) => {
        const ac = a.complete ? 1 : 0;
        const bc = b.complete ? 1 : 0;
        return ac - bc;
      });
      return mapped;
    }
    // Fallback to entity state (legacy, should not be used)
    const state = this.getEntityState(entityId);
    if (!state?.attributes?.['todo_items']) return [];
    const items = state.attributes['todo_items'] as any[];
    return items.map((item: any, idx: number) => ({
      id: idx,
      complete: item.status === 'completed' || item.complete === true || false,
      summary: item.summary || ''
    }));
  }

  getPendingTodoCount(entityId?: string): number { return this.getTodoItems(entityId).filter(i => !i.complete).length; }

  getTodoItemsLimited(entityId?: string, w = 2, h = 2): any[] {
    const max = Math.max(1, w * Math.max(1, h * 2));
    return this.getTodoItems(entityId).slice(0, max);
  }

  getVisibleTodoItems(entityId?: string): any[] {
    const items = this.getTodoItems(entityId);
    const max = this.config.maxItems;
    return max ? items.slice(0, max) : items;
  }

  trackByItemId(index: number, item: any) { return item.id || index; }

  getTitleColor(): string {
    return this.renderContext.titleColor;
  }

  getTextColor(): string {
    return this.renderContext.textColor;
  }

  getIconColor(): string {
    return this.renderContext.iconColor;
  }

  private get renderContext() {
    return resolveWidgetRenderContext(this.widget, this.colorScheme, this.designerSettings);
  }
}
