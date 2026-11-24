using System.Text.Json;
using FluentValidation;
using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// La firma del cargue por NINERA: una sola firma de cada parte (el inspector del viaje entrega, el conductor del viaje recibe) para todos los carros activos del viaje, guardada en la transferencia del cargue de cada
/// carro (colgada de su salida del cargue, la acomodacion) con la hora y el GPS del momento en que se firmo, ademas de cuando llego al servidor. Exige la llegada y la salida del cargue cerradas de CADA carro activo
/// (409 <c>cargue_incompleto</c> nombrando que falta y de que carro) y deja fuera a los «no sale». Dentro de la firma de quien recibe van las reservas por carro; y si una parte se niega, la otra deja la constancia de
/// no firma de toda la ninera (motivo, testigo y foto), que cierra la entrega del cargue de todos los carros y avisa al coordinador. Reenviar lo mismo no duplica nada.
/// </summary>
public sealed class clsCargueNineraService(
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IInspeccionRepository inspecciones,
    ITransferenciaCustodiaRepository transferencias,
    ICatalogosLectura catalogos,
    IUsuarioRepository usuarios,
    clsFabricaEvidencias fabricaEvidencias,
    clsEvidenciaDeHallazgos evidenciaDeHallazgos,
    clsEmisorNotificaciones emisor,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<RegistrarNoFirmaRequest> validadorNoFirma)
{
    private sealed class Carro(clsViajeVehiculo enlace, clsVehiculo vehiculo, clsInspeccion? llegada, clsInspeccion? salida, clsTransferenciaCustodia? transferencia, bool cerradaAntes)
    {
        public clsViajeVehiculo Enlace { get; } = enlace;

        public clsVehiculo Vehiculo { get; } = vehiculo;

        public clsInspeccion? Llegada { get; } = llegada;

        public clsInspeccion? Salida { get; } = salida;

        public clsTransferenciaCustodia? Transferencia { get; set; } = transferencia;

        /// <summary>El cargue de este carro ya se cerro carro por carro, antes de la firma por ninera (su transferencia cuelga de la llegada): cuenta como entrega cerrada y la firma por ninera no lo toca.</summary>
        public bool CerradaAntes { get; } = cerradaAntes;
    }

    // ------------------------------------------------------------------------------------------------ la firma

    /// <summary>
    /// Firma el cargue de toda la ninera: la parte del usuario autenticado (403 si no es el inspector ni el conductor del viaje, o si no es la parte indicada), con el trazo, la hora y el GPS del momento y, si quien recibe
    /// las deja, las reservas por carro. 400 con lo que falta o es invalido; 409 si el viaje no esta activo, si falta cerrar la llegada o la salida del cargue de algun carro activo, o si un carro ya esta cerrado con otra firma.
    /// </summary>
    public async Task<CargueNineraDto> FirmarAsync(Guid viajeId, FirmarCargueRequest solicitud, ContextoCliente cliente, CancellationToken ct = default)
    {
        var errores = new Dictionary<string, string[]>();
        var firma = string.IsNullOrEmpty(solicitud.Firma) ? null : solicitud.Firma;
        if (firma is null)
        {
            errores["firma"] = ["La firma es obligatoria"];
        }

        var parte = ParteValida(solicitud.Parte, errores);
        var momento = MomentoValido(solicitud.FechaMomento, solicitud.Lat, solicitud.Lng, solicitud.PrecisionM, errores);
        var reservas = LimpiarReservas(solicitud.Reservas, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var viaje = await ObtenerViajeAsync(viajeId, ct);
        viaje.AsegurarActivo();
        var actorId = ActorDe(viaje, parte!);
        if (parte == clsParteFirmante.ENTREGA && reservas.Count > 0)
        {
            throw new clsProhibidoException("Solo quien recibe (el conductor del viaje) deja reservas");
        }

        var carros = await CargarAsync(viaje, ct);
        VerificarReservasContraLosCarros(reservas, carros);
        AsegurarCargueCompleto(carros);

        var ahora = reloj.Ahora;
        foreach (var carro in carros.Where(c => !c.CerradaAntes))
        {
            var transferencia = ObtenerOCrear(carro, viaje, ahora);
            var antes = JsonSerializer.Serialize(clsInstantaneasAuditoria.De(transferencia));
            reservas.TryGetValue(carro.Vehiculo.Vin, out var observaciones);
            transferencia.FirmarPorNinera(actorId, firma, observaciones, momento!, cliente.Ip, ahora);
            if (JsonSerializer.Serialize(clsInstantaneasAuditoria.De(transferencia)) == antes)
            {
                continue;   // reenvio de la misma firma: no cambia nada ni deja otro registro
            }

            await RecalcularEvidenciaAsync(carro, transferencia, ct);
            await auditor.RegistrarDeViajeAsync(
                "transferencia.firmar_ninera", "transferencias_custodia", transferencia.Id, viaje.Id,
                new DetalleAuditoria(JsonSerializer.Deserialize<JsonElement>(antes), new { transferencia = clsInstantaneasAuditoria.De(transferencia), parte, vin = carro.Vehiculo.Vin, momento }), carro.Vehiculo.ClienteId, ct);
        }

        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return Armar(viaje, parte!, carros);
    }

    // ------------------------------------------------------------------------------------------------ la constancia de no firma

    /// <summary>
    /// La constancia de no firma de TODA la ninera: la parte que si esta (el inspector o el conductor del viaje; nunca la que se nego, 403) deja el motivo, un testigo con usuario y la foto de la constancia, con la hora y el
    /// GPS del momento. Cierra como «no firma» la transferencia del cargue de cada carro activo (no hay constancia para solo algunos: para eso estan las reservas) y avisa al coordinador de calidad del cliente, una sola vez por viaje.
    /// 409 si la parte que se nego ya firmo o si falta cerrar la llegada o la salida del cargue de algun carro; reenviar la misma constancia no crea otra.
    /// </summary>
    public async Task<CargueNineraDto> RegistrarNoFirmaAsync(Guid viajeId, NoFirmaDelCargueRequest solicitud, ContextoCliente cliente, CancellationToken ct = default)
    {
        await validadorNoFirma.ValidarOLanzarAsync(new RegistrarNoFirmaRequest(null, null, solicitud.Parte, solicitud.Motivo, solicitud.TestigoUsuarioId, solicitud.Evidencia), ct);
        var errores = new Dictionary<string, string[]>();
        var momento = MomentoValido(solicitud.FechaMomento, solicitud.Lat, solicitud.Lng, solicitud.PrecisionM, errores);
        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        var viaje = await ObtenerViajeAsync(viajeId, ct);
        viaje.AsegurarActivo();
        var actorId = actual.IdObligatorio();
        var quienRegistra = ParteDelUsuario(viaje, actorId) ?? throw new clsProhibidoException("Solo el inspector o el conductor del viaje dejan la constancia de no firma: nadie actúa por otro");
        if (solicitud.Parte == quienRegistra)
        {
            throw new clsProhibidoException("La no firma la registra la otra parte: nadie declara su propia negativa");
        }

        var carros = await CargarAsync(viaje, ct);
        AsegurarCargueCompleto(carros);
        var porFirmar = carros.Where(c => !c.CerradaAntes).ToList();
        var motivo = clsValidador.Opcional(solicitud.Motivo);
        if (porFirmar.Count > 0 && porFirmar.All(c => c.Transferencia is { TipoCierre: clsTipoCierre.NO_FIRMA, FirmaPorNinera: true } t && t.NoFirmaParte == solicitud.Parte && t.MotivoNoFirma == motivo))
        {
            return Armar(viaje, quienRegistra, carros);   // la misma constancia otra vez: no crea otra
        }

        var testigoId = solicitud.TestigoUsuarioId!.Value;
        var testigo = await usuarios.ObtenerPorIdAsync(testigoId, ct);
        if (testigo is not { Activo: true } || (actual.ClienteId is { } clienteActor && testigo.ClienteId != clienteActor))
        {
            throw clsErroresCampos.De("testigo_usuario_id", "El testigo no existe, está inactivo o no es de su cliente");
        }

        var ahora = reloj.Ahora;
        var foto = solicitud.Evidencia!;
        // Una sola foto de la constancia, colgada de la primera salida del cargue; la transferencia de cada carro la referencia.
        var primera = porFirmar.OrderBy(c => c.Vehiculo.Vin, StringComparer.Ordinal).First();
        var evidencia = await fabricaEvidencias.CrearAsync(
            primera.Salida!.Id, null, null, esConstancia: true, foto.StoragePath!, foto.SelloFecha!.Value, foto.SelloLat, foto.SelloLng, foto.Precision, foto.DispositivoId, foto.Nota, foto.HashSha256, ct);
        inspecciones.Agregar(evidencia);
        foreach (var carro in porFirmar)
        {
            var transferencia = ObtenerOCrear(carro, viaje, ahora);
            var antes = JsonSerializer.Serialize(clsInstantaneasAuditoria.De(transferencia));
            transferencia.RegistrarNoFirma(actorId, solicitud.Parte, solicitud.Motivo, testigoId, evidencia.Id, ahora, momento, porNinera: true);
            await RecalcularEvidenciaAsync(carro, transferencia, ct);
            await auditor.RegistrarDeViajeAsync(
                "transferencia.no_firma_ninera", "transferencias_custodia", transferencia.Id, viaje.Id,
                new DetalleAuditoria(JsonSerializer.Deserialize<JsonElement>(antes), new { transferencia = clsInstantaneasAuditoria.De(transferencia), parte_que_se_nego = solicitud.Parte, vin = carro.Vehiculo.Vin, momento }),
                carro.Vehiculo.ClienteId, ct);
        }

        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        await AvisarNoFirmaAsync(viaje, solicitud.Parte!, motivo!, actorId, porFirmar, ct);
        return Armar(viaje, quienRegistra, carros);
    }

    // ------------------------------------------------------------------------------------------------ comunes

    private static string? ParteValida(string? texto, Dictionary<string, string[]> errores)
    {
        var parte = clsValidador.Opcional(texto)?.ToLowerInvariant();
        if (parte is null || !clsParteFirmante.VALORES.Contains(parte))
        {
            errores["parte"] = [$"Indique la parte que firma: {clsParteFirmante.ENTREGA} (el inspector del viaje) o {clsParteFirmante.RECIBE} (el conductor del viaje)"];
            return null;
        }

        return parte;
    }

    /// <summary>La hora del momento (ISO 8601 con zona) y el GPS del momento (latitud y longitud juntas, en su rango; la precision, si llega, no negativa).</summary>
    private static MomentoDeFirma? MomentoValido(string? fecha, double? lat, double? lng, double? precisionM, Dictionary<string, string[]> errores)
    {
        var momento = clsFechaProgramada.DeIso(fecha);
        if (momento is null)
        {
            errores["fecha_momento"] = ["Debe traer la hora del momento en que se firmó, en formato ISO 8601 con su zona horaria (por ejemplo 2026-11-18T14:09:00-05:00)"];
        }

        if (lat is null || lng is null)
        {
            errores["lat"] = ["El GPS del momento de la firma es obligatorio: latitud y longitud"];
        }
        else
        {
            if (lat is < -90 or > 90 || !double.IsFinite(lat.Value))
            {
                errores["lat"] = ["La latitud debe estar entre -90 y 90"];
            }

            if (lng is < -180 or > 180 || !double.IsFinite(lng.Value))
            {
                errores["lng"] = ["La longitud debe estar entre -180 y 180"];
            }
        }

        if (precisionM is < 0 || (precisionM is { } p && !double.IsFinite(p)))
        {
            errores["precision_m"] = ["La precisión debe ser un número en metros mayor o igual a 0"];
        }

        return errores.Count > 0 || momento is null ? null : new MomentoDeFirma(momento.Value, lat!.Value, lng!.Value, precisionM);
    }

    /// <summary>Las reservas por carro (VIN normalizado -> observaciones): de 1 a 1 000 caracteres cada una, sin VIN repetidos (400 en <c>reservas</c> con lo que falla).</summary>
    private static Dictionary<string, string> LimpiarReservas(IReadOnlyList<ReservaDelCargueRequest>? reservas, Dictionary<string, string[]> errores)
    {
        var resultado = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var problemas = new List<string>();
        foreach (var reserva in reservas ?? [])
        {
            var vin = clsValidador.Opcional(reserva.Vin) is { } v ? clsVin.Normalizar(v) : null;
            var texto = clsValidador.Opcional(reserva.Observaciones);
            if (vin is null)
            {
                problemas.Add("Cada reserva debe traer el VIN del carro");
            }
            else if (texto is null)
            {
                problemas.Add($"La reserva del VIN {vin} no tiene observaciones: no se puede recibir con reservas sin ellas");
            }
            else if (texto.Length > clsTransferenciaCustodia.RESERVAS_MAX)
            {
                problemas.Add($"Las observaciones del VIN {vin} admiten máximo {clsTransferenciaCustodia.RESERVAS_MAX} caracteres");
            }
            else if (!resultado.TryAdd(vin, texto))
            {
                problemas.Add($"El VIN {vin} tiene más de una reserva");
            }
        }

        if (problemas.Count > 0)
        {
            errores["reservas"] = [.. problemas];
        }

        return resultado;
    }

    private static void VerificarReservasContraLosCarros(Dictionary<string, string> reservas, List<Carro> carros)
    {
        var activos = carros.Select(c => c.Vehiculo.Vin).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var ajenos = reservas.Keys.Where(vin => !activos.Contains(vin)).Order(StringComparer.Ordinal).ToList();
        if (ajenos.Count > 0)
        {
            throw clsErroresCampos.De("reservas", $"No son carros activos del viaje: {string.Join(", ", ajenos)}");
        }
    }

    private async Task<clsViaje> ObtenerViajeAsync(Guid viajeId, CancellationToken ct) =>
        await viajes.ObtenerPorIdAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado", clsMotivosRechazo.VIAJE_NO_EXISTE);

    private static string? ParteDelUsuario(clsViaje viaje, Guid usuarioId) =>
        viaje.InspectorId == usuarioId ? clsParteFirmante.ENTREGA : viaje.ConductorId == usuarioId ? clsParteFirmante.RECIBE : null;

    /// <summary>El usuario autenticado debe ser el inspector (entrega) o el conductor (recibe) del viaje, y la parte indicada la suya: nadie firma por otro (403).</summary>
    private Guid ActorDe(clsViaje viaje, string parteIndicada)
    {
        var actorId = actual.IdObligatorio();
        var suya = ParteDelUsuario(viaje, actorId) ?? throw new clsProhibidoException("Solo el inspector o el conductor del viaje firman el cargue: nadie actúa por otro", clsMotivosRechazo.VIAJE_NO_ASIGNADO);
        if (suya != parteIndicada)
        {
            throw new clsProhibidoException($"Usted es {(suya == clsParteFirmante.ENTREGA ? "quien entrega (el inspector del viaje)" : "quien recibe (el conductor del viaje)")}: firme como {suya}, nadie firma por la otra parte");
        }

        if (viaje.InspectorId is null || viaje.ConductorId is null)
        {
            throw new clsConflictoException("El viaje necesita su inspector y su conductor para firmar el cargue: falta asignar a alguno");
        }

        return actorId;
    }

    /// <summary>Los carros activos del viaje (los «no sale» quedan fuera) con la llegada y la salida del cargue (su ultima version) y la transferencia del cargue.</summary>
    private async Task<List<Carro>> CargarAsync(clsViaje viaje, CancellationToken ct)
    {
        var codigos = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).ToDictionary(v => v.Id, v => v.Codigo);
        var etapas = await viajes.ObtenerEtapasAsync(viaje.Id, null, ct);
        var resultado = new List<Carro>();
        foreach (var enlace in (await viajes.ObtenerVehiculosAsync(viaje.Id, ct)).Where(e => !e.NoSale))
        {
            var vehiculo = await vehiculos.ObtenerPorIdAsync(enlace.VehiculoId, ct) ?? throw new clsEntidadNoEncontradaException("El vehículo del viaje no existe");
            var cargue = etapas.FirstOrDefault(e => e.VehiculoId == enlace.VehiculoId && codigos.TryGetValue(e.EtapaCatalogoId, out var codigo) && codigo == clsCodigosEtapa.CARGUE);
            var llegada = cargue is null ? null : await inspecciones.ObtenerUltimaDeEtapaAsync(cargue.Id, clsMomento.LLEGADA, ct);
            var salida = cargue is null ? null : await inspecciones.ObtenerUltimaDeEtapaAsync(cargue.Id, clsMomento.SALIDA, ct);
            var deLaSalida = salida is null ? null : await transferencias.ObtenerPorInspeccionAsync(salida.Id, ct);
            // Un carro cuyo cargue ya se cerro carro por carro (transferencia colgada de la llegada) cuenta como entrega cerrada.
            var deLaLlegada = llegada is null || deLaSalida is not null ? null : await transferencias.ObtenerPorInspeccionAsync(llegada.Id, ct);
            var cerradaAntes = deLaLlegada is { EstaCerrada: true };
            resultado.Add(new Carro(enlace, vehiculo, llegada, salida, cerradaAntes ? deLaLlegada : deLaSalida, cerradaAntes));
        }

        if (resultado.Count == 0)
        {
            throw new clsConflictoException("El viaje no tiene carros activos para firmar el cargue");
        }

        return resultado;
    }

    /// <summary>409 <c>cargue_incompleto</c>: cada carro activo debe tener la llegada y la salida del cargue cerradas; el mensaje nombra que falta y de que carro.</summary>
    private static void AsegurarCargueCompleto(List<Carro> carros)
    {
        var faltantes = new List<string>();
        foreach (var carro in carros.Where(c => !c.CerradaAntes).OrderBy(c => c.Vehiculo.Vin, StringComparer.Ordinal))
        {
            if (carro.Llegada is not { EstaCerrada: true })
            {
                faltantes.Add($"Falta la llegada del cargue del VIN {carro.Vehiculo.Vin}");
            }

            if (carro.Salida is not { EstaCerrada: true })
            {
                faltantes.Add($"Falta la salida del cargue del VIN {carro.Vehiculo.Vin}");
            }
        }

        if (faltantes.Count > 0)
        {
            throw new clsConflictoException(string.Join("; ", faltantes), clsMotivosRechazo.CARGUE_INCOMPLETO, new Dictionary<string, object> { ["faltantes"] = faltantes });
        }
    }

    /// <summary>La transferencia del cargue del carro (colgada de su salida): la que ya existe o una nueva entre el inspector (entrega) y el conductor (recibe) del viaje; si ya existe, los firmantes deben ser los mismos (409).</summary>
    private clsTransferenciaCustodia ObtenerOCrear(Carro carro, clsViaje viaje, DateTimeOffset ahora)
    {
        if (carro.Transferencia is { } existente)
        {
            if ((existente.EntregaUsuarioId is not null && existente.EntregaUsuarioId != viaje.InspectorId) || (existente.RecibeUsuarioId is not null && existente.RecibeUsuarioId != viaje.ConductorId))
            {
                throw new clsConflictoException($"Los firmantes de la transferencia del VIN {carro.Vehiculo.Vin} no se pueden cambiar", clsMotivosRechazo.FIRMANTES_DE_LA_TRANSFERENCIA);
            }

            return existente;
        }

        var nueva = clsTransferenciaCustodia.Crear(carro.Salida!.Id, viaje.InspectorId!.Value, viaje.ConductorId!.Value, ahora);
        transferencias.Agregar(nueva);
        carro.Transferencia = nueva;
        return nueva;
    }

    /// <summary>La evidencia de los hallazgos de la llegada y de la salida del cargue depende de como cierra la transferencia del carro.</summary>
    private async Task RecalcularEvidenciaAsync(Carro carro, clsTransferenciaCustodia transferencia, CancellationToken ct)
    {
        if (carro.Llegada is not null)
        {
            await evidenciaDeHallazgos.RecalcularAsync(carro.Llegada.Id, transferencia.TipoCierre, ct);
        }

        if (carro.Salida is not null)
        {
            await evidenciaDeHallazgos.RecalcularAsync(carro.Salida.Id, transferencia.TipoCierre, ct);
        }
    }

    private static CargueNineraDto Armar(clsViaje viaje, string parte, List<Carro> carros)
    {
        var detalle = carros
            .OrderBy(c => c.Vehiculo.Vin, StringComparer.Ordinal)
            .Select(c => new CargueUnidadDto(c.Vehiculo.Vin, c.Transferencia?.TipoCierre ?? "abierta", c.Transferencia?.FirmaPorNinera ?? false))
            .ToList();
        var cerradas = detalle.Count(d => d.TipoCierre != "abierta");
        var firmadas = carros.Where(c => c.Transferencia is { FirmaPorNinera: true, TipoCierre: clsTipoCierre.FIRMADA or clsTipoCierre.CON_RESERVAS }).Select(c => c.Transferencia!.FirmadaAt).Where(f => f is not null).ToList();
        return new CargueNineraDto(viaje.Id, parte, detalle.Count, cerradas, firmadas.Count == detalle.Count(d => d.PorNinera) && firmadas.Count > 0 ? firmadas.Max() : null, detalle);
    }

    /// <summary>
    /// Avisa al coordinador de calidad (y administradores) del cliente que una parte se nego a firmar el cargue de la niñera: un solo aviso por viaje, sin cedulas. Nunca lanza: la constancia ya quedo guardada y no se deshace.
    /// </summary>
    private async Task AvisarNoFirmaAsync(clsViaje viaje, string parteQueSeNego, string motivo, Guid actorId, List<Carro> carros, CancellationToken ct)
    {
        try
        {
            var clienteId = carros[0].Vehiculo.ClienteId;
            var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD, clsRol.ADMIN], clienteId, ct);
            var nombres = new Dictionary<string, string?>
            {
                [clsParteFirmante.ENTREGA] = viaje.InspectorId is { } i ? (await usuarios.ObtenerPorIdAsync(i, ct))?.Nombre : null,
                [clsParteFirmante.RECIBE] = viaje.ConductorId is { } c ? (await usuarios.ObtenerPorIdAsync(c, ct))?.Nombre : null,
            };
            var seNego = nombres[parteQueSeNego] ?? (parteQueSeNego == clsParteFirmante.ENTREGA ? "El inspector" : "El conductor");
            var codigoViaje = viaje.CodigoMtrack is { } codigo ? $" {codigo}" : string.Empty;
            await emisor.EmitirAsync(
                new clsAlerta(
                    clsTipoNotificacion.NOVEDAD_PROCESO,
                    clsUrgenciaNotificacion.ALTA,
                    destinatarios.Select(d => d.Id).ToList(),
                    clienteId,
                    viaje.Id,
                    null,
                    null,
                    null,
                    $"Cargue sin firma de la niñera{codigoViaje}",
                    $"{seNego} se negó a firmar el cargue de la niñera del viaje{codigoViaje} ({carros.Count} carros). Motivo: {motivo}.",
                    $"{clsTipoNotificacion.NOVEDAD_PROCESO}:cargue_no_firmado:{viaje.Id}",
                    new Dictionary<string, object?>
                    {
                        ["hecho"] = "cargue_no_firmado",
                        ["viaje_id"] = viaje.Id,
                        ["parte_que_se_nego"] = parteQueSeNego,
                        ["carros"] = carros.Count,
                        ["motivo"] = motivo,
                    }),
                ct);
        }
        catch (Exception)
        {
            // El aviso es un efecto aparte: la constancia ya quedo guardada y no se deshace.
        }
    }
}
