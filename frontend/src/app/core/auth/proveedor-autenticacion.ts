import { Injectable, InjectionToken, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { ApiService } from '../api/api.service';
import { ServicioConfiguracion, type ProveedorLogin } from '../config/servicio-configuracion';
import type { Credenciales, Sesion } from './modelos';

/**
 * Abstracción del inicio de sesión. El resto de la aplicación depende solo de esta interfaz (y de la sesión), de modo que
 * cambiar de login propio a SSO (OIDC con PKCE) consiste en registrar otro proveedor, sin tocar páginas ni guards.
 */
export interface ProveedorAutenticacion {
  readonly tipo: ProveedorLogin;
  /** Autentica y devuelve la sesión (token de la API y datos del usuario). */
  iniciarSesion(credenciales: Credenciales): Promise<Sesion>;
  /** Cierra la sesión en el proveedor (el login propio no tiene nada que revocar en el servidor). */
  cerrarSesion(): Promise<void>;
}

/** Login propio: correo y contraseña contra POST /api/auth/login de la API. */
@Injectable({ providedIn: 'root' })
export class ProveedorLoginPropio implements ProveedorAutenticacion {
  readonly tipo = 'propio' as const;
  private readonly api = inject(ApiService);

  async iniciarSesion(credenciales: Credenciales): Promise<Sesion> {
    const { token, usuario } = await firstValueFrom(this.api.login(credenciales.email.trim(), credenciales.password));
    return { token, usuario: { id: usuario.id, nombre: usuario.nombre, email: usuario.email, rol: usuario.rol as Sesion['usuario']['rol'] } };
  }

  async cerrarSesion(): Promise<void> {
    // Nada que revocar en el servidor: el token se descarta en el cliente.
  }
}

/** Marcador del escenario SSO: documentado en docs/autenticacion.md, aún sin proveedor configurado. */
@Injectable({ providedIn: 'root' })
export class ProveedorSsoNoConfigurado implements ProveedorAutenticacion {
  readonly tipo = 'sso' as const;

  async iniciarSesion(): Promise<Sesion> {
    throw new Error('El inicio de sesión SSO aún no está configurado. Use proveedorLogin = "propio" en config.json.');
  }

  async cerrarSesion(): Promise<void> {
    // Sin sesión en proveedor externo.
  }
}

export const PROVEEDOR_AUTENTICACION = new InjectionToken<ProveedorAutenticacion>('PROVEEDOR_AUTENTICACION', {
  providedIn: 'root',
  factory: () => {
    const { proveedorLogin } = inject(ServicioConfiguracion).configuracion;
    return proveedorLogin === 'sso' ? inject(ProveedorSsoNoConfigurado) : inject(ProveedorLoginPropio);
  },
});
