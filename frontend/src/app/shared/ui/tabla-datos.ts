import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, Directive, TemplateRef, contentChildren, inject, input, output } from '@angular/core';
import { EstadoCarga, EstadoVacio } from './estados';

export interface Columna<T> {
  clave: string;
  encabezado: string;
  clase?: string;
  /** Texto de la celda cuando no se define una plantilla `appColumna` con esta clave. */
  texto?: (fila: T) => string;
}

/** Plantilla de una celda: `<ng-template appColumna="clave" let-fila>…</ng-template>` dentro de `<app-tabla-datos>`. */
@Directive({ selector: 'ng-template[appColumna]' })
export class PlantillaColumna {
  readonly clave = input.required<string>({ alias: 'appColumna' });
  readonly plantilla = inject(TemplateRef);
}

/** Tabla de datos de oficina (densidad media-alta, filas de 40 px) con estados de carga y vacío. */
@Component({
  selector: 'app-tabla-datos',
  imports: [NgTemplateOutlet, EstadoCarga, EstadoVacio],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    @if (cargando()) {
      <app-estado-carga />
    } @else if (filas().length === 0) {
      <app-estado-vacio [titulo]="tituloVacio()" [descripcion]="descripcionVacia()" />
    } @else {
      <div class="overflow-x-auto rounded-lg border border-border">
        <table class="w-full border-collapse text-[15px]">
          <thead>
            <tr class="bg-surface text-left text-[13px] font-semibold text-text-muted">
              @for (columna of columnas(); track columna.clave) {
                <th class="h-10 px-3 {{ columna.clase ?? '' }}">{{ columna.encabezado }}</th>
              }
            </tr>
          </thead>
          <tbody>
            @for (fila of filas(); track $index) {
              <tr
                class="border-t border-border hover:bg-surface"
                [class.cursor-pointer]="clicable()"
                (click)="clicable() && filaClic.emit(fila)"
              >
                @for (columna of columnas(); track columna.clave) {
                  <td class="h-10 px-3 text-text-primary {{ columna.clase ?? '' }}">
                    @let plantilla = plantillaDe(columna.clave);
                    @if (plantilla) {
                      <ng-container [ngTemplateOutlet]="plantilla" [ngTemplateOutletContext]="{ $implicit: fila }" />
                    } @else {
                      {{ columna.texto ? columna.texto(fila) : '' }}
                    }
                  </td>
                }
              </tr>
            }
          </tbody>
        </table>
      </div>
    }
  `,
})
export class TablaDatos<T> {
  readonly columnas = input.required<readonly Columna<T>[]>();
  readonly filas = input.required<readonly T[]>();
  readonly cargando = input(false);
  readonly tituloVacio = input('Sin resultados');
  readonly descripcionVacia = input('');
  /** Si hay observadores de `filaClic`, las filas se muestran como clicables. */
  readonly clicable = input(false);
  readonly filaClic = output<T>();

  private readonly plantillas = contentChildren(PlantillaColumna);

  protected plantillaDe(clave: string): TemplateRef<unknown> | null {
    return this.plantillas().find((p) => p.clave() === clave)?.plantilla ?? null;
  }
}
