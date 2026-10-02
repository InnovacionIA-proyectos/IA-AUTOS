import { DOCUMENT } from '@angular/common';
import { DestroyRef, Injectable, effect, inject } from '@angular/core';
import { ServicioConfiguracion } from '../config/servicio-configuracion';
import { AuthService } from './auth.service';
import { SesionService } from './sesion.service';

const EVENTOS_ACTIVIDAD = ['pointerdown', 'keydown', 'wheel', 'touchstart'] as const;

/**
 * Cierra la sesión tras N minutos sin actividad del usuario (config.json: inactividadMinutos; 0 lo desactiva).
 * Reduce el riesgo de una sesión abierta y desatendida (el token de la API dura 30 días).
 */
@Injectable({ providedIn: 'root' })
export class InactividadService {
  private readonly documento = inject(DOCUMENT);
  private readonly sesion = inject(SesionService);
  private readonly auth = inject(AuthService);
  private readonly configuracion = inject(ServicioConfiguracion);
  private temporizador: ReturnType<typeof setTimeout> | null = null;
  private readonly alActividad = () => this.armar();

  constructor() {
    effect(() => {
      if (this.sesion.autenticado()) this.iniciar();
      else this.detener();
    });
    inject(DestroyRef).onDestroy(() => this.detener());
  }

  private get minutos(): number {
    return this.configuracion.configuracion.inactividadMinutos;
  }

  private iniciar(): void {
    if (this.minutos <= 0) return;
    for (const evento of EVENTOS_ACTIVIDAD) this.documento.addEventListener(evento, this.alActividad, { passive: true });
    this.armar();
  }

  private detener(): void {
    for (const evento of EVENTOS_ACTIVIDAD) this.documento.removeEventListener(evento, this.alActividad);
    if (this.temporizador) clearTimeout(this.temporizador);
    this.temporizador = null;
  }

  private armar(): void {
    if (this.temporizador) clearTimeout(this.temporizador);
    this.temporizador = setTimeout(() => void this.auth.cerrarSesion('inactividad'), this.minutos * 60_000);
  }
}
