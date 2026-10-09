using System.Text.Json;
using IAAutos.API.Configuracion;
using IAAutos.API.Controllers.Comun;
using IAAutos.API.Seguridad;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Mvc;

namespace IAAutos.API.Controllers.Catalogos;

/// <summary>
/// Catalogos maestros (etapas, lugares, clases y tamanos de dano, vistas de foto, cuadrantes...). La lectura es para cualquier usuario
/// autenticado y lleva version (ETag) para que la app y el portal guarden una copia y solo la descarguen cuando cambia. Crear, editar
/// y borrar es solo de administradores y coordinadores de calidad; un valor en uso no se borra (409), se inactiva.
/// </summary>
[Route("api/catalogos")]
public sealed class clsCatalogosController(clsCatalogosService servicio) : clsControladorBase
{
    /// <summary>Todos los catalogos (activos e inactivos) con una version global. Responde 304 si If-None-Match coincide con la version.</summary>
    [HttpGet]
    [ProducesResponseType<CatalogosDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> Todos(CancellationToken ct)
    {
        var instantanea = await servicio.ObtenerTodosAsync(ct);
        if (PonerVersionYComprobar(instantanea.Version))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        return Ok(new CatalogosDto(
            instantanea.Version,
            instantanea.Catalogos.ToDictionary(c => c.Key, c => c.Value.Items)));
    }

    /// <summary>Un catalogo con su version propia. Responde 304 si If-None-Match coincide.</summary>
    [HttpGet("{nombre}")]
    [ProducesResponseType<CatalogoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status304NotModified)]
    public async Task<IActionResult> Uno(string nombre, CancellationToken ct)
    {
        var catalogo = await servicio.ObtenerAsync(nombre, ct);
        return PonerVersionYComprobar(catalogo.Version) ? StatusCode(StatusCodes.Status304NotModified) : Ok(catalogo);
    }

    /// <summary>Crea un valor. Cada catalogo admite solo sus campos (ver CONTRATO.md); un campo de mas responde 400.</summary>
    [HttpPost("{nombre}")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ValorCatalogo>(StatusCodes.Status201Created)]
    [CuerpoSolicitud(typeof(CatalogoValorRequest))]
    public async Task<IActionResult> Crear(string nombre, CancellationToken ct)
    {
        var campos = await LeerCamposAsync(ct);
        return Creado(await servicio.CrearAsync(nombre, campos, ct));
    }

    /// <summary>Cambia solo los campos enviados. El codigo no se puede cambiar. Para retirar un valor se envia activo = false.</summary>
    [HttpPatch("{nombre}/{id:guid}")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType<ValorCatalogo>(StatusCodes.Status200OK)]
    [CuerpoSolicitud(typeof(CatalogoValorRequest))]
    public async Task<IActionResult> Actualizar(string nombre, Guid id, CancellationToken ct)
    {
        var campos = await LeerCamposAsync(ct);
        return Ok(await servicio.ActualizarAsync(nombre, id, campos, ct));
    }

    /// <summary>Borra un valor que nada usa. Si esta en uso responde 409 y solo puede inactivarse.</summary>
    [HttpDelete("{nombre}/{id:guid}")]
    [RolesPermitidos(clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Eliminar(string nombre, Guid id, CancellationToken ct)
    {
        await servicio.EliminarAsync(nombre, id, ct);
        return NoContent();
    }

    /// <summary>
    /// Pone el ETag y Cache-Control en la respuesta y dice si el cliente ya tiene esta version (If-None-Match). "no-cache" obliga a
    /// revalidar en cada uso (el cliente manda su ETag y recibe 304 si no cambio), nunca a descargar de nuevo sin necesidad.
    /// </summary>
    private bool PonerVersionYComprobar(string version)
    {
        var etag = $"\"{version}\"";
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "private, no-cache";
        return Request.Headers.IfNoneMatch
            .SelectMany(v => (v ?? string.Empty).Split(','))
            .Select(v => v.Trim())
            .Any(v => v == "*" || (v.StartsWith("W/", StringComparison.Ordinal) ? v[2..] : v) == etag);
    }
}
