using System.Text.Json;
using FluentValidation;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Inspecciones.Validators;
using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Inspecciones;

/// <summary>
/// La revision de un hallazgo que registro el conductor: la zona (<c>cuadrante_numero</c> y <c>lado</c>, o <c>zona_provisional</c>), la clase, el nivel V (los tres obligatorios), el tamano si hay
/// que corregir el que eligio el conductor y una nota.
/// </summary>
public sealed record RevisarHallazgoRequest(
    int? CuadranteNumero,
    string? Lado,
    string? ZonaProvisional,
    string? ClaseDano,
    string? NivelSeveridad,
    string? TamanoDano,
    string? Nota);

public sealed class RevisarHallazgoRequestValidator : AbstractValidator<RevisarHallazgoRequest>
{
    public RevisarHallazgoRequestValidator()
    {
        clsReglasZona.Aplicar(this, x => x.CuadranteNumero, x => x.Lado, x => x.ZonaProvisional);
        RuleFor(x => x.ClaseDano).Must(c => !string.IsNullOrWhiteSpace(c)).WithMessage("Clase de daño inválida");
        RuleFor(x => x.NivelSeveridad).Must(n => !string.IsNullOrWhiteSpace(n)).WithMessage("El nivel de severidad es requerido");
        RuleFor(x => x.Nota).Must(n => n is null || n.Trim().Length <= clsHallazgo.NOTA_MAX)
            .WithMessage($"La nota admite máximo {clsHallazgo.NOTA_MAX} caracteres");
    }
}

