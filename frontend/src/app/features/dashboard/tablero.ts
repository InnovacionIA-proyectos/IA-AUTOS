import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, computed, inject, resource } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideBarChart3, LucideChevronRight } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { mensajeDeError } from '../../core/errors/error-api';
import { EstadoCarga, EstadoError, EstadoVacio, Tarjeta, TarjetaCabecera, TarjetaCuerpo, TarjetaTitulo } from '../../shared/ui';
import { humanizar } from '../../shared/util/formato';

interface Barra {
  etiqueta: string;
  total: number;
}

/** Tablero de patrones: métricas, mapa de calor por proceso y zona, y tasa de rechazo frente a la línea base. */
@Component({
  selector: 'app-tablero',
  imports: [NgTemplateOutlet, RouterLink, EstadoCarga, EstadoError, EstadoVacio, Tarjeta, TarjetaCabecera, TarjetaCuerpo, TarjetaTitulo, LucideBarChart3, LucideChevronRight],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-6">
      <div>
        <h1 class="text-[24px] font-semibold leading-[30px]">Tablero de patrones</h1>
        <p class="mt-1 text-[15px] text-text-muted">Mapa de calor de zonas dañadas y distribución por proceso.</p>
      </div>

      @if (cargando()) {
        <app-estado-carga etiqueta="Cargando tablero…" />
      } @else if (error()) {
        <app-estado-error [mensaje]="error()" (reintentar)="recargar()" />
      } @else if (metricas.value(); as m) {
        @if (m.totalMovilizadas + m.totalPendientes === 0) {
          <app-estado-vacio
            titulo="Aún no hay VINs cargados"
            descripcion="Sube el archivo de asignación en 'Cargar VINs'. A medida que se cierre el informe de cada una de las 3 etapas (cargue, transporte y descargue), las unidades pasarán de pendientes a movilizadas."
          >
            <svg icono lucideBarChart3 class="h-8 w-8" aria-hidden="true"></svg>
          </app-estado-vacio>
        } @else {
          <div class="flex flex-col gap-6">
            <div class="grid grid-cols-2 gap-4 lg:grid-cols-4">
              <a routerLink="/movilizados" class="group block h-full rounded-lg text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary">
                <div appTarjeta class="h-full transition group-hover:border-secondary group-hover:shadow-sm">
                  <div appTarjetaCuerpo>
                    <p class="text-[13px] text-text-muted">Unidades movilizadas</p>
                    <p class="mt-1 text-[24px] font-semibold text-text-primary">{{ m.totalMovilizadas }}</p>
                    <span class="mt-2 inline-flex items-center gap-1 text-[13px] font-medium text-secondary">Ver <svg lucideChevronRight class="h-3.5 w-3.5" aria-hidden="true"></svg></span>
                  </div>
                </div>
              </a>
              <a routerLink="/pendientes" class="group block h-full rounded-lg text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary">
                <div appTarjeta class="h-full transition group-hover:border-secondary group-hover:shadow-sm">
                  <div appTarjetaCuerpo>
                    <p class="text-[13px] text-text-muted">Pendientes por movilizar</p>
                    <p class="mt-1 text-[24px] font-semibold text-text-primary">{{ m.totalPendientes }}</p>
                    <span class="mt-2 inline-flex items-center gap-1 text-[13px] font-medium text-secondary">Ver <svg lucideChevronRight class="h-3.5 w-3.5" aria-hidden="true"></svg></span>
                  </div>
                </div>
              </a>
              <a routerLink="/novedades" class="group block h-full rounded-lg text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary">
                <div appTarjeta class="h-full transition group-hover:border-secondary group-hover:shadow-sm">
                  <div appTarjetaCuerpo>
                    <p class="text-[13px] text-text-muted">Unidades con novedad</p>
                    <p class="mt-1 text-[24px] font-semibold text-text-primary">{{ m.totalConNovedad }}</p>
                    <span class="mt-2 inline-flex items-center gap-1 text-[13px] font-medium text-secondary">Ver <svg lucideChevronRight class="h-3.5 w-3.5" aria-hidden="true"></svg></span>
                  </div>
                </div>
              </a>
              <button type="button" class="group block h-full rounded-lg text-left focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary" (click)="irATasa()">
                <div appTarjeta class="h-full transition group-hover:border-secondary group-hover:shadow-sm">
                  <div appTarjetaCuerpo>
                    <p class="text-[13px] text-text-muted">Tasa de rechazo (movilizadas)</p>
                    <p class="mt-1 text-[24px] font-semibold text-text-primary">{{ m.tasaGlobal }}%</p>
                    <span class="mt-2 inline-flex items-center gap-1 text-[13px] font-medium text-secondary">Ver <svg lucideChevronRight class="h-3.5 w-3.5" aria-hidden="true"></svg></span>
                  </div>
                </div>
              </button>
            </div>

            @if (m.totalMovilizadas === 0) {
              <app-estado-vacio
                titulo="Aún no hay unidades movilizadas"
                [descripcion]="'Hay ' + m.totalPendientes + ' unidad(es) pendiente(s) por movilizar. El mapa de calor, los informes y la tasa de rechazo aparecerán cuando un VIN cierre el informe de sus 3 etapas.'"
              >
                <svg icono lucideBarChart3 class="h-8 w-8" aria-hidden="true"></svg>
              </app-estado-vacio>
            } @else {
              <div class="grid grid-cols-1 gap-4 lg:grid-cols-2">
                <div appTarjeta>
                  <div appTarjetaCabecera><h3 appTarjetaTitulo>Hallazgos por proceso</h3></div>
                  <div appTarjetaCuerpo>
                    <ng-container *ngTemplateOutlet="barras; context: { $implicit: porProceso() }" />
                  </div>
                </div>
                <div appTarjeta>
                  <div appTarjetaCabecera><h3 appTarjetaTitulo>Hallazgos por zona del vehículo</h3></div>
                  <div appTarjetaCuerpo>
                    <ng-container *ngTemplateOutlet="barras; context: { $implicit: porZona() }" />
                  </div>
                </div>
              </div>

              <div appTarjeta id="tasa-tabla" class="scroll-mt-20">
                <div appTarjetaCabecera><h3 appTarjetaTitulo>Tasa de rechazo vs. línea base (por operación)</h3></div>
                <div appTarjetaCuerpo>
                  <div class="overflow-x-auto">
                    <table class="w-full text-[15px]">
                      <thead>
                        <tr class="text-left text-[13px] font-semibold text-text-muted">
                          <th class="py-2">Operación</th>
                          <th class="py-2">Unidades</th>
                          <th class="py-2">Con novedad</th>
                          <th class="py-2">Tasa actual</th>
                          <th class="py-2">Línea base</th>
                          <th class="py-2">Variación</th>
                        </tr>
                      </thead>
                      <tbody>
                        @for (o of m.porOperacion; track o.operacion) {
                          <tr class="border-t border-border">
                            <td class="py-2">{{ humanizar(o.operacion) }}</td>
                            <td class="py-2">{{ o.total }}</td>
                            <td class="py-2">{{ o.conNovedad }}</td>
                            <td class="py-2">{{ o.tasaActual }}%</td>
                            <td class="py-2 text-text-muted">{{ o.lineaBase }}%</td>
                            <td class="py-2" [class.text-danger]="o.variacion >= 0" [class.text-success]="o.variacion < 0">{{ o.variacion >= 0 ? '+' : '' }}{{ o.variacion }}%</td>
                          </tr>
                        }
                      </tbody>
                    </table>
                  </div>
                </div>
              </div>
            }
          </div>
        }
      }
    </div>

    <ng-template #barras let-items>
      @if (items.length === 0) {
        <p class="text-[13px] text-text-muted">Sin datos.</p>
      } @else {
        <ul class="flex flex-col gap-2">
          @for (item of items; track item.etiqueta) {
            <li class="flex items-center gap-3">
              <span class="w-28 shrink-0 text-[13px] text-text-primary">{{ item.etiqueta }}</span>
              <div class="h-4 flex-1 overflow-hidden rounded bg-surface">
                <div class="h-full rounded bg-primary" [style.width.%]="porcentaje(item.total, items)"></div>
              </div>
              <span class="w-6 shrink-0 text-right text-[13px] font-semibold text-text-primary">{{ item.total }}</span>
            </li>
          }
        </ul>
      }
    </ng-template>
  `,
})
export class Tablero {
  private readonly api = inject(ApiService);

  protected readonly metricas = resource({ loader: () => firstValueFrom(this.api.tasaRechazo()) });
  protected readonly patrones = resource({ loader: () => firstValueFrom(this.api.patrones()) });

  protected readonly cargando = computed(() => this.metricas.isLoading() || this.patrones.isLoading());
  protected readonly error = computed(() => {
    const e = this.metricas.error() ?? this.patrones.error();
    return e ? mensajeDeError(e) : '';
  });

  protected readonly porProceso = computed<Barra[]>(() =>
    (this.patrones.value()?.porProceso ?? []).map((p) => ({ etiqueta: humanizar(p.proceso), total: p.total })),
  );
  protected readonly porZona = computed<Barra[]>(() =>
    (this.patrones.value()?.porZona ?? []).map((z) => ({ etiqueta: humanizar(z.parte_vehiculo), total: z.total })),
  );

  protected readonly humanizar = humanizar;

  protected porcentaje(total: number, items: Barra[]): number {
    const maximo = Math.max(1, ...items.map((i) => i.total));
    return Math.round((total / maximo) * 100);
  }

  protected recargar(): void {
    this.metricas.reload();
    this.patrones.reload();
  }

  protected irATasa(): void {
    document.getElementById('tasa-tabla')?.scrollIntoView({ behavior: 'smooth' });
  }
}
