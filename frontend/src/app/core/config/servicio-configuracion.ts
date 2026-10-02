import { Injectable } from '@angular/core';

/** Cómo se autentica el usuario en la web. "sso" queda reservado (ver docs/autenticacion.md). */
export type ProveedorLogin = 'propio' | 'sso';

/**
 * Configuración en tiempo de ejecución (public/config.json). Un mismo build sirve para todos los entornos:
 * cada despliegue monta su propio config.json. NUNCA debe contener secretos (es un archivo público).
 */
export interface ConfiguracionApp {
  /** URL base de la API (sin barra final). Vacío = mismo origen. */
  apiUrl: string;
  /** Enlace de descarga del APK de la app móvil. */
  apkUrl: string;
  /** Minutos de inactividad tras los cuales se cierra la sesión. 0 desactiva el cierre. */
  inactividadMinutos: number;
  proveedorLogin: ProveedorLogin;
}

export const CONFIGURACION_POR_DEFECTO: ConfiguracionApp = {
  apiUrl: '',
  apkUrl: '',
  inactividadMinutos: 30,
  proveedorLogin: 'propio',
};

@Injectable({ providedIn: 'root' })
export class ServicioConfiguracion {
  private valores: ConfiguracionApp = { ...CONFIGURACION_POR_DEFECTO };

  get configuracion(): ConfiguracionApp {
    return this.valores;
  }

  get apiUrl(): string {
    return this.valores.apiUrl;
  }

  /** Carga config.json antes de arrancar la app. Si no existe o es inválido, usa los valores por defecto. */
  async cargar(): Promise<void> {
    try {
      const respuesta = await fetch('config.json', { cache: 'no-store' });
      if (!respuesta.ok) return;
      const leido = (await respuesta.json()) as Partial<ConfiguracionApp>;
      this.establecer(leido);
    } catch {
      // Sin config.json se usan los valores por defecto.
    }
  }

  establecer(parcial: Partial<ConfiguracionApp>): void {
    this.valores = {
      apiUrl: (parcial.apiUrl ?? this.valores.apiUrl).replace(/\/+$/, ''),
      apkUrl: parcial.apkUrl ?? this.valores.apkUrl,
      inactividadMinutos: parcial.inactividadMinutos ?? this.valores.inactividadMinutos,
      proveedorLogin: parcial.proveedorLogin === 'sso' ? 'sso' : 'propio',
    };
  }
}
