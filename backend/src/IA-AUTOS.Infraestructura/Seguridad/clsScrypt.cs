using System.Buffers.Binary;
using System.Security.Cryptography;

namespace IAAutos.Infraestructura.Seguridad;

/// <summary>
/// scrypt (RFC 7914). .NET no incluye scrypt y las contrasenas existentes se almacenaron con el scrypt de Node
/// (N=16384, r=8, p=1, 64 bytes). Se usa SOLO para verificar esos hashes heredados; los hashes nuevos usan PBKDF2.
/// La implementacion se valida con los vectores de la RFC y con hashes generados por node:crypto.
/// </summary>
internal static class clsScrypt
{
    public static byte[] Derivar(ReadOnlySpan<byte> password, ReadOnlySpan<byte> salt, int n, int r, int p, int longitud)
    {
        if (n < 2 || (n & (n - 1)) != 0)
        {
            throw new ArgumentException("N debe ser una potencia de 2 mayor que 1", nameof(n));
        }

        var tamanoBloque = 128 * r;
        var b = Rfc2898DeriveBytes.Pbkdf2(password, salt, 1, HashAlgorithmName.SHA256, p * tamanoBloque);
        var palabras = new uint[32 * r];
        var v = new uint[n * 32 * r];
        var temporal = new uint[32 * r];

        for (var i = 0; i < p; i++)
        {
            var bloque = b.AsSpan(i * tamanoBloque, tamanoBloque);
            LeerPalabras(bloque, palabras);
            ROMix(palabras, v, temporal, n, r);
            EscribirPalabras(palabras, bloque);
        }

        return Rfc2898DeriveBytes.Pbkdf2(password, b, 1, HashAlgorithmName.SHA256, longitud);
    }

    private static void ROMix(uint[] x, uint[] v, uint[] temporal, int n, int r)
    {
        var longitud = 32 * r;
        for (var i = 0; i < n; i++)
        {
            Array.Copy(x, 0, v, i * longitud, longitud);
            BlockMix(x, temporal, r);
        }

        for (var i = 0; i < n; i++)
        {
            var j = (int)(x[(2 * r - 1) * 16] & (uint)(n - 1));
            for (var k = 0; k < longitud; k++)
            {
                x[k] ^= v[(j * longitud) + k];
            }

            BlockMix(x, temporal, r);
        }
    }

    private static void BlockMix(uint[] b, uint[] y, int r)
    {
        Span<uint> x = stackalloc uint[16];
        b.AsSpan((2 * r - 1) * 16, 16).CopyTo(x);

        for (var i = 0; i < 2 * r; i++)
        {
            for (var k = 0; k < 16; k++)
            {
                x[k] ^= b[(i * 16) + k];
            }

            Salsa208(x);

            // Salida: bloques pares primero, luego impares.
            var destino = ((i / 2) + ((i % 2) * r)) * 16;
            x.CopyTo(y.AsSpan(destino, 16));
        }

        Array.Copy(y, b, 32 * r);
    }

    private static void Salsa208(Span<uint> b)
    {
        Span<uint> x = stackalloc uint[16];
        b.CopyTo(x);

        for (var i = 0; i < 8; i += 2)
        {
            x[4] ^= uint.RotateLeft(x[0] + x[12], 7);
            x[8] ^= uint.RotateLeft(x[4] + x[0], 9);
            x[12] ^= uint.RotateLeft(x[8] + x[4], 13);
            x[0] ^= uint.RotateLeft(x[12] + x[8], 18);
            x[9] ^= uint.RotateLeft(x[5] + x[1], 7);
            x[13] ^= uint.RotateLeft(x[9] + x[5], 9);
            x[1] ^= uint.RotateLeft(x[13] + x[9], 13);
            x[5] ^= uint.RotateLeft(x[1] + x[13], 18);
            x[14] ^= uint.RotateLeft(x[10] + x[6], 7);
            x[2] ^= uint.RotateLeft(x[14] + x[10], 9);
            x[6] ^= uint.RotateLeft(x[2] + x[14], 13);
            x[10] ^= uint.RotateLeft(x[6] + x[2], 18);
            x[3] ^= uint.RotateLeft(x[15] + x[11], 7);
            x[7] ^= uint.RotateLeft(x[3] + x[15], 9);
            x[11] ^= uint.RotateLeft(x[7] + x[3], 13);
            x[15] ^= uint.RotateLeft(x[11] + x[7], 18);
            x[1] ^= uint.RotateLeft(x[0] + x[3], 7);
            x[2] ^= uint.RotateLeft(x[1] + x[0], 9);
            x[3] ^= uint.RotateLeft(x[2] + x[1], 13);
            x[0] ^= uint.RotateLeft(x[3] + x[2], 18);
            x[6] ^= uint.RotateLeft(x[5] + x[4], 7);
            x[7] ^= uint.RotateLeft(x[6] + x[5], 9);
            x[4] ^= uint.RotateLeft(x[7] + x[6], 13);
            x[5] ^= uint.RotateLeft(x[4] + x[7], 18);
            x[11] ^= uint.RotateLeft(x[10] + x[9], 7);
            x[8] ^= uint.RotateLeft(x[11] + x[10], 9);
            x[9] ^= uint.RotateLeft(x[8] + x[11], 13);
            x[10] ^= uint.RotateLeft(x[9] + x[8], 18);
            x[12] ^= uint.RotateLeft(x[15] + x[14], 7);
            x[13] ^= uint.RotateLeft(x[12] + x[15], 9);
            x[14] ^= uint.RotateLeft(x[13] + x[12], 13);
            x[15] ^= uint.RotateLeft(x[14] + x[13], 18);
        }

        for (var i = 0; i < 16; i++)
        {
            b[i] += x[i];
        }
    }

    private static void LeerPalabras(ReadOnlySpan<byte> origen, uint[] destino)
    {
        for (var i = 0; i < destino.Length; i++)
        {
            destino[i] = BinaryPrimitives.ReadUInt32LittleEndian(origen.Slice(i * 4, 4));
        }
    }

    private static void EscribirPalabras(uint[] origen, Span<byte> destino)
    {
        for (var i = 0; i < origen.Length; i++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destino.Slice(i * 4, 4), origen[i]);
        }
    }
}
