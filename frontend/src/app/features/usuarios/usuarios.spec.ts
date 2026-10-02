import { render, screen, waitFor, within } from '@testing-library/angular';
import { userEvent } from '@testing-library/user-event';
import { of, throwError } from 'rxjs';
import { ApiService } from '../../core/api/api.service';
import { SesionService } from '../../core/auth/sesion.service';
import { ErrorApi } from '../../core/errors/error-api';
import { Usuarios } from './usuarios';

const ana = { id: 'u1', nombre: 'Ana Pérez', email: 'ana@boterosoto.com.co', rol: 'super_admin', subtipo_inspector: null, activo: true, created_at: '' };
const luis = { id: 'u2', nombre: 'Luis Gómez', email: 'luis@boterosoto.com.co', rol: 'inspector', subtipo_inspector: null, activo: true, created_at: '' };
const rita = { id: 'u3', nombre: 'Rita Soto', email: 'rita@boterosoto.com.co', rol: 'conductor', subtipo_inspector: null, activo: false, created_at: '' };

function montar(api: Partial<Record<keyof ApiService, unknown>> = {}) {
  const servicio = { listarUsuarios: vi.fn().mockReturnValue(of([ana, luis, rita])), ...api };
  return {
    servicio,
    render: render(Usuarios, {
      providers: [
        { provide: ApiService, useValue: servicio },
        { provide: SesionService, useValue: { usuario: () => ({ id: 'u1' }) } },
      ],
    }),
  };
}

