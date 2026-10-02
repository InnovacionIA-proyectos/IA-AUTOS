import { Injectable, inject } from '@angular/core';
import { Router } from '@angular/router';
import type { Credenciales } from './modelos';
import { PROVEEDOR_AUTENTICACION } from './proveedor-autenticacion';
import { SesionService } from './sesion.service';

/** Fachada de autenticación: coordina el proveedor activo, la sesión y la navegación. */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly proveedor = inject(PROVEEDOR_AUTENTICACION);
  private readonly sesion = inject(SesionService);
  private readonly router = inject(Router);

  async iniciarSesion(credenciales: Credenciales): Promise<void> {
    const sesion = await this.proveedor.iniciarSesion(credenciales);
    this.sesion.establecer(sesion);
  }

  /** Cierra la sesión y vuelve al login. `motivo` se muestra en la pantalla de inicio de sesión. */
  async cerrarSesion(motivo?: 'inactividad' | 'expirada'): Promise<void> {
    try {
      await this.proveedor.cerrarSesion();
    } finally {
      this.sesion.limpiar();
      await this.router.navigate(['/login'], { queryParams: motivo ? { motivo } : {} });
    }
  }

  /** Solo rutas internas: evita redirecciones abiertas a otros sitios tras el login. */
  static destinoSeguro(destino: string | null | undefined): string {
    return destino && destino.startsWith('/') && !destino.startsWith('//') && !destino.includes('://') ? destino : '/';
  }
}
