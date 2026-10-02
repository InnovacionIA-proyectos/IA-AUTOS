using System.Text;
using IAAutos.Infraestructura.Seguridad;
using Microsoft.Extensions.Options;

namespace IAAutos.Tests.Unitarios.Seguridad;

public class ScryptTests
{
    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant();

    // Vectores de prueba de la RFC 7914, seccion 12.
    [Fact]
    public void Rfc7914_vector_1_N16_r1_p1()
    {
        var dk = clsScrypt.Derivar([], [], 16, 1, 1, 64);

        Assert.Equal(
            "77d6576238657b203b19ca42c18a0497f16b4844e3074ae8dfdffa3fede21442fcd0069ded0948f8326a753a0fc81f17e8d3e0fb2e0d3628cf35e20c38d18906",
            Hex(dk));
    }

    [Fact]
    public void Rfc7914_vector_2_N1024_r8_p16()
    {
        var dk = clsScrypt.Derivar(Encoding.ASCII.GetBytes("password"), Encoding.ASCII.GetBytes("NaCl"), 1024, 8, 16, 64);

        Assert.Equal(
            "fdbabe1c9d3472007856e7190d01e9fe7c6ad7cbc8237830e77376634b3731622eaf30d92e22a3886ff109279d9830dac727afb94a83ee6d8360cbdfa2cc0640",
            Hex(dk));
    }

    [Fact]
    public void Rfc7914_vector_3_N16384_r8_p1()
    {
        var dk = clsScrypt.Derivar(Encoding.ASCII.GetBytes("pleaseletmein"), Encoding.ASCII.GetBytes("SodiumChloride"), 16384, 8, 1, 64);

        Assert.Equal(
            "7023bdcb3afd7348461c06cd81fd38ebfda8fbba904f8e3ea9b543f6545da1f2d5432955613f0fcf62d49705242a9af9e61e85dc0d651e40dfcf017b45575887",
            Hex(dk));
    }

    [Fact]
    public void Rechaza_N_que_no_es_potencia_de_dos()
    {
        Assert.Throws<ArgumentException>(() => clsScrypt.Derivar([], [], 1000, 8, 1, 32));
    }
}

public class ServicioContrasenasTests
{
    private static clsServicioContrasenas Servicio(int iteraciones = 1000) =>
        new(Options.Create(new clsOpcionesContrasenas { Iteraciones = iteraciones }));

    // Hashes generados con el codigo original (authService.ts: scrypt de node:crypto, sal de 16 bytes, clave de 64 bytes).
    [Theory]
    [InlineData("BoteroTest#1", "scrypt:00112233445566778899aabbccddeeff:ece352e504c8c5103e8714d376aeaa89a673114c211ec7c35a90c59a4542518c6bf9bdaeb5050dc92c656bd134d50bf497585aedada80e50e86eda30f270c7c1")]
    [InlineData("ñandú contraseña ✓ larga con espacios", "scrypt:ffeeddccbbaa99887766554433221100:115ae04cd70be124db6f62ab27d2d08ae8137cd2cca6cf8611c3a22decf374d2e572aa77ce836eb6533427376917dedbace01d51b99a3c971797c49c2a6512f5")]
    [InlineData("x", "scrypt:0102030405060708090a0b0c0d0e0f10:936c45fb5f22fb4ecda5400f1bbfa07b387f01b202839ac365449e69b750093f640ad4b15bf730c4e021efb6c3649f22a95482b9353ff6096229d8aaa3b552f5")]
    public void Verifica_los_hashes_scrypt_generados_por_el_sistema_anterior(string password, string hash)
    {
        var servicio = Servicio();

        Assert.True(servicio.Verificar(password, hash));
        Assert.False(servicio.Verificar(password + "x", hash));
        Assert.True(servicio.RequiereActualizacion(hash));
    }

    [Fact]
    public void Hash_nuevo_se_verifica_y_no_requiere_actualizacion()
    {
        var servicio = Servicio();

        var hash = servicio.Hashear("Clave-Segura-1");

        Assert.StartsWith("pbkdf2-sha256$1000$", hash);
        Assert.True(servicio.Verificar("Clave-Segura-1", hash));
        Assert.False(servicio.Verificar("clave-segura-1", hash));
        Assert.False(servicio.RequiereActualizacion(hash));
        Assert.True(hash.Length < 400);
    }

    [Fact]
    public void Dos_hashes_de_la_misma_clave_usan_sal_distinta()
    {
        var servicio = Servicio();

        Assert.NotEqual(servicio.Hashear("igual"), servicio.Hashear("igual"));
    }

    [Fact]
    public void Un_hash_con_menos_iteraciones_que_las_vigentes_requiere_actualizacion()
    {
        var viejo = Servicio(1000).Hashear("clave");

        Assert.True(Servicio(5000).RequiereActualizacion(viejo));
        Assert.False(Servicio(1000).RequiereActualizacion(viejo));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("texto-sin-formato")]
    [InlineData("scrypt:zz:yy")]
    [InlineData("scrypt:00:")]
    [InlineData("pbkdf2-sha256$abc$AA==$AA==")]
    [InlineData("pbkdf2-sha256$1000$no-base64$AA==")]
    public void Hashes_ausentes_o_mal_formados_no_verifican_y_no_lanzan_excepciones(string? hash)
    {
        Assert.False(Servicio().Verificar("lo-que-sea", hash));
    }

    [Fact]
    public void Sin_hash_no_requiere_actualizacion()
    {
        Assert.False(Servicio().RequiereActualizacion(null));
        Assert.False(Servicio().RequiereActualizacion(string.Empty));
    }
}