describe('Usuarios', () => {
  it('lista los usuarios con su rol y estado, y filtra por nombre o correo', async () => {
    montar();
    expect(await screen.findByText('Luis Gómez')).toBeTruthy();
    expect(screen.getByText('3 usuarios')).toBeTruthy();
    expect(screen.getByText('Retirado')).toBeTruthy();

    await userEvent.type(screen.getByLabelText('Buscar por nombre o correo'), 'luis@');
    expect(screen.queryByText('Ana Pérez')).toBeNull();
    expect(screen.getByText('1 usuario')).toBeTruthy();
  });

  it('no permite eliminarse a uno mismo', async () => {
    montar();
    await screen.findByText('Ana Pérez');
    expect(screen.getByRole('button', { name: 'Eliminar Ana Pérez' }).hasAttribute('disabled')).toBe(true);
    expect(screen.getByRole('button', { name: 'Eliminar Luis Gómez' }).hasAttribute('disabled')).toBe(false);
  });

  it('valida el formulario antes de crear', async () => {
    const crearUsuario = vi.fn();
    montar({ crearUsuario });
    await userEvent.click(await screen.findByRole('button', { name: /Nuevo usuario/ }));

    await userEvent.click(screen.getByRole('button', { name: 'Crear' }));

    expect(await screen.findByText('El nombre es obligatorio.')).toBeTruthy();
    expect(screen.getByText('Ingresa un correo válido.')).toBeTruthy();
    expect(screen.getByText('La contraseña debe tener al menos 6 caracteres.')).toBeTruthy();
    expect(crearUsuario).not.toHaveBeenCalled();
  });

  it('crea un usuario con los datos del formulario y avisa', async () => {
    const crearUsuario = vi.fn().mockReturnValue(of(luis));
    const { servicio } = montar({ crearUsuario });
    await userEvent.click(await screen.findByRole('button', { name: /Nuevo usuario/ }));

    const dialogo = screen.getByRole('dialog');
    await userEvent.type(within(dialogo).getByLabelText('Nombre'), 'Mario Ríos');
    await userEvent.type(within(dialogo).getByLabelText('Correo'), 'mario@boterosoto.com.co');
    await userEvent.selectOptions(within(dialogo).getByLabelText('Rol'), 'conductor');
    await userEvent.type(within(dialogo).getByLabelText('Contraseña'), 'Clave-Segura-1');
    await userEvent.click(within(dialogo).getByRole('button', { name: 'Crear' }));

    await waitFor(() =>
      expect(crearUsuario).toHaveBeenCalledWith({ nombre: 'Mario Ríos', email: 'mario@boterosoto.com.co', rol: 'conductor', subtipo_inspector: null, password: 'Clave-Segura-1' }),
    );
    expect(await screen.findByText('Usuario Mario Ríos creado.')).toBeTruthy();
    expect(servicio.listarUsuarios).toHaveBeenCalledTimes(2);
    expect(screen.queryByRole('dialog')).toBeNull();
  });

  it('muestra el error de la API (correo duplicado) sin cerrar el formulario', async () => {
    const crearUsuario = vi.fn().mockReturnValue(throwError(() => new ErrorApi('El correo ya está registrado', 'CONFLICT', 409)));
    montar({ crearUsuario });
    await userEvent.click(await screen.findByRole('button', { name: /Nuevo usuario/ }));
    const dialogo = screen.getByRole('dialog');
    await userEvent.type(within(dialogo).getByLabelText('Nombre'), 'Mario');
    await userEvent.type(within(dialogo).getByLabelText('Correo'), 'luis@boterosoto.com.co');
    await userEvent.type(within(dialogo).getByLabelText('Contraseña'), 'Clave-Segura-1');
    await userEvent.click(within(dialogo).getByRole('button', { name: 'Crear' }));

    expect(await within(dialogo).findByText('El correo ya está registrado')).toBeTruthy();
  });

  it('editar sin contraseña no la envía', async () => {
    const actualizarUsuario = vi.fn().mockReturnValue(of(luis));
    montar({ actualizarUsuario });
    await userEvent.click(await screen.findByRole('button', { name: 'Editar Luis Gómez' }));
    const dialogo = screen.getByRole('dialog');
    expect((within(dialogo).getByLabelText('Correo') as HTMLInputElement).value).toBe('luis@boterosoto.com.co');

    await userEvent.click(within(dialogo).getByRole('button', { name: 'Guardar' }));

    await waitFor(() => expect(actualizarUsuario).toHaveBeenCalled());
    expect(actualizarUsuario.mock.calls[0]).toEqual(['u2', { nombre: 'Luis Gómez', email: 'luis@boterosoto.com.co', rol: 'inspector', subtipo_inspector: null, password: null, activo: null }]);
  });

  it('eliminar explica cuando el usuario queda retirado por tener historial', async () => {
    const eliminarUsuario = vi.fn().mockReturnValue(of({ modo: 'retirado' }));
    montar({ eliminarUsuario });
    await userEvent.click(await screen.findByRole('button', { name: 'Eliminar Luis Gómez' }));
    await userEvent.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'Eliminar' }));

    expect(await screen.findByText(/tenía operaciones: se marcó como retirado/)).toBeTruthy();
    expect(eliminarUsuario).toHaveBeenCalledWith('u2');
  });

  it('reactiva a un usuario retirado', async () => {
    const actualizarUsuario = vi.fn().mockReturnValue(of(rita));
    montar({ actualizarUsuario });
    await userEvent.click(await screen.findByRole('button', { name: 'Reactivar Rita Soto' }));

    await waitFor(() => expect(actualizarUsuario).toHaveBeenCalledWith('u3', { nombre: null, email: null, rol: null, subtipo_inspector: null, password: null, activo: true }));
    expect(await screen.findByText('Rita Soto fue reactivado.')).toBeTruthy();
  });

  it('muestra el error al fallar la carga y permite reintentar', async () => {
    montar({ listarUsuarios: vi.fn().mockReturnValue(throwError(() => new ErrorApi('Sin permiso', 'FORBIDDEN', 403))) });
    expect(await screen.findByText('Sin permiso')).toBeTruthy();
    expect(screen.getByRole('button', { name: 'Reintentar' })).toBeTruthy();
  });
});
