import { ChangeDetectionStrategy, Component, booleanAttribute, computed, input, model } from '@angular/core';

let contador = 0;

/** Campo de texto con etiqueta y error. Enlace bidireccional: `[(valor)]`. */
@Component({
  selector: 'app-campo-texto',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex w-full flex-col gap-1' },
  template: `
    @if (etiqueta()) {
      <label [for]="idCampo" class="text-[13px] font-semibold text-text-primary">{{ etiqueta() }}</label>
    }
    <input
      [id]="idCampo"
      [type]="tipo()"
      [value]="valor()"
      [placeholder]="marcador()"
      [attr.autocomplete]="autocompletar() || null"
      [disabled]="disabled()"
      [class]="clases()"
      [attr.aria-invalid]="error() ? 'true' : null"
      (input)="valor.set($any($event.target).value)"
    />
    @if (error()) {
      <span class="text-[13px] text-danger">{{ error() }}</span>
    }
  `,
})
export class CampoTexto {
  readonly etiqueta = input('');
  readonly tipo = input<'text' | 'email' | 'password' | 'search'>('text');
  readonly marcador = input('');
  readonly autocompletar = input('');
  readonly error = input('');
  /** Tipografía monoespaciada (VIN, chasis, placa). */
  readonly mono = input(false, { transform: booleanAttribute });
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly valor = model('');

  protected readonly idCampo = `campo-${++contador}`;
  protected readonly clases = computed(
    () =>
      `h-11 rounded-md border bg-bg px-3 text-[15px] text-text-primary placeholder:text-text-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary ${this.error() ? 'border-danger' : 'border-border'} ${this.mono() ? 'font-mono' : ''}`,
  );
}

export interface OpcionSelector {
  valor: string;
  etiqueta: string;
}

/** Lista desplegable con etiqueta y error. Enlace bidireccional: `[(valor)]`. */
@Component({
  selector: 'app-selector',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'flex w-full flex-col gap-1' },
  template: `
    @if (etiqueta()) {
      <label [for]="idCampo" class="text-[13px] font-semibold text-text-primary">{{ etiqueta() }}</label>
    }
    <select
      [id]="idCampo"
      [class]="clases()"
      [disabled]="disabled()"
      [attr.aria-invalid]="error() ? 'true' : null"
      (change)="valor.set($any($event.target).value)"
    >
      @if (marcador()) {
        <option value="" [selected]="valor() === ''">{{ marcador() }}</option>
      }
      @for (opcion of opciones(); track opcion.valor) {
        <option [value]="opcion.valor" [selected]="opcion.valor === valor()">{{ opcion.etiqueta }}</option>
      }
    </select>
    @if (error()) {
      <span class="text-[13px] text-danger">{{ error() }}</span>
    }
  `,
})
export class Selector {
  readonly etiqueta = input('');
  readonly opciones = input.required<readonly OpcionSelector[]>();
  readonly marcador = input('');
  readonly error = input('');
  readonly disabled = input(false, { transform: booleanAttribute });
  readonly valor = model('');

  protected readonly idCampo = `selector-${++contador}`;
  protected readonly clases = computed(
    () =>
      `h-11 rounded-md border bg-bg px-3 text-[15px] text-text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-secondary ${this.error() ? 'border-danger' : 'border-border'}`,
  );
}
