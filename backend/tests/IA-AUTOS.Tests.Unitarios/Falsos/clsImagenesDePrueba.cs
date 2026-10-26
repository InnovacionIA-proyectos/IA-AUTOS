using System.IO.Compression;
using System.Text;

namespace IAAutos.Tests.Unitarios.Falsos;

/// <summary>Imagenes minimas para probar los PDF: lo justo que lee el generador.</summary>
public static class clsImagenesDePrueba
{
    /// <summary>JPEG minimo: solo lo que el generador lee (el marcador SOF0 con el tamano y los 3 componentes).</summary>
    public static byte[] Jpeg(int ancho = 40, int alto = 30) =>
    [
        0xFF, 0xD8, 0xFF, 0xC0, 0x00, 0x11, 0x08, (byte)(alto >> 8), (byte)alto, (byte)(ancho >> 8), (byte)ancho, 0x03,
        0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01, 0xFF, 0xD9,
    ];

    /// <summary>PNG RGB de 8 bits sin entrelazado, con sus datos comprimidos de verdad (el generador no valida los CRC).</summary>
    public static byte[] Png(int ancho = 2, int alto = 2, int tipoColor = 2, byte[]? paleta = null)
    {
        static byte[] Trozo(string nombre, byte[] cuerpo)
        {
            var longitud = new[] { (byte)(cuerpo.Length >> 24), (byte)(cuerpo.Length >> 16), (byte)(cuerpo.Length >> 8), (byte)cuerpo.Length };
            return [.. longitud, .. Encoding.ASCII.GetBytes(nombre), .. cuerpo, 0, 0, 0, 0];
        }

        var cabecera = new byte[13];
        cabecera[3] = (byte)ancho;
        cabecera[7] = (byte)alto;
        cabecera[8] = 8;
        cabecera[9] = (byte)tipoColor;
        var canales = tipoColor == 2 ? 3 : 1;
        var crudo = new byte[alto * (1 + (ancho * canales))];
        using var comprimido = new MemoryStream();
        using (var zlib = new ZLibStream(comprimido, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(crudo);
        }

        var firma = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        var partes = new List<byte[]> { firma, Trozo("IHDR", cabecera) };
        if (paleta is not null)
        {
            partes.Add(Trozo("PLTE", paleta));
        }

        partes.Add(Trozo("IDAT", comprimido.ToArray()));
        partes.Add(Trozo("IEND", []));
        return [.. partes.SelectMany(p => p)];
    }
}
