import type { components } from './api';

/**
 * Tipos del contrato OpenAPI (backend/docs/openapi.json). El archivo api.d.ts se genera con `npm run api:generar`
 * y no se edita a mano: aquí solo se les da un nombre cómodo en español.
 */
type Esquemas = components['schemas'];

export type Usuario = Esquemas['UsuarioDto'];
export type UsuarioAutenticado = Esquemas['UsuarioAutenticadoDto'];
export type RespuestaLogin = Esquemas['LoginResponse'];
export type CrearUsuario = Esquemas['CrearUsuarioRequest'];
export type ActualizarUsuario = Esquemas['ActualizarUsuarioRequest'];
export type RespuestaEliminarUsuario = Esquemas['EliminarUsuarioResponse'];

export type CargarVins = Esquemas['CargarVinsRequest'];
export type ResultadoCarga = Esquemas['ResultadoCargaDto'];
export type VehiculoBusqueda = Esquemas['VehiculoBusquedaDto'];
export type FilaListadoVehiculo = Esquemas['ListadoVehiculoDto'];
export type ListadoPaginado = Esquemas['ListadoPaginadoDto'];
export type Inventario = Esquemas['InventarioDto'];
export type InventarioEtapa = Esquemas['InventarioEtapaDto'];
export type InventarioInspeccion = Esquemas['InventarioInspeccionDto'];

export type Patrones = Esquemas['PatronesDto'];
export type TasaRechazo = Esquemas['TasaRechazoDto'];
export type TasaOperacion = Esquemas['TasaOperacionDto'];

export type InspeccionListado = Esquemas['InspeccionListadoDto'];
export type InformeDetalle = Esquemas['InformeDetalleDto'];
export type Hallazgo = Esquemas['HallazgoDto'];
export type Evidencia = Esquemas['EvidenciaDto'];

export type OrdenSofasa = Esquemas['OrdenSofasaDto'];

export type Rol = 'inspector' | 'conductor' | 'coordinador_calidad' | 'comercial' | 'admin' | 'super_admin';
export type TipoListado = 'movilizados' | 'pendientes' | 'novedad';
export type Operacion = 'importacion' | 'exportacion' | 'nacional';

export const ROLES: readonly Rol[] = ['inspector', 'conductor', 'coordinador_calidad', 'comercial', 'admin', 'super_admin'];
export const OPERACIONES: readonly Operacion[] = ['importacion', 'exportacion', 'nacional'];
