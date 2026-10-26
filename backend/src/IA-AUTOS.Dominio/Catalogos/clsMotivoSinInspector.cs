namespace IAAutos.Dominio.Catalogos;

/// <summary>
/// Motivo por el que el inspector previsto no esta en un cargue con personal de apoyo y el conductor hace la inspeccion de salida. <see cref="RequiereTexto"/> (el motivo
/// «otro») obliga a quien lo elige a escribir cual es; en los demas el texto no se admite.
/// </summary>
public sealed class clsMotivoSinInspector : clsCatalogoCodificado
{
    private clsMotivoSinInspector()
    {
    }

    public bool RequiereTexto { get; private set; }

    public static clsMotivoSinInspector Crear(string? codigo, string? nombre, bool? requiereTexto, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var motivo = new clsMotivoSinInspector { RequiereTexto = requiereTexto ?? false };
        motivo.Iniciar(codigo, nombre, orden, activo, ahora);
        return motivo;
    }

    /// <summary>Cambia si el motivo exige texto; un valor nulo conserva el actual.</summary>
    public void CambiarRequiereTexto(bool? requiereTexto, DateTimeOffset ahora)
    {
        if (requiereTexto is not null)
        {
            RequiereTexto = requiereTexto.Value;
        }

        Tocar(ahora);
    }
}
