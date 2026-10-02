import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink } from '@angular/router';
import { LucideCompass } from '@lucide/angular';
import { AuthService } from '../../core/auth/auth.service';
import { SesionService } from '../../core/auth/sesion.service';

/** Sesión válida pero sin permisos para el área solicitada. */
@Component({
  selector: 'app-acceso-restringido',
  imports: [RouterLink],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-md rounded-lg border border-border bg-surface p-6 text-center">
      <h1 class="text-[18px] font-semibold text-text-primary">Acceso restringido</h1>
      <p class="mt-2 text-[15px] text-text-muted">Tu cuenta no tiene permisos para esta sección.</p>
      <div class="mt-4 flex justify-center gap-2">
        @if (puedeVolver()) {
          <a routerLink="/" class="rounded-md border border-border px-4 py-2 text-[15px] font-semibold text-primary">Volver a la web</a>
        }
        <button type="button" (click)="cerrarSesion()" class="rounded-md bg-primary px-4 py-2 text-[15px] font-semibold text-white">Cerrar sesión</button>
      </div>
    </div>
  `,
})
export class AccesoRestringido {
  private readonly auth = inject(AuthService);
  private readonly sesion = inject(SesionService);

  protected readonly puedeVolver = computed(() => this.sesion.tieneRol(['admin', 'super_admin']));

  protected cerrarSesion(): void {
    void this.auth.cerrarSesion();
  }
}

@Component({
  selector: 'app-no-encontrado',
  imports: [RouterLink, LucideCompass],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex flex-col items-center justify-center gap-3 py-24 text-center">
      <svg lucideCompass class="h-10 w-10 text-text-muted" aria-hidden="true"></svg>
      <h1 class="text-[24px] font-semibold leading-[30px]">404 — Página no encontrada</h1>
      <p class="max-w-md text-[15px] text-text-muted">La ruta que buscas no existe o fue movida.</p>
      <a routerLink="/" class="mt-2 inline-flex h-11 items-center rounded-md bg-primary px-4 text-[15px] font-semibold text-white transition-colors hover:bg-primary-hover focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary">Volver al tablero</a>
    </div>
  `,
})
export class NoEncontrado {}
