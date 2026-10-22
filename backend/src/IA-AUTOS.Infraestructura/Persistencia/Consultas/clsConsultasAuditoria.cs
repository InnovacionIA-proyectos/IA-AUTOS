using System.Text.Json;
using IAAutos.Aplicacion.Auditoria;
using IAAutos.Dominio.Auditoria;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>La auditoria con el nombre y el rol de quien actuo y el codigo MTrack del viaje.</summary>
internal sealed class clsConsultasAuditoria(clsAppDbContext contexto) : IConsultasAuditoria
{
    /// <summary>Fila tal como sale de la base (el detalle aun es texto). Clase con propiedades init: EF no admite proyectar a un constructor y filtrar despues.</summary>
    private sealed class Fila
    {
        public required Guid Id { get; init; }

        public Guid? UsuarioId { get; init; }

        public string? UsuarioNombre { get; init; }

        public string? Rol { get; init; }

        public required string Accion { get; init; }

        public string? Entidad { get; init; }

        public Guid? EntidadId { get; init; }

        public Guid? ViajeId { get; init; }

        public string? CodigoMtrack { get; init; }

        public Guid? ClienteId { get; init; }

        public string? Ip { get; init; }

        public string? Dispositivo { get; init; }

        public string? Detalle { get; init; }

        public required DateTimeOffset CreatedAt { get; init; }
    }

    public async Task<(IReadOnlyList<AuditoriaDto> Filas, int Total)> ListarAsync(
        FiltrosAuditoria filtros, Guid? clienteId, int desplazamiento, int tamano, CancellationToken ct = default)
    {
        var consulta = contexto.AuditLog.AsNoTracking().AsQueryable();
        if (clienteId is { } cliente)
        {
            consulta = consulta.Where(a => a.ClienteId == cliente);
        }

        if (filtros.UsuarioId is { } usuario)
        {
            consulta = consulta.Where(a => a.UsuarioId == usuario);
        }

        if (filtros.Entidad is { } entidad)
        {
            consulta = consulta.Where(a => a.Entidad == entidad);
        }

        if (filtros.EntidadId is { } entidadId)
        {
            consulta = consulta.Where(a => a.EntidadId == entidadId);
        }

        if (filtros.ViajeId is { } viaje)
        {
            consulta = consulta.Where(a => a.ViajeId == viaje);
        }

        if (filtros.Accion is { } accion)
        {
            consulta = consulta.Where(a => a.Accion == accion);
        }

        if (filtros.Desde is { } desde)
        {
            consulta = consulta.Where(a => a.CreatedAt >= desde);
        }

        if (filtros.Hasta is { } hasta)
        {
            consulta = consulta.Where(a => a.CreatedAt <= hasta);
        }

        var total = await consulta.CountAsync(ct);
        var filas = await Proyectar(consulta).Skip(desplazamiento).Take(tamano).ToListAsync(ct);
        return (filas.Select(ADto).ToList(), total);
    }

    public async Task<AuditoriaDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var fila = await Proyectar(contexto.AuditLog.AsNoTracking().Where(a => a.Id == id)).FirstOrDefaultAsync(ct);
        return fila is null ? null : ADto(fila);
    }

    private IQueryable<Fila> Proyectar(IQueryable<clsAuditLog> consulta) =>
        from a in consulta
        join u in contexto.Usuarios on a.UsuarioId equals u.Id into usuarios
        from u in usuarios.DefaultIfEmpty()
        join v in contexto.Viajes on a.ViajeId equals v.Id into viajes
        from v in viajes.DefaultIfEmpty()
        orderby a.CreatedAt descending, a.Id descending
        select new Fila
        {
            Id = a.Id,
            UsuarioId = a.UsuarioId,
            UsuarioNombre = u.Nombre,
            Rol = u.Rol,
            Accion = a.Accion,
            Entidad = a.Entidad,
            EntidadId = a.EntidadId,
            ViajeId = a.ViajeId,
            CodigoMtrack = v.CodigoMtrack,
            ClienteId = a.ClienteId,
            Ip = a.Ip,
            Dispositivo = a.Dispositivo,
            Detalle = a.Detalle,
            CreatedAt = a.CreatedAt,
        };

    private static AuditoriaDto ADto(Fila f) =>
        new(f.Id, f.UsuarioId, f.UsuarioNombre, f.Rol, f.Accion, f.Entidad, f.EntidadId, f.ViajeId, f.CodigoMtrack, f.ClienteId, f.Ip, f.Dispositivo, LeerDetalle(f.Detalle), f.CreatedAt);

    private static DetalleAuditoriaDto? LeerDetalle(string? texto)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        using var documento = JsonDocument.Parse(texto);
        var raiz = documento.RootElement;
        return new DetalleAuditoriaDto(Propiedad(raiz, "antes"), Propiedad(raiz, "despues"));
    }

    private static JsonElement? Propiedad(JsonElement raiz, string nombre) =>
        raiz.ValueKind == JsonValueKind.Object && raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind != JsonValueKind.Null ? valor.Clone() : null;
}
