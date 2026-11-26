using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// El informe de la niñera de un viaje en PDF, armado al pedirlo con los informes del cargue de sus carros (no se guarda aparte: si un carro se baja del viaje, el siguiente informe sale solo con los que van): los puestos
/// con el VIN de cada uno, los daños de cada carro, las fotos de la niñera, la firma unica del cargue de cada parte con la hora y el GPS del momento en que firmo (o la constancia de no firma) y los carros que no salen
/// marcados con su motivo. Lo ve quien puede ver el viaje (403 si es de otro cliente o de un viaje ajeno) y al conductor se le quitan el nivel y la nota de cada daño. Un viaje con la acomodacion sin cerrar o el cargue
/// sin firmar sale igual, con lo que hay y avisandolo, en vez de fallar. Lleva hasta <see cref="MAXIMO_FOTOS"/> fotos de la niñera.
/// </summary>
public sealed class clsInformeNineraPdfService(
    clsViajesService viajesServicio,
    clsInspeccionesService inspeccionesServicio,
    IConsultasInspecciones consultas,
    IViajeRepository viajes,
    IInspeccionRepository inspecciones,
    ITransferenciaCustodiaRepository transferencias,
    IUsuarioRepository usuarios,
    ICatalogosLectura catalogos,
    IAlmacenArchivos almacen,
    IGeneradorPdfNinera generador,
    IUsuarioActual actual,
    IReloj reloj)
{
    public const int MAXIMO_FOTOS = 20;

    public async Task<InformePdf> GenerarAsync(Guid viajeId, CancellationToken ct = default)
    {
        // Pedir el viaje por el servicio de viajes aplica la misma visibilidad que el detalle del viaje (404 si no existe, 403 si no es suyo).
        var viaje = await viajesServicio.ObtenerAsync(viajeId, ct);
        var mostrarNivel = !actual.EsConductor();
        var codigos = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).ToDictionary(v => v.Id, v => v.Codigo);
        var etapas = await viajes.ObtenerEtapasAsync(viajeId, null, ct);

        var carros = new List<CarroNinera>();
        var transferenciasDeNinera = new List<clsTransferenciaCustodia>();
        // Un carro bajado del viaje conserva su historia pero ya no sale en el informe de la niñera.
        foreach (var unidad in viaje.Vehiculos.Where(v => v.EstadoUnidad != clsEstadoUnidad.BAJADO))
        {
            var cargue = etapas.FirstOrDefault(e => e.VehiculoId == unidad.VehiculoId && codigos.TryGetValue(e.EtapaCatalogoId, out var codigo) && codigo == clsCodigosEtapa.CARGUE);
            var danos = new List<DanoNinera>();
            var conInforme = false;
            foreach (var momento in new[] { clsMomento.LLEGADA, clsMomento.SALIDA })
            {
                var inspeccion = cargue is null ? null : await inspecciones.ObtenerUltimaDeEtapaAsync(cargue.Id, momento, ct);
                if (inspeccion is null)
                {
                    continue;
                }

                conInforme = true;
                // El informe ya viene sin el nivel ni la nota para el conductor.
                var informe = await inspeccionesServicio.ObtenerInformeAsync(inspeccion.Id, ct);
                danos.AddRange(informe.Hallazgos.Select(h => ADano(h, informe.MomentoNombre, mostrarNivel)));
                if (momento == clsMomento.SALIDA && await transferencias.ObtenerPorInspeccionAsync(inspeccion.Id, ct) is { FirmaPorNinera: true } deNinera)
                {
                    transferenciasDeNinera.Add(deNinera);
                }
            }

            carros.Add(new CarroNinera(unidad.Vin, unidad.Marca, unidad.Modelo, unidad.Color, unidad.Posicion, unidad.EstadoUnidad == clsEstadoUnidad.NO_SALE, MotivoDeNoSale(unidad.NoSale), danos, !conInforme));
        }

        var puestos = (viaje.Disposicion?.Puestos ?? []).Select(p => new PuestoNinera(p.Numero, p.Etiqueta, p.Vin)).ToList();
        var ordenados = carros.OrderBy(c => c.Puesto ?? int.MaxValue).ThenBy(c => c.Vin, StringComparer.Ordinal).ToList();
        var (fotos, omitidas) = await CargarFotosAsync(await consultas.FotosDeAcomodacionDelViajeAsync(viajeId, ct), ct);
        var (entrega, recibe) = await FirmasAsync(viaje, transferenciasDeNinera, ct);
        var constancia = await ConstanciaAsync(viaje, transferenciasDeNinera, ct);

        var datos = new DatosNinera(
            viaje.CodigoMtrack,
            viaje.Placa,
            viaje.Trailer,
            viaje.Unidad,
            viaje.ConductorNombre,
            viaje.InspectorNombre,
            viaje.LugarCargue,
            viaje.Estado,
            viaje.Capacidad,
            puestos,
            ordenados,
            fotos,
            omitidas,
            await viajes.TieneAcomodacionCerradaAsync(viajeId, ct),
            entrega,
            recibe,
            constancia,
            mostrarNivel,
            reloj.Ahora);
        var nombre = new string((viaje.CodigoMtrack ?? viajeId.ToString()).Select(c => char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-').ToArray());
        return new InformePdf(generador.Generar(datos), $"ninera-{nombre}.pdf");
    }

    /// <summary>El motivo de «no sale» con su texto, si lo trae: «Otro: Llanta pinchada, sin repuesto».</summary>
    private static string? MotivoDeNoSale(NoSaleDto? noSale) =>
        noSale is null ? null : string.IsNullOrWhiteSpace(noSale.MotivoTexto) ? noSale.Motivo : $"{noSale.Motivo}: {noSale.MotivoTexto}";

    private static DanoNinera ADano(HallazgoDto h, string informe, bool mostrarNivel) =>
        new(
            clsMapeadorInspecciones.ZonaConAiag(h.Zona, h.AiagArea) ?? h.ParteVehiculo ?? "Sin ubicar",
            h.ClaseDano,
            h.TamanoDano,
            mostrarNivel ? h.NivelNombre ?? h.NivelSeveridad ?? "sin nivel" : null,
            mostrarNivel ? h.Nota : null,
            informe);

    private async Task<(IReadOnlyList<FotoInforme> Fotos, int Omitidas)> CargarFotosAsync(IReadOnlyList<EvidenciaDto> fotos, CancellationToken ct)
    {
        var cargadas = new List<FotoInforme>();
        foreach (var foto in fotos.Take(MAXIMO_FOTOS))
        {
            var contenido = foto.RutaArchivo is null ? null : await almacen.LeerAsync(foto.RutaArchivo, ct);
            cargadas.Add(new FotoInforme(contenido, "Foto de la niñera", clsPieDeFoto.De("Foto de la niñera", foto)));
        }

        return (cargadas, Math.Max(fotos.Count - MAXIMO_FOTOS, 0));
    }

    /// <summary>La firma de cada parte, tomada de la transferencia del cargue de cualquiera de los carros firmados por niñera (todas llevan la misma firma, hora y GPS). Nula mientras esa parte no firmo.</summary>
    private async Task<(FirmaNinera? Entrega, FirmaNinera? Recibe)> FirmasAsync(ViajeDto viaje, List<clsTransferenciaCustodia> deNinera, CancellationToken ct)
    {
        var entrega = deNinera.FirstOrDefault(t => !string.IsNullOrEmpty(t.FirmaEntrega));
        var recibe = deNinera.FirstOrDefault(t => !string.IsNullOrEmpty(t.FirmaRecibe));
        return (
            entrega is null ? null : await ArmarFirmaAsync(viaje.InspectorNombre, entrega.FirmaEntrega!, entrega.FirmaEntregaAt, entrega.FirmaEntregaMomentoAt, entrega.FirmaEntregaLat, entrega.FirmaEntregaLng, entrega.FirmaEntregaPrecisionM, ct),
            recibe is null ? null : await ArmarFirmaAsync(viaje.ConductorNombre, recibe.FirmaRecibe!, recibe.FirmaRecibeAt, recibe.FirmaRecibeMomentoAt, recibe.FirmaRecibeLat, recibe.FirmaRecibeLng, recibe.FirmaRecibePrecisionM, ct));
    }

    private async Task<FirmaNinera> ArmarFirmaAsync(
        string? persona, string firma, DateTimeOffset? llegoAt, DateTimeOffset? momento, double? lat, double? lng, double? precisionM, CancellationToken ct)
    {
        var armada = await clsArmadorFirmas.ArmarAsync(almacen, persona ?? "—", firma, llegoAt, null, ct);
        return new FirmaNinera(armada.Persona, momento, lat, lng, precisionM, llegoAt, armada.Trazo, armada.Imagen);
    }

    /// <summary>La constancia de no firma de la niñera, si una parte se nego y la otra la dejo: la parte, el motivo, el testigo y la hora del momento.</summary>
    private async Task<ConstanciaNinera?> ConstanciaAsync(ViajeDto viaje, List<clsTransferenciaCustodia> deNinera, CancellationToken ct)
    {
        if (viaje.CargueNoFirmadoAt is null || deNinera.FirstOrDefault(t => t.TipoCierre == clsTipoCierre.NO_FIRMA) is not { } t)
        {
            return null;
        }

        var testigo = t.TestigoUsuarioId is { } id ? (await usuarios.ObtenerPorIdAsync(id, ct))?.Nombre : null;
        return new ConstanciaNinera(t.NoFirmaParte ?? "—", t.MotivoNoFirma ?? "—", testigo, t.FirmaEntregaMomentoAt ?? t.FirmaRecibeMomentoAt);
    }
}
