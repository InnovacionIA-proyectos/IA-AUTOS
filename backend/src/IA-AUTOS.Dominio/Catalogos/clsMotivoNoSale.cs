namespace IAAutos.Dominio.Catalogos;

/// <summary>Los codigos de los motivos de «no sale» que el servidor conoce (el catalogo <c>motivos-no-sale</c> los trae sembrados; la lista la confirma el negocio).</summary>
public static class clsCodigosMotivoNoSale
{
    public const string DANO_V1_PLUS = "dano_v1_plus";
    public const string DANO_REPORTADO_CONDUCTOR = "dano_reportado_conductor";
    public const string OTRO = "otro";
}

/// <summary>
/// Motivo por el que un carro no sale del viaje: <see cref="ExigeHallazgo"/> (el daño V1+ y el daño que reporto el conductor se apoyan en un hallazgo), <see cref="ExigeTexto"/> (el motivo «otro» obliga a escribir
/// cual es) y <see cref="SoloSinInspector"/> (el daño reportado por el conductor solo vale en un lugar de cargue que admite el cargue sin inspector).
/// </summary>
public sealed class clsMotivoNoSale : clsCatalogoCodificado
{
    private clsMotivoNoSale()
    {
    }

    public bool ExigeHallazgo { get; private set; }

    public bool ExigeTexto { get; private set; }

    public bool SoloSinInspector { get; private set; }

    public static clsMotivoNoSale Crear(string? codigo, string? nombre, bool? exigeHallazgo, bool? exigeTexto, bool? soloSinInspector, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var motivo = new clsMotivoNoSale { ExigeHallazgo = exigeHallazgo ?? false, ExigeTexto = exigeTexto ?? false, SoloSinInspector = soloSinInspector ?? false };
        motivo.Iniciar(codigo, nombre, orden, activo, ahora);
        return motivo;
    }

    /// <summary>Cambia las marcas del motivo; un valor nulo conserva el actual.</summary>
    public void CambiarMarcas(bool? exigeHallazgo, bool? exigeTexto, bool? soloSinInspector, DateTimeOffset ahora)
    {
        ExigeHallazgo = exigeHallazgo ?? ExigeHallazgo;
        ExigeTexto = exigeTexto ?? ExigeTexto;
        SoloSinInspector = soloSinInspector ?? SoloSinInspector;
        Tocar(ahora);
    }
}
