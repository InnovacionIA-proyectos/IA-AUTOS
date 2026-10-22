using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Infraestructura.Seguridad;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAAutos.Infraestructura.Almacenamiento;

/// <summary>
/// Firma y valida las URL de las fotos: <c>/api/uploads/{archivo}?exp={epoch}&amp;firma={hmac}</c>, donde la firma es el HMAC-SHA256 del nombre del archivo y de su vencimiento con un secreto
/// propio del servidor (<c>Storage:FirmaSecreto</c>). Quien abre la URL no necesita enviar un token; sin firma, con una firma que no coincide o ya vencida no ve la foto. Si el secreto propio no esta
/// configurado se deriva del secreto de los tokens (el mismo en todas las instancias y estable entre reinicios) y se avisa en el log.
/// </summary>
public sealed class clsFirmadorUrls : IFirmadorUrls
{
    public const int SECRETO_MINIMO = 32;

    private readonly byte[] _clave;
    private readonly IReloj _reloj;

    public clsFirmadorUrls(IOptions<clsOpcionesAlmacenamiento> almacenamiento, IOptions<clsOpcionesJwt> jwt, IReloj reloj, ILogger<clsFirmadorUrls> registro)
    {
        _reloj = reloj;
        var propio = almacenamiento.Value.FirmaSecreto;
        if (!string.IsNullOrWhiteSpace(propio))
        {
            _clave = Encoding.UTF8.GetBytes(propio);
            return;
        }

        registro.LogWarning("Storage:FirmaSecreto no esta configurado: las URL de las fotos se firman con una clave derivada del secreto de los tokens. Configure Storage__FirmaSecreto.");
        _clave = HMACSHA256.HashData(Encoding.UTF8.GetBytes(jwt.Value.Secret), Encoding.UTF8.GetBytes("iaautos:firma-de-urls"));
    }

    public clsUrlFirmada? Firmar(string? rutaArchivo, string baseUrl, int vigenciaMinutos)
    {
        // Solo los archivos subidos por esta API (nombre plano) tienen URL; las rutas con carpetas (de integraciones anteriores) no se exponen.
        if (string.IsNullOrEmpty(rutaArchivo) || rutaArchivo.Contains('/'))
        {
            return null;
        }

        var expira = _reloj.Ahora.AddMinutes(Math.Max(vigenciaMinutos, 1));
        var exp = expira.ToUnixTimeSeconds();
        return new clsUrlFirmada($"{baseUrl.TrimEnd('/')}{clsUrlsArchivos.RUTA_BASE}{Uri.EscapeDataString(rutaArchivo)}?exp={exp}&firma={Calcular(rutaArchivo, exp)}", DateTimeOffset.FromUnixTimeSeconds(exp));
    }

    public bool Validar(string archivo, string? exp, string? firma)
    {
        if (string.IsNullOrEmpty(firma) || !long.TryParse(exp, NumberStyles.None, CultureInfo.InvariantCulture, out var vencimiento) || vencimiento < _reloj.Ahora.ToUnixTimeSeconds())
        {
            return false;
        }

        // La comparacion es de tiempo constante para no revelar la firma correcta por la demora de la respuesta.
        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(Calcular(archivo, vencimiento)), Encoding.ASCII.GetBytes(firma.ToLowerInvariant()));
    }

    private string Calcular(string archivo, long exp) =>
        Convert.ToHexString(HMACSHA256.HashData(_clave, Encoding.UTF8.GetBytes($"{archivo}|{exp}"))).ToLowerInvariant();
}
