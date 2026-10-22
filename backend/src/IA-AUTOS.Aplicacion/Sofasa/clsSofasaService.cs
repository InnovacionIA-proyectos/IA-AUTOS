using System.Globalization;
using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Sofasa.DTOSofasa;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Sofasa;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.Extensions.Logging;

namespace IAAutos.Aplicacion.Sofasa;

public sealed class IngestarOrdenesRequestValidator : AbstractValidator<IngestarOrdenesRequest>
{
    public const int MAXIMO_ORDENES = 5000;

    public IngestarOrdenesRequestValidator()
    {
        RuleFor(x => x.Ordenes)
            .Must(o => o is not null).WithMessage("Se requiere la lista de órdenes")
            .Must(o => o is null || o.Count <= MAXIMO_ORDENES).WithMessage($"Máximo {MAXIMO_ORDENES} órdenes por lote");
        RuleForEach(x => x.Ordenes).Must(o => o is not null && o.SourceId is not null).WithMessage("source_id es requerido");
    }
}

/// <summary>Consulta de ordenes Sofasa para la web y la app, por paginas.</summary>
public interface IConsultasSofasa
{
    /// <summary>
    /// Una pagina de ordenes (sin <paramref name="orden"/>, las mas recientes primero) y el total que cumple los filtros. Con <paramref name="soloDeUsuario"/>, solo las ordenes de los viajes de ese usuario;
    /// con <paramref name="soloDeCliente"/>, solo las de VIN de ese cliente.
    /// </summary>
    Task<(IReadOnlyList<OrdenSofasaDto> Filas, int Total)> ListarAsync(
        FiltrosOrdenesSofasa filtros, Guid? soloDeUsuario, Guid? soloDeCliente, PaginaSolicitada pagina, OrdenSolicitado? orden, CancellationToken ct = default);
}

