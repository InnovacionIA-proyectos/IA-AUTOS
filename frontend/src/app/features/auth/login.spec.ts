import { ActivatedRoute, convertToParamMap, provideRouter, Router } from '@angular/router';
import { render, screen } from '@testing-library/angular';
import { userEvent } from '@testing-library/user-event';
import { AuthService } from '../../core/auth/auth.service';
import { ErrorApi } from '../../core/errors/error-api';
import { Login } from './login';

async function montar(iniciarSesion: ReturnType<typeof vi.fn>, parametros: Record<string, string> = {}) {
  return render(Login, {
    providers: [
      provideRouter([]),
      { provide: AuthService, useValue: { iniciarSesion } },
      { provide: ActivatedRoute, useValue: { snapshot: { queryParamMap: convertToParamMap(parametros) } } },
    ],
  });
}

describe('Login', () => {
  it('el botón está deshabilitado hasta que correo y contraseña son válidos', async () => {
    await montar(vi.fn());
    const ingresar = screen.getByRole('button', { name: /ingresar/i });
    expect(ingresar.hasAttribute('disabled')).toBe(true);

    await userEvent.type(screen.getByLabelText('Correo'), 'ana@boterosoto.com.co');
    await userEvent.type(screen.getByLabelText('Contraseña'), '12345');
    expect(ingresar.hasAttribute('disabled')).toBe(true);

    await userEvent.type(screen.getByLabelText('Contraseña'), '6');
    expect(ingresar.hasAttribute('disabled')).toBe(false);
  });

  it('envía las credenciales y navega al inicio', async () => {
    const iniciarSesion = vi.fn().mockResolvedValue(undefined);
    const { fixture } = await montar(iniciarSesion);
    const navegar = vi.spyOn(fixture.debugElement.injector.get(Router), 'navigateByUrl').mockResolvedValue(true);

    await userEvent.type(screen.getByLabelText('Correo'), 'ana@boterosoto.com.co');
    await userEvent.type(screen.getByLabelText('Contraseña'), 'Clave-Segura-1');
    await userEvent.click(screen.getByRole('button', { name: /ingresar/i }));

    expect(iniciarSesion).toHaveBeenCalledWith({ email: 'ana@boterosoto.com.co', password: 'Clave-Segura-1' });
    expect(navegar).toHaveBeenCalledWith('/', { replaceUrl: true });
  });

  it('credenciales incorrectas (401) muestran el mensaje y permiten reintentar', async () => {
    const iniciarSesion = vi.fn().mockRejectedValue(new ErrorApi('x', 'UNAUTHORIZED', 401));
    await montar(iniciarSesion);

    await userEvent.type(screen.getByLabelText('Correo'), 'ana@boterosoto.com.co');
    await userEvent.type(screen.getByLabelText('Contraseña'), 'mala-clave');
    await userEvent.click(screen.getByRole('button', { name: /ingresar/i }));

    expect(await screen.findByText('Correo o contraseña incorrectos.')).toBeTruthy();
    expect(screen.getByRole('button', { name: /ingresar/i }).hasAttribute('disabled')).toBe(false);
  });

  it('demasiados intentos (429) y errores de red tienen su propio mensaje', async () => {
    const iniciarSesion = vi.fn().mockRejectedValueOnce(new ErrorApi('x', 'RATE_LIMITED', 429)).mockRejectedValueOnce(new ErrorApi('x', 'RED', 0));
    await montar(iniciarSesion);
    await userEvent.type(screen.getByLabelText('Correo'), 'ana@boterosoto.com.co');
    await userEvent.type(screen.getByLabelText('Contraseña'), 'Clave-Segura-1');

    await userEvent.click(screen.getByRole('button', { name: /ingresar/i }));
    expect(await screen.findByText(/Demasiados intentos/)).toBeTruthy();

    await userEvent.click(screen.getByRole('button', { name: /ingresar/i }));
    expect(await screen.findByText(/No se pudo iniciar sesión/)).toBeTruthy();
  });

  it('muestra el logo y el título', async () => {
    await montar(vi.fn());

    expect(screen.getByAltText(/Botero Soto/)).toBeTruthy();
    expect(screen.getByRole('heading', { name: 'Traza Autos 360°' })).toBeTruthy();
  });
});

describe('Login: motivo y redirección', () => {
  it('avisa cuando la sesión se cerró por inactividad o expiró', async () => {
    await montar(vi.fn(), { motivo: 'inactividad' });
    expect(screen.getByRole('status').textContent).toContain('por inactividad');
  });

  it('no muestra aviso con un motivo desconocido', async () => {
    await montar(vi.fn(), { motivo: 'inventado' });
    expect(screen.queryByRole('status')).toBeNull();
  });

  it('tras iniciar sesión vuelve a la ruta interna que se pidió', async () => {
    const iniciarSesion = vi.fn().mockResolvedValue(undefined);
    const { fixture } = await montar(iniciarSesion, { redirigir: '/inventario?vin=ABC' });
    const navegar = vi.spyOn(fixture.debugElement.injector.get(Router), 'navigateByUrl').mockResolvedValue(true);

    await userEvent.type(screen.getByLabelText('Correo'), 'ana@boterosoto.com.co');
    await userEvent.type(screen.getByLabelText('Contraseña'), 'Clave-Segura-1');
    await userEvent.click(screen.getByRole('button', { name: /ingresar/i }));

    expect(navegar).toHaveBeenCalledWith('/inventario?vin=ABC', { replaceUrl: true });
  });

  it('ignora una redirección a otro sitio y va al inicio', async () => {
    const iniciarSesion = vi.fn().mockResolvedValue(undefined);
    const { fixture } = await montar(iniciarSesion, { redirigir: 'https://malo.com' });
    const navegar = vi.spyOn(fixture.debugElement.injector.get(Router), 'navigateByUrl').mockResolvedValue(true);

    await userEvent.type(screen.getByLabelText('Correo'), 'ana@boterosoto.com.co');
    await userEvent.type(screen.getByLabelText('Contraseña'), 'Clave-Segura-1');
    await userEvent.click(screen.getByRole('button', { name: /ingresar/i }));

    expect(navegar).toHaveBeenCalledWith('/', { replaceUrl: true });
  });
});
