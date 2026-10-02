import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'div[appTarjeta]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block rounded-lg border border-border bg-surface' },
  template: '<ng-content />',
})
export class Tarjeta {}

@Component({
  selector: 'div[appTarjetaCabecera]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block border-b border-border px-4 py-3' },
  template: '<ng-content />',
})
export class TarjetaCabecera {}

@Component({
  selector: 'h3[appTarjetaTitulo]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block text-[17px] font-semibold text-text-primary' },
  template: '<ng-content />',
})
export class TarjetaTitulo {}

@Component({
  selector: 'div[appTarjetaCuerpo]',
  changeDetection: ChangeDetectionStrategy.OnPush,
  host: { class: 'block px-4 py-4' },
  template: '<ng-content />',
})
export class TarjetaCuerpo {}
