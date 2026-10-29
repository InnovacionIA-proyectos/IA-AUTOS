using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using Microsoft.Extensions.Logging;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// Carga masiva de viajes desde un Excel (una fila por VIN: VIN, placa, trailer, cedula del conductor, unidad, codigo MTrack, fecha y hora programada del cargue, lugar de cargue y lugar de
/// destino de ESA unidad). Las filas con el mismo codigo MTrack forman un viaje y deben coincidir en placa, trailer, conductor, unidad, fecha programada y lugar de cargue; el destino es de cada
/// unidad. Los lugares se buscan por nombre en el catalogo <c>lugares</c>. Primero se muestra una vista previa con el error de cada fila (sin guardar nada) y al confirmar solo se guardan los
/// viajes de las filas validas; una fila invalida no frena a las demas.
/// </summary>
public sealed class clsCargaViajesService(
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IUsuarioRepository usuarios,
    ICatalogosLectura catalogos,
    clsEnsamblajeViajes ensamblaje,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<CargarViajesRequest> validador,
    ILogger<clsCargaViajesService> registro)
{
    /// <summary>Una fila del archivo ya limpia, con lo que se resolvio de ella y sus errores.</summary>
    private sealed class Fila
    {
        public int Numero { get; init; }

        public string? Vin { get; init; }

        public string? Placa { get; init; }

        public string? Trailer { get; init; }

        public string? Cedula { get; init; }

        public string? UnidadTexto { get; init; }

        public string? Codigo { get; init; }

        public string? FechaTexto { get; init; }

        public string? OrigenTexto { get; init; }

        public string? DestinoTexto { get; init; }

        public DateTimeOffset? FechaProgramada { get; set; }

        public Guid? LugarCargueId { get; set; }

        public Guid? LugarDestinoId { get; set; }

        public clsVehiculo? Vehiculo { get; set; }

        public Guid? TipoUnidadId { get; set; }

        public Guid? ConductorId { get; set; }

        public List<string> Errores { get; } = [];

        public bool Valida => Errores.Count == 0;

        public Guid? ViajeId { get; set; }
    }

    public Task<ResultadoCargaViajesDto> PrevisualizarAsync(CargarViajesRequest solicitud, CancellationToken ct = default) =>
        ProcesarAsync(solicitud, confirmar: false, ct);

    public Task<ResultadoCargaViajesDto> CargarAsync(CargarViajesRequest solicitud, CancellationToken ct = default) =>
        ProcesarAsync(solicitud, confirmar: true, ct);

    private async Task<ResultadoCargaViajesDto> ProcesarAsync(CargarViajesRequest solicitud, bool confirmar, CancellationToken ct)
    {
        await validador.ValidarOLanzarAsync(solicitud, ct);
        var filas = Limpiar(solicitud.Filas!);
        await ValidarFilasAsync(filas, ct);

        var grupos = filas.Where(f => f.Valida).GroupBy(f => f.Codigo!, StringComparer.OrdinalIgnoreCase).ToList();
        var viajesCreados = grupos.Count;
        if (confirmar)
        {
            viajesCreados = 0;
            var ruta = await catalogos.RutaEstandarAsync(ct);
            foreach (var grupo in grupos)
            {
                if (await GuardarViajeAsync(grupo.ToList(), ruta, ct))
                {
                    viajesCreados++;
                }
            }

            auditor.Registrar("viajes.cargar", "viajes", detalle: new DetalleAuditoria(null, new { filas = filas.Count, viajes_creados = viajesCreados }));
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }

        var resultado = filas.Select(f => new FilaCargaViajeDto(f.Numero, f.Vin, f.Codigo, f.Valida, f.Errores, f.ViajeId)).ToList();
        var validas = resultado.Count(f => f.Valida);
        return new ResultadoCargaViajesDto(confirmar, resultado.Count, validas, resultado.Count - validas, viajesCreados, resultado);
    }

    private static List<Fila> Limpiar(IReadOnlyList<FilaCargaViajeRequest> entrada) =>
        entrada.Select((f, i) => new Fila
        {
            Numero = f.Fila ?? i + 1,
            Vin = clsValidador.Opcional(f.Vin) is { } vin ? clsVin.Normalizar(vin) : null,
            Placa = clsValidador.Opcional(f.Placa),
            Trailer = clsValidador.Opcional(f.Trailer),
            Cedula = ValorSinCedulaInvalida(f.CedulaConductor),
            UnidadTexto = clsValidador.Opcional(f.Unidad),
            Codigo = clsValidador.Opcional(f.CodigoMtrack),
            FechaTexto = clsValidador.Opcional(f.FechaProgramada),
            OrigenTexto = clsValidador.Opcional(f.Origen),
            DestinoTexto = clsValidador.Opcional(f.Destino),
        }).ToList();

    /// <summary>La cedula se compara normalizada; si no es valida se deja el texto para que la validacion de la fila lo reporte.</summary>
    private static string? ValorSinCedulaInvalida(string? cedula)
    {
        try
        {
            return clsUsuario.NormalizarCedula(cedula);
        }
        catch (clsValidacionException)
        {
            return clsValidador.Opcional(cedula);
        }
    }

    // ------------------------------------------------------------------------------------------- validacion

    private async Task ValidarFilasAsync(List<Fila> filas, CancellationToken ct)
    {
        var vehiculosPorVin = await vehiculos.ObtenerPorVinesAsync(filas.Where(f => f.Vin is not null).Select(f => f.Vin!).Distinct().ToList(), ct);
        var conductores = await usuarios.ObtenerPorCedulasAsync(filas.Where(f => f.Cedula is not null).Select(f => f.Cedula!).Distinct().ToList(), ct);
        var unidades = (await catalogos.ValoresAsync(clsNombresCatalogo.TIPOS_UNIDAD, ct)).Where(u => u.Activo).ToList();
        var lugares = (await catalogos.ValoresAsync(clsNombresCatalogo.LUGARES, ct)).Where(l => l.Activo).ToList();

        foreach (var fila in filas)
        {
            ValidarCampos(fila, vehiculosPorVin, conductores, unidades);
            ValidarProgramacion(fila, lugares);
        }

        ValidarCoherenciaDeViajes(filas);
        await ValidarContraLaBaseAsync(filas, ct);
    }

    private static void ValidarCampos(
        Fila fila,
        IReadOnlyDictionary<string, clsVehiculo> vehiculosPorVin,
        IReadOnlyDictionary<string, clsUsuario> conductores,
        IReadOnlyList<ValorCatalogo> unidades)
    {
        if (fila.Vin is null)
        {
            fila.Errores.Add("El VIN es requerido");
        }
        else if (vehiculosPorVin.TryGetValue(fila.Vin, out var vehiculo))
        {
            fila.Vehiculo = vehiculo;
        }
        else
        {
            fila.Errores.Add($"El VIN {fila.Vin} no existe en el inventario: cárguelo antes");
        }

        if (fila.Codigo is null)
        {
            fila.Errores.Add("El código MTrack es requerido");
        }
        else
        {
            try
            {
                clsViaje.CodigoValido(fila.Codigo);
            }
            catch (clsValidacionException ex)
            {
                fila.Errores.Add(MensajeDeCampo(ex));
            }
        }

        if (fila.Placa is null)
        {
            fila.Errores.Add("La placa es requerida");
        }
        else if (fila.Placa.Length > clsViaje.PLACA_MAX)
        {
            fila.Errores.Add($"La placa admite máximo {clsViaje.PLACA_MAX} caracteres");
        }

        if (fila.Trailer is { Length: > clsViaje.TRAILER_MAX })
        {
            fila.Errores.Add($"El tráiler admite máximo {clsViaje.TRAILER_MAX} caracteres");
        }

        if (fila.UnidadTexto is null)
        {
            fila.Errores.Add("La unidad transportadora es requerida");
        }
        else
        {
            var buscada = clsTextoComparable.Normalizar(fila.UnidadTexto);
            var unidad = unidades.FirstOrDefault(u => clsTextoComparable.Normalizar(u.Codigo) == buscada
                || clsTextoComparable.Normalizar(u.TryGetValue("nombre", out var nombre) ? nombre as string : null) == buscada);
            if (unidad is null)
            {
                fila.Errores.Add($"La unidad \"{fila.UnidadTexto}\" no existe en el catálogo o está inactiva");
            }
            else
            {
                fila.TipoUnidadId = unidad.Id;
            }
        }

        if (fila.Cedula is not null)
        {
            if (conductores.TryGetValue(fila.Cedula, out var conductor) && conductor.Activo && conductor.Rol == clsRol.CONDUCTOR)
            {
                fila.ConductorId = conductor.Id;
            }
            else
            {
                fila.Errores.Add($"No hay un conductor activo con la cédula {fila.Cedula}");
            }
        }
    }

    /// <summary>
    /// La programacion de la fila: la fecha y hora del cargue (obligatoria; sin zona horaria es hora de Colombia) y los lugares de cargue (origen) y de destino de la unidad, que deben existir y estar
    /// activos en el catalogo de lugares (se buscan por nombre, sin distinguir mayusculas ni acentos).
    /// </summary>
    private static void ValidarProgramacion(Fila fila, IReadOnlyList<ValorCatalogo> lugares)
    {
        if (fila.FechaTexto is null)
        {
            fila.Errores.Add("La fecha y hora programada del cargue es requerida");
        }
        else if (clsFechaProgramada.DeCarga(fila.FechaTexto) is { } fecha)
        {
            fila.FechaProgramada = fecha;
        }
        else
        {
            fila.Errores.Add($"La fecha y hora programada \"{fila.FechaTexto}\" no es válida: use AAAA-MM-DD HH:mm (por ejemplo 2026-11-03 06:00)");
        }

        fila.LugarCargueId = BuscarLugar(fila, fila.OrigenTexto, "El lugar de cargue (origen) es requerido", lugares);
        fila.LugarDestinoId = BuscarLugar(fila, fila.DestinoTexto, "El lugar de destino de la unidad es requerido", lugares);
    }

    private static Guid? BuscarLugar(Fila fila, string? texto, string faltante, IReadOnlyList<ValorCatalogo> lugares)
    {
        if (texto is null)
        {
            fila.Errores.Add(faltante);
            return null;
        }

        var buscado = clsTextoComparable.Normalizar(texto);
        var lugar = lugares.FirstOrDefault(l => clsTextoComparable.Normalizar(l.TryGetValue("nombre", out var nombre) ? nombre as string : null) == buscado);
        if (lugar is null)
        {
            fila.Errores.Add($"El lugar {texto} no existe en el catálogo");
            return null;
        }

        return lugar.Id;
    }

    /// <summary>
    /// Las filas con el mismo codigo MTrack son un solo viaje: deben coincidir en placa, trailer, conductor, unidad, fecha programada y lugar de cargue con la primera
    /// fila sin errores propios del grupo, y un VIN no puede repetirse dentro del viaje.
    /// </summary>
    private static void ValidarCoherenciaDeViajes(List<Fila> filas)
    {
        // Un VIN no puede estar en dos viajes del mismo archivo (en el mismo viaje, repetido, se reporta mas abajo).
        var primerViajeDelVin = new Dictionary<string, (string Codigo, int Fila)>(StringComparer.OrdinalIgnoreCase);
        foreach (var fila in filas.Where(f => f.Vin is not null && f.Codigo is not null))
        {
            if (!primerViajeDelVin.TryGetValue(fila.Vin!, out var previo))
            {
                primerViajeDelVin[fila.Vin!] = (fila.Codigo!, fila.Numero);
            }
            else if (!string.Equals(previo.Codigo, fila.Codigo, StringComparison.OrdinalIgnoreCase))
            {
                fila.Errores.Add($"El VIN {fila.Vin} ya está en el viaje {previo.Codigo} de este archivo (fila {previo.Fila}): no puede estar en dos viajes");
            }
        }

        foreach (var grupo in filas.Where(f => f.Codigo is not null).GroupBy(f => f.Codigo!, StringComparer.OrdinalIgnoreCase))
        {
            var cabecera = grupo.FirstOrDefault(f => f.Valida);
            var vistos = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var fila in grupo)
            {
                if (cabecera is not null && fila != cabecera && fila.Valida)
                {
                    var distintos = new List<string>();
                    if (!string.Equals(fila.Placa, cabecera.Placa, StringComparison.OrdinalIgnoreCase))
                    {
                        distintos.Add("la placa");
                    }

                    if (!string.Equals(fila.Trailer, cabecera.Trailer, StringComparison.OrdinalIgnoreCase))
                    {
                        distintos.Add("el tráiler");
                    }

                    if (fila.ConductorId != cabecera.ConductorId)
                    {
                        distintos.Add("el conductor");
                    }

                    if (fila.TipoUnidadId != cabecera.TipoUnidadId)
                    {
                        distintos.Add("la unidad");
                    }

                    if (fila.FechaProgramada != cabecera.FechaProgramada)
                    {
                        distintos.Add("la fecha programada");
                    }

                    if (fila.LugarCargueId != cabecera.LugarCargueId)
                    {
                        distintos.Add("el lugar de cargue");
                    }

                    if (distintos.Count > 0)
                    {
                        fila.Errores.Add($"No coincide con el viaje de la fila {cabecera.Numero} ({string.Join(", ", distintos)}): un código MTrack es un solo viaje");
                    }
                }

                if (fila.Vin is not null)
                {
                    if (vistos.TryGetValue(fila.Vin, out var primera))
                    {
                        fila.Errores.Add($"El VIN {fila.Vin} está repetido en el viaje (fila {primera})");
                    }
                    else
                    {
                        vistos[fila.Vin] = fila.Numero;
                    }
                }
            }
        }
    }

    /// <summary>El codigo MTrack no puede existir ya, y un VIN no puede estar en otro viaje activo.</summary>
    private async Task ValidarContraLaBaseAsync(List<Fila> filas, CancellationToken ct)
    {
        var codigos = filas.Where(f => f.Codigo is not null).Select(f => f.Codigo!).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var existentes = await viajes.CodigosMtrackExistentesAsync(codigos, ct);
        var vehiculoIds = filas.Where(f => f.Vehiculo is not null).Select(f => f.Vehiculo!.Id).Distinct().ToList();
        var enViajes = await viajes.ViajesActivosPorVehiculoAsync(vehiculoIds, null, ct);

        foreach (var fila in filas)
        {
            if (fila.Codigo is not null && existentes.Contains(fila.Codigo))
            {
                fila.Errores.Add($"Ya existe un viaje con el código MTrack {fila.Codigo}");
            }

            if (fila.Vehiculo is not null && enViajes.TryGetValue(fila.Vehiculo.Id, out var activo))
            {
                fila.Errores.Add($"El VIN {fila.Vin} ya está en el viaje activo {activo.CodigoMtrack ?? activo.ViajeId.ToString()}");
            }
        }
    }

    // ------------------------------------------------------------------------------------------- guardado

    /// <summary>
    /// Crea el viaje de un grupo de filas validas (cada viaje se confirma por separado: si uno falla, por ejemplo por un codigo repetido
    /// justo en ese momento, sus filas quedan con el error y los demas viajes siguen).
    /// </summary>
    private async Task<bool> GuardarViajeAsync(List<Fila> grupo, IReadOnlyList<clsEtapaDeRuta> ruta, CancellationToken ct)
    {
        var cabecera = grupo[0];
        try
        {
            var viaje = clsViaje.Crear(
                cabecera.Codigo, cabecera.TipoUnidadId, cabecera.Placa, cabecera.Trailer, cabecera.ConductorId, null, null, null,
                actual.IdObligatorio(), reloj.Ahora, cabecera.FechaProgramada, cabecera.LugarCargueId);
            viajes.Agregar(viaje);
            var destinos = grupo.Where(f => f.LugarDestinoId is not null).ToDictionary(f => f.Vehiculo!.Id, f => f.LugarDestinoId!.Value);
            await ensamblaje.AgregarVehiculosAsync(viaje, grupo.Select(f => f.Vehiculo!).ToList(), false, ruta, ct, destinos);
            await auditor.RegistrarDeViajeAsync(
                "viaje.crear",
                "viajes",
                viaje.Id,
                viaje.Id,
                new DetalleAuditoria(null, new { viaje = clsInstantaneasAuditoria.De(viaje), vines = grupo.Select(f => f.Vin).ToList(), destinos = grupo.Select(f => new { vin = f.Vin, lugar_destino_id = f.LugarDestinoId }).ToList() }),
                grupo[0].Vehiculo!.ClienteId,
                ct);
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
            foreach (var fila in grupo)
            {
                fila.ViajeId = viaje.Id;
            }

            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            unidadDeTrabajo.DescartarCambios();
            var mensaje = ex is clsExcepcionDominio dominio ? dominio.Message : "No se pudo crear el viaje";
            if (ex is not clsExcepcionDominio)
            {
                registro.LogWarning(ex, "No se pudo crear el viaje {Codigo} en la carga masiva", cabecera.Codigo);
            }

            foreach (var fila in grupo)
            {
                fila.Errores.Add(mensaje);
            }

            return false;
        }
    }

    private static string MensajeDeCampo(clsValidacionException ex) =>
        ex.Detalles is Dictionary<string, object> detalles
        && detalles.TryGetValue("fieldErrors", out var campos)
        && campos is Dictionary<string, string[]> porCampo
        && porCampo.Values.FirstOrDefault()?.FirstOrDefault() is { } mensaje
            ? mensaje
            : ex.Message;
}
