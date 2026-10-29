using System.Globalization;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>El texto de las fotos de un PDF: su sello (fecha, hora, ubicacion, sello del servidor y hash).</summary>
internal static class clsPieDeFoto
{
    public static IReadOnlyList<string> De(string titulo, EvidenciaDto e)
    {
        var lineas = new List<string>
        {
            titulo,
            $"Fecha y hora: {Fecha(e.SelloFecha)}",
            e.SinUbicacion
                ? "Ubicación: sin ubicación (sin señal de GPS)"
                : $"Ubicación: {Numero(e.SelloLat)}, {Numero(e.SelloLng)}{(e.Precision is { } p ? $" (± {p.ToString("0.#", CultureInfo.InvariantCulture)} m)" : string.Empty)}",
            $"Sello del servidor: {Fecha(e.SelloServidorAt)}",
        };
        if (!string.IsNullOrEmpty(e.HashSha256))
        {
            // El hash (64 caracteres) no cabe en una linea de la celda de una foto: va en dos mitades, la segunda sin titulo.
            var mitad = e.HashSha256.Length / 2;
            lineas.Add($"SHA-256: {e.HashSha256[..mitad]}");
            lineas.Add(e.HashSha256[mitad..]);
        }

        if (!string.IsNullOrWhiteSpace(e.Nota))
        {
            lineas.Add($"Nota: {e.Nota}");
        }

        return lineas;
    }

    private static string Numero(double valor) => valor.ToString("0.#####", CultureInfo.InvariantCulture);

    public static string Fecha(DateTimeOffset fecha) => fecha.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture);
}

/// <summary>El PDF del informe de una inspeccion, con su nombre de archivo.</summary>
public sealed record InformePdf(byte[] Contenido, string Nombre);

