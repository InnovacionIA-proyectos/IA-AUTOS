import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { LucideCircleCheck, LucideFileSpreadsheet, LucideInfo, LucideUpload, LucideXCircle } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { OPERACIONES, type Operacion, type ResultadoCarga } from '../../core/api/api-tipos';
import { mensajeDeError } from '../../core/errors/error-api';
import { Boton, CampoTexto, Insignia, OpcionSelector, Selector } from '../../shared/ui';
import { Hoja } from './lector-excel';
import { ETIQUETA_OPERACION, MAX_FILAS_CARGA, Mapeo, autoMapear, encabezadosDe, letraColumna, revisarFilas, texto } from './mapeo-vins';
import { LectorExcel } from './servicio-lector-excel';

const FILAS_VISTA_PREVIA = 3;
const FILAS_REVISION = 6;

/** Carga masiva de VINs desde Excel/CSV: archivo, hoja y encabezados, mapeo de columnas, revisión y envío. */
@Component({
  selector: 'app-cargar-vins',
  imports: [Boton, CampoTexto, Insignia, Selector, LucideCircleCheck, LucideFileSpreadsheet, LucideInfo, LucideUpload, LucideXCircle],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-6">
      <div>
        <h1 class="text-[24px] font-semibold leading-[30px]">Cargar VINs desde Excel</h1>
        <p class="mt-1 max-w-2xl text-[15px] text-text-muted">
          Sube el archivo de asignación, revisa el mapeo de columnas y carga. Cada VIN queda con su ruta de custodia
          <b>Cargue</b> → <b>Transporte</b> → <b>Descargue</b> lista para la app.
        </p>
      </div>

      <!-- Paso 1: archivo -->
      <section class="overflow-hidden rounded-xl border border-border bg-surface">
        <header class="flex items-center gap-3 border-b border-border px-5 py-4">
          <span class="flex h-7 w-7 items-center justify-center rounded-full text-[13px] font-semibold text-white" [class.bg-success]="!!nombreArchivo()" [class.bg-primary]="!nombreArchivo()">1</span>
          <div>
            <h2 class="text-[16px] font-semibold">Archivo</h2>
            @if (nombreArchivo()) { <p class="text-[13px] text-text-muted">{{ nombreArchivo() }}</p> }
          </div>
        </header>
        <div class="p-5">
          <button
            type="button"
            class="flex w-full flex-col items-center justify-center gap-2 rounded-lg border-2 border-dashed border-border px-6 py-10 text-center transition-colors hover:border-secondary hover:bg-bg"
            (click)="selector.click()"
            (dragover)="$event.preventDefault()"
            (drop)="soltar($event)"
          >
            @if (nombreArchivo()) {
              <svg lucideFileSpreadsheet class="h-9 w-9 text-success" aria-hidden="true"></svg>
              <span class="text-[15px] font-semibold">{{ nombreArchivo() }}</span>
              <span class="text-[13px] text-text-muted">Haz clic o arrastra otro archivo para reemplazarlo.</span>
            } @else {
              <svg lucideUpload class="h-9 w-9 text-text-muted" aria-hidden="true"></svg>
              <span class="text-[15px] font-semibold">Arrastra el Excel aquí o haz clic para elegirlo</span>
              <span class="text-[13px] text-text-muted">.xlsx · .csv — máximo 10 MB; se procesa en tu navegador</span>
            }
          </button>
          <input #selector type="file" accept=".xlsx,.csv" class="hidden" aria-label="Archivo de VINs" (change)="elegir($event)" />
          @if (errorLectura()) { <p role="alert" class="mt-3 text-[13px] text-danger">{{ errorLectura() }}</p> }
          @if (leyendo()) { <p role="status" class="mt-3 text-[13px] text-text-muted">Leyendo archivo…</p> }
        </div>
      </section>

      @if (hojaActual(); as hoja) {
        <!-- Paso 2: hoja y encabezados -->
        <section class="overflow-hidden rounded-xl border border-border bg-surface">
          <header class="border-b border-border px-5 py-4">
            <h2 class="text-[16px] font-semibold">2. Hoja y fila de encabezados</h2>
            <p class="text-[13px] text-text-muted">Los datos se leen desde la fila {{ filaEncabezado() + 1 }} en adelante</p>
          </header>
          <div class="p-5">
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
              <app-selector etiqueta="Hoja del archivo" [opciones]="opcionesHojas()" [valor]="hoja.nombre" (valorChange)="cambiarHoja($event)" />
              <app-campo-texto etiqueta="Fila de encabezados" [valor]="'' + filaEncabezado()" (valorChange)="cambiarFilaEncabezado($event)" />
            </div>
            <div class="mt-4 overflow-x-auto rounded-lg border border-border">
              <table class="min-w-full text-[13px]">
                <tbody>
                  <tr class="border-b border-border bg-bg">
                    <td class="px-3 py-2 font-semibold text-secondary">{{ filaEncabezado() }}</td>
                    @for (h of encabezados(); track $index) {
                      <td class="whitespace-nowrap px-3 py-2 font-semibold">{{ h || '—' }}</td>
                    }
                  </tr>
                  @for (f of vistaPrevia(); track f.fila) {
                    <tr class="border-b border-border last:border-0">
                      <td class="px-3 py-2 text-secondary">{{ f.fila }}</td>
                      @for (c of f.celdas; track $index) {
                        <td class="whitespace-nowrap px-3 py-2 text-text-muted">{{ c }}</td>
                      }
                    </tr>
                  }
                </tbody>
              </table>
            </div>
          </div>
        </section>

        <!-- Paso 3: mapeo -->
        <section class="overflow-hidden rounded-xl border border-border bg-surface">
          <header class="border-b border-border px-5 py-4">
            <h2 class="text-[16px] font-semibold">3. ¿Qué columna corresponde a cada dato?</h2>
            <p class="text-[13px] text-text-muted">Detectamos las columnas automáticamente; ajusta si hace falta</p>
          </header>
          <div class="p-5">
            <div class="grid grid-cols-1 gap-4 sm:grid-cols-2 lg:grid-cols-4">
              <div>
                <app-selector etiqueta="VIN *" marcador="— Selecciona —" [opciones]="opcionesColumnas()" [(valor)]="colVin" />
                <p class="mt-1 text-[12px] text-text-muted">A veces llega como “CHASIS”.</p>
              </div>
              <div>
                <app-selector etiqueta="Marca *" marcador="— No cargar —" [opciones]="opcionesColumnas()" [(valor)]="colMarca" />
                <p class="mt-1 text-[12px] text-text-muted">Columna del archivo o valor fijo.</p>
                @if (colMarca() === '') {
                  <app-campo-texto class="mt-2" marcador="Marca fija para todo el archivo (ej. Toyota)" [(valor)]="marcaFija" />
                }
              </div>
              <app-selector etiqueta="Modelo / Descripción" marcador="— No cargar —" [opciones]="opcionesColumnas()" [(valor)]="colModelo" />
              <app-selector etiqueta="Color" marcador="— No cargar —" [opciones]="opcionesColumnas()" [(valor)]="colColor" />
            </div>
            <div class="mt-5 max-w-xs border-t border-border pt-4">
              <app-selector etiqueta="Operación (para todo el archivo)" [opciones]="opcionesOperacion" [valor]="operacion()" (valorChange)="operacion.set($any($event))" />
            </div>
            <p class="mt-4 flex items-start gap-2 text-[13px] text-text-muted">
              <svg lucideInfo class="mt-0.5 h-4 w-4 shrink-0 text-secondary" aria-hidden="true"></svg>
              <span>Ruta de custodia: <b>Cargue</b> → <b>Transporte</b> → <b>Descargue</b>. El tipo de vehículo y la niñera se registran desde la app en el cargue.</span>
            </p>
          </div>
        </section>

        <!-- Paso 4: revisar y cargar -->
        <section class="overflow-hidden rounded-xl border border-border bg-surface">
          <header class="border-b border-border px-5 py-4"><h2 class="text-[16px] font-semibold">4. Revisar y cargar</h2></header>
          <div class="p-5">
            <div class="mb-4 flex flex-wrap gap-2">
              <app-insignia variante="info">{{ conVin() }} filas con VIN</app-insignia>
              <app-insignia variante="success">{{ listas().length }} listas</app-insignia>
              <app-insignia variante="warning">{{ omitidas() }} se omiten</app-insignia>
            </div>

            <div class="overflow-x-auto rounded-lg border border-border">
              <table class="min-w-full text-[13px]">
                <thead>
                  <tr class="border-b border-border text-left text-text-muted">
                    <th class="px-3 py-2 font-medium">Fila</th>
                    <th class="px-3 py-2 font-medium">VIN</th>
                    <th class="px-3 py-2 font-medium">Marca</th>
                    <th class="px-3 py-2 font-medium">Modelo</th>
                    <th class="px-3 py-2 font-medium">Color</th>
                    <th class="px-3 py-2 font-medium">Estado</th>
                  </tr>
                </thead>
                <tbody>
                  @for (r of muestra(); track r.fila) {
                    <tr class="border-b border-border last:border-0">
                      <td class="px-3 py-2 text-secondary">{{ r.fila }}</td>
                      <td class="whitespace-nowrap px-3 py-2 font-mono">@if (r.vin) { {{ r.vin }} } @else { <span class="text-danger">falta VIN</span> }</td>
                      <td class="px-3 py-2">@if (r.marca) { {{ r.marca }} } @else { <span class="text-warning">falta marca</span> }</td>
                      <td class="whitespace-nowrap px-3 py-2">{{ r.modelo }}</td>
                      <td class="whitespace-nowrap px-3 py-2">{{ r.color }}</td>
                      <td class="px-3 py-2">
                        @if (r.lista) {
                          <span class="inline-flex items-center gap-1 text-success"><svg lucideCircleCheck class="h-4 w-4" aria-hidden="true"></svg> Lista</span>
                        } @else {
                          <span class="inline-flex items-center gap-1 text-danger"><svg lucideXCircle class="h-4 w-4" aria-hidden="true"></svg> {{ r.faltaVin ? 'Falta VIN' : 'Falta marca' }}</span>
                        }
                      </td>
                    </tr>
                  }
                </tbody>
              </table>
            </div>
            @if (revisadas().length > muestra().length) {
              <p class="mt-2 text-[13px] text-text-muted">…y {{ revisadas().length - muestra().length }} filas más.</p>
            }

            @if (excedeLimite()) {
              <p role="alert" class="mt-4 text-[14px] text-danger">El archivo tiene {{ listas().length }} filas listas y el máximo por carga es {{ maximo }}. Divídelo en varios archivos.</p>
            }

            <div class="mt-5 flex flex-wrap items-center gap-3">
              <button appBoton [disabled]="listas().length === 0 || excedeLimite()" [cargando]="enviando()" (click)="cargar()">
                <svg lucideUpload class="h-4 w-4" aria-hidden="true"></svg> Cargar {{ listas().length }} VINs
              </button>
              @if (colVin() === '') {
                <span class="text-[13px] text-warning">Falta mapear el VIN{{ listas().length === 0 ? ' y definir la marca' : '' }}.</span>
              }
              @if (errorEnvio()) { <span role="alert" class="text-[14px] text-danger">{{ errorEnvio() }}</span> }
            </div>

            @if (resultado(); as res) {
              <div role="status" class="mt-5 rounded-lg border border-border bg-bg p-4">
                <p class="inline-flex items-center gap-1 text-[14px] font-medium text-success">
                  <svg lucideCircleCheck class="h-4 w-4" aria-hidden="true"></svg>
                  Se cargaron {{ res.creados + res.actualizados }} VIN(s): {{ res.creados }} nuevo(s), {{ res.actualizados }} actualizado(s){{ res.fallidos ? ', ' + res.fallidos + ' con error.' : '.' }}
                </p>
                @if (res.errores.length > 0) {
                  <table class="mt-3 w-full text-[13px]">
                    <thead><tr class="text-left text-text-muted"><th class="py-1 pr-3 font-medium">VIN</th><th class="py-1 font-medium">Motivo</th></tr></thead>
                    <tbody>
                      @for (e of res.errores; track $index) {
                        <tr class="border-t border-border"><td class="py-1 pr-3 font-mono">{{ e.vin }}</td><td class="py-1 text-danger">{{ e.error }}</td></tr>
                      }
                    </tbody>
                  </table>
                }
              </div>
            }
          </div>
        </section>
      }
    </div>
  `,
})
export class CargarVins {
  private readonly api = inject(ApiService);
  private readonly lector = inject(LectorExcel);

  protected readonly maximo = MAX_FILAS_CARGA;
  protected readonly nombreArchivo = signal('');
  protected readonly leyendo = signal(false);
  protected readonly errorLectura = signal('');
  private readonly hojas = signal<Hoja[]>([]);
  private readonly nombreHoja = signal('');
  protected readonly filaEncabezado = signal(1);

  protected readonly colVin = signal('');
  protected readonly colMarca = signal('');
  protected readonly colModelo = signal('');
  protected readonly colColor = signal('');
  protected readonly marcaFija = signal('');
  protected readonly operacion = signal<Operacion>('importacion');

  protected readonly enviando = signal(false);
  protected readonly errorEnvio = signal('');
  protected readonly resultado = signal<ResultadoCarga | null>(null);

  protected readonly opcionesOperacion: OpcionSelector[] = OPERACIONES.map((valor) => ({ valor, etiqueta: ETIQUETA_OPERACION[valor] }));

  protected readonly hojaActual = computed(() => this.hojas().find((h) => h.nombre === this.nombreHoja()) ?? null);
  protected readonly opcionesHojas = computed<OpcionSelector[]>(() =>
    this.hojas().map((h) => ({ valor: h.nombre, etiqueta: `${h.nombre} (${h.filas.filter((f) => f.some((c) => texto(c) !== '')).length} filas)` })),
  );
  protected readonly encabezados = computed(() => encabezadosDe(this.hojaActual()?.filas ?? [], this.filaEncabezado()));
  protected readonly opcionesColumnas = computed<OpcionSelector[]>(() =>
    this.encabezados().map((h, i) => ({ valor: String(i), etiqueta: `${letraColumna(i)} — ${h || `Columna ${letraColumna(i)}`}` })),
  );
  protected readonly vistaPrevia = computed(() => {
    const filas = this.hojaActual()?.filas ?? [];
    const columnas = this.encabezados().length;
    return filas
      .map((celdas, i) => ({ fila: i + 1, celdas: Array.from({ length: columnas }, (_, c) => texto(celdas[c])) }))
      .filter((f) => f.fila > this.filaEncabezado() && f.celdas.some((c) => c !== ''))
      .slice(0, FILAS_VISTA_PREVIA);
  });

  protected readonly mapeo = computed<Mapeo>(() => ({ vin: this.colVin(), marca: this.colMarca(), modelo: this.colModelo(), color: this.colColor() }));
  protected readonly revisadas = computed(() => revisarFilas(this.hojaActual()?.filas ?? [], this.filaEncabezado(), this.mapeo(), this.marcaFija()));
  protected readonly listas = computed(() => this.revisadas().filter((r) => r.lista));
  protected readonly conVin = computed(() => this.revisadas().filter((r) => r.vin).length);
  protected readonly omitidas = computed(() => this.revisadas().length - this.listas().length);
  protected readonly muestra = computed(() => this.revisadas().slice(0, FILAS_REVISION));
  protected readonly excedeLimite = computed(() => this.listas().length > MAX_FILAS_CARGA);

  protected elegir(evento: Event): void {
    const entrada = evento.target as HTMLInputElement;
    void this.abrir(entrada.files?.[0]);
    entrada.value = '';
  }

  protected soltar(evento: DragEvent): void {
    evento.preventDefault();
    void this.abrir(evento.dataTransfer?.files?.[0]);
  }

  protected cambiarHoja(nombre: string): void {
    this.nombreHoja.set(nombre);
    this.aplicarMapeoAutomatico();
  }

  protected cambiarFilaEncabezado(valor: string): void {
    this.filaEncabezado.set(Math.max(1, Math.floor(Number(valor)) || 1));
    this.aplicarMapeoAutomatico();
  }

  protected async cargar(): Promise<void> {
    const filas = this.listas();
    if (filas.length === 0 || this.excedeLimite()) return;
    this.enviando.set(true);
    this.errorEnvio.set('');
    this.resultado.set(null);
    try {
      const items = filas.map((r) => ({ vin: r.vin, marca: r.marca, modelo: r.modelo || null, color: r.color || null, operacion: this.operacion() }));
      this.resultado.set(await firstValueFrom(this.api.cargarVins({ items })));
    } catch (error) {
      this.errorEnvio.set(mensajeDeError(error));
    } finally {
      this.enviando.set(false);
    }
  }

  private async abrir(archivo: File | undefined): Promise<void> {
    if (!archivo) return;
    this.errorLectura.set('');
    this.errorEnvio.set('');
    this.resultado.set(null);
    this.leyendo.set(true);
    try {
      const hojas = await this.lector.leer(archivo);
      this.hojas.set(hojas);
      this.nombreArchivo.set(archivo.name);
      this.nombreHoja.set(hojas[0]?.nombre ?? '');
      this.filaEncabezado.set(1);
      this.aplicarMapeoAutomatico();
    } catch (error) {
      this.errorLectura.set(mensajeDeError(error));
    } finally {
      this.leyendo.set(false);
    }
  }

  private aplicarMapeoAutomatico(): void {
    const detectado = autoMapear(this.encabezados());
    this.colVin.set(detectado.vin);
    this.colMarca.set(detectado.marca);
    this.colModelo.set(detectado.modelo);
    this.colColor.set(detectado.color);
  }
}
