import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

export type VarianteInsignia = 'success' | 'warning' | 'danger' | 'info' | 'neutral';

// El verde de marca no lleva texto blanco; los tintes usan texto del mismo tono oscuro.
const VARIANTES: Record<VarianteInsignia, string> = {
  success: 'bg-success/10 text-success',
  warning: 'bg-warning/15 text-[#8A5300]',
  danger: 'bg-danger/10 text-danger',
  info: 'bg-info/10 text-info',
  neutral: 'bg-surface text-text-muted',
};

@Component({
  selector: 'app-insignia',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { '[class]': 'clases()' },
  template: '<ng-content />',
})
export class Insignia {
  readonly variante = input<VarianteInsignia>('neutral');

  protected readonly clases = computed(
    () => `inline-flex items-center rounded-full px-2.5 py-0.5 text-[13px] font-semibold ${VARIANTES[this.variante()]}`,
  );
}
