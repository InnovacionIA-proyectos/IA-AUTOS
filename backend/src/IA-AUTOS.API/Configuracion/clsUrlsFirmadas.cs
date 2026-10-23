using System.Runtime.CompilerServices;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Archivos.DTOArchivos;
using IAAutos.Aplicacion.Inspecciones.DTOInspecciones;
using IAAutos.Aplicacion.Parametros;
using IAAutos.Dominio.Parametros;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;

namespace IAAutos.API.Configuracion;

/// <summary>
/// Antes de armar una respuesta lee el parametro <c>url_firmada_minutos</c> (con su cache de un minuto) y lo deja en la solicitud: asi las URL firmadas de las fotos que lleva la respuesta usan la
/// vigencia vigente sin consultar la base mientras se escribe el JSON.
/// </summary>
public sealed class clsFiltroVigenciaUrls(IParametros parametros) : IAsyncResultFilter
{
    public const string CLAVE = "iaautos.url_firmada_minutos";
    public const int MINUTOS_POR_DEFECTO = 15;

    public async Task OnResultExecutionAsync(ResultExecutingContext contexto, ResultExecutionDelegate siguiente)
    {
        if (contexto.Result is ObjectResult)
        {
            contexto.HttpContext.Items[CLAVE] = await parametros.EnteroAsync(clsClavesParametros.URL_FIRMADA_MINUTOS, MINUTOS_POR_DEFECTO);
        }

        await siguiente();
    }
}

/// <summary>
/// Hace que toda foto salga con su URL firmada: al escribir una evidencia, un archivo subido o una firma de la transferencia que es una ruta, la API reemplaza su URL (o su ruta) por una URL absoluta
/// firmada y agrega cuando vence. Va en la serializacion para cubrir todo lo que devuelve la API (inventario, informe, hallazgos, seguimiento, transferencia, subida) sin que cada servicio firme.
/// </summary>
public sealed partial class clsConfiguracionJsonUrls(IFirmadorUrls firmador, IHttpContextAccessor accesor) : IConfigureOptions<JsonOptions>
{
    // Una evidencia o un archivo escribe su URL y su vencimiento con la misma firma: se recuerda por objeto para que los dos valores coincidan.
    private readonly ConditionalWeakTable<object, clsUrlFirmada?> _firmadas = [];

    public void Configure(JsonOptions opciones)
    {
        var resolutor = opciones.JsonSerializerOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver();
        opciones.JsonSerializerOptions.TypeInfoResolver = resolutor.WithAddedModifier(Modificar);
    }

    private void Modificar(JsonTypeInfo tipo)
    {
        if (tipo.Type == typeof(EvidenciaDto))
        {
            Reemplazar(tipo, "url", o => FirmaDe(o, ((EvidenciaDto)o).RutaArchivo)?.Url ?? ((EvidenciaDto)o).Url);
            Reemplazar(tipo, "url_expira_at", o => FirmaDe(o, ((EvidenciaDto)o).RutaArchivo)?.ExpiraAt);
        }
        else if (tipo.Type == typeof(ArchivoSubidoDto))
        {
            Reemplazar(tipo, "url", o => FirmaDe(o, ((ArchivoSubidoDto)o).StoragePath)?.Url ?? ((ArchivoSubidoDto)o).Url);
            Reemplazar(tipo, "url_expira_at", o => FirmaDe(o, ((ArchivoSubidoDto)o).StoragePath)?.ExpiraAt);
        }
        else if (tipo.Type == typeof(TransferenciaDetalleDto))
        {
            Reemplazar(tipo, "firma_entrega", o => FirmaComoUrl(((TransferenciaDetalleDto)o).FirmaEntrega));
            Reemplazar(tipo, "firma_recibe", o => FirmaComoUrl(((TransferenciaDetalleDto)o).FirmaRecibe));
        }
    }

    private static void Reemplazar(JsonTypeInfo tipo, string nombre, Func<object, object?> obtener)
    {
        if (tipo.Properties.FirstOrDefault(p => p.Name == nombre) is { } propiedad)
        {
            propiedad.Get = obtener;
        }
    }

    private clsUrlFirmada? FirmaDe(object dueno, string? ruta)
    {
        if (accesor.HttpContext is not { } contexto)
        {
            return null;
        }

        return _firmadas.GetValue(dueno, _ => firmador.Firmar(ruta, BaseUrl(contexto), Vigencia(contexto)));
    }

    /// <summary>La firma dibujada de una transferencia es un trazo (texto) o la ruta de una imagen subida: solo la ruta se convierte en URL firmada.</summary>
    private string? FirmaComoUrl(string? firma) =>
        firma is not null && accesor.HttpContext is { } contexto && NombreDeArchivo().IsMatch(firma)
            ? firmador.Firmar(firma, BaseUrl(contexto), Vigencia(contexto))?.Url ?? firma
            : firma;

    private static string BaseUrl(HttpContext contexto) => $"{contexto.Request.Scheme}://{contexto.Request.Host}{contexto.Request.PathBase}";

    private static int Vigencia(HttpContext contexto) =>
        contexto.Items.TryGetValue(clsFiltroVigenciaUrls.CLAVE, out var minutos) && minutos is int valor ? valor : clsFiltroVigenciaUrls.MINUTOS_POR_DEFECTO;

    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]*\.(jpg|jpeg|png)$", RegexOptions.IgnoreCase)]
    private static partial Regex NombreDeArchivo();
}
