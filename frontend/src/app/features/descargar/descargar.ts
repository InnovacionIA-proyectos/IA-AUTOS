import { ChangeDetectionStrategy, Component, inject, resource } from '@angular/core';
import { LucideDownload, LucideLogIn, LucideShieldCheck, LucideSmartphone } from '@lucide/angular';
import { ServicioConfiguracion } from '../../core/config/servicio-configuracion';

/** Genera el QR (data URL) del enlace de descarga; se hace en el navegador para que siga a `apkUrl` de config.json. */
export async function generarQr(url: string): Promise<string> {
  const { toDataURL } = await import('qrcode');
  return toDataURL(url, { width: 168, margin: 1, errorCorrectionLevel: 'M' });
}

/** Página pública con el enlace (y QR) para instalar la app Android. El APK se aloja fuera de este repositorio. */
@Component({
  selector: 'app-descargar',
  imports: [LucideDownload, LucideLogIn, LucideShieldCheck, LucideSmartphone],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: `
    <div class="mx-auto max-w-[480px] px-1 py-2">
      <div class="rounded-2xl border border-border bg-surface p-6 text-center">
        <div class="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-primary">
          <svg lucideSmartphone class="h-8 w-8 text-white" aria-hidden="true"></svg>
        </div>
        <h1 class="text-[22px] font-semibold text-text-primary">Traza Autos 360°</h1>
        <p class="mb-5 text-[14px] text-text-muted">App móvil para Android · Botero Soto</p>

        @if (apkUrl) {
          <div class="mb-5 flex flex-col items-center gap-2">
            <div class="rounded-xl border border-border bg-white p-3">
              @if (qr.value(); as imagen) {
                <img [src]="imagen" alt="Código QR para descargar la app" width="168" height="168" class="block h-[168px] w-[168px]" />
              } @else {
                <div class="h-[168px] w-[168px]" aria-hidden="true"></div>
              }
            </div>
            <span class="text-[12px] text-text-muted">Escanea el QR para descargar</span>
          </div>

          <a [href]="apkUrl" class="flex items-center justify-center gap-2 rounded-xl bg-primary px-5 py-3.5 text-[16px] font-semibold text-white no-underline hover:bg-primary-hover">
            <svg lucideDownload class="h-5 w-5" aria-hidden="true"></svg>
            Descargar APK (Android)
          </a>
        } @else {
          <p role="status" class="rounded-md border border-border bg-bg px-4 py-3 text-[14px] text-text-muted">
            El enlace de descarga todavía no está configurado. Pide al administrador que lo defina.
          </p>
        }
      </div>

      <ol class="mt-6 flex flex-col gap-4">
        <li class="flex items-start gap-3">
          <svg lucideDownload class="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden="true"></svg>
          <div>
            <strong class="text-text-primary">1. Descarga el archivo</strong>
            <p class="mt-0.5 text-[14px] text-text-muted">Toca el botón. El archivo baja a tu carpeta <b>Descargas</b> (revisa la barra de notificaciones).</p>
          </div>
        </li>
        <li class="flex items-start gap-3">
          <svg lucideShieldCheck class="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden="true"></svg>
          <div>
            <strong class="text-text-primary">2. Instálalo</strong>
            <p class="mt-0.5 text-[14px] text-text-muted">Ábrelo desde Descargas. Si Android lo pide, permite instalar desde <b>orígenes desconocidos</b>.</p>
          </div>
        </li>
        <li class="flex items-start gap-3">
          <svg lucideLogIn class="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden="true"></svg>
          <div>
            <strong class="text-text-primary">3. Inicia sesión</strong>
            <p class="mt-0.5 text-[14px] text-text-muted">Usa tu correo corporativo y la contraseña que te asignaron. El botón <b>+</b> para una inspección nueva está abajo a la derecha.</p>
          </div>
        </li>
      </ol>

      @if (apkUrl) {
        <p class="mt-6 break-all text-[12px] text-text-muted">Enlace directo: <a [href]="apkUrl" class="text-primary">{{ apkUrl }}</a></p>
      }
    </div>
  `,
})
export class Descargar {
  protected readonly apkUrl = inject(ServicioConfiguracion).configuracion.apkUrl;
  protected readonly qr = resource({ loader: async () => (this.apkUrl ? generarQr(this.apkUrl) : null) });
}
