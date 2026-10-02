import { Injectable } from '@angular/core';
import { ErrorLectura, Hoja, MAX_BYTES_ARCHIVO, leerArchivo } from './lector-excel';
import type { RespuestaLectura, SolicitudLectura } from './lector-excel.worker';

/** Lee el archivo subido en un Web Worker; si el entorno no lo soporta (pruebas), lo hace en el hilo principal. */
@Injectable({ providedIn: 'root' })
export class LectorExcel {
  async leer(archivo: File): Promise<Hoja[]> {
    if (archivo.size > MAX_BYTES_ARCHIVO) {
      throw new ErrorLectura(`El archivo supera el máximo de ${MAX_BYTES_ARCHIVO / 1024 / 1024} MB.`);
    }
    const buffer = await archivo.arrayBuffer();
    if (typeof Worker === 'undefined') return leerArchivo(buffer, archivo.name);

    return new Promise<Hoja[]>((resolver, rechazar) => {
      const worker = new Worker(new URL('./lector-excel.worker', import.meta.url), { type: 'module' });
      const terminar = () => worker.terminate();
      worker.onmessage = ({ data }: MessageEvent<RespuestaLectura>) => {
        terminar();
        if (data.ok) resolver(data.hojas);
        else rechazar(new ErrorLectura(data.mensaje));
      };
      worker.onerror = () => {
        terminar();
        rechazar(new ErrorLectura('No se pudo leer el archivo.'));
      };
      const solicitud: SolicitudLectura = { buffer, nombre: archivo.name };
      worker.postMessage(solicitud, [buffer]);
    });
  }
}
