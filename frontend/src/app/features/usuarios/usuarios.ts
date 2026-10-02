import { ChangeDetectionStrategy, Component, computed, inject, resource, signal } from '@angular/core';
import { LucidePencil, LucidePlus, LucideRotateCcw, LucideTrash2 } from '@lucide/angular';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import type { Usuario } from '../../core/api/api-tipos';
import { SesionService } from '../../core/auth/sesion.service';
import { mensajeDeError } from '../../core/errors/error-api';
import { Boton, CampoTexto, Columna, EstadoError, Insignia, Modal, OpcionSelector, PlantillaColumna, Selector, TablaDatos } from '../../shared/ui';

const ROLES_GESTIONABLES: OpcionSelector[] = [
  { valor: 'super_admin', etiqueta: 'Super administrador' },
  { valor: 'admin', etiqueta: 'Administrador' },
  { valor: 'inspector', etiqueta: 'Inspector' },
  { valor: 'conductor', etiqueta: 'Conductor' },
];

const ETIQUETA_ROL: Record<string, string> = {
  ...Object.fromEntries(ROLES_GESTIONABLES.map((r) => [r.valor, r.etiqueta])),
  coordinador_calidad: 'Coordinador',
  comercial: 'Comercial',
};

const LARGO_MINIMO_CLAVE = 6;
const PATRON_CORREO = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

type ModoFormulario = 'crear' | 'editar';

