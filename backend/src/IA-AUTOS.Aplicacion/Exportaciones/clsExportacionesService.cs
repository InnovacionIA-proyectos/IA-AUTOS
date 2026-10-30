using System.Globalization;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;

namespace IAAutos.Aplicacion.Exportaciones;

/// <summary>Un archivo listo para descargar: su contenido, su nombre (con la fecha) y su tipo de contenido.</summary>
public sealed record ArchivoExportado(byte[] Contenido, string Nombre, string TipoContenido);

/// <summary>
/// Las exportaciones a Excel (viajes, inspecciones y hallazgos) para quien exporta soportes: coordinador de calidad, comercial y administradores. Aceptan los mismos filtros de los listados
/// y solo traen los datos del cliente de quien las pide (el super administrador ve todos y es el unico que elige un cliente). Un archivo no pasa de <see cref="MAXIMO_FILAS"/> filas: si el
/// filtro trae mas se responde 400 para que se acote.
/// </summary>
public sealed class clsExportacionesService(
    IConsultasExportaciones consultas,
    IGeneradorExcel excel,
    ICatalogosLectura catalogos,
    IUsuarioActual actual,
    IReloj reloj)
{
    public const int MAXIMO_FILAS = 50_000;
    public const string TIPO_XLSX = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    private static readonly ColumnaExcel[] COLUMNAS_CONTEXTO =
    [
        new("VIN", 20), new("Marca", 14), new("Tipo de vehículo", 18), new("Operación", 13), new("Cliente", 16),
        new("Código MTrack", 16), new("Placa", 10), new("Tráiler", 10), new("Unidad", 14), new("Conductor", 24), new("Cédula del conductor", 16),
        new("Etapa", 12), new("Lugar", 22),
    ];

    // ------------------------------------------------------------------------------------------------ viajes

    public async Task<ArchivoExportado> ExportarViajesAsync(FiltrosViajesRequest solicitud, CancellationToken ct = default)
    {
        var resultado = await consultas.ViajesAsync(clsViajesService.ParsearFiltros(solicitud, actual.ClienteRestringido()), actual.ClienteRestringido(), MAXIMO_FILAS, ct);
        var clientes = await ClientesAsync(ct);
        return Armar(
            "viajes",
            "Viajes",
            [.. COLUMNAS_CONTEXTO, new("Estado del viaje", 14), new("Estado de la unidad", 14), new("Origen", 22), new("Destino", 22), new("Momento", 10), new("Versión", 9), new("Estado", 15), new("Inicio", 20), new("Cierre", 20)],
            resultado,
            f => [.. Contexto(f.Contexto, clientes), f.EstadoViaje, f.EstadoUnidad, f.Origen, f.Destino, f.Momento, f.Version, f.Estado, f.Inicio, f.Cierre]);
    }

    // ------------------------------------------------------------------------------------------- inspecciones

    public async Task<ArchivoExportado> ExportarInspeccionesAsync(FiltrosInspeccionesRequest solicitud, CancellationToken ct = default)
    {
        var filtros = await clsInspeccionesService.ParsearFiltrosAsync(solicitud, catalogos, ct);
        var resultado = await consultas.InspeccionesAsync(filtros, actual.ClienteDelFiltro(filtros.ClienteId), MAXIMO_FILAS, ct);
        var clientes = await ClientesAsync(ct);
        return Armar(
            "inspecciones",
            "Inspecciones",
            [
                .. COLUMNAS_CONTEXTO, new("Momento", 10), new("Versión", 9), new("Estado", 10), new("Inicio", 20), new("Cierre", 20), new("Inspector", 24), new("Tipo de cierre", 14),
                new("Entrega: quién", 24), new("Entrega: fecha", 20), new("Entrega: IP", 16), new("Recibe: quién", 24), new("Recibe: fecha", 20), new("Recibe: IP", 16), new("Extras", 8),
            ],
            resultado,
            f => [.. Contexto(f.Contexto, clientes), f.Momento, f.Version, f.Estado, f.Inicio, f.Cierre, f.Inspector, f.TipoCierre, f.EntregaNombre, f.EntregaFecha, f.EntregaIp, f.RecibeNombre, f.RecibeFecha, f.RecibeIp, f.Extras]);
    }

    // ---------------------------------------------------------------------------------------------- hallazgos

    public async Task<ArchivoExportado> ExportarHallazgosAsync(FiltrosHallazgosRequest solicitud, CancellationToken ct = default)
    {
        var (filtros, clienteElegido) = clsHallazgosService.Parsear(solicitud);
        var resultado = await consultas.HallazgosAsync(filtros, actual.ClienteDelFiltro(clienteElegido), MAXIMO_FILAS, ct);
        var clientes = await ClientesAsync(ct);
        return Armar(
            "hallazgos",
            "Hallazgos",
            [
                .. COLUMNAS_CONTEXTO, new("Momento", 10), new("Versión", 9), new("Estado de la inspección", 15), new("Inicio", 20), new("Cierre", 20),
                new("Lado", 11), new("Número", 9), new("Zona", 24), new("Código AIAG", 16), new("Clase de daño", 16), new("Tamaño", 14), new("Nivel", 8), new("Preexistente", 12), new("Origen", 11),
                new("Estado de seguimiento", 18), new("Foto: fecha y hora", 20), new("Foto: latitud", 12), new("Foto: longitud", 12),
            ],
            resultado,
            f => [.. Contexto(f.Contexto, clientes), f.Momento, f.Version, f.EstadoInspeccion, f.Inicio, f.Cierre, f.ZonaLado, f.ZonaNumero, f.ZonaNombre, f.ZonaAiag, f.Clase, f.Tamano, f.Nivel, f.EsPreexistente, f.Origen, f.EstadoSeguimiento, f.FotoFecha, f.FotoLat, f.FotoLng]);
    }

    // ------------------------------------------------------------------------------------------------ comunes

    private ArchivoExportado Armar<T>(string archivo, string hoja, ColumnaExcel[] columnas, ResultadoExportacion<T> resultado, Func<T, object?[]> fila)
    {
        if (resultado.Excedido)
        {
            throw clsErroresCampos.De("desde", $"El archivo tendría más de {MAXIMO_FILAS} filas: acote los filtros (por ejemplo, el rango de fechas)");
        }

        var contenido = excel.Generar(hoja, columnas, resultado.Filas.Select(fila).ToList());
        var dia = reloj.Ahora.ToOffset(TimeSpan.FromHours(-5)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return new ArchivoExportado(contenido, $"{archivo}-{dia}.xlsx", TIPO_XLSX);
    }

    private static object?[] Contexto(ContextoExportacion c, IReadOnlyDictionary<Guid, string> clientes) =>
        [c.Vin, c.Marca, c.TipoVehiculo, c.Operacion, clientes.GetValueOrDefault(c.ClienteId), c.CodigoMtrack, c.Placa, c.Trailer, c.Unidad, c.ConductorNombre, c.ConductorCedula, c.Etapa, c.Lugar];

    private async Task<Dictionary<Guid, string>> ClientesAsync(CancellationToken ct) =>
        (await catalogos.ValoresAsync(clsNombresCatalogo.CLIENTES, ct))
            .Where(v => v.TryGetValue("nombre", out var nombre) && nombre is string)
            .ToDictionary(v => v.Id, v => (string)v["nombre"]!);
}