/// <summary>
/// El informe de una inspeccion en PDF: los datos de la unidad, el viaje y la etapa; las fotos de cada vista con su sello; los hallazgos con su zona, clase, tamano, nivel y foto; el panel de danos con
/// las zonas marcadas; la transferencia con sus firmas, reservas o constancia; las notas aclaratorias y el historial de seguimiento. Las imagenes salen del almacen. Lo ve quien puede ver el informe
/// (403 si es de otro cliente o de un viaje ajeno; el conductor, sin los hallazgos que no registro). Un PDF no lleva mas de <see cref="MAXIMO_FOTOS"/> fotos (primero las de los hallazgos): las
/// demas se indican y se ven en el informe.
/// </summary>
public sealed class clsInformePdfService(
    clsInspeccionesService inspecciones,
    IConsultasInspecciones consultas,
    IConsultasNotas notas,
    IVehiculoRepository vehiculos,
    IViajeRepository viajes,
    IUsuarioRepository usuarios,
    ICatalogosLectura catalogos,
    clsPanelesDeDano paneles,
    IAlmacenArchivos almacen,
    IGeneradorPdfInforme generador,
    IUsuarioActual actual,
    IReloj reloj)
{
    public const int MAXIMO_FOTOS = 60;

    public async Task<InformePdf> GenerarAsync(Guid inspeccionId, CancellationToken ct = default)
    {
        var informe = await inspecciones.ObtenerInformeAsync(inspeccionId, ct);
        var vehiculo = await vehiculos.ObtenerPorVinAsync(informe.Vin, ct);
        var viaje = await viajes.ObtenerPorIdAsync(informe.ViajeId, ct);
        var cedula = viaje?.ConductorId is { } conductorId ? (await usuarios.ObtenerPorIdAsync(conductorId, ct))?.Cedula : null;
        var vistas = await NombresAsync(clsNombresCatalogo.VISTAS_FOTO, ct);
        var cliente = vehiculo is null ? null : (await NombresAsync(clsNombresCatalogo.CLIENTES, ct)).GetValueOrDefault(vehiculo.ClienteId);
        var presupuesto = new Presupuesto(MAXIMO_FOTOS);

        // Los hallazgos primero: sus fotos son las que mas importan; despues las de las vistas.
        // El conductor ve los hallazgos en lectura: sin su seguimiento ni sus notas aclaratorias.
        var esConductor = actual.EsConductor();
        var notasDeHallazgos = esConductor
            ? new Dictionary<Guid, IReadOnlyList<NotaAclaratoriaDto>>()
            : await notas.ListarPorEntidadesAsync(clsEntidadNota.HALLAZGO, informe.Hallazgos.Select(h => h.Id).ToList(), ct);
        var hallazgos = new List<HallazgoInforme>();
        var numero = 0;
        foreach (var h in informe.Hallazgos)
        {
            numero++;
            var fotos = new List<FotoInforme>();
            foreach (var foto in informe.Evidencias.Where(e => e.HallazgoId == h.Id && !e.EsSello).OrderBy(e => e.CreatedAt))
            {
                if (await CargarAsync(foto, "Foto del hallazgo", presupuesto, ct) is { } cargada)
                {
                    fotos.Add(cargada);
                }
            }

            var seguimiento = new List<SeguimientoInforme>();
            foreach (var paso in esConductor ? [] : await consultas.ObtenerHistorialAsync(h.Id, ct))
            {
                var sello = paso.EvidenciaSello is { } e ? await CargarAsync(e, "Foto del sello de quien valida", presupuesto, ct) : null;
                seguimiento.Add(new SeguimientoInforme(paso.CreatedAt, paso.Accion, paso.EstadoAnterior, paso.EstadoNuevo, paso.UsuarioNombre, paso.Nota, sello));
            }

            hallazgos.Add(new HallazgoInforme(
                numero,
                h.Zona ?? h.ParteVehiculo ?? "Sin ubicar",
                h.ClaseDano,
                h.TamanoDano,
                h.NivelNombre ?? h.NivelSeveridad,
                h.NivelAccion,
                h.Origen,
                h.EsPreexistente,
                h.EstadoSeguimiento,
                h.Nota,
                fotos,
                seguimiento,
                (notasDeHallazgos.GetValueOrDefault(h.Id) ?? []).Select(AInforme).ToList()));
        }

        // La segunda inspeccion del cargue (salida de la etapa Cargue) documenta la acomodacion: sus fotos sin vista son las de la acomodacion y el informe lista cada unidad con su posicion.
        var esAcomodacion = informe.Proceso == clsCodigosEtapa.CARGUE && informe.Momento == clsMomento.SALIDA;
        var fotosDeVistas = new List<FotoInforme>();
        var fotosDeAcomodacion = new List<FotoInforme>();
        foreach (var foto in informe.Evidencias.Where(e => e.HallazgoId is null && !e.EsConstancia && !e.EsSello).OrderBy(e => e.CreatedAt))
        {
            var deAcomodacion = esAcomodacion && foto.VistaId is null;
            var titulo = foto.VistaId is { } vistaId ? $"Vista: {vistas.GetValueOrDefault(vistaId) ?? "sin nombre"}" : deAcomodacion ? "Foto de la acomodación" : "Foto adicional";
            if (await CargarAsync(foto, titulo, presupuesto, ct) is { } cargada)
            {
                (deAcomodacion ? fotosDeAcomodacion : fotosDeVistas).Add(cargada);
            }
        }

        var zonas = hallazgos
            .Zip(informe.Hallazgos, (informeH, dto) => (informeH, dto))
            .GroupBy(x => (x.dto.CuadranteNumero, x.dto.Lado, Nombre: x.informeH.Zona))
            .Select(g => new ZonaConHallazgos(g.Key.CuadranteNumero, g.Key.Lado, g.Key.Nombre, clsPanelesDeDano.Etiqueta(g.Select(x => x.informeH.Numero))))
            .ToList();
        var dibujados = await paneles.ArmarAsync(zonas, ct);

        var datos = new DatosInforme(
            informe.Vin,
            informe.Marca,
            informe.Modelo,
            vehiculo?.TipoVehiculo,
            vehiculo?.Color,
            vehiculo?.Operacion,
            cliente,
            informe.CodigoMtrack,
            informe.Placa,
            informe.Trailer,
            informe.Unidad,
            informe.ConductorNombre,
            cedula,
            informe.Proceso,
            informe.Lugar,
            informe.MomentoNombre,
            informe.Version,
            informe.Estado,
            informe.IniciadaAt,
            informe.CerradaAt,
            informe.InspectorNombre,
            informe.Notas,
            fotosDeVistas,
            hallazgos,
            dibujados.Paneles,
            dibujados.ZonasSinRegion,
            await TransferenciaAsync(informe, presupuesto, ct),
            informe.NotasAclaratorias.Select(AInforme).ToList(),
            presupuesto.Omitidas,
            reloj.Ahora,
            esAcomodacion ? new AcomodacionInforme(await UnidadesDeAcomodacionAsync(informe.ViajeId, ct), fotosDeAcomodacion) : null);
        var momento = informe.Momento;
        return new InformePdf(generador.Generar(datos), $"informe-{informe.Vin}-{informe.Proceso}-{momento}-v{informe.Version}.pdf");
    }

    /// <summary>Las unidades del viaje con su posicion en la ninera, por posicion (las que no la tienen al final, por VIN).</summary>
    private async Task<IReadOnlyList<UnidadAcomodacionInforme>> UnidadesDeAcomodacionAsync(Guid viajeId, CancellationToken ct)
    {
        var unidades = new List<UnidadAcomodacionInforme>();
        foreach (var enlace in await viajes.ObtenerVehiculosAsync(viajeId, ct))
        {
            var vin = (await vehiculos.ObtenerPorIdAsync(enlace.VehiculoId, ct))?.Vin ?? enlace.VehiculoId.ToString();
            unidades.Add(new UnidadAcomodacionInforme(vin, enlace.Posicion, enlace.NoSale));
        }

        return unidades.OrderBy(u => u.Posicion ?? int.MaxValue).ThenBy(u => u.Vin, StringComparer.Ordinal).ToList();
    }

    private async Task<TransferenciaInforme?> TransferenciaAsync(InformeDetalleDto informe, Presupuesto presupuesto, CancellationToken ct)
    {
        if (informe.Transferencia is not { } t)
        {
            return null;
        }

        var entrega = await clsArmadorFirmas.ArmarAsync(almacen, t.TipoCierre == IAAutos.Dominio.Custodia.clsTipoCierre.RECIBO_SIN_ENTREGA ? "Sin entrega" : t.EntregaNombre ?? "—", t.FirmaEntrega, t.EntregaFirmadaAt, t.EntregaFirmaIp, ct);
        var recibe = await clsArmadorFirmas.ArmarAsync(almacen, t.RecibeNombre ?? "—", t.FirmaRecibe, t.RecibeFirmadaAt, t.RecibeFirmaIp, ct);
        FotoInforme? constancia = null;
        if (informe.Evidencias.FirstOrDefault(e => e.Id == t.EvidenciaConstanciaId) is { } foto)
        {
            constancia = await CargarAsync(foto, "Foto de la constancia de no firma", presupuesto, ct);
        }

        return new TransferenciaInforme(
            t.TipoCierre, t.FirmadaAt, entrega, recibe, t.ReservasTexto, t.NoFirmaParte, t.MotivoNoFirma, t.TestigoNombre, constancia, t.NotasAclaratorias.Select(AInforme).ToList());
    }

    private static NotaInforme AInforme(NotaAclaratoriaDto n) => new(n.CreatedAt, n.AutorNombre ?? "—", n.Texto);

    /// <summary>La foto con su sello leida del almacen; nula si ya no hay presupuesto de fotos (se cuenta como omitida). Si el archivo ya no esta, se indica en su lugar.</summary>
    private async Task<FotoInforme?> CargarAsync(EvidenciaDto foto, string titulo, Presupuesto presupuesto, CancellationToken ct)
    {
        if (!presupuesto.Tomar())
        {
            return null;
        }

        var contenido = foto.RutaArchivo is null ? null : await almacen.LeerAsync(foto.RutaArchivo, ct);
        return new FotoInforme(contenido, titulo, clsPieDeFoto.De(titulo, foto));
    }

    private async Task<Dictionary<Guid, string>> NombresAsync(string catalogo, CancellationToken ct) =>
        (await catalogos.ValoresAsync(catalogo, ct))
            .Where(v => v.TryGetValue("nombre", out var nombre) && nombre is string)
            .ToDictionary(v => v.Id, v => (string)v["nombre"]!);

    /// <summary>Cuantas fotos mas caben en el PDF.</summary>
    private sealed class Presupuesto(int maximo)
    {
        private int _usadas;

        public int Omitidas { get; private set; }

        public bool Tomar()
        {
            if (_usadas >= maximo)
            {
                Omitidas++;
                return false;
            }

            _usadas++;
            return true;
        }
    }
}
