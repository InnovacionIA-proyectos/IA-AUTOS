using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Inspecciones;

/// <summary>
/// Transferencia de custodia de una inspeccion. Cada firma la envia su titular (sale del token): nadie firma por otro. Se cierra firmada, con
/// reservas o con constancia de no firma, y una transferencia sin cierre bloquea la etapa siguiente.
/// </summary>
[Route("api/inspecciones/{id:guid}/transferencia")]
public sealed class clsTransferenciasController(clsTransferenciasService servicio) : clsControladorBase
{
    /// <summary>
    /// Firma la propia parte (quien entrega firma la entrega y quien recibe, el recibo). 403 si quien envia no es ninguna de las dos partes. La
    /// primera firma crea la transferencia y exige los dos ids, que deben ser usuarios del viaje. Cada firma guarda su fecha y su IP.
    /// </summary>
    [HttpPost]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<TransferenciaDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(FirmarTransferenciaRequest))]
    public async Task<IActionResult> Firmar(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<FirmarTransferenciaRequest>(ct);
        return Creado(await servicio.FirmarAsync(id, solicitud, Cliente(), ct));
    }

    /// <summary>Quien recibe firma «con reservas»: sus observaciones son obligatorias (400 sin ellas). Solo quien recibe puede (403 para cualquier otro).</summary>
    [HttpPost("reservas")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<TransferenciaDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(RecibirConReservasRequest))]
    public async Task<IActionResult> RecibirConReservas(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<RecibirConReservasRequest>(ct);
        return Creado(await servicio.RecibirConReservasAsync(id, solicitud, Cliente(), ct));
    }

    /// <summary>
    /// Constancia de que una parte se nego a firmar: la registra la otra parte y exige el motivo, un testigo con usuario y la foto. Cierra la
    /// transferencia como «no firma».
    /// </summary>
    [HttpPost("no-firma")]
    [RolesPermitidos(clsRol.INSPECTOR, clsRol.CONDUCTOR, clsRol.ADMIN)]
    [ProducesResponseType<TransferenciaDto>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(RegistrarNoFirmaRequest))]
    public async Task<IActionResult> RegistrarNoFirma(Guid id, CancellationToken ct)
    {
        var solicitud = await LeerCuerpoAsync<RegistrarNoFirmaRequest>(ct);
        return Creado(await servicio.RegistrarNoFirmaAsync(id, solicitud, Cliente(), ct));
    }

    /// <summary>La constancia de la transferencia en PDF: firmas con su fecha e IP, reservas o motivo de la no firma, testigo y fotos.</summary>
    [HttpGet("constancia/pdf")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> ConstanciaPdf(Guid id, CancellationToken ct) =>
        File(await servicio.ObtenerConstanciaPdfAsync(id, ct), "application/pdf", $"constancia-{id}.pdf");

    private ContextoCliente Cliente() =>
        new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Headers.UserAgent.ToString());
}
