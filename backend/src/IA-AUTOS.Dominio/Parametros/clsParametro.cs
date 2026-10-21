namespace IAAutos.Dominio.Parametros;

/// <summary>Un valor que el negocio ajusta sin desplegar codigo. Valor nulo = sin valor. <see cref="Tipo"/> dice como se valida: numero, porcentaje, texto, lista, hora o correos.</summary>
public sealed class clsParametro
{
    private clsParametro()
    {
    }

    public string Clave { get; private set; } = string.Empty;

    public string? Valor { get; private set; }

    public string Tipo { get; private set; } = string.Empty;

    public string Descripcion { get; private set; } = string.Empty;

    public Guid? ActualizadoPor { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}

/// <summary>Las claves de los parametros a las que la logica del negocio hace referencia.</summary>
public static class clsClavesParametros
{
    public const string URL_FIRMADA_MINUTOS = "url_firmada_minutos";
    public const string NIVELES_CRITICOS = "niveles_criticos";
    public const string UMBRAL_PATRON_CANTIDAD = "umbral_patron_cantidad";
    public const string UMBRAL_PATRON_VENTANA_DIAS = "umbral_patron_ventana_dias";
    public const string CAPTURA_INCOMPLETA_MINUTOS = "captura_incompleta_minutos";
    public const string TRANSFERENCIA_PENDIENTE_MINUTOS = "transferencia_pendiente_minutos";
}

public interface IParametroRepository
{
    /// <summary>Todos los parametros (son pocos: se leen juntos y se guardan en cache).</summary>
    Task<IReadOnlyList<clsParametro>> ListarAsync(CancellationToken ct = default);
}
