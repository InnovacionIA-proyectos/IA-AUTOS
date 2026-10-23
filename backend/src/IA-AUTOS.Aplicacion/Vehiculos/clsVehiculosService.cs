using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Vehiculos.DTOVehiculos;
using IAAutos.Dominio.Comun;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.Extensions.Logging;

namespace IAAutos.Aplicacion.Vehiculos;

/// <summary>Carga masiva de VINs, busqueda, listados del tablero e inventario por VIN.</summary>
public sealed class clsVehiculosService(
    IVehiculoRepository vehiculos,
    IConsultasVehiculos consultas,
    IConsultasViajes consultasViajes,
    ICatalogosLectura catalogos,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<CargarVinsRequest> validadorCarga,
    ILogger<clsVehiculosService> registro)
{
    /// <summary>Los campos por los que se puede ordenar el listado (<c>orden=campo:asc|desc</c>).</summary>
    public static readonly IReadOnlySet<string> CAMPOS_ORDEN = new HashSet<string> { "vin", "marca", "modelo", "color", "operacion", "activo", "created_at" };

    /// <summary>
    /// Carga masiva de vehiculos. Cada VIN se inserta (o se actualiza si ya existe). Las etapas no se crean aqui: nacen cuando el VIN
    /// se agrega a un viaje. Re-subir el mismo archivo no duplica. Los errores se reportan por VIN sin detener el resto del lote. Los
    /// vehiculos nuevos quedan en el cliente de quien carga (el super administrador, que no tiene, usa el cliente por defecto).
    /// </summary>
    public async Task<ResultadoCargaDto> CargarVinsAsync(CargarVinsRequest solicitud, CancellationToken ct = default)
    {
        await validadorCarga.ValidarOLanzarAsync(solicitud, ct);
        var items = solicitud.Items!;
        var materializador = new clsMaterializadorVehiculos(vehiculos);
        await materializador.PrepararAsync(items.Select(i => clsVin.Normalizar(i.Vin!)).ToList(), ct);

        int creados = 0, actualizados = 0;
        var errores = new List<ErrorCargaDto>();
        var clienteId = actual.ClienteId ?? await catalogos.ClienteAsync(null, ct);

        foreach (var item in items)
        {
            var vin = clsVin.Normalizar(item.Vin!);
            try
            {
                var existente = await materializador.ObtenerAsync(vin, ct);
                var esNuevo = existente is null;
                var vehiculo = existente;
                if (vehiculo is null)
                {
                    vehiculo = clsVehiculo.Crear(vin, item.Marca!, item.Modelo, item.Color, item.Operacion!, clienteId, reloj.Ahora);
                    vehiculos.Agregar(vehiculo);
                }
                else
                {
                    vehiculo.ActualizarDesdeCarga(item.Marca!, item.Modelo, item.Color, item.Operacion!, reloj.Ahora);
                }

                await unidadDeTrabajo.GuardarCambiosAsync(ct);

                if (esNuevo)
                {
                    materializador.Registrar(vehiculo);
                    creados++;
                }
                else
                {
                    actualizados++;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unidadDeTrabajo.DescartarCambios();
                materializador.InvalidarCache();
                errores.Add(new ErrorCargaDto(vin, MensajeSeguro(ex, vin)));
            }
        }

        auditor.Registrar(
            "vehiculos.cargar",
            "vehiculos",
            detalle: new DetalleAuditoria(null, new { recibidos = items.Count, creados, actualizados, con_error = errores.Count }));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new ResultadoCargaDto(items.Count, creados, actualizados, errores.Count, errores);
    }

    /// <summary>
    /// Baja logica del vehiculo: queda inactivo (<c>activo = false</c>) y ya no se busca ni se agrega a un viaje, pero conserva sus etapas, inspecciones y evidencias. 404 si el VIN no existe;
    /// 403 si es de otro cliente. Darlo de baja otra vez responde igual y no cambia nada. Queda en la auditoria.
    /// </summary>
    public async Task<BajaVehiculoResponse> DarDeBajaAsync(string vin, CancellationToken ct = default)
    {
        var vehiculo = await vehiculos.ObtenerPorVinAsync(vin, ct)
            ?? throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vin} no encontrado");
        if (actual.ClienteRestringido() is { } cliente && vehiculo.ClienteId != cliente)
        {
            throw new clsProhibidoException("El VIN no es de su cliente");
        }

        if (vehiculo.Activo)
        {
            vehiculo.DarDeBaja(reloj.Ahora);
            auditor.Registrar(
                "vehiculo.baja",
                "vehiculos",
                vehiculo.Id,
                detalle: new DetalleAuditoria(new { vehiculo.Vin, activo = true }, new { vehiculo.Vin, activo = false }),
                clienteId: vehiculo.ClienteId);
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }

        return new BajaVehiculoResponse("inactivado");
    }

    /// <summary>
    /// Autocompletado por fragmento de VIN o de marca (el de Inventario). Un fragmento corto o una marca se aceptan tal cual; un texto de 17 caracteres alfanumericos es un VIN completo y debe cumplir la regla
    /// del VIN (sin las letras I, O ni Q), si no responde 400 con el motivo. Inspectores y conductores solo encuentran los VIN de sus viajes; el resto, los de su cliente (el super administrador, todos).
    /// </summary>
    public async Task<IReadOnlyList<VehiculoBusquedaDto>> BuscarAsync(string? termino, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(termino))
        {
            return [];
        }

        if (clsVin.ParecePesquisaDeVinCompleto(termino) && clsVin.Validar(termino) is { } motivo)
        {
            throw clsErroresCampos.De("vin", motivo);
        }

        return await consultas.BuscarAsync(termino.Trim(), actual.SoloSusViajes(), actual.ClienteRestringido(), ct);
    }

    /// <summary>
    /// Una pagina de vehiculos: <c>{items, total, pagina, tamano}</c> (50 por defecto, maximo 200). Los filtros, la pagina y el orden se validan juntos y un error responde 400. Quien se limita a su cliente solo
    /// ve el suyo (pedir otro responde 403).
    /// </summary>
    public async Task<PaginaDto<ListadoVehiculoDto>> ListarAsync(FiltrosVehiculosRequest solicitud, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        var filtros = new FiltrosVehiculos(
            clsParseoFiltros.Catalogo(solicitud.Tipo, clsTipoListado.VALORES, "tipo", errores) ?? clsTipoListado.MOVILIZADOS,
            clsParseoFiltros.Vin(solicitud.Vin, "vin", errores),
            clsParseoFiltros.Catalogo(solicitud.Operacion, clsOperacion.VALORES, "operacion", errores),
            clsValidador.Opcional(solicitud.Marca),
            clsParseoFiltros.Uuid(solicitud.ClienteId, "cliente_id", errores),
            clsParseoFiltros.Catalogo(solicitud.EstadoUnidad, clsEstadoUnidad.VALORES, "estado_unidad", errores),
            clsParseoFiltros.Booleano(solicitud.Activo, "activo", errores),
            clsParseoFiltros.Fecha(solicitud.Desde, "desde", errores),
            clsParseoFiltros.Fecha(solicitud.Hasta, "hasta", errores));
        var pagina = clsPaginacion.Pagina(solicitud.Pagina, solicitud.Tamano, errores);
        var orden = clsPaginacion.Orden(solicitud.Orden, CAMPOS_ORDEN, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var (filas, total) = await consultas.ListarAsync(filtros, actual.SoloSusViajes(), actual.ClienteDelFiltro(filtros.ClienteId), pagina, orden, ct);
        return new PaginaDto<ListadoVehiculoDto>(filas, total, pagina.Pagina, pagina.Tamano);
    }

    /// <summary>
    /// Inventario del VIN. Un inspector o conductor solo puede abrir los VIN de sus viajes (403 si no es de ellos); de ese VIN ve unicamente
    /// las etapas de sus viajes. Quien se limita a su cliente recibe 403 con el VIN de otro cliente. El conductor recibe sin hallazgos ni fotos de hallazgos las inspecciones que no creo.
    /// </summary>
    public async Task<InventarioDto> ObtenerInventarioAsync(string vin, CancellationToken ct = default)
    {
        if (actual.ClienteRestringido() is { } cliente)
        {
            var delVin = await vehiculos.ObtenerPorVinAsync(vin, ct)
                ?? throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vin} no encontrado");
            if (delVin.ClienteId != cliente)
            {
                throw new clsProhibidoException("El VIN no es de su cliente");
            }
        }

        var soloDeUsuario = actual.SoloSusViajes();
        if (soloDeUsuario is { } usuarioId)
        {
            if (await vehiculos.ObtenerPorVinAsync(vin, ct) is null)
            {
                throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vin} no encontrado");
            }

            if (!await consultasViajes.UsuarioVeVinAsync(vin, usuarioId, ct))
            {
                throw new clsProhibidoException("El VIN no pertenece a ninguno de sus viajes");
            }
        }

        var inventario = await consultas.ObtenerInventarioAsync(vin, soloDeUsuario, ct)
            ?? throw new clsEntidadNoEncontradaException($"Vehículo con VIN {vin} no encontrado");
        return inventario.ParaConductor(actual);
    }

    private string MensajeSeguro(Exception ex, string vin)
    {
        if (ex is clsExcepcionDominio dominio)
        {
            return dominio.Message;
        }

        registro.LogWarning(ex, "No se pudo procesar el VIN {Vin} en la carga masiva", vin);
        return "No se pudo procesar el VIN";
    }
}
