using Microsoft.EntityFrameworkCore;

namespace IAAutos.Infraestructura.Persistencia.Consultas;

/// <summary>
/// La regla de «el punto tiene inspector», definida UNA sola vez: un lugar tiene inspector si alguien activo lo tiene asignado en <c>tblInspectorLugares</c>. La usan la firma de la entrega, el cargue, el permiso de la
/// excepcion y los datos que la app lee (la entrega por punto y el viaje): ninguno tiene una copia propia, asi que no pueden discrepar. Un inspector inactivo no cuenta.
/// </summary>
internal static class clsReglaInspectorDelPunto
{
    /// <summary>Los lugares (sus ids, con repeticiones) que tienen al menos un inspector activo asignado.</summary>
    public static IQueryable<Guid> LugaresConInspector(clsAppDbContext contexto) =>
        from p in contexto.InspectorLugares.AsNoTracking()
        join u in contexto.Usuarios.AsNoTracking() on p.UsuarioId equals u.Id
        where u.Activo
        select p.LugarId;
}
