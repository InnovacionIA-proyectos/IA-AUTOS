using IAAutos.API.Configuracion;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IAAutos.API.Controllers.Comun;
using IAAutos.Aplicacion.Comun;
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
    [CuerpoSolicitud(typeof(IAAutos.Aplicacion.Sofasa.DTOSofasa.IngestarOrdenesRequest))]
    public async Task<IActionResult> Ingestar(CancellationToken ct)
    {
        AsegurarTokenEtl();
        var solicitud = await LeerCuerpoAsync<IngestarOrdenesRequest>(ct);
        return Ok(await servicio.IngestarAsync(solicitud, ct));
    }

    /// <summary>
    /// Una pagina de ordenes <c>{items, total, pagina, tamano}</c> (mas recientes primero por defecto) para la web y la app. Filtros: <c>vin</c>, <c>estado</c> (el <c>status_code</c>), <c>ciudad</c>, <c>desde</c> y
    /// <c>hasta</c>; <c>pagina</c>, <c>tamano</c> (50, maximo 200) y <c>orden</c> (<c>campo:asc|desc</c>).
    /// </summary>
    [HttpGet("api/sofasa/ordenes")]
    [ProducesResponseType<PaginaDto<OrdenSofasaDto>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(
        [FromQuery] string? vin,
        [FromQuery] string? estado,
        [FromQuery] string? ciudad,
        [FromQuery] string? desde,
        [FromQuery] string? hasta,
        [FromQuery] string? pagina,
        [FromQuery] string? tamano,
        [FromQuery] string? orden,
        CancellationToken ct) =>
        Ok(await servicio.ListarAsync(new FiltrosOrdenesSofasaRequest(vin, estado, ciudad, desde, hasta, pagina, tamano, orden), ct));

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
