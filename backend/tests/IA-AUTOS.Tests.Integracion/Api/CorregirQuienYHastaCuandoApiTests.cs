using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// Corregir una inspeccion (S7-HU-19): solo quien la hizo o un administrador, y solo mientras su transferencia no tenga ninguna firma ni cierre. Cada rechazo lleva su codigo estable (CORRECCION_SOLO_AUTOR 403 e
/// INSPECCION_YA_FIRMADA 409) y no crea ninguna version; despues de firmar se sigue aclarando con una nota y firmando con reservas.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class CorregirQuienYHastaCuandoApiTests(clsBaseDatosFixture bd) : clsPruebaApiBase(bd)
{
    private clsSembrador Sembrador => new(BaseDatos.CadenaConexion);

    private sealed record Sesion(string Token, Guid UsuarioId);

    /// <summary>Una inspeccion de salida cerrada que hizo el inspector, en un viaje activo del inspector y del conductor.</summary>
    private sealed record Escenario(Guid Inspeccion, Sesion Inspector, Sesion Conductor, Sesion Admin);

    private async Task<Sesion> SesionAsync(string email, string rol)
    {
        var usuario = await Fabrica.SembrarUsuarioAsync(email, "Clave-Segura-1", rol, nombre: "Nombre " + email);
        return new Sesion(await LoginAsync(email, "Clave-Segura-1"), usuario.Id);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage r) => (await r.Content.ReadFromJsonAsync<JsonElement>()).Clone();

    private async Task<Escenario> EscenarioAsync()
    {
        var inspector = await SesionAsync("inspector@ejemplo.com", clsRol.INSPECTOR);
        var conductor = await SesionAsync("conductor@ejemplo.com", clsRol.CONDUCTOR);
        var admin = await SesionAsync("admin@ejemplo.com", clsRol.ADMIN);
        var (_, etapas) = await Sembrador.VehiculoConRutaAsync("VIN-CORR-0001", conductorId: conductor.UsuarioId, inspectorId: inspector.UsuarioId);
        var inspeccion = await Sembrador.InformeAsync(etapas[0], inspector.UsuarioId, cerrada: true);
        return new Escenario(inspeccion, inspector, conductor, admin);
    }

    private Task<HttpResponseMessage> CorregirAsync(Sesion quien, Guid inspeccion) => Cliente(quien.Token).PostAsync($"/api/inspecciones/{inspeccion}/corregir", null);

    private Task<int> VersionesAsync(Guid inspeccion) =>
        Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblInspecciones WHERE EtapaId = (SELECT EtapaId FROM dbo.tblInspecciones WHERE Id = @i)", ("@i", inspeccion));

    // ------------------------------------------------------------------------------------- quien corrige

    [Fact]
    public async Task El_conductor_no_corrige_lo_que_hizo_el_inspector_y_no_se_crea_ninguna_version()
    {
        var e = await EscenarioAsync();

        var r = await CorregirAsync(e.Conductor, e.Inspeccion);

        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
        var error = await JsonAsync(r);
        Assert.Equal("CORRECCION_SOLO_AUTOR", error.GetProperty("code").GetString());
        Assert.Contains("quien la hizo o un administrador", error.GetProperty("message").GetString());
        Assert.Equal(1, await VersionesAsync(e.Inspeccion));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.corregir'"));
    }

    [Fact]
    public async Task Quien_la_hizo_la_corrige_mientras_nadie_firme_y_la_anterior_no_cambia()
    {
        var e = await EscenarioAsync();
        var antes = await Sembrador.EscalarAsync("SELECT CONCAT(Estado, '|', UpdatedAt, '|', Version) FROM dbo.tblInspecciones WHERE Id = @i", ("@i", e.Inspeccion));

        var r = await CorregirAsync(e.Inspector, e.Inspeccion);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var nueva = await JsonAsync(r);
        Assert.Equal(2, nueva.GetProperty("version").GetInt32());
        Assert.Equal("borrador", nueva.GetProperty("estado").GetString());
        Assert.Equal(e.Inspeccion, nueva.GetProperty("corrige_inspeccion_id").GetGuid());
        Assert.Equal(antes, await Sembrador.EscalarAsync("SELECT CONCAT(Estado, '|', UpdatedAt, '|', Version) FROM dbo.tblInspecciones WHERE Id = @i", ("@i", e.Inspeccion)));
    }

    [Fact]
    public async Task El_administrador_corrige_lo_de_otro_mientras_nadie_firme()
    {
        var e = await EscenarioAsync();

        var r = await CorregirAsync(e.Admin, e.Inspeccion);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var nueva = await JsonAsync(r);
        Assert.Equal(2, nueva.GetProperty("version").GetInt32());
        // La version nueva queda a nombre de quien corrige; la anterior sigue siendo del inspector.
        Assert.Equal(e.Admin.UsuarioId, nueva.GetProperty("usuario_id").GetGuid());
        Assert.Equal(e.Inspector.UsuarioId, (Guid)(await Sembrador.EscalarAsync("SELECT UsuarioId FROM dbo.tblInspecciones WHERE Id = @i", ("@i", e.Inspeccion)))!);
    }

    [Fact]
    public async Task Una_transferencia_registrada_pero_sin_firmas_no_impide_corregir()
    {
        var e = await EscenarioAsync();
        await Sembrador.EjecutarAsync(
            "INSERT dbo.tblTransferenciasCustodia (Id, InspeccionId, EntregaUsuarioId, RecibeUsuarioId) VALUES (NEWID(), @i, @e, @r)",
            ("@i", e.Inspeccion), ("@e", e.Inspector.UsuarioId), ("@r", e.Conductor.UsuarioId));

        var r = await CorregirAsync(e.Inspector, e.Inspeccion);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    // ------------------------------------------------------------------------------- hasta la primera firma

    [Theory]
    [InlineData(null)]
    [InlineData("firmada")]
    [InlineData("con_reservas")]
    [InlineData("no_firma")]
    public async Task Con_una_firma_o_un_cierre_no_corrige_nadie_y_el_rechazo_dice_que_ya_fue_firmada(string? tipoCierre)
    {
        var e = await EscenarioAsync();
        await Sembrador.TransferenciaAsync(e.Inspeccion, e.Inspector.UsuarioId, e.Conductor.UsuarioId, tipoCierre, testigoId: tipoCierre == "no_firma" ? e.Admin.UsuarioId : null);

        // Quien la hizo, un administrador y quien no la hizo: el mismo rechazo, con su codigo propio y distinto del de «solo el autor».
        foreach (var quien in new[] { e.Inspector, e.Admin, e.Conductor })
        {
            var r = await CorregirAsync(quien, e.Inspeccion);

            Assert.Equal(HttpStatusCode.Conflict, r.StatusCode);
            var error = await JsonAsync(r);
            Assert.Equal("INSPECCION_YA_FIRMADA", error.GetProperty("code").GetString());
            Assert.Contains("ya fue firmada", error.GetProperty("message").GetString());
        }

        Assert.Equal(1, await VersionesAsync(e.Inspeccion));
        Assert.Equal(0, await Sembrador.ContarAsync("SELECT COUNT(*) FROM dbo.tblAuditLog WHERE Accion = 'inspeccion.corregir'"));
    }

    [Fact]
    public async Task Despues_de_firmar_la_nota_aclaratoria_y_la_firma_con_reservas_siguen_funcionando_y_la_inspeccion_no_cambia()
    {
        var e = await EscenarioAsync();
        // Firmo solo quien entrega (el inspector): ya no se corrige, pero quien recibe aun puede firmar con reservas y se puede aclarar con una nota.
        await Sembrador.TransferenciaAsync(e.Inspeccion, e.Inspector.UsuarioId, e.Conductor.UsuarioId, tipoCierre: null);
        var antes = await Sembrador.EscalarAsync("SELECT CONCAT(Estado, '|', UpdatedAt, '|', Version) FROM dbo.tblInspecciones WHERE Id = @i", ("@i", e.Inspeccion));

        Assert.Equal(HttpStatusCode.Conflict, (await CorregirAsync(e.Inspector, e.Inspeccion)).StatusCode);
        var nota = await Cliente(e.Inspector.Token).PostAsJsonAsync($"/api/inspecciones/{e.Inspeccion}/notas", new { texto = "Se aclara el estado del carro al entregar" });
        var reservas = await Cliente(e.Conductor.Token).PostAsJsonAsync($"/api/inspecciones/{e.Inspeccion}/transferencia/reservas", new { firma = "B", reservas = "Un rayon en la puerta" });

        Assert.Equal(HttpStatusCode.Created, nota.StatusCode);
        Assert.Equal("Se aclara el estado del carro al entregar", (await JsonAsync(nota)).GetProperty("texto").GetString());
        Assert.Equal(HttpStatusCode.Created, reservas.StatusCode);
        Assert.Equal("con_reservas", (await JsonAsync(reservas)).GetProperty("tipo_cierre").GetString());
        Assert.Equal(antes, await Sembrador.EscalarAsync("SELECT CONCAT(Estado, '|', UpdatedAt, '|', Version) FROM dbo.tblInspecciones WHERE Id = @i", ("@i", e.Inspeccion)));
        Assert.Equal(1, await VersionesAsync(e.Inspeccion));
    }
}