/// <summary>
/// Un inspector (de un viaje de la unidad) o un administrador revisa el hallazgo que registro el conductor: lo ubica en su zona, le da su clase y le asigna el nivel V, que el conductor nunca
/// asigna, y desde ahi el hallazgo entra al seguimiento y a los patrones. El coordinador de calidad tambien revisa, a distancia, los que el conductor registra en la salida de un Cargue sin personal
/// de apoyo. La revision queda en el historial del hallazgo y, si el nivel es critico, alerta al coordinador y al administrador del cliente.
/// </summary>
public sealed class clsRevisionService(
    IInspeccionRepository inspecciones,
    IEtapaRepository etapas,
    IViajeRepository viajes,
    IVehiculoRepository vehiculos,
    IConsultasViajes consultasViajes,
    IConsultasInspecciones consultas,
    ICatalogosLectura catalogos,
    clsResolutorZona resolutorZona,
    clsAlertaHallazgoCritico alertaCritica,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj,
    IValidator<RevisarHallazgoRequest> validador)
{
    private static readonly JsonSerializerOptions JSON = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>
    /// Revisa la novedad y responde con el hallazgo ya completo. 400 sin zona, clase o nivel validos; 403 si no es de su viaje o de su cliente (o, para el coordinador, si no es de un Cargue sin
    /// personal de apoyo); 409 si el hallazgo no es del conductor y 409 HALLAZGO_YA_REVISADO si ya se reviso.
    /// </summary>
    public async Task<HallazgoDto> RevisarAsync(Guid hallazgoId, RevisarHallazgoRequest solicitud, CancellationToken ct = default)
    {
        await ValidarAsync(solicitud, ct);
        var hallazgo = await inspecciones.ObtenerHallazgoAsync(hallazgoId, ct) ?? throw new clsEntidadNoEncontradaException($"Hallazgo {hallazgoId} no encontrado");
        var inspeccion = await inspecciones.ObtenerPorIdAsync(hallazgo.InspeccionId, ct) ?? throw new clsEntidadNoEncontradaException($"Inspección {hallazgo.InspeccionId} no encontrada");
        var etapa = await etapas.ObtenerPorIdAsync(inspeccion.EtapaId, ct) ?? throw new clsEntidadNoEncontradaException("La etapa de la inspección no existe");
        var viaje = await viajes.ObtenerPorIdAsync(inspeccion.ViajeId, ct) ?? throw new clsEntidadNoEncontradaException("El viaje de la inspección no existe");
        var vehiculo = await vehiculos.ObtenerPorIdAsync(etapa.VehiculoId, ct) ?? throw new clsEntidadNoEncontradaException("El vehículo de la etapa no existe");
        await AsegurarPermisoAsync(vehiculo, inspeccion, etapa, ct);
        hallazgo.AsegurarRevisable();

        var zona = await resolutorZona.ResolverAsync(inspeccion.Id, solicitud.CuadranteNumero, solicitud.Lado, solicitud.ZonaProvisional, ct);
        var tamanoAnterior = hallazgo.TamanoDano;
        var antes = clsInstantaneasAuditoria.De(hallazgo);
        var tamano = clsValidador.Opcional(solicitud.TamanoDano);
        hallazgo.Revisar(zona.Id, solicitud.ClaseDano!.Trim(), solicitud.NivelSeveridad!.Trim(), tamano, solicitud.Nota, actual.IdObligatorio(), reloj.Ahora);

        inspecciones.Agregar(clsHallazgoSeguimiento.CrearRevision(hallazgo.Id, actual.IdObligatorio(), solicitud.Nota, Detalle(solicitud, zona, hallazgo, tamanoAnterior), reloj.Ahora));
        await auditor.RegistrarDeViajeAsync(
            "hallazgo.revision", "hallazgos", hallazgo.Id, viaje.Id, new DetalleAuditoria(antes, clsInstantaneasAuditoria.De(hallazgo)), vehiculo.ClienteId, ct);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);

        var proceso = (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == etapa.EtapaCatalogoId)?.Codigo ?? "la etapa";
        await alertaCritica.EmitirSiHayCriticosAsync([hallazgo], vehiculo, viaje.Id, proceso, ct);
        return await consultas.ObtenerHallazgoAsync(hallazgo.Id, ct) ?? throw new clsEntidadNoEncontradaException($"Hallazgo {hallazgoId} no encontrado");
    }

    /// <summary>
    /// El inspector revisa los de un VIN de sus viajes; el administrador, los de su cliente; el coordinador de calidad, solo los de la salida de un Cargue sin personal de apoyo. Quien se limita a su
    /// cliente no revisa los de otro.
    /// </summary>
    private async Task AsegurarPermisoAsync(clsVehiculo vehiculo, clsInspeccion inspeccion, clsEtapa etapa, CancellationToken ct)
    {
        if (actual.ClienteRestringido() is { } cliente && vehiculo.ClienteId != cliente)
        {
            throw new clsProhibidoException("El hallazgo es de un VIN de otro cliente");
        }

        switch (actual.Rol)
        {
            case clsRol.INSPECTOR when !await consultasViajes.UsuarioVeVinAsync(vehiculo.Vin, actual.IdObligatorio(), ct):
                throw new clsProhibidoException("El VIN del hallazgo no pertenece a ninguno de sus viajes");
            case clsRol.COORDINADOR_CALIDAD when !await EsSalidaDeCargueSinApoyoAsync(inspeccion, etapa, ct):
                throw new clsProhibidoException("El coordinador de calidad solo revisa los hallazgos del conductor en la salida de un Cargue sin personal de apoyo");
            case clsRol.INSPECTOR or clsRol.COORDINADOR_CALIDAD or clsRol.ADMIN or clsRol.SUPER_ADMIN:
                return;
            default:
                throw new clsProhibidoException("No tiene permisos para revisar hallazgos");
        }
    }

    private async Task<bool> EsSalidaDeCargueSinApoyoAsync(clsInspeccion inspeccion, clsEtapa etapa, CancellationToken ct) =>
        inspeccion.Momento == clsMomento.SALIDA && !etapa.ConPersonalApoyo
        && (await catalogos.ValoresAsync(clsNombresCatalogo.ETAPAS, ct)).FirstOrDefault(e => e.Id == etapa.EtapaCatalogoId)?.Codigo == clsCodigosEtapa.CARGUE;

    /// <summary>Valida el cuerpo y que la clase, el nivel y el tamano (si llega) existan en el catalogo y esten activos; los errores de ambas validaciones salen juntos.</summary>
    private async Task ValidarAsync(RevisarHallazgoRequest solicitud, CancellationToken ct)
    {
        var deCatalogo = new Dictionary<string, string[]>();
        if (!string.IsNullOrWhiteSpace(solicitud.ClaseDano)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.CLASES_DANO, soloActivos: true, ct)).Contains(solicitud.ClaseDano.Trim()))
        {
            deCatalogo["clase_dano"] = ["Clase de daño inválida"];
        }

        if (!string.IsNullOrWhiteSpace(solicitud.NivelSeveridad)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.NIVELES_SEVERIDAD, soloActivos: true, ct)).Contains(solicitud.NivelSeveridad.Trim()))
        {
            deCatalogo["nivel_severidad"] = ["Nivel de severidad inválido"];
        }

        if (!string.IsNullOrWhiteSpace(solicitud.TamanoDano)
            && !(await catalogos.CodigosAsync(clsNombresCatalogo.TAMANOS_DANO, soloActivos: true, ct)).Contains(solicitud.TamanoDano.Trim()))
        {
            deCatalogo["tamano_dano"] = ["Tamaño de daño inválido"];
        }

        try
        {
            await validador.ValidarOLanzarAsync(solicitud, ct);
        }
        catch (clsValidacionException ex) when (deCatalogo.Count > 0 && ex.Detalles is Dictionary<string, object> detalles
            && detalles.TryGetValue("fieldErrors", out var campos) && campos is Dictionary<string, string[]> porCampo)
        {
            foreach (var (campo, mensajes) in deCatalogo)
            {
                porCampo.TryAdd(campo, mensajes);
            }

            throw;
        }

        if (deCatalogo.Count > 0)
        {
            throw clsErroresCampos.Varios(deCatalogo);
        }
    }

    /// <summary>Lo asignado en la revision, como JSON para el historial: la zona, la clase, el nivel y, si se corrigio, el tamano que habia y el nuevo.</summary>
    private static string Detalle(RevisarHallazgoRequest solicitud, clsZonaDano zona, clsHallazgo hallazgo, string tamanoAnterior) =>
        JsonSerializer.Serialize(
            new Dictionary<string, object?>
            {
                ["zona_id"] = zona.Id,
                ["cuadrante_numero"] = zona.Numero,
                ["lado"] = zona.Lado,
                ["zona_provisional"] = zona.Codigo,
                ["clase_dano"] = hallazgo.ClaseDano,
                ["nivel_severidad"] = hallazgo.NivelSeveridad,
                ["tamano_dano"] = hallazgo.TamanoDano,
                ["tamano_dano_anterior"] = hallazgo.TamanoDano == tamanoAnterior ? null : tamanoAnterior,
            },
            JSON);
}