/// <summary>
/// Ingesta de ordenes de carga desde el ETL. Cada orden materializa su VIN como vehiculo (operacion "nacional") y se guarda de forma
/// idempotente por SourceId. Si el VIN ya esta en un viaje (y, cuando la orden trae codigo MTrack, el codigo coincide), la orden queda
/// vinculada a ese viaje. Los errores se reportan por orden.
/// </summary>
public sealed class clsSofasaService(
    IVehiculoRepository vehiculos,
    IViajeRepository viajes,
    IOrdenSofasaRepository ordenes,
    ICatalogosLectura catalogos,
    IConsultasSofasa consultas,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<IngestarOrdenesRequest> validador,
    ILogger<clsSofasaService> registro)
{
    /// <summary>Los campos por los que se puede ordenar el listado (<c>orden=campo:asc|desc</c>).</summary>
    public static readonly IReadOnlySet<string> CAMPOS_ORDEN = new HashSet<string> { "fecha", "vin", "estado", "ciudad", "orden", "marca", "created_at" };

    public async Task<ResultadoIngestaDto> IngestarAsync(IngestarOrdenesRequest solicitud, CancellationToken ct = default)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var lote = solicitud.Ordenes!;
        if (lote.Count == 0)
        {
            return new ResultadoIngestaDto(0, 0, 0, 0, []);
        }

        var materializador = new clsMaterializadorVehiculos(vehiculos);
        var vines = lote.Select(o => Limpiar(o.Vin)).Where(v => v is not null).Select(v => clsVin.Normalizar(v!)).ToList();
        await materializador.PrepararAsync(vines, ct);

        int guardadas = 0, vehiculosCreados = 0, vehiculosActualizados = 0;
        // Los vehiculos de las ordenes Sofasa son del cliente Sofasa (o del cliente por defecto si ese codigo no esta activo).
        var clienteId = await catalogos.ClienteAsync(clsCliente.CODIGO_SOFASA, ct);
        var errores = new List<ErrorIngestaDto>();

        foreach (var orden in lote)
        {
            var sourceId = orden.SourceId!.Value;
            try
            {
                var datos = ADatos(orden);
                var vin = Limpiar(orden.Vin) is { } crudo ? clsVin.Normalizar(crudo) : null;
                Guid? vehiculoId = null;
                clsVehiculo? vehiculoNuevo = null;
                var vehiculoCreado = false;
                var vehiculoActualizado = false;

                if (vin is not null)
                {
                    var existente = await materializador.ObtenerAsync(vin, ct);
                    var esNuevo = existente is null;
                    var vehiculo = existente;
                    if (vehiculo is null)
                    {
                        vehiculo = clsVehiculo.CrearDesdeSofasa(vin, orden.Make, orden.ModelDescription, clienteId, reloj.Ahora);
                        vehiculos.Agregar(vehiculo);
                        vehiculoNuevo = vehiculo;
                    }
                    else
                    {
                        vehiculo.ActualizarDesdeSofasa(orden.Make, orden.ModelDescription, reloj.Ahora);
                    }

                    vehiculoId = vehiculo.Id;
                    vehiculoCreado = esNuevo;
                    vehiculoActualizado = !esNuevo;
                }

                // Un vehiculo recien creado no puede estar aun en un viaje.
                var viajeId = vehiculoId is { } vid && !vehiculoCreado ? await viajes.BuscarViajeParaOrdenAsync(vid, datos.CodigoMtrack, ct) : null;
                var existenteOrden = await ordenes.ObtenerPorSourceIdAsync(sourceId, ct);
                if (existenteOrden is null)
                {
                    var nueva = clsOrdenSofasa.Crear(datos, vehiculoId, reloj.Ahora);
                    nueva.AsignarViaje(viajeId, reloj.Ahora);
                    ordenes.Agregar(nueva);
                }
                else
                {
                    existenteOrden.Aplicar(datos, vehiculoId, reloj.Ahora);
                    if (viajeId is not null)
                    {
                        existenteOrden.AsignarViaje(viajeId, reloj.Ahora);
                    }
                }

                await unidadDeTrabajo.GuardarCambiosAsync(ct);

                if (vehiculoNuevo is not null)
                {
                    materializador.Registrar(vehiculoNuevo);
                }

                guardadas++;
                vehiculosCreados += vehiculoCreado ? 1 : 0;
                vehiculosActualizados += vehiculoActualizado ? 1 : 0;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                unidadDeTrabajo.DescartarCambios();
                materializador.InvalidarCache();
                errores.Add(new ErrorIngestaDto(sourceId, MensajeSeguro(ex, sourceId)));
            }
        }

        // Accion del sistema (el ETL no es un usuario): UsuarioId nulo.
        auditor.Registrar(
            "sofasa.ingesta",
            "ordenes_sofasa",
            detalle: new DetalleAuditoria(
                null,
                new { recibidas = lote.Count, guardadas, vehiculos_creados = vehiculosCreados, vehiculos_actualizados = vehiculosActualizados, errores = errores.Count }));
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return new ResultadoIngestaDto(lote.Count, guardadas, vehiculosCreados, vehiculosActualizados, errores);
    }

    /// <summary>
    /// Una pagina de ordenes: <c>{items, total, pagina, tamano}</c>. Los filtros (<c>vin</c> con la regla unica del VIN, <c>estado</c>, <c>ciudad</c>, <c>desde</c> y <c>hasta</c>), la pagina y el orden se
    /// validan juntos y un error responde 400. Una fecha sin zona horaria se toma como UTC.
    /// </summary>
    public async Task<PaginaDto<OrdenSofasaDto>> ListarAsync(FiltrosOrdenesSofasaRequest solicitud, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        var filtros = new FiltrosOrdenesSofasa(
            clsParseoFiltros.Vin(solicitud.Vin, "vin", errores),
            Limpiar(solicitud.Estado),
            Limpiar(solicitud.Ciudad),
            FechaDeFiltro(solicitud.Desde, "desde", errores),
            FechaDeFiltro(solicitud.Hasta, "hasta", errores));
        var pagina = clsPaginacion.Pagina(solicitud.Pagina, solicitud.Tamano, errores);
        var orden = clsPaginacion.Orden(solicitud.Orden, CAMPOS_ORDEN, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var (filas, total) = await consultas.ListarAsync(filtros, actual.SoloSusViajes(), actual.ClienteRestringido(), pagina, orden, ct);
        return new PaginaDto<OrdenSofasaDto>(filas, total, pagina.Pagina, pagina.Tamano);
    }

    /// <summary>La fecha ISO de un filtro (sin zona, UTC); un valor que no es fecha agrega el error del campo.</summary>
    private static DateTimeOffset? FechaDeFiltro(string? texto, string campo, Dictionary<string, string[]> errores)
    {
        var limpio = Limpiar(texto);
        if (limpio is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(limpio, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fecha))
        {
            return fecha;
        }

        errores[campo] = [$"{campo} debe ser una fecha ISO"];
        return null;
    }

    /// <summary>Vacios y espacios pasan a nulo (evita guardar cadenas vacias en columnas de texto o fecha).</summary>
    internal static string? Limpiar(string? valor)
    {
        var recortado = valor?.Trim();
        return string.IsNullOrEmpty(recortado) ? null : recortado;
    }

    /// <summary>Interpreta una fecha ISO 8601; sin zona horaria se asume UTC.</summary>
    internal static DateTimeOffset? InterpretarFecha(string? texto, string campo)
    {
        var limpio = Limpiar(texto);
        if (limpio is null)
        {
            return null;
        }

        if (DateTimeOffset.TryParse(limpio, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fecha))
        {
            return fecha;
        }

        throw new clsValidacionException($"{campo}: fecha inválida");
    }

    private static clsDatosOrdenSofasa ADatos(OrdenSofasaIngestDto o) => new(
        o.SourceId!.Value,
        Limpiar(o.StatusCode),
        InterpretarFecha(o.DatetimeEvent, "datetime_event"),
        Limpiar(o.OrdenNumber),
        Limpiar(o.OrdenSap),
        Limpiar(o.OriginCedi),
        Limpiar(o.Vin) is { } vin ? clsVin.Normalizar(vin) : null,
        Limpiar(o.Make),
        Limpiar(o.ModelDescription),
        Limpiar(o.DealerCode),
        Limpiar(o.DestinationCity),
        Limpiar(o.DestinationAddress),
        InterpretarFecha(o.LoadingDate, "loading_date"),
        InterpretarFecha(o.Appointment, "appointment"),
        Limpiar(o.LoadingDock),
        o.Weight,
        Limpiar(o.CodigoMtrack));

    private string MensajeSeguro(Exception ex, long sourceId)
    {
        if (ex is clsExcepcionDominio dominio)
        {
            return dominio.Message;
        }

        registro.LogWarning(ex, "No se pudo procesar la orden Sofasa {SourceId}", sourceId);
        return "No se pudo procesar la orden";
    }
}
