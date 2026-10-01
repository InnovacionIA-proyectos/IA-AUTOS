using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IAAutos.API.Controllers.Comun;
using IAAutos.Aplicacion.Sofasa;
using IAAutos.Aplicacion.Sofasa.DTOSofasa;
using IAAutos.Dominio.Comun;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace IAAutos.API.Controllers.Sofasa;

public sealed class clsOpcionesEtl
{
    public const string SECCION = "Etl";

    /// <summary>Token compartido que autoriza al ETL (red local) a ingerir ordenes Sofasa.</summary>
    public string IngestToken { get; set; } = string.Empty;
}

public sealed class clsSofasaController(clsSofasaService servicio, IOptions<clsOpcionesEtl> opcionesEtl) : clsControladorBase
{
    /// <summary>Ingesta de ordenes desde el ETL. Se autoriza con el token compartido (Authorization: Bearer).</summary>
    [HttpPost("api/etl/sofasa/ordenes")]
    [AllowAnonymous]
    [ProducesResponseType<ResultadoIngestaDto>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Ingestar(CancellationToken ct)
    {
        AsegurarTokenEtl();
        var solicitud = await LeerCuerpoAsync<IngestarOrdenesRequest>(ct);
        return Ok(await servicio.IngestarAsync(solicitud, ct));
    }

    /// <summary>Listado de ordenes (mas recientes primero, maximo 500) para la web y la app.</summary>
    [HttpGet("api/sofasa/ordenes")]
    [ProducesResponseType<IReadOnlyList<OrdenSofasaDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? vin,
        [FromQuery] string? estado,
        [FromQuery] string? ciudad,
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        CancellationToken ct)
    {
        var filtros = new FiltrosOrdenesSofasa(vin, estado, ciudad, LeerFecha(desde, "desde"), LeerFecha(hasta, "hasta"));
        return Ok(await servicio.ListarAsync(filtros, ct));
    }

    private static DateTimeOffset? LeerFecha(string? texto, string campo)
    {
        if (string.IsNullOrWhiteSpace(texto))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(texto, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fecha))
        {
            return fecha;
        }

        throw new clsValidacionException(
            "Datos inválidos",
            new Dictionary<string, object>
            {
                ["formErrors"] = Array.Empty<string>(),
                ["fieldErrors"] = new Dictionary<string, string[]> { [campo] = [$"{campo} debe ser una fecha ISO"] },
            });
    }

    /// <summary>Compara el token en tiempo constante (se comparan los hashes para igualar longitudes).</summary>
    private void AsegurarTokenEtl()
    {
        var esperado = opcionesEtl.Value.IngestToken;
        var cabecera = Request.Headers.Authorization.ToString();
        var recibido = cabecera.StartsWith("Bearer ", StringComparison.Ordinal) ? cabecera[7..].Trim() : string.Empty;

        var coincide = !string.IsNullOrEmpty(esperado)
            && CryptographicOperations.FixedTimeEquals(
                SHA256.HashData(Encoding.UTF8.GetBytes(esperado)),
                SHA256.HashData(Encoding.UTF8.GetBytes(recibido)));
        if (!coincide)
        {
            throw new clsNoAutenticadoException("Token de ingesta inválido");
        }
    }
}
