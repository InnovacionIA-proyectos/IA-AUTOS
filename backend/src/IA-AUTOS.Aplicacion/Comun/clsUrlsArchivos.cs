namespace IAAutos.Aplicacion.Comun;

public static class clsUrlsArchivos
{
    public const string RUTA_BASE = "/api/uploads/";

    /// <summary>
    /// URL servible a partir del storage_path guardado. Solo los archivos subidos por esta API (nombre plano)
    /// tienen URL aqui; las rutas con carpetas (de integraciones anteriores) no se exponen.
    /// </summary>
    public static string? ParaStoragePath(string? storagePath) =>
        string.IsNullOrEmpty(storagePath) || storagePath.Contains('/') ? null : RUTA_BASE + storagePath;
}