/** Gestión de usuarios (solo super_admin): crear, editar, retirar/eliminar y reactivar. */
@Component({
  selector: 'app-usuarios',
  imports: [Boton, CampoTexto, EstadoError, Insignia, Modal, PlantillaColumna, Selector, TablaDatos, LucidePencil, LucidePlus, LucideRotateCcw, LucideTrash2],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col gap-6">
      <div class="flex items-start justify-between gap-4">
        <div>
          <h1 class="text-[24px] font-semibold leading-[30px]">Usuarios</h1>
          <p class="mt-1 text-[15px] text-text-muted">
            Crea, edita, retira y reactiva usuarios. Super administrador gestiona usuarios y ve la web; Administrador solo ve la web;
            Inspector y Conductor usan la app móvil.
          </p>
        </div>
        <button appBoton (click)="abrirCrear()"><svg lucidePlus class="h-4 w-4" aria-hidden="true"></svg> Nuevo usuario</button>
      </div>

      @if (aviso()) {
        <div role="status" class="rounded-md border border-border bg-surface px-4 py-2 text-[14px] text-text-primary">{{ aviso() }}</div>
      }

      <div class="flex flex-wrap items-end justify-between gap-2">
        <div class="w-full sm:w-80">
          <app-campo-texto etiqueta="Buscar por nombre o correo" tipo="search" marcador="Todos" [(valor)]="busqueda" />
        </div>
        @if (usuarios.hasValue()) {
          <span class="text-[14px] text-text-muted">{{ filtrados().length }} {{ filtrados().length === 1 ? 'usuario' : 'usuarios' }}</span>
        }
      </div>

      @if (error()) {
        <app-estado-error [mensaje]="error()" (reintentar)="usuarios.reload()" />
      } @else {
        <app-tabla-datos
          [columnas]="columnas"
          [filas]="filtrados()"
          [cargando]="usuarios.isLoading()"
          [tituloVacio]="busqueda() ? 'Sin coincidencias' : 'Sin usuarios'"
          [descripcionVacia]="busqueda() ? 'Ningún usuario coincide con la búsqueda.' : 'Crea el primer usuario con Nuevo usuario.'"
        >
          <ng-template appColumna="rol" let-u>
            <app-insignia [variante]="u.rol === 'super_admin' || u.rol === 'admin' ? 'info' : 'neutral'">{{ etiquetaRol(u.rol) }}</app-insignia>
          </ng-template>
          <ng-template appColumna="estado" let-u>
            <app-insignia [variante]="u.activo ? 'success' : 'neutral'">{{ u.activo ? 'Activo' : 'Retirado' }}</app-insignia>
          </ng-template>
          <ng-template appColumna="acciones" let-u>
            <div class="flex justify-end gap-1">
              <button type="button" class="rounded p-2 text-text-muted hover:bg-surface hover:text-primary" [attr.aria-label]="'Editar ' + u.nombre" (click)="abrirEditar(u)">
                <svg lucidePencil class="h-4 w-4" aria-hidden="true"></svg>
              </button>
              @if (u.activo) {
                <button
                  type="button"
                  class="rounded p-2 text-text-muted hover:bg-surface hover:text-danger disabled:opacity-40"
                  [attr.aria-label]="'Eliminar ' + u.nombre"
                  [disabled]="u.id === yoId()"
                  (click)="pedirEliminar(u)"
                >
                  <svg lucideTrash2 class="h-4 w-4" aria-hidden="true"></svg>
                </button>
              } @else {
                <button type="button" class="rounded p-2 text-text-muted hover:bg-surface hover:text-success" [attr.aria-label]="'Reactivar ' + u.nombre" title="Reactivar" (click)="reactivar(u)">
                  <svg lucideRotateCcw class="h-4 w-4" aria-hidden="true"></svg>
                </button>
              }
            </div>
          </ng-template>
        </app-tabla-datos>
      }
    </div>

    <app-modal [abierto]="formulario() !== null" [titulo]="formulario() === 'crear' ? 'Nuevo usuario' : 'Editar usuario'" (cerrar)="cerrarFormulario()">
      <form id="usuario-form" class="flex flex-col gap-3" (submit)="guardar($event)" novalidate>
        <app-campo-texto etiqueta="Nombre" [(valor)]="nombre" [error]="intentado() ? errores().nombre : ''" />
        <app-campo-texto etiqueta="Correo" tipo="email" autocompletar="off" [(valor)]="correo" [error]="intentado() ? errores().correo : ''" />
        <app-selector etiqueta="Rol" [opciones]="roles" [(valor)]="rol" />
        <app-campo-texto
          [etiqueta]="formulario() === 'crear' ? 'Contraseña' : 'Nueva contraseña (opcional)'"
          tipo="password"
          autocompletar="new-password"
          [marcador]="formulario() === 'editar' ? 'Dejar en blanco para no cambiar' : 'Mínimo 6 caracteres'"
          [(valor)]="clave"
          [error]="intentado() ? errores().clave : ''"
        />
        @if (errorGuardar()) {
          <p role="alert" class="text-[14px] text-danger">{{ errorGuardar() }}</p>
        }
      </form>
      <div pie>
        <button appBoton variante="ghost" type="button" (click)="cerrarFormulario()">Cancelar</button>
        <button appBoton type="submit" form="usuario-form" [cargando]="guardando()">{{ formulario() === 'crear' ? 'Crear' : 'Guardar' }}</button>
      </div>
    </app-modal>

    <app-modal [abierto]="aEliminar() !== null" titulo="Eliminar usuario" (cerrar)="aEliminar.set(null)">
      <p>¿Eliminar a <strong>{{ aEliminar()?.nombre }}</strong> ({{ aEliminar()?.email }})?</p>
      <p class="mt-2 text-[14px] text-text-muted">
        Si el usuario ya realizó operaciones, no se borra: se marca como <strong>retirado</strong> (no podrá ingresar, pero se conserva su historial para consultas).
      </p>
      @if (errorEliminar()) {
        <p role="alert" class="mt-3 text-[14px] text-danger">{{ errorEliminar() }}</p>
      }
      <div pie>
        <button appBoton variante="ghost" (click)="aEliminar.set(null)">Cancelar</button>
        <button appBoton variante="danger" [cargando]="eliminando()" (click)="eliminar()">Eliminar</button>
      </div>
    </app-modal>
  `,
})
export class Usuarios {
  private readonly api = inject(ApiService);
  private readonly sesion = inject(SesionService);
  protected readonly yoId = computed(() => this.sesion.usuario()?.id);

  protected readonly usuarios = resource({ loader: () => firstValueFrom(this.api.listarUsuarios()) });
  protected readonly error = computed(() => (this.usuarios.error() ? mensajeDeError(this.usuarios.error()) : ''));
  protected readonly busqueda = signal('');
  protected readonly aviso = signal('');

  protected readonly filtrados = computed(() => {
    const q = this.busqueda().trim().toLowerCase();
    const todos = this.usuarios.value() ?? [];
    return q ? todos.filter((u) => u.nombre.toLowerCase().includes(q) || u.email.toLowerCase().includes(q)) : todos;
  });

  protected readonly roles = ROLES_GESTIONABLES;
  protected readonly columnas: Columna<Usuario>[] = [
    { clave: 'nombre', encabezado: 'Nombre', texto: (u) => u.nombre },
    { clave: 'email', encabezado: 'Correo', texto: (u) => u.email },
    { clave: 'rol', encabezado: 'Rol' },
    { clave: 'estado', encabezado: 'Estado' },
    { clave: 'acciones', encabezado: '', clase: 'w-28' },
  ];
  protected readonly etiquetaRol = (rol: string): string => ETIQUETA_ROL[rol] ?? rol;

  // --- Formulario crear / editar
  protected readonly formulario = signal<ModoFormulario | null>(null);
  private editando: Usuario | null = null;
  protected readonly nombre = signal('');
  protected readonly correo = signal('');
  protected readonly rol = signal('inspector');
  protected readonly clave = signal('');
  protected readonly intentado = signal(false);
  protected readonly guardando = signal(false);
  protected readonly errorGuardar = signal('');

  protected readonly errores = computed(() => {
    const crear = this.formulario() === 'crear';
    const clave = this.clave();
    return {
      nombre: this.nombre().trim() ? '' : 'El nombre es obligatorio.',
      correo: PATRON_CORREO.test(this.correo().trim()) ? '' : 'Ingresa un correo válido.',
      clave: (crear || clave) && clave.length < LARGO_MINIMO_CLAVE ? `La contraseña debe tener al menos ${LARGO_MINIMO_CLAVE} caracteres.` : '',
    };
  });

  // --- Eliminación
  protected readonly aEliminar = signal<Usuario | null>(null);
  protected readonly eliminando = signal(false);
  protected readonly errorEliminar = signal('');

  protected abrirCrear(): void {
    this.editando = null;
    this.prepararFormulario('crear', '', '', 'inspector');
  }

  protected abrirEditar(usuario: Usuario): void {
    this.editando = usuario;
    const rol = ROLES_GESTIONABLES.some((r) => r.valor === usuario.rol) ? usuario.rol : 'inspector';
    this.prepararFormulario('editar', usuario.nombre, usuario.email, rol);
  }

  protected cerrarFormulario(): void {
    this.formulario.set(null);
  }

  protected async guardar(evento: Event): Promise<void> {
    evento.preventDefault();
    this.intentado.set(true);
    const e = this.errores();
    if (e.nombre || e.correo || e.clave) return;

    const modo = this.formulario();
    const cuerpo = { nombre: this.nombre().trim(), email: this.correo().trim(), rol: this.rol(), subtipo_inspector: null };
    this.guardando.set(true);
    this.errorGuardar.set('');
    try {
      if (modo === 'crear') {
        await firstValueFrom(this.api.crearUsuario({ ...cuerpo, password: this.clave() }));
        this.aviso.set(`Usuario ${cuerpo.nombre} creado.`);
      } else if (this.editando) {
        await firstValueFrom(this.api.actualizarUsuario(this.editando.id, { ...cuerpo, password: this.clave() || null, activo: null }));
        this.aviso.set(`Usuario ${cuerpo.nombre} actualizado.`);
      }
      this.formulario.set(null);
      this.usuarios.reload();
    } catch (error) {
      this.errorGuardar.set(mensajeDeError(error));
    } finally {
      this.guardando.set(false);
    }
  }

  protected pedirEliminar(usuario: Usuario): void {
    this.errorEliminar.set('');
    this.aEliminar.set(usuario);
  }

  protected async eliminar(): Promise<void> {
    const usuario = this.aEliminar();
    if (!usuario) return;
    this.eliminando.set(true);
    this.errorEliminar.set('');
    try {
      const respuesta = await firstValueFrom(this.api.eliminarUsuario(usuario.id));
      this.aviso.set(
        respuesta.modo === 'retirado'
          ? `${usuario.nombre} tenía operaciones: se marcó como retirado (se conserva su historial).`
          : `${usuario.nombre} fue eliminado.`,
      );
      this.aEliminar.set(null);
      this.usuarios.reload();
    } catch (error) {
      this.errorEliminar.set(mensajeDeError(error));
    } finally {
      this.eliminando.set(false);
    }
  }

  protected async reactivar(usuario: Usuario): Promise<void> {
    this.aviso.set('');
    try {
      await firstValueFrom(this.api.actualizarUsuario(usuario.id, { nombre: null, email: null, rol: null, subtipo_inspector: null, password: null, activo: true }));
      this.aviso.set(`${usuario.nombre} fue reactivado.`);
      this.usuarios.reload();
    } catch (error) {
      this.aviso.set(mensajeDeError(error));
    }
  }

  private prepararFormulario(modo: ModoFormulario, nombre: string, correo: string, rol: string): void {
    this.nombre.set(nombre);
    this.correo.set(correo);
    this.rol.set(rol);
    this.clave.set('');
    this.intentado.set(false);
    this.errorGuardar.set('');
    this.formulario.set(modo);
  }
}
