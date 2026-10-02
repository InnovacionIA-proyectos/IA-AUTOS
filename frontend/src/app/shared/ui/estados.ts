import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { LucideInbox, LucideLoader2, LucideTriangleAlert } from '@lucide/angular';
import { Boton } from './boton';

@Component({
  selector: 'app-spinner',
  imports: [LucideLoader2],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<svg lucideLoader2 class="h-5 w-5 animate-spin text-primary" aria-hidden="true"></svg>',
})
export class Spinner {}

@Component({
  selector: 'app-estado-carga',
  imports: [Spinner],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col items-center justify-center gap-3 py-16 text-text-muted', role: 'status' },
  template: `
    <app-spinner />
    <p class="text-[15px]">{{ etiqueta() }}</p>
  `,
})
export class EstadoCarga {
  readonly etiqueta = input('Cargando…');
}

/** Estado vacío "educativo": no solo dice "sin datos", explica qué hacer (principio UX del sistema de diseño). */
@Component({
  selector: 'app-estado-vacio',
  imports: [LucideInbox],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    class: 'flex flex-col items-center justify-center gap-3 rounded-lg border border-dashed border-border bg-surface px-6 py-16 text-center',
  },
  template: `
    <div class="text-text-muted">
      <ng-content select="[icono]">
        <svg lucideInbox class="h-8 w-8" aria-hidden="true"></svg>
      </ng-content>
    </div>
    <h3 class="text-[17px] font-semibold text-text-primary">{{ titulo() }}</h3>
    @if (descripcion()) {
      <p class="max-w-md text-[15px] text-text-muted">{{ descripcion() }}</p>
    }
    <div class="mt-2 empty:hidden"><ng-content /></div>
  `,
})
export class EstadoVacio {
  readonly titulo = input.required<string>();
  readonly descripcion = input('');
}

@Component({
  selector: 'app-estado-error',
  imports: [LucideTriangleAlert, Boton],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex flex-col items-center justify-center gap-3 rounded-lg border border-border bg-surface px-6 py-16 text-center', role: 'alert' },
  template: `
    <svg lucideTriangleAlert class="h-8 w-8 text-danger" aria-hidden="true"></svg>
    <h3 class="text-[17px] font-semibold text-text-primary">{{ titulo() }}</h3>
    @if (mensaje()) {
      <p class="max-w-md text-[15px] text-text-muted">{{ mensaje() }}</p>
    }
    @if (reintentable()) {
      <button appBoton variante="secondary" tamano="sm" class="mt-2" (click)="reintentar.emit()">Reintentar</button>
    }
  `,
})
export class EstadoError {
  readonly titulo = input('Algo salió mal');
  readonly mensaje = input('');
  readonly reintentable = input(true);
  readonly reintentar = output<void>();
}
