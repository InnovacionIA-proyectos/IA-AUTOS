using System.Globalization;
using System.Text.RegularExpressions;
using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Parametros;

/// <summary>Como se valida el valor de un parametro (la columna <c>Tipo</c>).</summary>
public static class clsTiposParametro
{
    public const string NUMERO = "numero";
    public const string PORCENTAJE = "porcentaje";
    public const string TEXTO = "texto";
    public const string LISTA = "lista";
    public const string HORA = "hora";
    public const string CORREOS = "correos";
}

/// <summary>Un valor que el negocio ajusta sin desplegar codigo. Valor nulo = sin valor. <see cref="Tipo"/> dice como se valida: numero, porcentaje, texto, lista, hora o correos.</summary>
public sealed partial class clsParametro
{
    public const int VALOR_MAX = 1000;
    public const int NUMERO_MAX = 1_000_000;

    /// <summary>Los unicos parametros que pueden quedar sin valor: no hay destinatarios del reporte diario o aun no se define la retencion. Los demas siempre tienen valor.</summary>
    private static readonly HashSet<string> PUEDEN_QUEDAR_SIN_VALOR = new(StringComparer.Ordinal) { clsClavesParametros.REPORTE_DIARIO_DESTINATARIOS, clsClavesParametros.RETENCION_DIAS };

    private clsParametro()
    {
    }

    public string Clave { get; private set; } = string.Empty;

    public string? Valor { get; private set; }

    public string Tipo { get; private set; } = string.Empty;

    public string Descripcion { get; private set; } = string.Empty;

    public Guid? ActualizadoPor { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>
    /// Cambia el valor tras validarlo contra su tipo y lo guarda normalizado (sin espacios sobrantes ni repetidos). Un valor vacio o nulo deja el parametro sin valor, solo si es de los que lo admiten.
    /// <paramref name="codigosPermitidos"/> son los codigos que acepta un parametro de tipo lista (el catalogo al que se refiere). Devuelve false si el valor ya era ese y no cambia nada.
    /// </summary>
    public bool CambiarValor(string? valor, IReadOnlySet<string>? codigosPermitidos, Guid usuarioId, DateTimeOffset ahora)
    {
        var normalizado = Normalizar(valor, codigosPermitidos);
        if (string.Equals(normalizado, Valor, StringComparison.Ordinal))
        {
            return false;
        }

        Valor = normalizado;
        ActualizadoPor = usuarioId;
        UpdatedAt = ahora;
        return true;
    }

    private string? Normalizar(string? valor, IReadOnlySet<string>? codigosPermitidos)
    {
        var texto = valor?.Trim();
        if (string.IsNullOrEmpty(texto))
        {
            return PUEDEN_QUEDAR_SIN_VALOR.Contains(Clave) ? null : throw Error("Este parámetro siempre tiene valor");
        }

        if (texto.Length > VALOR_MAX)
        {
            throw Error($"El valor admite máximo {VALOR_MAX} caracteres");
        }

        return Tipo switch
        {
            clsTiposParametro.NUMERO => Numero(texto),
            clsTiposParametro.PORCENTAJE => Porcentaje(texto),
            clsTiposParametro.HORA => HoraValida().IsMatch(texto) ? texto : throw Error("La hora debe tener el formato HH:mm (de 00:00 a 23:59)"),
            clsTiposParametro.LISTA => Lista(texto, codigosPermitidos),
            clsTiposParametro.CORREOS => Correos(texto),
            _ => texto,
        };
    }

    private string Numero(string texto)
    {
        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var numero) || numero is < 1 or > NUMERO_MAX)
        {
            throw Error($"Debe ser un número entero entre 1 y {NUMERO_MAX}");
        }

