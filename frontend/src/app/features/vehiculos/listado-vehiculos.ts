import { ChangeDetectionStrategy, Component, computed, inject, resource, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import type { FilaListadoVehiculo, TipoListado } from '../../core/api/api-tipos';
import { mensajeDeError } from '../../core/errors/error-api';
import { Boton, CampoTexto, Columna, EstadoError, Insignia, PlantillaColumna, TablaDatos } from '../../shared/ui';
import { humanizar } from '../../shared/util/formato';

/** Datos de ruta que parametrizan el listado (movilizados, pendientes, novedades). */
export interface DatosListado {
  tipo: TipoListado;
  titulo: string;
  descripcion: string;
  vacio: string;
}

const RETARDO_BUSQUEDA_MS = 300;

/** Listado paginado de vehículos con búsqueda por VIN; un mismo componente sirve a las tres rutas. */
@Component({
  selector: 'app-listado-vehiculos',
  imports: [Boton, CampoTexto, EstadoError, Insignia, PlantillaColumna, TablaDatos],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-4">
      <div>
        <h1 class="text-[24px] font-semibold leading-[30px]">{{ datos.titulo }}</h1>
        <p class="mt-1 text-[15px] text-text-muted">{{ datos.descripcion }}</p>
      </div>

      <div class="max-w-sm">
        <app-campo-texto etiqueta="Buscar por VIN" tipo="search" marcador="Ej. 9FB…" [mono]="true" [valor]="vinEscrito()" (valorChange)="escribir($event)" />
      </div>

      @if (error()) {
        <app-estado-error [mensaje]="error()" (reintentar)="listado.reload()" />
      } @else {
        <app-tabla-datos
          [columnas]="columnas"
          [filas]="filas()"
          [cargando]="listado.isLoading()"
          [tituloVacio]="vinAplicado() ? 'Sin coincidencias' : 'Sin unidades'"
          [descripcionVacia]="vinAplicado() ? 'Ningún VIN coincide con la búsqueda.' : datos.vacio"
          [clicable]="true"
          (filaClic)="abrir($event)"
        >
          <ng-template appColumna="vin" let-f><span class="font-mono">{{ f.vin }}</span></ng-template>
          <ng-template appColumna="operacion" let-f>{{ humanizar(f.operacion) }}</ng-template>
          <ng-template appColumna="etapas" let-f>{{ f.completas }} / {{ f.etapas }}</ng-template>
          <ng-template appColumna="novedades" let-f>
            @if (f.novedades > 0) {
              <app-insignia variante="danger">{{ f.novedades }}</app-insignia>
            } @else {
              <app-insignia variante="success">Sin novedad</app-insignia>
            }
          </ng-template>
        </app-tabla-datos>

        @if (totalPaginas() > 1) {
          <nav class="flex items-center justify-between" aria-label="Paginación">
            <span class="text-[13px] text-text-muted">Página {{ pagina() }} de {{ totalPaginas() }} · {{ total() }} unidades</span>
            <div class="flex gap-2">
              <button appBoton variante="secondary" tamano="sm" [disabled]="pagina() <= 1" (click)="irAPagina(pagina() - 1)">Anterior</button>
              <button appBoton variante="secondary" tamano="sm" [disabled]="pagina() >= totalPaginas()" (click)="irAPagina(pagina() + 1)">Siguiente</button>
            </div>
          </nav>
        }
      }
    </div>
  `,
})
export class ListadoVehiculos {
  private readonly api = inject(ApiService);
  private readonly router = inject(Router);
  protected readonly datos = inject(ActivatedRoute).snapshot.data as DatosListado;

  protected readonly vinEscrito = signal('');
  protected readonly vinAplicado = signal('');
  protected readonly pagina = signal(1);
  private temporizador: ReturnType<typeof setTimeout> | undefined;

  protected readonly listado = resource({
    params: () => ({ vin: this.vinAplicado(), pagina: this.pagina() }),
    loader: ({ params }) => firstValueFrom(this.api.listarVehiculos(this.datos.tipo, params.vin, params.pagina)),
  });

  protected readonly filas = computed<FilaListadoVehiculo[]>(() => this.listado.value()?.rows ?? []);
  protected readonly total = computed(() => this.listado.value()?.total ?? 0);
  protected readonly totalPaginas = computed(() => {
    const l = this.listado.value();
    return l && l.pageSize > 0 ? Math.max(1, Math.ceil(l.total / l.pageSize)) : 1;
  });
  protected readonly error = computed(() => (this.listado.error() ? mensajeDeError(this.listado.error()) : ''));

  protected readonly columnas: Columna<FilaListadoVehiculo>[] = [
    { clave: 'vin', encabezado: 'VIN' },
    { clave: 'marca', encabezado: 'Marca', texto: (f) => f.marca },
    { clave: 'modelo', encabezado: 'Modelo', texto: (f) => f.modelo ?? '—' },
    { clave: 'color', encabezado: 'Color', texto: (f) => f.color ?? '—' },
    { clave: 'operacion', encabezado: 'Operación' },
    { clave: 'etapas', encabezado: 'Etapas' },
    { clave: 'novedades', encabezado: 'Novedades' },
  ];

  protected readonly humanizar = humanizar;

  protected escribir(valor: string): void {
    this.vinEscrito.set(valor);
    clearTimeout(this.temporizador);
    this.temporizador = setTimeout(() => {
      this.pagina.set(1);
      this.vinAplicado.set(valor.trim());
    }, RETARDO_BUSQUEDA_MS);
  }

  protected irAPagina(pagina: number): void {
    this.pagina.set(Math.min(Math.max(1, pagina), this.totalPaginas()));
  }

  protected abrir(fila: FilaListadoVehiculo): void {
    void this.router.navigate(['/inventario'], { queryParams: { vin: fila.vin } });
  }
}
