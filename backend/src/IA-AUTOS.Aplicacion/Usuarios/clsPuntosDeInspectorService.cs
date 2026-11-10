using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Aplicacion.Usuarios;

/// <summary>
/// Los puntos de un inspector (lugares reales del catalogo <c>lugares</c> donde trabaja; ver <c>tblInspectorLugares</c>): se consultan, se asignan (uno o varios) y se quitan desde el portal. Los administra el coordinador de
/// calidad y los administradores (cada uno solo los inspectores de su cliente: el super administrador, los de todos); el inspector solo consulta los suyos. Asignado un punto, el inspector ve en la app los pendientes de
/// ese punto sin otro paso (ya lo leen el reclamo y el acceso a los viajes). Cada asignacion y cada retiro quedan en la auditoria.
/// </summary>
public sealed class clsPuntosDeInspectorService(
    IUsuarioRepository usuarios,
    IReclamoRepository reclamos,
    ICatalogosLectura catalogos,
    IUnidadDeTrabajo unidadDeTrabajo,
    IAuditor auditor,
    IUsuarioActual actual,
    IReloj reloj)
{
    private bool VeTodosLosClientes => actual.Rol is null or clsRol.SUPER_ADMIN;

    /// <summary>Los puntos del inspector. 404 si el usuario no existe (o es de otro cliente); 403 si lo consulta un inspector que no es el mismo.</summary>
    public async Task<PuntosDeInspectorDto> ListarAsync(Guid usuarioId, CancellationToken ct = default)
    {
        var inspector = await ObtenerInspectorAsync(usuarioId, soloLectura: true, ct);
        return await ArmarAsync(inspector.Id, ct);
    }

    /// <summary>
    /// Asigna uno o varios puntos al inspector (los que ya tenia se conservan; repetir un punto no cambia nada). 404 si el usuario no existe; 400 si no es inspector, si la lista viene vacia o si algun lugar no existe
    /// o esta inactivo (se nombran todos juntos). Queda en la auditoria con quien y cuando.
    /// </summary>
    public async Task<PuntosDeInspectorDto> AsignarAsync(Guid usuarioId, AsignarPuntosRequest solicitud, CancellationToken ct = default)
    {
        var inspector = await ObtenerInspectorAsync(usuarioId, soloLectura: false, ct);
        var ids = (solicitud.LugarIds ?? []).Distinct().ToList();
        if (ids.Count == 0 || ids.Any(i => i == Guid.Empty))
        {
            throw clsErroresCampos.De("lugar_ids", "Indique al menos un lugar: la lista de ids de lugares del catálogo lugares");
        }

        var invalidos = new List<Guid>();
        foreach (var id in ids)
        {
            if (!await catalogos.ExisteActivoAsync(clsNombresCatalogo.LUGARES, id, ct))
            {
                invalidos.Add(id);
            }
        }

        if (invalidos.Count > 0)
        {
            throw clsErroresCampos.De("lugar_ids", $"Estos lugares no existen en el catálogo de lugares o están inactivos: {string.Join(", ", invalidos)}");
        }

        var actuales = (await reclamos.ObtenerPuntosAsync(inspector.Id, ct)).Select(p => p.LugarId).ToHashSet();
        var nuevos = ids.Where(i => !actuales.Contains(i)).ToList();
        foreach (var lugarId in nuevos)
        {
            reclamos.AgregarPunto(clsInspectorLugar.Crear(inspector.Id, lugarId, reloj.Ahora));
        }

        if (nuevos.Count > 0)
        {
            auditor.Registrar(
                "usuario.puntos_asignar", "usuarios", inspector.Id,
                detalle: new DetalleAuditoria(new { lugar_ids = actuales.OrderBy(x => x) }, new { lugar_ids = actuales.Concat(nuevos).OrderBy(x => x), agregados = nuevos }), clienteId: inspector.ClienteId);
            await unidadDeTrabajo.GuardarCambiosAsync(ct);
        }

        return await ArmarAsync(inspector.Id, ct);
    }

    /// <summary>Quita un punto del inspector. 404 si el usuario no existe o no tiene ese punto. Queda en la auditoria con quien y cuando.</summary>
    public async Task<PuntosDeInspectorDto> QuitarAsync(Guid usuarioId, Guid lugarId, CancellationToken ct = default)
    {
        var inspector = await ObtenerInspectorAsync(usuarioId, soloLectura: false, ct);
        var punto = (await reclamos.ObtenerPuntosAsync(inspector.Id, ct)).FirstOrDefault(p => p.LugarId == lugarId)
            ?? throw new clsEntidadNoEncontradaException("El inspector no tiene ese punto asignado");
        reclamos.QuitarPunto(punto);
        auditor.Registrar(
            "usuario.punto_quitar", "usuarios", inspector.Id,
            detalle: new DetalleAuditoria(new { lugar_id = lugarId, asignado_at = punto.CreatedAt }, null), clienteId: inspector.ClienteId);
        await unidadDeTrabajo.GuardarCambiosAsync(ct);
        return await ArmarAsync(inspector.Id, ct);
    }

    private async Task<clsUsuario> ObtenerInspectorAsync(Guid usuarioId, bool soloLectura, CancellationToken ct)
    {
        var usuario = await usuarios.ObtenerPorIdAsync(usuarioId, ct) ?? throw new clsEntidadNoEncontradaException("Usuario no encontrado");
        if (actual.Rol == clsRol.INSPECTOR)
        {
            // El inspector solo consulta sus propios puntos; no asigna ni quita.
            if (!soloLectura || usuario.Id != actual.IdObligatorio())
            {
                throw new clsProhibidoException("Solo el coordinador de calidad y los administradores administran los puntos de un inspector");
            }
        }
        else if (!VeTodosLosClientes && usuario.ClienteId != actual.ClienteId)
        {
            throw new clsEntidadNoEncontradaException("Usuario no encontrado");
        }

        return usuario.Rol == clsRol.INSPECTOR ? usuario : throw clsErroresCampos.De("usuario", "El usuario no es un inspector: solo los inspectores tienen puntos");
    }

    private async Task<PuntosDeInspectorDto> ArmarAsync(Guid usuarioId, CancellationToken ct)
    {
        var lugares = (await catalogos.ValoresAsync(clsNombresCatalogo.LUGARES, ct)).ToDictionary(l => l.Id);
        var puntos = (await reclamos.ObtenerPuntosAsync(usuarioId, ct))
            .Select(p => new PuntoDeInspectorDto(
                p.LugarId,
                lugares.TryGetValue(p.LugarId, out var lugar) && lugar.TryGetValue("nombre", out var nombre) ? nombre as string : null,
                lugares.TryGetValue(p.LugarId, out var l2) && l2.TryGetValue("ciudad", out var ciudad) ? ciudad as string : null,
                p.CreatedAt))
            .OrderBy(p => p.Lugar, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        return new PuntosDeInspectorDto(usuarioId, puntos);
    }
}
