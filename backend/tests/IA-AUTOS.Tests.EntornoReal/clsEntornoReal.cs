namespace IAAutos.Tests.EntornoReal;

/// <summary>
/// Acceso a las variables del entorno real (las mismas que usa la API, ver backend/.env.github).
/// Estas pruebas son SOLO LECTURA: nunca insertan, actualizan ni borran nada en la base ni en el almacen.
/// </summary>
internal static class clsEntornoReal
{
    public const string CADENA_CONEXION = "ConnectionStrings__DefaultConnection";
    public const string S3_ENDPOINT = "Storage__S3__Endpoint";
    public const string S3_BUCKET = "Storage__S3__Bucket";
    public const string S3_REGION = "Storage__S3__Region";
    public const string S3_ACCESS_KEY = "Storage__S3__AccessKey";
    public const string S3_SECRET_KEY = "Storage__S3__SecretKey";
    public const string JWT_SECRETO = "Jwt__Secret";

    /// <summary>Variables sin las cuales no se puede probar el entorno real.</summary>
    public static readonly string[] Requeridas =
        [CADENA_CONEXION, JWT_SECRETO, S3_ENDPOINT, S3_BUCKET, S3_ACCESS_KEY, S3_SECRET_KEY];

    /// <summary>En GitHub Actions una variable ausente es un error; en local las pruebas se omiten.</summary>
    public static bool EsCi => string.Equals(Environment.GetEnvironmentVariable("GITHUB_ACTIONS"), "true", StringComparison.OrdinalIgnoreCase);

    public static string? Leer(string nombre)
    {
        var valor = Environment.GetEnvironmentVariable(nombre);
        return string.IsNullOrWhiteSpace(valor) ? null : valor;
    }

    public static string Exigir(string nombre) =>
        Leer(nombre) ?? throw new InvalidOperationException(
            $"Falta la variable de entorno {nombre}. En GitHub debe existir como secreto (ver backend/.env.github).");

    public static string[] Faltantes() => [.. Requeridas.Where(n => Leer(n) is null)];
}
