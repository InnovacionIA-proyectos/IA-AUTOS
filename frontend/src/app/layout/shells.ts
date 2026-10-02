import { ChangeDetectionStrategy, Component, computed, inject } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import {
  LucideArrowLeft,
  LucideBarChart3,
  LucideCar,
  LucideClipboardList,
  LucideLogOut,
  LucideTruck,
  LucideUpload,
  LucideUsers,
} from '@lucide/angular';
import { AuthService } from '../core/auth/auth.service';
import { SesionService } from '../core/auth/sesion.service';

const CLASES_PRINCIPAL = 'mx-auto w-full max-w-6xl flex-1 px-4 py-8';

@Component({
  selector: 'app-pie',
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <footer class="border-t border-border bg-bg">
      <div class="mx-auto max-w-6xl px-4 py-4 text-[13px] text-text-muted">Botero Soto · Traza Autos 360°</div>
    </footer>
  `,
})
export class Pie {}

/** Áreas públicas (login y descarga del APK): sin navegación. */
@Component({
  selector: 'app-shell-publico',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `<main class="${CLASES_PRINCIPAL}"><router-outlet /></main>`,
})
export class ShellPublico {}

/** Web de informes (admin y super_admin): encabezado con navegación, contenido y pie. */
@Component({
  selector: 'app-shell-informes',
  imports: [RouterOutlet, RouterLink, RouterLinkActive, Pie, LucideBarChart3, LucideClipboardList, LucideCar, LucideTruck, LucideUpload, LucideUsers, LucideLogOut],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="sticky top-0 z-40 border-b border-primary-hover bg-primary text-white">
      <div class="mx-auto flex h-14 max-w-6xl items-center gap-6 px-4">
        <a routerLink="/" class="flex items-center gap-2 text-[17px] font-semibold">
          <span class="flex h-8 w-8 items-center justify-center rounded-md bg-white p-1 shadow-sm">
            <img src="logo-mark.png" alt="Botero Soto" class="h-full w-full object-contain" />
          </span>
          Traza Autos 360°
        </a>
        <nav class="flex items-center gap-1" aria-label="Principal">
          <a routerLink="/" [routerLinkActiveOptions]="{ exact: true }" routerLinkActive="bg-white/15 font-semibold" class="flex items-center gap-2 rounded-md px-3 py-2 text-[15px] text-white/80 transition-colors hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white">
            <svg lucideBarChart3 class="h-4 w-4" aria-hidden="true"></svg>Tablero
          </a>
          <a routerLink="/inspecciones" routerLinkActive="bg-white/15 font-semibold" class="flex items-center gap-2 rounded-md px-3 py-2 text-[15px] text-white/80 transition-colors hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white">
            <svg lucideClipboardList class="h-4 w-4" aria-hidden="true"></svg>Inspecciones
          </a>
          <a routerLink="/inventario" routerLinkActive="bg-white/15 font-semibold" class="flex items-center gap-2 rounded-md px-3 py-2 text-[15px] text-white/80 transition-colors hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white">
            <svg lucideCar class="h-4 w-4" aria-hidden="true"></svg>Inventario
          </a>
          <a routerLink="/sofasa" routerLinkActive="bg-white/15 font-semibold" class="flex items-center gap-2 rounded-md px-3 py-2 text-[15px] text-white/80 transition-colors hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white">
            <svg lucideTruck class="h-4 w-4" aria-hidden="true"></svg>Sofasa
          </a>
          <a routerLink="/cargar-vins" routerLinkActive="bg-white/15 font-semibold" class="flex items-center gap-2 rounded-md px-3 py-2 text-[15px] text-white/80 transition-colors hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white">
            <svg lucideUpload class="h-4 w-4" aria-hidden="true"></svg>Cargar VINs
          </a>
        </nav>
        @if (nombre()) {
          <div class="ml-auto flex items-center gap-3">
            @if (esSuperAdmin()) {
              <a routerLink="/usuarios" title="Administración de usuarios" class="flex items-center gap-2 rounded-md px-3 py-2 text-[14px] text-white/80 transition-colors hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white">
                <svg lucideUsers class="h-4 w-4" aria-hidden="true"></svg>
                <span class="hidden sm:inline">Usuarios</span>
              </a>
            }
            <span class="hidden text-[14px] text-white/80 sm:inline">{{ nombre() }}</span>
            <button type="button" (click)="cerrarSesion()" class="flex items-center gap-2 rounded-md px-3 py-2 text-[14px] text-white/80 transition-colors hover:bg-white/10 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-white">
              <svg lucideLogOut class="h-4 w-4" aria-hidden="true"></svg>Cerrar sesión
            </button>
          </div>
        }
      </div>
    </header>
    <main class="${CLASES_PRINCIPAL}"><router-outlet /></main>
    <app-pie />
  `,
})
export class ShellInformes {
  private readonly sesion = inject(SesionService);
  private readonly auth = inject(AuthService);

  protected readonly nombre = computed(() => this.sesion.usuario()?.nombre ?? null);
  protected readonly esSuperAdmin = computed(() => this.sesion.rol() === 'super_admin');

  protected cerrarSesion(): void {
    void this.auth.cerrarSesion();
  }
}

/** Área separada de gestión de usuarios (solo super_admin), con encabezado propio. */
@Component({
  selector: 'app-shell-gestion',
  imports: [RouterOutlet, RouterLink, Pie, LucideArrowLeft, LucideLogOut],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <header class="sticky top-0 z-40 border-b border-border bg-text-primary text-white">
      <div class="mx-auto flex h-14 max-w-6xl items-center gap-3 px-4">
        <span class="flex h-8 w-8 items-center justify-center rounded-md bg-white p-1 shadow-sm">
          <img src="logo-mark.png" alt="Botero Soto" class="h-full w-full object-contain" />
        </span>
        <span class="text-[17px] font-semibold">Administración de usuarios</span>
        <a routerLink="/" class="flex items-center gap-2 rounded-md px-3 py-2 text-[14px] text-white/80 hover:bg-white/10">
          <svg lucideArrowLeft class="h-4 w-4" aria-hidden="true"></svg>Volver a la web de informes
        </a>
        <button type="button" (click)="cerrarSesion()" class="ml-auto flex items-center gap-2 rounded-md px-3 py-2 text-[14px] text-white/80 hover:bg-white/10">
          <svg lucideLogOut class="h-4 w-4" aria-hidden="true"></svg>Cerrar sesión
        </button>
      </div>
    </header>
    <main class="${CLASES_PRINCIPAL}"><router-outlet /></main>
    <app-pie />
  `,
})
export class ShellGestion {
  private readonly auth = inject(AuthService);

  protected cerrarSesion(): void {
    void this.auth.cerrarSesion();
  }
}
