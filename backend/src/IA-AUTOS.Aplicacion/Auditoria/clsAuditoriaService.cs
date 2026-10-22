using System.Text.Json;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;

namespace IAAutos.Aplicacion.Auditoria;

/// <summary>Lo que cambio la accion: el estado del registro <c>antes</c> y <c>despues</c> (nulo el que no aplica, p. ej. el antes de un alta).</summary>
public sealed record DetalleAuditoriaDto(JsonElement? Antes, JsonElement? Despues);

/// <summary>
/// Un registro de la auditoria: quien hizo que, sobre que registro, desde que IP y dispositivo, y el detalle del cambio. <c>Detalle</c> es nulo en las acciones que no cambian datos (p. ej. un
/// inicio de sesion).
/// </summary>
public sealed record AuditoriaDto(
    Guid Id,
    Guid? UsuarioId,
    string? UsuarioNombre,
    string? Rol,
    string Accion,
    string? Entidad,
    Guid? EntidadId,
    Guid? ViajeId,
    string? CodigoMtrack,
    Guid? ClienteId,
    string? Ip,
    string? Dispositivo,
    DetalleAuditoriaDto? Detalle,
    DateTimeOffset CreatedAt);

public sealed record ListadoAuditoriaDto(IReadOnlyList<AuditoriaDto> Items, int Total, int Pagina, int Tamano);

/// <summary>Filtros del listado tal como llegan en la consulta (texto); el servicio los valida y responde todos los errores juntos.</summary>
public sealed record FiltrosAuditoriaRequest(
    string? UsuarioId,
    string? Entidad,
    string? EntidadId,
    string? ViajeId,
    string? Accion,
    string? Desde,
    string? Hasta,
    int? Pagina,
    int? Tamano);

/// <summary>Los filtros ya validados.</summary>
public sealed record FiltrosAuditoria(Guid? UsuarioId, string? Entidad, Guid? EntidadId, Guid? ViajeId, string? Accion, DateTimeOffset? Desde, DateTimeOffset? Hasta);

/// <summary>Lectura de la auditoria. La implementa la infraestructura.</summary>
public interface IConsultasAuditoria
{
    /// <summary>Una pagina, de la mas reciente a la mas antigua. Con <paramref name="clienteId"/>, solo los registros de ese cliente.</summary>
    Task<(IReadOnlyList<AuditoriaDto> Filas, int Total)> ListarAsync(FiltrosAuditoria filtros, Guid? clienteId, int desplazamiento, int tamano, CancellationToken ct = default);

    Task<AuditoriaDto?> ObtenerAsync(Guid id, CancellationToken ct = default);
}

/// <summary>
/// Consulta de la auditoria (capacidad <c>ver_auditoria</c>): el administrador ve la de su cliente y el super administrador la de todos; los demas roles reciben 403. Los registros no se
/// modifican ni se borran.
/// </summary>
public sealed class clsAuditoriaService(IConsultasAuditoria consultas, IUsuarioActual actual)
{
    public const int TAMANO_POR_DEFECTO = 50;
    public const int TAMANO_MAXIMO = 200;

    public async Task<ListadoAuditoriaDto> ListarAsync(FiltrosAuditoriaRequest solicitud, CancellationToken ct = default)
    {
        var filtros = Parsear(solicitud);
        var pagina = Math.Max(solicitud.Pagina ?? 1, 1);
        var tamano = Math.Clamp(solicitud.Tamano ?? TAMANO_POR_DEFECTO, 1, TAMANO_MAXIMO);
        var (filas, total) = await consultas.ListarAsync(filtros, actual.ClienteRestringido(), (pagina - 1) * tamano, tamano, ct);
        return new ListadoAuditoriaDto(filas, total, pagina, tamano);
    }

    /// <summary>El registro. 404 si no existe; 403 si es de otro cliente.</summary>
    public async Task<AuditoriaDto> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var registro = await consultas.ObtenerAsync(id, ct) ?? throw new clsEntidadNoEncontradaException($"Registro de auditoría {id} no encontrado");
        if (actual.ClienteRestringido() is { } cliente && registro.ClienteId != cliente)
        {
            throw new clsProhibidoException("El registro de auditoría es de otro cliente");
        }

        return registro;
    }

    private static FiltrosAuditoria Parsear(FiltrosAuditoriaRequest s)
    {
        var errores = new Dictionary<string, string[]>();
        var usuarioId = clsParseoFiltros.Uuid(s.UsuarioId, "usuario_id", errores);
        var entidadId = clsParseoFiltros.Uuid(s.EntidadId, "entidad_id", errores);
        var viajeId = clsParseoFiltros.Uuid(s.ViajeId, "viaje_id", errores);
        var desde = clsParseoFiltros.Fecha(s.Desde, "desde", errores);
        var hasta = clsParseoFiltros.Fecha(s.Hasta, "hasta", errores);
        if (desde is { } d && hasta is { } h && d > h)
        {
            errores["desde"] = ["desde no puede ser posterior a hasta"];
        }

        if (errores.Count > 0)
        {
            throw clsErroresCampos.Varios(errores);
        }

        return new FiltrosAuditoria(usuarioId, clsValidador.Opcional(s.Entidad), entidadId, viajeId, clsValidador.Opcional(s.Accion), desde, hasta);
    }
}
