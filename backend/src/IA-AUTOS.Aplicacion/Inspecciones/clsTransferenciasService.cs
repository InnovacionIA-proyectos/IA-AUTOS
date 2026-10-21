using System.Text.RegularExpressions;
using FluentValidation;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// Transferencia de custodia de una inspeccion: cada firma la envia su titular (quien entrega o quien recibe; nadie firma por otro), quien
/// recibe puede hacerlo con reservas, y si una parte se niega a firmar se deja constancia (motivo, testigo y foto). Los firmantes son usuarios
/// del viaje. Una transferencia sin tipo de cierre bloquea la etapa siguiente. Cada firma guarda su fecha y la IP desde la que se envio.
/// </summary>
public sealed partial class clsTransferenciasService(
    IInspeccionRepository inspecciones,
    ITransferenciaCustodiaRepository transferencias,
    IViajeRepository viajes,
    IUsuarioRepository usuarios,
    IConsultasInspecciones consultas,
    clsFabricaEvidencias fabricaEvidencias,
    IAlmacenArchivos almacen,
    IGeneradorPdfConstancia generadorPdf,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<FirmarTransferenciaRequest> validadorFirma,
    IValidator<RecibirConReservasRequest> validadorReservas,
    IValidator<RegistrarNoFirmaRequest> validadorNoFirma)
{
    /// <summary>Firma de la propia parte. Crea la transferencia si es la primera firma.</summary>
    public async Task<TransferenciaDto> FirmarAsync(Guid inspeccionId, FirmarTransferenciaRequest solicitud, ContextoCliente cliente, CancellationToken ct = default)
    {
        await validadorFirma.ValidarOLanzarAsync(solicitud, ct);
        var transferencia = await ObtenerOCrearAsync(inspeccionId, solicitud.EntregaUsuarioId, solicitud.RecibeUsuarioId, ct);
        transferencia.Firmar(actual.IdObligatorio(), solicitud.Firma!, cliente.Ip, reloj.Ahora);
        auditor.Registrar("transferencia.firmar", "transferencias_custodia", transferencia.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorInspecciones.ATransferencia(transferencia);
    }

    /// <summary>Quien recibe firma con reservas: sus observaciones son obligatorias. Solo quien recibe puede (403 para cualquier otro).</summary>
    public async Task<TransferenciaDto> RecibirConReservasAsync(Guid inspeccionId, RecibirConReservasRequest solicitud, ContextoCliente cliente, CancellationToken ct = default)
    {
        await validadorReservas.ValidarOLanzarAsync(solicitud, ct);
        var transferencia = await ObtenerOCrearAsync(inspeccionId, solicitud.EntregaUsuarioId, solicitud.RecibeUsuarioId, ct);
        transferencia.FirmarConReservas(actual.IdObligatorio(), solicitud.Firma!, solicitud.Reservas, cliente.Ip, reloj.Ahora);
        auditor.Registrar("transferencia.reservas", "transferencias_custodia", transferencia.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorInspecciones.ATransferencia(transferencia);
    }

    /// <summary>
    /// Constancia de no firma: la registra la otra parte, con el motivo, un testigo con usuario y la foto. La foto es una evidencia marcada
    /// como constancia (se admite aunque la inspeccion ya este cerrada).
    /// </summary>
    public async Task<TransferenciaDto> RegistrarNoFirmaAsync(Guid inspeccionId, RegistrarNoFirmaRequest solicitud, ContextoCliente cliente, CancellationToken ct = default)
    {
        await validadorNoFirma.ValidarOLanzarAsync(solicitud, ct);
        var transferencia = await ObtenerOCrearAsync(inspeccionId, solicitud.EntregaUsuarioId, solicitud.RecibeUsuarioId, ct);
        var actorId = actual.IdObligatorio();
        if (transferencia.ParteDe(actorId) is null)
        {
            throw new clsProhibidoException("Solo quien entrega o quien recibe puede registrar la no firma: nadie actúa por otro");
        }

        var testigoId = solicitud.TestigoUsuarioId!.Value;
        var testigo = await usuarios.ObtenerPorIdAsync(testigoId, ct);
        if (testigo is not { Activo: true } || (actual.ClienteId is { } clienteActor && testigo.ClienteId != clienteActor))
        {
            throw clsErroresCampos.De("testigo_usuario_id", "El testigo no existe, está inactivo o no es de su cliente");
        }

        var foto = solicitud.Evidencia!;
        var evidencia = await fabricaEvidencias.CrearAsync(
            inspeccionId, null, null, esConstancia: true, foto.StoragePath!, foto.SelloFecha!.Value, foto.SelloLat, foto.SelloLng, foto.Precision,
            foto.DispositivoId, foto.Nota, foto.HashSha256, ct);
        transferencia.RegistrarNoFirma(actorId, solicitud.Parte, solicitud.Motivo, testigoId, evidencia.Id, reloj.Ahora);
        inspecciones.Agregar(evidencia);
        auditor.Registrar("transferencia.no_firma", "transferencias_custodia", transferencia.Id);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return clsMapeadorInspecciones.ATransferencia(transferencia);
    }

    /// <summary>
    /// La constancia de la transferencia en PDF: firmas con su fecha e IP, reservas o motivo de la no firma, testigo y fotos. Inspector y
    /// conductor solo la descargan de sus viajes y quien se limita a su cliente, la de VIN de su cliente (403 si es de otro).
    /// </summary>
    public async Task<byte[]> ObtenerConstanciaPdfAsync(Guid inspeccionId, CancellationToken ct = default)
    {
        var informe = await consultas.ObtenerInformeAsync(inspeccionId, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {inspeccionId} no encontrada");
        await AsegurarVisibleAsync(informe.ViajeId, ct);
        if (actual.ClienteRestringido() is { } cliente && !await consultas.EsDeClienteAsync(inspeccionId, cliente, ct))
        {
            throw new clsProhibidoException("La inspección es de un VIN de otro cliente");
        }

        var transferencia = await transferencias.ObtenerPorInspeccionAsync(inspeccionId, ct)
            ?? throw new clsEntidadNoEncontradaException("La inspección no tiene transferencia de custodia");

        var entrega = await ArmarFirmaAsync(transferencia.EntregaUsuarioId, transferencia.FirmaEntrega, transferencia.FirmaEntregaAt, transferencia.FirmaEntregaIp, ct);
        var recibe = await ArmarFirmaAsync(transferencia.RecibeUsuarioId, transferencia.FirmaRecibe, transferencia.FirmaRecibeAt, transferencia.FirmaRecibeIp, ct);
        string? testigo = null;
        var fotos = new List<FotoConstancia>();
        if (transferencia.TestigoUsuarioId is { } testigoId)
        {
            testigo = (await usuarios.ObtenerPorIdAsync(testigoId, ct))?.Nombre;
        }

        if (transferencia.EvidenciaConstanciaId is { } evidenciaId && await inspecciones.ObtenerEvidenciaAsync(evidenciaId, ct) is { } evidencia
            && await almacen.LeerAsync(evidencia.StoragePath, ct) is { } contenido)
        {
            var ubicacion = evidencia.SinUbicacion ? "sin ubicación" : $"{evidencia.SelloLat:0.#####}, {evidencia.SelloLng:0.#####}";
            fotos.Add(new FotoConstancia(contenido, $"Foto de la constancia · {evidencia.SelloFecha.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC · {ubicacion} · recibida {evidencia.SelloServidorAt.UtcDateTime:yyyy-MM-dd HH:mm:ss} UTC"));
        }

        return generadorPdf.Generar(new DatosConstancia(
            informe.Vin,
            informe.Marca,
            informe.Modelo,
            informe.Proceso,
            informe.MomentoNombre,
            informe.CodigoMtrack,
            informe.Placa,
            transferencia.TipoCierre,
            entrega,
            recibe,
            transferencia.ReservasTexto,
            transferencia.NoFirmaParte,
            transferencia.MotivoNoFirma,
            testigo,
            transferencia.FirmadaAt,
            fotos,
            reloj.Ahora));
    }

    /// <summary>Los usuarios del viaje que pueden firmar una transferencia: su conductor y su inspector activos.</summary>
    public async Task<IReadOnlyList<FirmanteDto>> FirmantesDelViajeAsync(Guid viajeId, CancellationToken ct = default)
    {
        var viaje = await viajes.ObtenerPorIdAsync(viajeId, ct)
            ?? throw new clsEntidadNoEncontradaException("Viaje no encontrado");
        await AsegurarVisibleAsync(viaje, ct);
        var firmantes = new List<FirmanteDto>();
        foreach (var id in new[] { viaje.InspectorId, viaje.ConductorId }.OfType<Guid>().Distinct())
        {
            if (await usuarios.ObtenerPorIdAsync(id, ct) is { Activo: true } usuario)
            {
                firmantes.Add(new FirmanteDto(usuario.Id, usuario.Nombre, usuario.Rol));
            }
        }

        return firmantes;
    }

    /// <summary>
    /// La transferencia existente, o una nueva si es la primera vez. Al crearla los dos ids son obligatorios, quien envia debe ser uno de los dos
    /// (403: nadie actua por otro) y los dos deben ser usuarios del viaje. Si ya existe, los ids que lleguen deben ser los mismos (409).
    /// </summary>
    private async Task<clsTransferenciaCustodia> ObtenerOCrearAsync(Guid inspeccionId, Guid? entregaId, Guid? recibeId, CancellationToken ct)
    {
        var inspeccion = await inspecciones.ObtenerPorIdAsync(inspeccionId, ct)
            ?? throw new clsEntidadNoEncontradaException($"Inspección {inspeccionId} no encontrada");
        var existente = await transferencias.ObtenerPorInspeccionAsync(inspeccionId, ct);
        if (existente is not null)
        {
            if ((entregaId is not null && entregaId != existente.EntregaUsuarioId) || (recibeId is not null && recibeId != existente.RecibeUsuarioId))
            {
                throw new clsConflictoException("Los firmantes de la transferencia no se pueden cambiar");
            }

            return existente;
        }

        var faltantes = new Dictionary<string, string[]>();
        if (entregaId is null)
        {
            faltantes["entrega_usuario_id"] = ["Indique quién entrega: la primera firma crea la transferencia"];
        }

        if (recibeId is null)
        {
            faltantes["recibe_usuario_id"] = ["Indique quién recibe: la primera firma crea la transferencia"];
        }

        if (faltantes.Count > 0)
        {
            throw clsErroresCampos.Varios(faltantes);
        }

        var actorId = actual.IdObligatorio();
        if (actorId != entregaId && actorId != recibeId)
        {
            throw new clsProhibidoException("Solo quien entrega o quien recibe puede firmar: nadie actúa por otro");
        }

        var viaje = await viajes.ObtenerPorIdAsync(inspeccion.ViajeId, ct)
            ?? throw new clsEntidadNoEncontradaException("El viaje de la inspección no existe");
        var fuera = new Dictionary<string, string[]>();
        if (!EsDelViaje(viaje, entregaId!.Value))
        {
            fuera["entrega_usuario_id"] = ["Quien entrega debe ser un usuario del viaje"];
        }

        if (!EsDelViaje(viaje, recibeId!.Value))
        {
            fuera["recibe_usuario_id"] = ["Quien recibe debe ser un usuario del viaje"];
        }

        if (fuera.Count > 0)
        {
            throw clsErroresCampos.Varios(fuera);
        }

        var nueva = clsTransferenciaCustodia.Crear(inspeccionId, entregaId.Value, recibeId.Value, reloj.Ahora);
        transferencias.Agregar(nueva);
        return nueva;
    }

    private static bool EsDelViaje(clsViaje viaje, Guid usuarioId) => viaje.ConductorId == usuarioId || viaje.InspectorId == usuarioId;

    private async Task AsegurarVisibleAsync(Guid viajeId, CancellationToken ct)
    {
        var viaje = await viajes.ObtenerPorIdAsync(viajeId, ct);
        if (viaje is null)
        {
            throw new clsEntidadNoEncontradaException("El viaje de la inspección no existe");
        }

        await AsegurarVisibleAsync(viaje, ct);
    }

    /// <summary>Inspector y conductor solo ven lo de sus viajes (403 si es de otro).</summary>
    private Task AsegurarVisibleAsync(clsViaje viaje, CancellationToken ct)
    {
        if (actual.SoloSusViajes() is { } usuarioViaje && !viaje.Participa(usuarioViaje))
        {
            throw new clsProhibidoException("El viaje no está asignado a su usuario");
        }

        return Task.CompletedTask;
    }

    /// <summary>La firma de una parte para el PDF: si se guardo como archivo se incrusta la imagen; si no, se dibuja el trazo.</summary>
    private async Task<FirmaConstancia> ArmarFirmaAsync(Guid usuarioId, string? firma, DateTimeOffset? fecha, string? ip, CancellationToken ct)
    {
        var nombre = (await usuarios.ObtenerPorIdAsync(usuarioId, ct))?.Nombre ?? "—";
        if (string.IsNullOrEmpty(firma))
        {
            return new FirmaConstancia(nombre, fecha, ip, null, null);
        }

        if (ArchivoSubido().IsMatch(firma))
        {
            var imagen = await almacen.LeerAsync(firma, ct);
            return new FirmaConstancia(nombre, fecha, ip, imagen is null ? firma : null, imagen);
        }

        return new FirmaConstancia(nombre, fecha, ip, firma, null);
    }

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,200}\.(jpg|jpeg|png)$", RegexOptions.IgnoreCase)]
    private static partial Regex ArchivoSubido();
}
