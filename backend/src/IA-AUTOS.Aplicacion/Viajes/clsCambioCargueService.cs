using System.Text.Json;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Viajes.DTOViajes;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Notificaciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>
/// El muelle y el trailer del viaje durante el cargue: los cambian el inspector del viaje (o quien reclamo el cargue) y el coordinador de calidad, nadie mas (conductor, admin y super admin reciben 403; TI conserva
/// <c>PATCH /api/viajes/{id}</c>). Al inspector se le acepta solo mientras la entrega del cargue no se haya cerrado (409 <c>cargue_firmado</c>); el muelle debe ser uno activo del lugar de cargue del viaje (400). Cuando lo cambia
/// el inspector, el coordinador del cliente recibe un aviso <c>cambio_cargue_inspector</c> (urgencia media, solo bandeja) por cada cosa que cambio; los cambios del propio coordinador no avisan.
/// </summary>
public sealed class clsCambioCargueService(
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IUsuarioRepository usuarios,
    IConsultasViajes consultas,
    ICatalogosLectura catalogos,
    clsAccesoViajes acceso,
    clsEmisorNotificaciones emisor,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj)
{
    private static readonly string[] CAMPOS = ["muelle_id", "trailer"];

    public async Task<ViajeDto> CambiarAsync(Guid viajeId, IReadOnlyDictionary<string, JsonElement> campos, CancellationToken ct = default)
    {
        var lector = new clsCamposCatalogo(campos);
        lector.SoloPermitidos(CAMPOS, "el cargue del viaje");
        if (!lector.Tiene("muelle_id") && !lector.Tiene("trailer"))
        {
            throw clsErroresCampos.De("muelle_id", "Indique el muelle o el tráiler que cambia");
        }

        var viaje = await viajes.ObtenerPorIdAsync(viajeId, ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado", clsMotivosRechazo.VIAJE_NO_EXISTE);
        var esInspector = actual.Rol == clsRol.INSPECTOR;
        if (esInspector)
        {
            if (!await acceso.ParticipaOReclamaAsync(viaje, actual.IdObligatorio(), ct))
            {
                throw new clsProhibidoException("El viaje no está asignado a su usuario", clsMotivosRechazo.VIAJE_NO_ASIGNADO);
            }
        }
        else if (actual.Rol != clsRol.COORDINADOR_CALIDAD
            || (actual.ClienteRestringido() is { } cliente && !await consultas.ViajeEsDeClienteAsync(viaje.Id, cliente, ct)))
        {
            throw new clsProhibidoException("Solo el inspector del viaje y el coordinador de calidad cambian el tráiler y el muelle durante el cargue");
        }

        viaje.AsegurarActivo();
        if (esInspector && await viajes.EntregaDelCargueCerradaAsync(viaje.Id, ct))
        {
            throw new clsConflictoException("El tráiler y el muelle solo los cambia el inspector durante el cargue", clsMotivosRechazo.CARGUE_FIRMADO);
        }

        var cambiaMuelle = lector.Tiene("muelle_id");
        var muelleId = lector.Identificador("muelle_id");
        var muelles = await catalogos.ValoresAsync(clsNombresCatalogo.MUELLES, ct);
        if (cambiaMuelle && muelleId is { } elegido
            && !muelles.Any(m => m.Id == elegido && m.Activo && m.TryGetValue("lugar_id", out var lugar) && lugar is Guid l && l == viaje.LugarCargueId))
        {
            throw clsErroresCampos.De("muelle_id", "El muelle no pertenece al lugar de cargue del viaje");
        }

        string NombreDelMuelle(string? id) =>
            id is not null && Guid.TryParse(id, out var guid) && muelles.FirstOrDefault(m => m.Id == guid) is { } m && m.TryGetValue("nombre", out var nombre) && nombre is string n ? n : "sin muelle";

        var ahora = reloj.Ahora;
        var cambios = viaje.CambiarCargue(cambiaMuelle, muelleId, lector.Tiene("trailer"), lector.Texto("trailer"), ahora);
        if (cambios.Count == 0)
        {
            return await consultas.ObtenerAsync(viaje.Id, actual.ClienteRestringido(), ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");
        }

        await auditor.RegistrarDeViajeAsync(
            "viaje.cambiar_cargue",
            "viajes",
            viaje.Id,
            viaje.Id,
            new DetalleAuditoria(
                new { cambios = cambios.Select(c => new { campo = c.Campo, antes = c.Campo == "muelle" ? NombreDelMuelle(c.Antes) : c.Antes }).ToList() },
                new { muelle_id = viaje.MuelleId, trailer = viaje.Trailer }),
            null,
            ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        if (esInspector)
        {
            await AvisarAsync(viaje, cambios, NombreDelMuelle(viaje.MuelleId?.ToString()), NombreDelMuelle, ct);
        }

        return await consultas.ObtenerAsync(viaje.Id, actual.ClienteRestringido(), ct) ?? throw new clsEntidadNoEncontradaException($"Viaje {viajeId} no encontrado");
    }

    /// <summary>Avisa al coordinador de calidad (y a los administradores) del cliente lo que cambio el inspector: solo bandeja, urgencia media, sin ningun dato personal. Nunca lanza: el cambio ya quedo guardado.</summary>
    private async Task AvisarAsync(
        clsViaje viaje, IReadOnlyList<(string Campo, string? Antes)> cambios, string muelleNuevo, Func<string?, string> nombreDelMuelle, CancellationToken ct)
    {
        try
        {
            var clienteId = (await viajes.ObtenerVehiculosAsync(viaje.Id, ct)).Select(e => e.VehiculoId).FirstOrDefault() is { } vehiculoId && vehiculoId != Guid.Empty
                ? (await vehiculos.ObtenerPorIdAsync(vehiculoId, ct))?.ClienteId
                : null;
            if (clienteId is not { } cliente)
            {
                return;
            }

            var destinatarios = await usuarios.ObtenerActivosPorRolesAsync([clsRol.COORDINADOR_CALIDAD], cliente, ct);
            var nombre = (await usuarios.ObtenerPorIdAsync(actual.IdObligatorio(), ct))?.Nombre ?? "El inspector";
            var codigo = viaje.CodigoMtrack ?? viaje.Id.ToString();
            foreach (var (campo, antes) in cambios)
            {
                var esMuelle = campo == "muelle";
                var despues = esMuelle ? muelleNuevo : viaje.Trailer ?? "sin tráiler";
                var anterior = esMuelle ? nombreDelMuelle(antes) : antes ?? "sin tráiler";
                var mensaje = esMuelle
                    ? (antes is null ? $"{nombre} asignó el {despues.ToLowerInvariant()} al viaje {codigo}" : $"{nombre} cambió el muelle del viaje {codigo} de {anterior} a {despues}")
                    : $"{nombre} cambió el tráiler del viaje {codigo} de {anterior} a {despues}";
                await emisor.EmitirAsync(
                    new clsAlerta(
                        clsTipoNotificacion.CAMBIO_CARGUE_INSPECTOR,
                        clsUrgenciaNotificacion.MEDIA,
                        destinatarios.Select(d => d.Id).ToList(),
                        clienteId,
                        viaje.Id,
                        null,
                        null,
                        null,
                        esMuelle ? "Muelle del viaje" : "Tráiler del viaje",
                        mensaje,
                        $"{clsTipoNotificacion.CAMBIO_CARGUE_INSPECTOR}:{viaje.Id}:{campo}:{reloj.Ahora.UtcTicks}",
                        new Dictionary<string, object?>
                        {
                            ["viaje_id"] = viaje.Id,
                            ["codigo_mtrack"] = viaje.CodigoMtrack,
                            ["campo"] = campo,
                            ["antes"] = anterior,
                            ["despues"] = despues,
                            ["usuario_nombre"] = nombre,
                        },
                        SoloBandeja: true),
                    ct);
            }
        }
        catch (Exception)
        {
            // El aviso es un efecto aparte: el cambio ya quedo guardado y no se deshace.
        }
    }
}
