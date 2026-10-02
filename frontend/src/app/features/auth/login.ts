import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { LucideLogIn } from '@lucide/angular';
import { AuthService } from '../../core/auth/auth.service';
import { ErrorApi } from '../../core/errors/error-api';
import { Boton, CampoTexto, Tarjeta, TarjetaCuerpo } from '../../shared/ui';

const MENSAJES_MOTIVO: Record<string, string> = {
  inactividad: 'La sesión se cerró por inactividad. Vuelve a iniciar sesión.',
  expirada: 'Tu sesión expiró. Vuelve a iniciar sesión.',
};

@Component({
  selector: 'app-login',
  imports: [Boton, CampoTexto, Tarjeta, TarjetaCuerpo, LucideLogIn],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="flex min-h-[60vh] items-center justify-center">
      <div appTarjeta class="w-full max-w-sm">
        <div appTarjetaCuerpo class="!py-8">
          <div class="mb-6 text-center">
            <img src="logo-full.png" alt="Botero Soto — Soluciones Logísticas" class="mx-auto mb-5 w-40" />
            <h1 class="text-[20px] font-semibold leading-[26px]">Traza Autos 360°</h1>
            <p class="mt-1 text-[15px] text-text-muted">Inicia sesión con tu correo y contraseña.</p>
          </div>
          @if (aviso()) {
            <p class="mb-4 rounded-md bg-info/10 px-3 py-2 text-[14px] text-info" role="status">{{ aviso() }}</p>
          }
          <form class="flex flex-col gap-4" (submit)="enviar($event)">
            <app-campo-texto etiqueta="Correo" tipo="email" autocompletar="username" marcador="nombre@boterosoto.com.co" [(valor)]="email" />
            <app-campo-texto etiqueta="Contraseña" tipo="password" autocompletar="current-password" marcador="••••••••" [(valor)]="password" [error]="error()" />
            <button appBoton type="submit" tamano="lg" class="w-full" [cargando]="cargando()" [disabled]="!puedeEnviar()">
              <svg lucideLogIn class="h-4 w-4" aria-hidden="true"></svg>
              Ingresar
            </button>
          </form>
        </div>
      </div>
    </div>
  `,
})
export class Login {
  private readonly auth = inject(AuthService);
  private readonly router = inject(Router);
  private readonly ruta = inject(ActivatedRoute);

  protected readonly email = signal('');
  protected readonly password = signal('');
  protected readonly cargando = signal(false);
  protected readonly error = signal('');
  protected readonly aviso = computed(() => MENSAJES_MOTIVO[this.ruta.snapshot.queryParamMap.get('motivo') ?? ''] ?? '');
  protected readonly puedeEnviar = computed(() => this.email().trim().length > 3 && this.password().length >= 6 && !this.cargando());

  protected async enviar(evento: Event): Promise<void> {
    evento.preventDefault();
    if (!this.puedeEnviar()) return;
    this.cargando.set(true);
    this.error.set('');
    try {
      await this.auth.iniciarSesion({ email: this.email(), password: this.password() });
      const destino = AuthService.destinoSeguro(this.ruta.snapshot.queryParamMap.get('redirigir'));
      await this.router.navigateByUrl(destino, { replaceUrl: true });
    } catch (error) {
      this.error.set(mensajeLogin(error));
      this.cargando.set(false);
    }
  }
}

function mensajeLogin(error: unknown): string {
  if (error instanceof ErrorApi) {
    if (error.estado === 401) return 'Correo o contraseña incorrectos.';
    if (error.estado === 429) return 'Demasiados intentos. Espera un minuto e inténtalo de nuevo.';
  }
  return 'No se pudo iniciar sesión. Revisa tu conexión.';
}
