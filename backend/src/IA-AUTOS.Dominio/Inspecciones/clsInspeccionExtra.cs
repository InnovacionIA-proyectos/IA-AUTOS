using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Inspecciones;

/// <summary>
/// Un extra del vehiculo fuera del dibujo estandar (barras de techo, volco, platon, otro...) que reporta una inspeccion: su tipo (el <c>Codigo</c> del catalogo de tipos de extra), un texto libre solo cuando el
/// tipo es <see cref="clsTipoExtra.OTRO"/>, una observacion opcional y sus fotos selladas (evidencias con <c>ExtraId</c>). No es un hallazgo: no toca el dibujo ni cuenta en los patrones, el mapa de calor ni la
/// tasa de rechazo. Solo se agrega, cambia o quita mientras la inspeccion esta en borrador; con fotos no se quita (la foto es evidencia).
/// </summary>
public sealed class clsInspeccionExtra : clsEntidadBase
{
    public const int TEXTO_MAX = 100;
    public const int OBSERVACION_MAX = 500;

    private clsInspeccionExtra()
    {
    }

    public Guid InspeccionId { get; private set; }

    /// <summary>El codigo del tipo de extra (catalogo <c>tipos-extra</c>).</summary>
    public string Tipo { get; private set; } = string.Empty;

    /// <summary>El texto libre que describe el extra cuando el tipo es «otro»; nulo en los demas.</summary>
    public string? Texto { get; private set; }

    public string? Observacion { get; private set; }

    public static clsInspeccionExtra Crear(Guid inspeccionId, string? tipo, string? texto, string? observacion, DateTimeOffset ahora)
    {
        var extra = new clsInspeccionExtra { InspeccionId = inspeccionId };
        extra.Aplicar(tipo, texto, observacion);
        extra.Inicializar(ahora);
        return extra;
    }

    /// <summary>Cambia el tipo, el texto libre y la observacion (los tres juntos: lo que no llega queda sin valor).</summary>
    public void Actualizar(string? tipo, string? texto, string? observacion, DateTimeOffset ahora)
    {
        Aplicar(tipo, texto, observacion);
        Tocar(ahora);
    }

    /// <summary>El nombre con que se muestra y se nombra en los mensajes: el del tipo, o «Otro: texto» cuando el tipo es «otro».</summary>
    public static string NombreParaMostrar(string tipo, string nombreDelTipo, string? texto) =>
        tipo == clsTipoExtra.OTRO && !string.IsNullOrWhiteSpace(texto) ? $"{nombreDelTipo}: {texto}" : nombreDelTipo;

    private void Aplicar(string? tipo, string? texto, string? observacion)
    {
        var codigo = clsValidador.Opcional(tipo) ?? throw clsErroresCampos.De("tipo", "El tipo del extra es requerido: use el código de un tipo activo de tipos-extra");
        var textoLimpio = clsValidador.Opcional(texto);
        if (codigo == clsTipoExtra.OTRO)
        {
            if (textoLimpio is null)
            {
                throw clsErroresCampos.De("texto", "Indique qué es el extra: el tipo «otro» exige un texto");
            }

            if (textoLimpio.Length > TEXTO_MAX)
            {
                throw clsErroresCampos.De("texto", $"El texto admite máximo {TEXTO_MAX} caracteres");
            }
        }
        else if (textoLimpio is not null)
        {
            throw clsErroresCampos.De("texto", "El texto libre solo se admite cuando el tipo es «otro»");
        }

        var observacionLimpia = clsValidador.Opcional(observacion);
        if (observacionLimpia is { Length: > OBSERVACION_MAX })
        {
            throw clsErroresCampos.De("observacion", $"La observación admite máximo {OBSERVACION_MAX} caracteres");
        }

        Tipo = codigo;
        Texto = textoLimpio;
        Observacion = observacionLimpia;
    }
}
