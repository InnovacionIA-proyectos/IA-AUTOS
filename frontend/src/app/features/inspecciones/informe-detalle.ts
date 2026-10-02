import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { mensajeDeError } from '../../core/errors/error-api';
import { EstadoCarga, EstadoError, Insignia, Tarjeta, TarjetaCabecera, TarjetaCuerpo, TarjetaTitulo } from '../../shared/ui';
import { etiquetaEstado, etiquetaMomento, etiquetaProceso } from '../../shared/util/etiquetas';
import { esTrazoSvg, viewBoxDeFirma } from '../../shared/util/firma';
import { formatearFecha, humanizar } from '../../shared/util/formato';

/** Informe completo: datos de la etapa, hallazgos, evidencias fotográficas selladas y firmas de la transferencia. */
@Component({
  selector: 'app-informe-detalle',
  imports: [NgTemplateOutlet, RouterLink, EstadoCarga, EstadoError, Insignia, Tarjeta, TarjetaCabecera, TarjetaCuerpo, TarjetaTitulo],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-4">
      <a routerLink="/inspecciones" class="text-[13px] font-medium text-secondary hover:underline">Volver a informes</a>

      @if (informe.isLoading()) {
        <app-estado-carga etiqueta="Cargando informe…" />
      } @else if (error()) {
        <app-estado-error [mensaje]="error()" (reintentar)="informe.reload()" />
      } @else if (informe.value(); as i) {
        <div>
          <h1 class="text-[24px] font-semibold leading-[30px]">Informe de {{ etiquetaProceso(i.proceso) }}</h1>
          <p class="mt-1 text-[15px] text-text-muted"><span class="font-mono">{{ i.vin }}</span> · {{ i.marca }} {{ i.modelo ?? '' }}</p>
        </div>

        <div appTarjeta>
          <div appTarjetaCabecera class="flex items-center justify-between">
            <h3 appTarjetaTitulo>Datos del informe</h3>
            <app-insignia [variante]="i.estado === 'cerrada' ? 'success' : 'warning'">{{ etiquetaEstado(i.estado) }}</app-insignia>
          </div>
          <div appTarjetaCuerpo>
            <dl class="grid grid-cols-2 gap-x-6 gap-y-3 text-[15px] md:grid-cols-4">
              <div><dt class="text-[13px] text-text-muted">Momento</dt><dd>{{ etiquetaMomento(i.momento) }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Fecha</dt><dd>{{ formatearFecha(i.fecha) }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Inspector</dt><dd>{{ i.inspector_nombre ?? '—' }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Conductor</dt><dd>{{ i.conductor_nombre ?? '—' }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Placa</dt><dd class="font-mono">{{ i.placa ?? '—' }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Tráiler</dt><dd class="font-mono">{{ i.trailer ?? '—' }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Niñera</dt><dd>{{ i.ninera ?? '—' }}</dd></div>
              <div><dt class="text-[13px] text-text-muted">Ubicación</dt><dd>{{ i.lat !== null && i.lng !== null ? i.lat.toFixed(5) + ', ' + i.lng.toFixed(5) : '—' }}</dd></div>
            </dl>
            @if (i.notas) {
              <p class="mt-4 text-[15px]"><span class="text-[13px] text-text-muted">Notas: </span>{{ i.notas }}</p>
            }
          </div>
        </div>

        <div appTarjeta>
          <div appTarjetaCabecera><h3 appTarjetaTitulo>Hallazgos ({{ i.hallazgos.length }})</h3></div>
          <div appTarjetaCuerpo>
            @if (i.hallazgos.length === 0) {
              <p class="text-[15px] text-text-muted">Sin novedad: no se registraron daños.</p>
            } @else {
              <ul class="flex flex-col gap-2 text-[15px]">
                @for (h of i.hallazgos; track h.id) {
                  <li class="flex flex-wrap items-center gap-2">
                    <span class="font-semibold">{{ humanizar(h.parte_vehiculo) }}</span>
                    <app-insignia variante="danger">{{ humanizar(h.clase_dano) }}</app-insignia>
                    <app-insignia variante="warning">{{ humanizar(h.tamano_dano) }}</app-insignia>
                  </li>
                }
              </ul>
            }
          </div>
        </div>

        @if (i.evidencias.length > 0) {
          <div appTarjeta>
            <div appTarjetaCabecera><h3 appTarjetaTitulo>Evidencias ({{ i.evidencias.length }})</h3></div>
            <div appTarjetaCuerpo>
              <div class="grid grid-cols-2 gap-3 md:grid-cols-4">
                @for (e of evidencias(); track e.id) {
                  <figure class="overflow-hidden rounded-md border border-border">
                    @if (e.src) {
                      <a [href]="e.src" target="_blank" rel="noopener noreferrer">
                        <img [src]="e.src" alt="Evidencia fotográfica" loading="lazy" class="h-32 w-full object-cover" />
                      </a>
                    }
                    <figcaption class="px-2 py-1 text-[13px] text-text-muted">{{ e.fecha }}</figcaption>
                  </figure>
                }
              </div>
            </div>
          </div>
        }

        @if (i.transferencia; as t) {
          <div appTarjeta>
            <div appTarjetaCabecera class="flex items-center justify-between">
              <h3 appTarjetaTitulo>Transferencia</h3>
              <app-insignia [variante]="t.firmada ? 'success' : 'warning'">{{ t.firmada ? 'Firmada' : 'Sin firmar' }}</app-insignia>
            </div>
            <div appTarjetaCuerpo>
              <div class="grid grid-cols-1 gap-6 md:grid-cols-2">
                <div>
                  <p class="text-[13px] text-text-muted">Entrega: {{ t.entrega_nombre ?? '—' }}</p>
                  <ng-container [ngTemplateOutlet]="firma" [ngTemplateOutletContext]="{ $implicit: t.firma_entrega, etiqueta: 'Firma de quien entrega' }" />
                </div>
                <div>
                  <p class="text-[13px] text-text-muted">Recibe: {{ t.recibe_nombre ?? '—' }}</p>
                  <ng-container [ngTemplateOutlet]="firma" [ngTemplateOutletContext]="{ $implicit: t.firma_recibe, etiqueta: 'Firma de quien recibe' }" />
                </div>
              </div>
              @if (t.firmada) {
                <p class="mt-3 text-[13px] text-text-muted">Firmada el {{ formatearFecha(t.firmada_at) }}</p>
              }
            </div>
          </div>
        }
      }
    </div>

    <ng-template #firma let-trazo let-etiqueta="etiqueta">
      @if (esTrazoSvg(trazo)) {
        <svg [attr.viewBox]="viewBoxDeFirma(trazo)" class="mt-2 h-28 w-full rounded-md border border-border bg-bg" role="img" [attr.aria-label]="etiqueta">
          <path [attr.d]="trazo" fill="none" stroke="#0F2347" stroke-width="2.5" stroke-linecap="round" stroke-linejoin="round" />
        </svg>
      } @else {
        <p class="mt-2 text-[15px] text-text-muted">{{ trazo ? 'Firma registrada (formato anterior).' : 'Sin firma.' }}</p>
      }
    </ng-template>
  `,
})
export class InformeDetalle {
  private readonly api = inject(ApiService);
  private readonly id = inject(ActivatedRoute).snapshot.paramMap.get('id') ?? '';

  protected readonly informe = resource({ loader: () => firstValueFrom(this.api.informe(this.id)) });
  protected readonly error = computed(() => (this.informe.error() ? mensajeDeError(this.informe.error()) : ''));
  protected readonly evidencias = computed(() =>
    (this.informe.value()?.evidencias ?? []).map((e) => ({ id: e.id, src: this.api.urlArchivo(e.url), fecha: formatearFecha(e.sello_fecha) })),
  );

  protected readonly formatearFecha = formatearFecha;
  protected readonly humanizar = humanizar;
  protected readonly etiquetaProceso = etiquetaProceso;
  protected readonly etiquetaMomento = etiquetaMomento;
  protected readonly etiquetaEstado = etiquetaEstado;
  protected readonly esTrazoSvg = esTrazoSvg;
  protected readonly viewBoxDeFirma = viewBoxDeFirma;
}
