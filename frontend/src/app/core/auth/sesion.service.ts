import { Injectable, computed, signal } from '@angular/core';
import type { Rol } from '../api/api-tipos';
import type { Sesion } from './modelos';

/** Clave de la sesión en sessionStorage (se pierde al cerrar la pestaña; no se usa localStorage). */
export const CLAVE_SESION = 'ivvin.sesion.v2';

/**
 * Estado de la sesión del usuario. El token vive en memoria (signal) y se refleja en sessionStorage solo para
 * sobrevivir a una recarga de la pestaña; a diferencia del sistema anterior no persiste en localStorage entre sesiones
 * del navegador, lo que reduce la ventana de exposición ante XSS.
 */
@Injectable({ providedIn: 'root' })
export class SesionService {
  private readonly estado = signal<Sesion | null>(this.leer());

  readonly sesion = this.estado.asReadonly();
  readonly usuario = computed(() => this.estado()?.usuario ?? null);
  readonly token = computed(() => this.estado()?.token ?? null);
  readonly rol = computed<Rol | null>(() => this.estado()?.usuario.rol ?? null);
  readonly autenticado = computed(() => !!this.estado()?.token);

  establecer(sesion: Sesion): void {
    this.estado.set(sesion);
    try {
      sessionStorage.setItem(CLAVE_SESION, JSON.stringify(sesion));
    } catch {
      // Sin sessionStorage la sesión queda solo en memoria.
    }
  }

  limpiar(): void {
    this.estado.set(null);
    try {
      sessionStorage.removeItem(CLAVE_SESION);
    } catch {
      // Nada que limpiar.
    }
  }

  tieneRol(roles: readonly Rol[]): boolean {
    const rol = this.rol();
    return rol !== null && roles.includes(rol);
  }

  private leer(): Sesion | null {
    try {
      const crudo = sessionStorage.getItem(CLAVE_SESION);
      if (!crudo) return null;
      const sesion = JSON.parse(crudo) as Sesion;
      return sesion?.token && sesion.usuario?.rol ? sesion : null;
    } catch {
      return null;
    }
  }
}
