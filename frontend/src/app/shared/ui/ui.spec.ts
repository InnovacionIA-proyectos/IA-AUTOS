import { Component, signal } from '@angular/core';
import { fireEvent, render, screen } from '@testing-library/angular';
import { userEvent } from '@testing-library/user-event';
import { Boton, CampoTexto, EstadoError, EstadoVacio, Insignia, Modal, PlantillaColumna, Selector, TablaDatos, type Columna } from './index';

describe('Boton', () => {
  it('con cargando se deshabilita y muestra el indicador', async () => {
    await render('<button appBoton [cargando]="true">Guardar</button>', { imports: [Boton] });

    const boton = screen.getByRole('button', { name: /guardar/i });
    expect(boton.hasAttribute('disabled')).toBe(true);
    expect(boton.getAttribute('aria-busy')).toBe('true');
    expect(boton.querySelector('svg.animate-spin')).not.toBeNull();
  });

  it('habilitado aplica la variante y el tamaño', async () => {
    await render('<button appBoton variante="danger" tamano="sm">Borrar</button>', { imports: [Boton] });

    const boton = screen.getByRole('button', { name: /borrar/i });
    expect(boton.hasAttribute('disabled')).toBe(false);
    expect(boton.className).toContain('bg-danger');
    expect(boton.className).toContain('h-9');
  });

  it('respeta disabled', async () => {
    await render('<button appBoton [disabled]="true">No</button>', { imports: [Boton] });

    expect(screen.getByRole('button', { name: /no/i }).hasAttribute('disabled')).toBe(true);
  });
});

describe('Insignia', () => {
  it('usa los colores de la variante', async () => {
    await render('<app-insignia variante="danger">3</app-insignia>', { imports: [Insignia] });

    expect(screen.getByText('3').className).toContain('text-danger');
  });
});

describe('Estados', () => {
  it('EstadoVacio muestra título y descripción educativa', async () => {
    await render('<app-estado-vacio titulo="Sin unidades" descripcion="Carga un archivo para empezar." />', { imports: [EstadoVacio] });

    expect(screen.getByRole('heading', { name: 'Sin unidades' })).toBeTruthy();
    expect(screen.getByText('Carga un archivo para empezar.')).toBeTruthy();
  });

  it('EstadoError ofrece reintentar y emite el evento', async () => {
    const reintentar = vi.fn();
    await render('<app-estado-error mensaje="Fallo de red" (reintentar)="r()" />', { imports: [EstadoError], componentProperties: { r: reintentar } as never });

    expect(screen.getByRole('alert').textContent).toContain('Fallo de red');
    await userEvent.click(screen.getByRole('button', { name: /reintentar/i }));
    expect(reintentar).toHaveBeenCalledTimes(1);
  });

  it('EstadoError puede ocultar el reintento', async () => {
    await render('<app-estado-error [reintentable]="false" mensaje="x" />', { imports: [EstadoError] });

    expect(screen.queryByRole('button', { name: /reintentar/i })).toBeNull();
  });
});

describe('CampoTexto y Selector', () => {
  @Component({
    imports: [CampoTexto, Selector],
    template: `
      <app-campo-texto etiqueta="Correo" [(valor)]="correo" error="Obligatorio" />
      <app-selector etiqueta="Rol" marcador="Elige" [opciones]="opciones" [(valor)]="rol" />
      <p data-testid="salida">{{ correo() }}|{{ rol() }}</p>
    `,
  })
  class Anfitrion {
    correo = signal('');
    rol = signal('');
    opciones = [
      { valor: 'admin', etiqueta: 'Administrador' },
      { valor: 'inspector', etiqueta: 'Inspector' },
    ];
  }

  it('actualizan el valor enlazado y muestran el error', async () => {
    await render(Anfitrion);

    await userEvent.type(screen.getByLabelText('Correo'), 'a@e.co');
    await userEvent.selectOptions(screen.getByLabelText('Rol'), 'inspector');

    expect(screen.getByTestId('salida').textContent).toBe('a@e.co|inspector');
    expect(screen.getByText('Obligatorio')).toBeTruthy();
    expect(screen.getByLabelText('Correo').getAttribute('aria-invalid')).toBe('true');
  });
});

describe('TablaDatos', () => {
  interface Fila {
    vin: string;
    marca: string;
  }
  const columnas: Columna<Fila>[] = [
    { clave: 'vin', encabezado: 'VIN', texto: (f) => f.vin },
    { clave: 'marca', encabezado: 'Marca' },
  ];

  @Component({
    imports: [TablaDatos, PlantillaColumna],
    template: `
      <app-tabla-datos [columnas]="columnas" [filas]="filas()" [cargando]="cargando()" [clicable]="true" (filaClic)="clic($event)" tituloVacio="Nada aquí">
        <ng-template appColumna="marca" let-fila><strong>{{ fila.marca }}!</strong></ng-template>
      </app-tabla-datos>
    `,
  })
  class Anfitrion {
    columnas = columnas;
    filas = signal<Fila[]>([{ vin: 'V1', marca: 'Renault' }]);
    cargando = signal(false);
    clic = vi.fn();
  }

  it('renderiza encabezados, texto y plantillas de celda', async () => {
    await render(Anfitrion);

    expect(screen.getByText('VIN')).toBeTruthy();
    expect(screen.getByText('V1')).toBeTruthy();
    expect(screen.getByText('Renault!').tagName).toBe('STRONG');
  });

  it('emite la fila al hacer clic', async () => {
    const { fixture } = await render(Anfitrion);

    await userEvent.click(screen.getByText('V1'));

    expect(fixture.componentInstance.clic).toHaveBeenCalledWith({ vin: 'V1', marca: 'Renault' });
  });

  it('muestra el estado vacío y el de carga', async () => {
    const { fixture } = await render(Anfitrion);

    fixture.componentInstance.filas.set([]);
    fixture.detectChanges();
    expect(await screen.findByText('Nada aquí')).toBeTruthy();

    fixture.componentInstance.cargando.set(true);
    fixture.detectChanges();
    expect(await screen.findByText('Cargando…')).toBeTruthy();
  });
});

describe('Modal', () => {
  @Component({
    imports: [Modal],
    template: `
      <app-modal [abierto]="abierto()" titulo="Confirmar" (cerrar)="abierto.set(false)">
        Contenido
        <button pie>Aceptar</button>
      </app-modal>
    `,
  })
  class Anfitrion {
    abierto = signal(true);
  }

  it('muestra título, contenido y pie, y se cierra con Escape', async () => {
    const { fixture } = await render(Anfitrion);

    expect(screen.getByRole('dialog', { name: 'Confirmar' })).toBeTruthy();
    expect(screen.getByText('Contenido')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Aceptar' })).toBeTruthy();

    fireEvent.keyDown(document, { key: 'Escape' });
    fixture.detectChanges();

    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('se cierra con el botón de cierre y al pulsar fuera', async () => {
    const { fixture } = await render(Anfitrion);

    await userEvent.click(screen.getByRole('button', { name: 'Cerrar' }));
    expect(fixture.componentInstance.abierto()).toBe(false);

    fixture.componentInstance.abierto.set(true);
    fixture.detectChanges();
    await userEvent.click(screen.getByRole('dialog').parentElement!);
    expect(fixture.componentInstance.abierto()).toBe(false);
  });
});
