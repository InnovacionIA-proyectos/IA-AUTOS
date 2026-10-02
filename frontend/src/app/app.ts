import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { RouterOutlet } from '@angular/router';
import { InactividadService } from './core/auth/inactividad.service';

@Component({
  selector: 'app-root',
  imports: [RouterOutlet],
  changeDetection: ChangeDetectionStrategy.OnPush,
  template: '<router-outlet />',
})
export class App {
  // Se instancia aquí para que el cierre por inactividad vigile la sesión durante toda la vida de la app.
  protected readonly inactividad = inject(InactividadService);
}
