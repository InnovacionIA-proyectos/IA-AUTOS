import { ChangeDetectionStrategy, Component, computed, inject, resource, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import type { OrdenSofasa } from '../../core/api/api-tipos';
import { mensajeDeError } from '../../core/errors/error-api';
import { CampoTexto, Columna, EstadoError, PlantillaColumna, TablaDatos } from '../../shared/ui';
import { formatearFecha } from '../../shared/util/formato';

const RETARDO_BUSQUEDA_MS = 300;

/** Órdenes de despacho sincronizadas desde Sofasa, con filtros por VIN, estado y ciudad de destino. */
@Component({
  selector: 'app-ordenes-sofasa',
  imports: [CampoTexto, EstadoError, PlantillaColumna, TablaDatos],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-4">
      <div>
        <h1 class="text-[24px] font-semibold leading-[30px]">Órdenes Sofasa</h1>
        <p class="mt-1 text-[15px] text-text-muted">Órdenes de despacho recibidas de Sofasa y su vínculo con los VINs cargados.</p>
      </div>

      <div class="grid grid-cols-1 gap-3 md:grid-cols-3">
        <app-campo-texto etiqueta="VIN" tipo="search" [mono]="true" [valor]="vinEscrito()" (valorChange)="escribir(vin, vinEscrito, $event)" />
        <app-campo-texto etiqueta="Estado" tipo="search" [valor]="estadoEscrito()" (valorChange)="escribir(estado, estadoEscrito, $event)" />
        <app-campo-texto etiqueta="Ciudad de destino" tipo="search" [valor]="ciudadEscrita()" (valorChange)="escribir(ciudad, ciudadEscrita, $event)" />
      </div>

      @if (error()) {
        <app-estado-error [mensaje]="error()" (reintentar)="lista.reload()" />
      } @else {
        <app-tabla-datos
          [columnas]="columnas"
          [filas]="filas()"
          [cargando]="lista.isLoading()"
          tituloVacio="Sin órdenes"
          descripcionVacia="Aún no hay órdenes sincronizadas o ninguna coincide con los filtros."
        >
          <ng-template appColumna="vin" let-f><span class="font-mono">{{ f.vin ?? '—' }}</span></ng-template>
        </app-tabla-datos>
      }
    </div>
  `,
})
export class OrdenesSofasa {
  private readonly api = inject(ApiService);

  protected readonly vinEscrito = signal('');
  protected readonly estadoEscrito = signal('');
  protected readonly ciudadEscrita = signal('');
  protected readonly vin = signal('');
  protected readonly estado = signal('');
  protected readonly ciudad = signal('');
  private temporizador: ReturnType<typeof setTimeout> | undefined;

  protected readonly lista = resource({
    params: () => ({ vin: this.vin(), estado: this.estado(), ciudad: this.ciudad() }),
    loader: ({ params }) => firstValueFrom(this.api.listarSofasa(params)),
  });

  protected readonly filas = computed(() => this.lista.value() ?? []);
  protected readonly error = computed(() => (this.lista.error() ? mensajeDeError(this.lista.error()) : ''));

  protected readonly columnas: Columna<OrdenSofasa>[] = [
    { clave: 'orden', encabezado: 'Orden', texto: (f) => f.orden_number ?? '—' },
    { clave: 'vin', encabezado: 'VIN' },
    { clave: 'modelo', encabezado: 'Modelo', texto: (f) => f.model_description ?? '—' },
    { clave: 'estado', encabezado: 'Estado', texto: (f) => f.status_code ?? '—' },
    { clave: 'origen', encabezado: 'Origen', texto: (f) => f.origin_cedi ?? '—' },
    { clave: 'destino', encabezado: 'Destino', texto: (f) => f.destination_city ?? '—' },
    { clave: 'cargue', encabezado: 'Cargue', texto: (f) => formatearFecha(f.loading_date) },
    { clave: 'evento', encabezado: 'Evento', texto: (f) => formatearFecha(f.datetime_event) },
  ];

  /** Refleja lo escrito de inmediato y aplica el filtro tras una pausa para no consultar en cada tecla. */
  protected escribir(filtro: { set(valor: string): void }, escrito: { set(valor: string): void }, valor: string): void {
    escrito.set(valor);
    clearTimeout(this.temporizador);
    this.temporizador = setTimeout(() => filtro.set(valor.trim()), RETARDO_BUSQUEDA_MS);
  }
}
