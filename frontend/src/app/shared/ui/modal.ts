import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { LucideX } from '@lucide/angular';

/** Diálogo modal. Se cierra con Escape, con el botón de cierre o al pulsar fuera. Pie opcional: `<div pie>…</div>`. */
@Component({
  selector: 'app-modal',
  imports: [LucideX],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '(document:keydown.escape)': 'abierto() && cerrar.emit()' },
  template: `
    @if (abierto()) {
      <div class="fixed inset-0 z-50 flex items-center justify-center bg-text-primary/40 p-4" (click)="cerrar.emit()">
        <div
          role="dialog"
          aria-modal="true"
          [attr.aria-label]="titulo()"
          class="w-full max-w-lg rounded-lg bg-bg shadow-xl"
          (click)="$event.stopPropagation()"
        >
          <div class="flex items-center justify-between border-b border-border px-4 py-3">
            <h2 class="text-[17px] font-semibold text-text-primary">{{ titulo() }}</h2>
            <button
              type="button"
              aria-label="Cerrar"
              class="rounded p-1 text-text-muted hover:bg-surface focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary"
              (click)="cerrar.emit()"
            >
              <svg lucideX class="h-5 w-5"></svg>
            </button>
          </div>
          <div class="px-4 py-4 text-[15px] text-text-primary"><ng-content /></div>
          <div class="flex justify-end gap-2 border-t border-border px-4 py-3 empty:hidden"><ng-content select="[pie]" /></div>
        </div>
      </div>
    }
  `,
})
export class Modal {
  readonly abierto = input.required<boolean>();
  readonly titulo = input('');
  readonly cerrar = output<void>();
}
