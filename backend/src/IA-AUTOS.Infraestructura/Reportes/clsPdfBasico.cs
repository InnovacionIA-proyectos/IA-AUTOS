using System.Text;

namespace IAAutos.Infraestructura.Reportes;

/// <summary>Piezas comunes de los generadores de PDF sin dependencias: texto en WinAnsi y cadenas literales de PDF.</summary>
internal static class clsPdfBasico
{
    private static readonly Encoding WIN_ANSI = CrearWinAnsi();

    public static byte[] Texto(string texto) => Encoding.Latin1.GetBytes(texto);

    /// <summary>Cadena literal de PDF en WinAnsi: se escapan parentesis y barra, y los bytes no imprimibles en octal.</summary>
    public static byte[] CadenaPdf(string texto)
    {
        var bytes = WIN_ANSI.GetBytes(texto);
        var salida = new List<byte>(bytes.Length);
        foreach (var b in bytes)
        {
            if (b is (byte)'(' or (byte)')' or (byte)'\\')
            {
                salida.Add((byte)'\\');
                salida.Add(b);
            }
            else if (b is < 32 or > 126)
            {
                salida.AddRange(Encoding.ASCII.GetBytes("\\" + Convert.ToString(b, 8).PadLeft(3, '0')));
            }
            else
            {
                salida.Add(b);
            }
        }

        return [.. salida];
    }

    private static Encoding CrearWinAnsi()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(1252, EncoderFallback.ReplacementFallback, DecoderFallback.ReplacementFallback);
    }
}
