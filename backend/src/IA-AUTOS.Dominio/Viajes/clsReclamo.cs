using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Viajes;

/// <summary>Lo que se reclama de un viaje: la revision del cargue o la revision de la entrega (la etapa Descargue).</summary>
public static class clsProcesoReclamo
{
    public const string CARGUE = "cargue";
    public const string ENTREGA = "entrega";

    public static readonly IReadOnlySet<string> VALORES = new HashSet<string> { CARGUE, ENTREGA };
}

/// <summary>
/// El reclamo de un camion por un inspector: quien lo reclamo, cuando y, al liberarse, quien y cuando lo libero. Solo hay un reclamo ACTIVO (sin liberar) por viaje y proceso: lo garantiza un indice unico
/// de la base, asi que si dos inspectores reclaman a la vez gana uno y el otro recibe 409. No caduca: solo lo libera una persona. Los reclamos liberados son el historial y no se borran.
/// </summary>
public sealed class clsReclamo
{
    private clsReclamo()
    {
    }

    public Guid Id { get; private set; }

    public Guid ViajeId { get; private set; }

    public Guid InspectorId { get; private set; }

    public string Proceso { get; private set; } = string.Empty;

    public DateTimeOffset ReclamadoAt { get; private set; }

    public DateTimeOffset? LiberadoAt { get; private set; }

    public Guid? LiberadoPor { get; private set; }

    public bool EstaActivo => LiberadoAt is null;

    public static clsReclamo Crear(Guid viajeId, Guid inspectorId, string proceso, DateTimeOffset ahora)
    {
        clsValidador.EnCatalogo(proceso, clsProcesoReclamo.VALORES, "proceso");
        return new clsReclamo { Id = Guid.NewGuid(), ViajeId = viajeId, InspectorId = inspectorId, Proceso = proceso, ReclamadoAt = ahora };
    }

    /// <summary>Libera el reclamo: queda quien y cuando. Un reclamo ya liberado no se libera otra vez (409).</summary>
    public void Liberar(Guid usuarioId, DateTimeOffset ahora)
    {
        if (!EstaActivo)
        {
            throw new clsConflictoException("El reclamo ya fue liberado");
        }

        LiberadoAt = ahora;
        LiberadoPor = usuarioId;
    }
}

/// <summary>Un punto de un inspector: un lugar real del catalogo <c>lugares</c> donde trabaja. Un inspector puede tener varios; se leen aqui y los asigna Datos.</summary>
public sealed class clsInspectorLugar
{
    private clsInspectorLugar()
    {
    }

    public Guid Id { get; private set; }

    public Guid UsuarioId { get; private set; }

    public Guid LugarId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Asigna el punto (un lugar real) al inspector.</summary>
    public static clsInspectorLugar Crear(Guid usuarioId, Guid lugarId, DateTimeOffset ahora) =>
        new() { Id = Guid.NewGuid(), UsuarioId = usuarioId, LugarId = lugarId, CreatedAt = ahora };
}

public interface IReclamoRepository
{
    /// <summary>El reclamo activo (sin liberar) del viaje y proceso, o nulo.</summary>
    Task<clsReclamo?> ObtenerActivoAsync(Guid viajeId, string proceso, CancellationToken ct = default);

    /// <summary>Todos los reclamos del viaje (activos y liberados), del mas reciente al mas antiguo.</summary>
    Task<IReadOnlyList<clsReclamo>> ObtenerDeViajeAsync(Guid viajeId, CancellationToken ct = default);

    /// <summary>Los lugares (puntos) del inspector.</summary>
    Task<IReadOnlyList<Guid>> LugaresDelInspectorAsync(Guid usuarioId, CancellationToken ct = default);

    /// <summary>Los puntos del inspector (con la fecha en que se le asignaron), del mas antiguo al mas reciente.</summary>
    Task<IReadOnlyList<clsInspectorLugar>> ObtenerPuntosAsync(Guid usuarioId, CancellationToken ct = default);

    void AgregarPunto(clsInspectorLugar punto);

    void QuitarPunto(clsInspectorLugar punto);

    /// <summary>Los inspectores activos asignados al lugar (sus ids).</summary>
    Task<IReadOnlyList<Guid>> InspectoresDelLugarAsync(Guid lugarId, CancellationToken ct = default);

    /// <summary>True si el lugar tiene al menos un inspector activo asignado (<c>tblInspectorLugares</c>): ahi la entrega la firma el inspector y no una persona externa.</summary>
    Task<bool> LugarTieneInspectoresAsync(Guid lugarId, CancellationToken ct = default);

    void Agregar(clsReclamo reclamo);
}
