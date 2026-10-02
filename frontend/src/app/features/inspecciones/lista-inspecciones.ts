import { ChangeDetectionStrategy, Component, computed, inject, resource, signal } from '@angular/core';
import { Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import type { InspeccionListado } from '../../core/api/api-tipos';
import { mensajeDeError } from '../../core/errors/error-api';
import { CampoTexto, Columna, EstadoError, Insignia, OpcionSelector, PlantillaColumna, Selector, TablaDatos } from '../../shared/ui';
import { ETIQUETA_ESTADO, ETIQUETA_PROCESO, etiquetaEstado, etiquetaMomento, etiquetaProceso } from '../../shared/util/etiquetas';
import { formatearFecha } from '../../shared/util/formato';

const RETARDO_BUSQUEDA_MS = 300;

/** Listado de informes con filtros por VIN, estado y proceso; cada fila abre el informe completo. */
@Component({
  selector: 'app-lista-inspecciones',
  imports: [CampoTexto, Selector, EstadoError, Insignia, PlantillaColumna, TablaDatos],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-4">
      <div>
        <h1 class="text-[24px] font-semibold leading-[30px]">Informes</h1>
        <p class="mt-1 text-[15px] text-text-muted">Informes registrados en campo por etapa.</p>
      </div>

      <div class="grid grid-cols-1 gap-3 md:grid-cols-3">
        <app-campo-texto etiqueta="VIN" tipo="search" [mono]="true" [valor]="vinEscrito()" (valorChange)="escribirVin($event)" />
        <app-selector etiqueta="Estado" marcador="Todos" [opciones]="estados" [(valor)]="estado" />
        <app-selector etiqueta="Proceso" marcador="Todos" [opciones]="procesos" [(valor)]="proceso" />
      </div>

      @if (error()) {
        <app-estado-error [mensaje]="error()" (reintentar)="lista.reload()" />
      } @else {
        <app-tabla-datos
          [columnas]="columnas"
          [filas]="filas()"
          [cargando]="lista.isLoading()"
          tituloVacio="Sin informes"
          descripcionVacia="No hay informes que coincidan con los filtros."
          [clicable]="true"
          (filaClic)="abrir($event)"
        >
          <ng-template appColumna="vin" let-f><span class="font-mono">{{ f.vin }}</span></ng-template>
          <ng-template appColumna="estado" let-f>
            <app-insignia [variante]="f.estado === 'cerrada' ? 'success' : 'warning'">{{ etiquetaEstado(f.estado) }}</app-insignia>
          </ng-template>
        </app-tabla-datos>
      }
    </div>
  `,
})
export class ListaInspecciones {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);

  protected readonly vinEscrito = signal('');
  private readonly vin = signal('');
  protected readonly estado = signal('');
  protected readonly proceso = signal('');
  private temporizador: ReturnType<typeof setTimeout> | undefined;

  protected readonly lista = resource({
    params: () => ({ vin: this.vin(), estado: this.estado(), proceso: this.proceso() }),
    loader: ({ params }) => firstValueFrom(this.api.listarInspecciones(params)),
  });

  protected readonly filas = computed(() => this.lista.value() ?? []);
  protected readonly error = computed(() => (this.lista.error() ? mensajeDeError(this.lista.error()) : ''));

  protected readonly estados: OpcionSelector[] = Object.entries(ETIQUETA_ESTADO).map(([valor, etiqueta]) => ({ valor, etiqueta }));
  protected readonly procesos: OpcionSelector[] = Object.entries(ETIQUETA_PROCESO).map(([valor, etiqueta]) => ({ valor, etiqueta }));

  protected readonly columnas: Columna<InspeccionListado>[] = [
    { clave: 'vin', encabezado: 'VIN' },
    { clave: 'proceso', encabezado: 'Etapa', texto: (f) => etiquetaProceso(f.proceso) },
    { clave: 'momento', encabezado: 'Momento', texto: (f) => etiquetaMomento(f.momento) },
    { clave: 'fecha', encabezado: 'Fecha', texto: (f) => formatearFecha(f.fecha) },
    { clave: 'conductor', encabezado: 'Conductor', texto: (f) => f.conductor_nombre ?? '—' },
    { clave: 'estado', encabezado: 'Estado' },
  ];

  protected readonly etiquetaEstado = etiquetaEstado;

  protected escribirVin(valor: string): void {
    this.vinEscrito.set(valor);
    clearTimeout(this.temporizador);
    this.temporizador = setTimeout(() => this.vin.set(valor.trim()), RETARDO_BUSQUEDA_MS);
  }

  protected abrir(fila: InspeccionListado): void {
    void this.router.navigate(['/inspecciones', fila.id]);
  }
}
