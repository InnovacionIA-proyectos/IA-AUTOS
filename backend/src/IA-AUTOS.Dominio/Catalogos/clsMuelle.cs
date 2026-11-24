namespace IAAutos.Dominio.Catalogos;

/// <summary>
/// Un muelle de un lugar (hijo del catalogo <c>lugares</c>): tiene codigo (unico dentro del lugar), nombre, orden y estado. El muelle del viaje es uno de los de su lugar de cargue; en un lugar con al menos un muelle
/// activo el viaje necesita su muelle para cerrar la acomodacion.
/// </summary>
public sealed class clsMuelle : clsCatalogoCodificado
{
    private clsMuelle()
    {
    }

    public Guid LugarId { get; private set; }

    public static clsMuelle Crear(Guid lugarId, string? codigo, string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var muelle = new clsMuelle { LugarId = lugarId };
        muelle.Iniciar(codigo, nombre, orden, activo, ahora);
        return muelle;
    }
}
