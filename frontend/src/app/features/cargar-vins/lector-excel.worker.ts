/// <reference lib="webworker" />
import { ErrorLectura, leerArchivo } from './lector-excel';

export interface SolicitudLectura {
  buffer: ArrayBuffer;
  nombre: string;
}

export type RespuestaLectura = { ok: true; hojas: Awaited<ReturnType<typeof leerArchivo>> } | { ok: false; mensaje: string };

// El parseo corre fuera del hilo de la interfaz: un archivo malicioso o muy grande no congela la página.
addEventListener('message', async ({ data }: MessageEvent<SolicitudLectura>) => {
  let respuesta: RespuestaLectura;
  try {
    respuesta = { ok: true, hojas: await leerArchivo(data.buffer, data.nombre) };
  } catch (error) {
    respuesta = { ok: false, mensaje: error instanceof ErrorLectura ? error.message : 'No se pudo leer el archivo.' };
  }
  postMessage(respuesta);
});
