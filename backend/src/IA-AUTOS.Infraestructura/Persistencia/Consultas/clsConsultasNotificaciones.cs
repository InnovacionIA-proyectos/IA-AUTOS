using IAAutos.Aplicacion.Notificaciones;
using IAAutos.Aplicacion.Notificaciones.DTONotificaciones;
using IAAutos.Dominio.Notificaciones;
using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>La bandeja: las notificaciones del canal bandeja con el codigo del viaje, el VIN y quien respondio.</summary>
internal sealed class clsConsultasNotificaciones(clsAppDbContext contexto) : IConsultasNotificaciones
{
    /// <summary>Una clase con propiedades de inicializacion (y no un registro con constructor): EF solo traduce los filtros que siguen a una proyeccion que arma asi.</summary>
    private sealed class Fila
    {
        public required clsNotificacion Notificacion { get; init; }

        public string? CodigoMtrack { get; init; }

        public string? Vin { get; init; }

        public string? RespondidaPorNombre { get; init; }
    }

    private IQueryable<Fila> Base(Guid? soloDeUsuario) =>
        from n in contexto.Notificaciones.AsNoTracking()
        join v in contexto.Viajes on n.ViajeId equals v.Id into viajes
        from v in viajes.DefaultIfEmpty()
        join h in contexto.Vehiculos on n.VehiculoId equals h.Id into vehiculos
        from h in vehiculos.DefaultIfEmpty()
        join u in contexto.Usuarios on n.RespondidaPor equals u.Id into usuarios
        from u in usuarios.DefaultIfEmpty()
        where n.Canal == clsCanalNotificacion.BANDEJA && (soloDeUsuario == null || n.UsuarioId == soloDeUsuario)
        select new Fila { Notificacion = n, CodigoMtrack = v.CodigoMtrack, Vin = h.Vin, RespondidaPorNombre = u.Nombre };

    public async Task<(IReadOnlyList<NotificacionDto> Filas, int Total, int NoLeidas)> ListarAsync(
        FiltrosNotificaciones f, Guid? soloDeUsuario, int desplazamiento, int tamano, CancellationToken ct = default)
    {
        var alcance = Base(soloDeUsuario);
        var noLeidas = await alcance.CountAsync(x => x.Notificacion.LeidaAt == null, ct);

        var consulta = alcance;
        if (f.Leida is { } leida)
        {
            consulta = consulta.Where(x => (x.Notificacion.LeidaAt != null) == leida);
        }

        if (f.Tipo is not null)
        {
            consulta = consulta.Where(x => x.Notificacion.Tipo == f.Tipo);
        }

        if (f.Urgencia is not null)
        {
            consulta = consulta.Where(x => x.Notificacion.Urgencia == f.Urgencia);
        }

        var total = await consulta.CountAsync(ct);
        var filas = await consulta.OrderByDescending(x => x.Notificacion.CreatedAt).ThenBy(x => x.Notificacion.Id).Skip(desplazamiento).Take(tamano).ToListAsync(ct);
        return (filas.Select(ADto).ToList(), total, noLeidas);
    }

    public async Task<NotificacionDto?> ObtenerAsync(Guid id, CancellationToken ct = default)
    {
        var fila = await Base(null).Where(x => x.Notificacion.Id == id).FirstOrDefaultAsync(ct);
        return fila is null ? null : ADto(fila);
    }

    private static NotificacionDto ADto(Fila f)
    {
        var n = f.Notificacion;
        var respuesta = n.RespondidaAt is { } fecha && n.RespuestaTexto is { } texto && n.RespuestaMedio is { } medio
            ? new RespuestaNotificacionDto(texto, medio, f.RespondidaPorNombre, fecha)
            : null;
        return new NotificacionDto(
            n.Id, n.Tipo, n.Urgencia, n.Titulo, n.Mensaje, n.ViajeId, f.CodigoMtrack, f.Vin, n.HallazgoId, n.InspeccionId, n.LeidaAt, respuesta, n.CreatedAt);
    }
}
