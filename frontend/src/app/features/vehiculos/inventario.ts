import { ChangeDetectionStrategy, Component, computed, inject, resource, signal } from '@angular/core';
import { ActivatedRoute, Router, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { mensajeDeError } from '../../core/errors/error-api';
import { Boton, CampoTexto, EstadoCarga, EstadoError, EstadoVacio, Insignia, Tarjeta, TarjetaCabecera, TarjetaCuerpo, TarjetaTitulo } from '../../shared/ui';
import { etiquetaEstado, etiquetaMomento, etiquetaProceso } from '../../shared/util/etiquetas';
import { formatearFecha, humanizar } from '../../shared/util/formato';

const RETARDO_BUSQUEDA_MS = 250;
const MINIMO_CARACTERES = 3;

/** Inventario de un VIN: búsqueda con sugerencias, datos del vehículo, etapas con sus informes y descarga del PDF. */
@Component({
  selector: 'app-inventario',
  imports: [RouterLink, Boton, CampoTexto, EstadoCarga, EstadoError, EstadoVacio, Insignia, Tarjeta, TarjetaCabecera, TarjetaCuerpo, TarjetaTitulo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-4">
      <div>
        <h1 class="text-[24px] font-semibold leading-[30px]">Inventario por VIN</h1>
        <p class="mt-1 text-[15px] text-text-muted">Consulta el recorrido completo de una unidad y descarga su informe en PDF.</p>
      </div>

      <div class="relative max-w-sm">
        <app-campo-texto etiqueta="VIN" tipo="search" marcador="Escribe al menos 3 caracteres" [mono]="true" [valor]="texto()" (valorChange)="escribir($event)" />
        @if (sugerencias.value()?.length) {
          <ul class="absolute z-10 mt-1 w-full overflow-hidden rounded-md border border-border bg-bg shadow-sm" role="listbox" aria-label="Sugerencias de VIN">
            @for (v of sugerencias.value(); track v.vin) {
              <li role="option">
                <button type="button" class="flex w-full items-center justify-between px-3 py-2 text-left text-[15px] hover:bg-surface" (click)="seleccionar(v.vin)">
                  <span class="font-mono">{{ v.vin }}</span>
                  <span class="text-[13px] text-text-muted">{{ v.marca }}</span>
                </button>
              </li>
            }
          </ul>
        }
      </div>

      @if (!vin()) {
        <app-estado-vacio titulo="Busca un VIN" descripcion="Escribe parte del VIN y elige una unidad de la lista para ver su inventario." />
      } @else if (inventario.isLoading()) {
        <app-estado-carga etiqueta="Cargando inventario…" />
      } @else if (error()) {
        <app-estado-error [mensaje]="error()" (reintentar)="inventario.reload()" />
      } @else if (inventario.value(); as inv) {
        <div appTarjeta>
          <div appTarjetaCabecera class="flex items-center justify-between gap-3">
            <h3 appTarjetaTitulo><span class="font-mono">{{ inv.vin }}</span></h3>
            <button appBoton variante="secondary" tamano="sm" [cargando]="descargando()" (click)="descargarPdf(inv.vin)">Descargar PDF</button>
          </div>
          <div appTarjetaCuerpo>
            <dl class="grid grid-cols-2 gap-x-6 gap-y-3 text-[15px] md:grid-cols-4">
              <div><dt class="text-[13px] text-text-muted">Marca</dt><dd>{{ inv.marca }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Modelo</dt><dd>{{ inv.modelo ?? '—' }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Tipo</dt><dd>{{ inv.tipo_vehiculo ?? '—' }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Color</dt><dd>{{ inv.color ?? '—' }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Chasis</dt><dd class="font-mono">{{ inv.chasis ?? '—' }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Operación</dt><dd>{{ humanizar(inv.operacion) }}</dd></div>
            </dl>
          </div>
        </div>

        @for (etapa of inv.etapas; track etapa.id) {
          <div appTarjeta>
            <div appTarjetaCabecera><h3 appTarjetaTitulo>{{ etapa.orden }}. {{ etiquetaProceso(etapa.proceso) }}</h3></div>
            <div appTarjetaCuerpo>
              @if (etapa.inspecciones.length === 0) {
                <p class="text-[15px] text-text-muted">{{ etapa.requiere_inspeccion ? 'Sin informe registrado todavía.' : 'Esta etapa no requiere informe.' }}</p>
              } @else {
                <ul class="flex flex-col gap-3">
                  @for (i of etapa.inspecciones; track i.id) {
                    <li class="flex flex-wrap items-center justify-between gap-2 text-[15px]">
                      <div>
                        <span class="font-semibold">{{ etiquetaMomento(i.momento) }}</span>
                        <span class="text-text-muted"> · {{ formatearFecha(i.fecha) }}</span>
                        @if (i.conductor_nombre) { <span class="text-text-muted"> · {{ i.conductor_nombre }}</span> }
                      </div>
                      <div class="flex items-center gap-3">
                        <app-insignia [variante]="i.hallazgos.length ? 'danger' : 'success'">{{ i.hallazgos.length ? i.hallazgos.length + ' hallazgo(s)' : 'Sin novedad' }}</app-insignia>
                        <app-insignia>{{ etiquetaEstado(i.estado) }}</app-insignia>
                        <a [routerLink]="['/inspecciones', i.id]" class="text-[13px] font-medium text-secondary hover:underline">Ver informe</a>
                      </div>
                    </li>
                  }
                </ul>
              }
            </div>
          </div>
        }
      }
    </div>
  `,
})
export class Inventario {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  private readonly ruta = inject(ActivatedRoute);

  protected readonly texto = signal('');
  private readonly busqueda = signal('');
  protected readonly vin = signal(this.ruta.snapshot.queryParamMap.get('vin') ?? '');
  protected readonly descargando = signal(false);
  private temporizador: ReturnType<typeof setTimeout> | undefined;

  constructor() {
    this.texto.set(this.vin());
  }

  protected readonly sugerencias = resource({
    params: () => (this.busqueda().length >= MINIMO_CARACTERES ? this.busqueda() : undefined),
    loader: ({ params }) => firstValueFrom(this.api.buscarVehiculos(params)),
  });

  protected readonly inventario = resource({
    params: () => this.vin() || undefined,
    loader: ({ params }) => firstValueFrom(this.api.inventario(params)),
  });

  protected readonly error = computed(() => (this.inventario.error() ? mensajeDeError(this.inventario.error()) : ''));

  protected readonly formatearFecha = formatearFecha;
  protected readonly humanizar = humanizar;
  protected readonly etiquetaProceso = etiquetaProceso;
  protected readonly etiquetaMomento = etiquetaMomento;
  protected readonly etiquetaEstado = etiquetaEstado;

  protected escribir(valor: string): void {
    this.texto.set(valor);
    clearTimeout(this.temporizador);
    this.temporizador = setTimeout(() => this.busqueda.set(valor.trim()), RETARDO_BUSQUEDA_MS);
  }

  protected seleccionar(vin: string): void {
    this.texto.set(vin);
    this.busqueda.set('');
    this.vin.set(vin);
    void this.router.navigate([], { queryParams: { vin }, replaceUrl: true });
  }

  protected async descargarPdf(vin: string): Promise<void> {
    this.descargando.set(true);
    try {
      const blob = await firstValueFrom(this.api.inventarioPdf(vin));
      const url = URL.createObjectURL(blob);
      const enlace = document.createElement('a');
      enlace.href = url;
      enlace.download = `inventario-${vin}.pdf`;
      enlace.click();
      URL.revokeObjectURL(url);
    } finally {
      this.descargando.set(false);
    }
  }
}
