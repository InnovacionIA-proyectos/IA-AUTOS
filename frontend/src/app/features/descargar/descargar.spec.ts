import { render, screen } from '@testing-library/angular';
import { ServicioConfiguracion } from '../../core/config/servicio-configuracion';
import { Descargar } from './descargar';

vi.mock('qrcode', () => ({ toDataURL: vi.fn().mockResolvedValue('data:image/png;base64,AAAA') }));

const montar = (apkUrl: string) =>
  render(Descargar, { providers: [{ provide: ServicioConfiguracion, useValue: { configuracion: { apkUrl } } }] });

describe('Descargar', () => {
  it('con apkUrl configurada muestra el botón, el enlace directo y el QR', async () => {
    await montar('https://almacen.test/app.apk');

    const boton = screen.getByRole('link', { name: /Descargar APK/ });
    expect(boton.getAttribute('href')).toBe('https://almacen.test/app.apk');
    expect((await screen.findByAltText('Código QR para descargar la app')).getAttribute('src')).toBe('data:image/png;base64,AAAA');
  });

  it('sin apkUrl avisa que falta configurarla y no muestra enlaces', async () => {
    await montar('');

    expect(screen.getByRole('status').textContent).toContain('todavía no está configurado');
    expect(screen.queryByRole('link')).toBeNull();
    expect(screen.queryByAltText('Código QR para descargar la app')).toBeNull();
  });
});