        return numero.ToString(CultureInfo.InvariantCulture);
    }

    private string Porcentaje(string texto)
    {
        if (!PorcentajeValido().IsMatch(texto) || !decimal.TryParse(texto, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var porcentaje) || porcentaje is < 0 or > 100)
        {
            throw Error("Debe ser un porcentaje de 0 a 100, con punto decimal (por ejemplo 1.2)");
        }

        return porcentaje.ToString("0.####", CultureInfo.InvariantCulture);
    }

    private string Lista(string texto, IReadOnlySet<string>? codigosPermitidos)
    {
        var codigos = Separar(texto);
        if (codigos.Count == 0)
        {
            throw Error("Debe tener al menos un código");
        }

        var desconocidos = codigos.Where(c => codigosPermitidos is null ? !CodigoValido().IsMatch(c) : !codigosPermitidos.Contains(c)).ToList();
        if (desconocidos.Count > 0)
        {
            var permitidos = codigosPermitidos is null ? string.Empty : $". Permitidos: {string.Join(", ", codigosPermitidos.Order(StringComparer.Ordinal))}";
            throw Error($"Códigos que no existen: {string.Join(", ", desconocidos)}{permitidos}");
        }

        return string.Join(',', codigos);
    }

    private string Correos(string texto)
    {
        var correos = Separar(texto).Select(c => c.ToLowerInvariant()).Distinct(StringComparer.Ordinal).ToList();
        var invalidos = correos.Where(c => c.Length > 254 || !CorreoValido().IsMatch(c)).ToList();
        if (correos.Count == 0 || invalidos.Count > 0)
        {
            throw Error(correos.Count == 0 ? "Debe tener al menos un correo" : $"Correos no válidos: {string.Join(", ", invalidos)}");
        }

        return string.Join(',', correos);
    }

    private static List<string> Separar(string texto) =>
        texto.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToList();

    private static clsValidacionException Error(string mensaje) => clsErroresCampos.De("valor", mensaje);

    [GeneratedRegex(@"^([01]\d|2[0-3]):[0-5]\d$")]
    private static partial Regex HoraValida();

    [GeneratedRegex(@"^\d{1,3}(\.\d{1,4})?$")]
    private static partial Regex PorcentajeValido();

    [GeneratedRegex(@"^[a-z0-9_]{1,50}$")]
    private static partial Regex CodigoValido();

    [GeneratedRegex(@"^[^@\s,;]+@[^@\s,;]+\.[^@\s,;]+$")]
    private static partial Regex CorreoValido();
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
    public const string RECLAMO_SIN_ACTIVIDAD_MINUTOS = "reclamo_sin_actividad_minutos";

    /// <summary>Radio, en metros, de un lugar de cargue para confirmar «en sitio» la evidencia (se usa si el lugar no trae su propio radio).</summary>
    public const string RADIO_LUGAR_CARGUE_M = "radio_lugar_cargue_m";

    /// <summary>Radio, en metros, de un destino para confirmar «en sitio» la evidencia (se usa si el lugar no trae su propio radio).</summary>
    public const string RADIO_DESTINO_M = "radio_destino_m";

    /// <summary>Precision del GPS, en metros, a partir de la cual una foto no confirma ni acusa la ubicacion.</summary>
    public const string VALVULA_PRECISION_M = "valvula_precision_m";

    /// <summary>Minutos desde la hora programada de un cargue sin reclamo antes de avisar al coordinador.</summary>
    public const string CARGUE_SIN_RECLAMAR_MINUTOS = "cargue_sin_reclamar_minutos";
    public const string ACOMODACION_FOTOS_MINIMAS = "acomodacion_fotos_minimas";
    public const string REPORTE_DIARIO_DESTINATARIOS = "reporte_diario_destinatarios";
    public const string REPORTE_DIARIO_HORA = "reporte_diario_hora";
    public const string RETENCION_DIAS = "retencion_dias";
    public const string OBJETIVO_DIARIO_NOVEDAD = "objetivo_diario_novedad";
}

public interface IParametroRepository
{
    /// <summary>Todos los parametros (son pocos: se leen juntos y se guardan en cache).</summary>
    Task<IReadOnlyList<clsParametro>> ListarAsync(CancellationToken ct = default);

    /// <summary>El parametro para modificarlo; nulo si la clave no existe.</summary>
    Task<clsParametro?> ObtenerAsync(string clave, CancellationToken ct = default);
}
