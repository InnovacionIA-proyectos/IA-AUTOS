import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';
import type {
  ActualizarUsuario,
  CargarVins,
  CrearUsuario,
  InformeDetalle,
  InspeccionListado,
  Inventario,
  ListadoPaginado,
  OrdenSofasa,
  Patrones,
  RespuestaEliminarUsuario,
  RespuestaLogin,
  ResultadoCarga,
  TasaRechazo,
  TipoListado,
  Usuario,
  UsuarioAutenticado,
  VehiculoBusqueda,
} from './api-tipos';
import { ServicioConfiguracion } from '../config/servicio-configuracion';

export interface FiltrosInspecciones {
  vin?: string;
  estado?: string;
  proceso?: string;
}

export interface FiltrosSofasa {
  vin?: string;
  estado?: string;
  ciudad?: string;
}

/** Cliente tipado de la API (contrato en backend/docs/openapi.json). Los errores llegan como ErrorApi (interceptor). */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly configuracion = inject(ServicioConfiguracion);

  // --- Autenticación
  login(email: string, password: string): Observable<RespuestaLogin> {
    return this.http.post<RespuestaLogin>(this.url('/api/auth/login'), { email, password });
  }

  perfil(): Observable<UsuarioAutenticado> {
    return this.http.get<UsuarioAutenticado>(this.url('/api/auth/me'));
  }

  // --- Usuarios
  listarUsuarios(): Observable<Usuario[]> {
    return this.http.get<Usuario[]>(this.url('/api/usuarios'));
  }

  crearUsuario(cuerpo: CrearUsuario): Observable<Usuario> {
    return this.http.post<Usuario>(this.url('/api/usuarios'), cuerpo);
  }

  actualizarUsuario(id: string, cuerpo: ActualizarUsuario): Observable<Usuario> {
    return this.http.patch<Usuario>(this.url(`/api/usuarios/${encodeURIComponent(id)}`), cuerpo);
  }

  eliminarUsuario(id: string): Observable<RespuestaEliminarUsuario> {
    return this.http.delete<RespuestaEliminarUsuario>(this.url(`/api/usuarios/${encodeURIComponent(id)}`));
  }

  // --- Vehículos
  cargarVins(cuerpo: CargarVins): Observable<ResultadoCarga> {
    return this.http.post<ResultadoCarga>(this.url('/api/vehiculos/cargar'), cuerpo);
  }

  buscarVehiculos(vin: string): Observable<VehiculoBusqueda[]> {
    return this.http.get<VehiculoBusqueda[]>(this.url('/api/vehiculos'), { params: new HttpParams().set('vin', vin) });
  }

  listarVehiculos(tipo: TipoListado, vin: string, pagina: number): Observable<ListadoPaginado> {
    let params = new HttpParams().set('tipo', tipo).set('page', String(pagina));
    if (vin) params = params.set('vin', vin);
    return this.http.get<ListadoPaginado>(this.url('/api/vehiculos/listado'), { params });
  }

  inventario(vin: string): Observable<Inventario> {
    return this.http.get<Inventario>(this.url(`/api/vehiculos/${encodeURIComponent(vin)}/inventario`));
  }

  /** Descarga el PDF del inventario como Blob (autenticado por el interceptor). */
  inventarioPdf(vin: string): Observable<Blob> {
    return this.http.get(this.url(`/api/vehiculos/${encodeURIComponent(vin)}/inventario/pdf`), { responseType: 'blob' });
  }

  // --- Reportes
  patrones(): Observable<Patrones> {
    return this.http.get<Patrones>(this.url('/api/patrones'));
  }

  tasaRechazo(): Observable<TasaRechazo> {
    return this.http.get<TasaRechazo>(this.url('/api/metricas/rechazo'));
  }

  // --- Inspecciones
  listarInspecciones(filtros: FiltrosInspecciones = {}): Observable<InspeccionListado[]> {
    let params = new HttpParams();
    for (const [clave, valor] of Object.entries(filtros)) {
      if (valor) params = params.set(clave, valor);
    }
    return this.http.get<InspeccionListado[]>(this.url('/api/inspecciones'), { params });
  }

  informe(id: string): Observable<InformeDetalle> {
    return this.http.get<InformeDetalle>(this.url(`/api/inspecciones/${encodeURIComponent(id)}`));
  }

  // --- Sofasa
  listarSofasa(filtros: FiltrosSofasa = {}): Observable<OrdenSofasa[]> {
    let params = new HttpParams();
    for (const [clave, valor] of Object.entries(filtros)) {
      if (valor) params = params.set(clave, valor);
    }
    return this.http.get<OrdenSofasa[]>(this.url('/api/sofasa/ordenes'), { params });
  }

  /** URL absoluta de una imagen servida por la API (las evidencias vienen con ruta relativa). */
  urlArchivo(relativa: string | null | undefined): string | null {
    if (!relativa) return null;
    return relativa.startsWith('http') ? relativa : this.url(relativa);
  }

  private url(ruta: string): string {
    return `${this.configuracion.apiUrl}${ruta}`;
  }
}
