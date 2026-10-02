import { ChangeDetectionStrategy, Component, booleanAttribute, computed, input } from '@angular/core';
import { LucideLoader2 } from '@lucide/angular';

export type VarianteBoton = 'primary' | 'secondary' | 'ghost' | 'danger';
export type TamanoBoton = 'sm' | 'md' | 'lg';

const VARIANTES: Record<VarianteBoton, string> = {
  primary: 'bg-primary text-white hover:bg-primary-hover',
  secondary: 'bg-secondary text-white hover:bg-primary',
  ghost: 'bg-transparent text-primary hover:bg-surface',
  danger: 'bg-danger text-white hover:opacity-90',
};

const TAMANOS: Record<TamanoBoton, string> = {
  sm: 'h-9 px-3 text-[13px]',
  md: 'h-11 px-4 text-[15px]',
  lg: 'h-[52px] px-6 text-[15px]',
};

/** Botón con variantes y estado de carga. Uso: `<button appBoton variante="primary" [cargando]="x">Texto</button>`. */
@Component({
  selector: 'button[appBoton]',
  imports: [LucideLoader2],
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: {
    '[class]': 'clases()',
    '[disabled]': 'disabled() || cargando()',
    '[attr.aria-busy]': 'cargando() ? "true" : null',
  },
  template: `
    @if (cargando()) {
      <svg lucideLoader2 class="h-4 w-4 animate-spin"></svg>
    }
    <ng-content />
  `,
})
export class Boton {
  readonly variante = input<VarianteBoton>('primary');
  readonly tamano = input<TamanoBoton>('md');
  readonly cargando = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });

  protected readonly clases = computed(
    () =>
      `inline-flex items-center justify-center gap-2 rounded-md font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary focus-visible:ring-offset-1 disabled:cursor-not-allowed disabled:opacity-50 ${VARIANTES[this.variante()]} ${TAMANOS[this.tamano()]}`,
  );
}
