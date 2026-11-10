using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Integracion.Infraestructura;

namespace IAAutos.Tests.Integracion.Api;

/// <summary>
/// La adopcion mide lo que Calidad acordo (S7-HU-07): solo cuentan las salidas CERRADAS (una inspeccion a medias no suma), por carro (cada unidad en su propio destino) y por camion (cada viaje una sola vez), las dos
/// con su numerador y su denominador en la misma respuesta; los viajes cancelados no cuentan y un viaje antiguo sin fecha programada sigue contando por su fecha de creacion.
/// </summary>
[Collection(clsColecciones.BASE_DE_DATOS_4)]
public sealed class AdopcionPorCamionApiTests(clsBaseDatosFixture bd) : clsPruebaTableroBase(bd)
{
    private const string DIA = "2026-11-18";
    private const string FECHA = "2026-11-18T06:00:00-05:00";
    private const string CONSULTA = "?desde=" + DIA + "&hasta=" + DIA;

    private static StringContent Cuerpo(object valor) => new(JsonSerializer.Serialize(valor), Encoding.UTF8, "application/json");

    private async Task<Guid> UnidadAsync() => (Guid)(await Sembrador.EscalarAsync("SELECT Id FROM dbo.tblTiposUnidadTransportadora WHERE Codigo = 'ninera'"))!;

    /// <summary>Un viaje del dia con sus unidades (cada una a su destino) creado por la API; devuelve su id.</summary>
    private async Task<Guid> ViajeAsync(Sesion coordinador, string codigo, params (string Vin, Guid Destino)[] unidades)
    {
        foreach (var (vin, _) in unidades)
        {
            await Sembrador.VehiculoAsync(vin);
        }

        var lugarCargue = await LugarRealAsync(Cliente(coordinador.Token), "CEDI Envigado", "cedi_1", "Envigado");
        var r = await Cliente(coordinador.Token).PostAsync(
            "/api/viajes",
            Cuerpo(new
            {
                codigo_mtrack = codigo, tipo_unidad_id = await UnidadAsync(), placa = "ABC123", fecha_programada = FECHA, lugar_cargue_id = lugarCargue,
                unidades = unidades.Select(u => new { vin = u.Vin, lugar_destino_id = u.Destino }).ToArray(),
            }));
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        return (await ComoJsonAsync(r)).GetProperty("id").GetGuid();
    }

    /// <summary>La inspeccion de salida del Cargue de la unidad: cerrada o en borrador.</summary>
    private async Task SalidaAsync(Guid viaje, string vin, Guid usuario, bool cerrada)
    {
        var etapa = (Guid)(await Sembrador.EscalarAsync(
            "SELECT e.Id FROM dbo.tblEtapas e JOIN dbo.tblVehiculos v ON v.Id = e.VehiculoId JOIN dbo.tblEtapasCatalogo c ON c.Id = e.EtapaCatalogoId WHERE e.ViajeId = @v AND v.Vin = @vin AND c.Codigo = 'cargue'",
            ("@v", viaje), ("@vin", vin)))!;
        await Sembrador.InformeAsync(etapa, usuario, cerrada, momento: "salida");
    }

    private static JsonElement Medida(JsonElement r, string medida) => r.GetProperty(medida);

    private static JsonElement Destino(JsonElement medida, string? lugar) =>
        medida.GetProperty("por_destino").EnumerateArray().Single(d => d.GetProperty("lugar").GetString() == lugar);

    [Fact]
    public async Task Una_salida_en_borrador_no_cuenta_y_el_viaje_cuenta_una_vez_por_camion_y_cada_carro_por_carro()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var inspector = await SesionAsync(clsRol.INSPECTOR);
        var norte = await LugarRealAsync(Cliente(coordinador.Token), "Concesionario Norte", "concesionario", "Bogota");
        var viaje = await ViajeAsync(coordinador, "MT-2026-0417", ("VIN-ADO-A1", norte), ("VIN-ADO-A2", norte), ("VIN-ADO-A3", norte), ("VIN-ADO-A4", norte));
        await SalidaAsync(viaje, "VIN-ADO-A1", inspector.UsuarioId, cerrada: true);
        await SalidaAsync(viaje, "VIN-ADO-A2", inspector.UsuarioId, cerrada: true);
        await SalidaAsync(viaje, "VIN-ADO-A3", inspector.UsuarioId, cerrada: false);

        var r = await ConsultarAsync(coordinador, "/api/reportes/adopcion", CONSULTA);

        // CA-1 y ESC-01: de 4 carros, 2 con la salida cerrada; la de A3 esta en borrador y no suma, y A4 no tiene.
        var carro = Medida(r, "por_carro");
        Assert.Equal(4, carro.GetProperty("programados").GetInt32());
        Assert.Equal(2, carro.GetProperty("con_salida_digital").GetInt32());
        Assert.Equal(50m, carro.GetProperty("adopcion").GetDecimal());
        Assert.Equal(4, r.GetProperty("programados").GetInt32());
        Assert.Equal(2, r.GetProperty("con_salida_digital").GetInt32());
        // El camion cuenta una sola vez y esta adoptado porque alguna de sus unidades tiene la salida cerrada.
        var camion = Medida(r, "por_camion");
        Assert.Equal(1, camion.GetProperty("programados").GetInt32());
        Assert.Equal(1, camion.GetProperty("con_salida_digital").GetInt32());
        Assert.Equal(100m, camion.GetProperty("adopcion").GetDecimal());
        // Cada una trae su desglose por destino; todo el viaje va a un solo lugar.
        Assert.Equal(4, Destino(carro, "Concesionario Norte").GetProperty("programados").GetInt32());
        Assert.Equal(2, Destino(carro, "Concesionario Norte").GetProperty("con_salida_digital").GetInt32());
        Assert.Equal(1, Destino(camion, "Concesionario Norte").GetProperty("programados").GetInt32());
    }

    [Fact]
    public async Task Cada_carro_cuenta_en_su_destino_y_el_viaje_con_varios_destinos_cuenta_una_sola_vez_sin_destino_unico()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var inspector = await SesionAsync(clsRol.INSPECTOR);
        var norte = await LugarRealAsync(Cliente(coordinador.Token), "Concesionario Norte", "concesionario", "Bogota");
        var sur = await LugarRealAsync(Cliente(coordinador.Token), "Concesionario Sur", "concesionario", "Cali");
        var unidades = Enumerable.Range(1, 5).Select(i => ($"VIN-ADO-N{i}", norte)).Concat(Enumerable.Range(1, 3).Select(i => ($"VIN-ADO-S{i}", sur))).ToArray();
        var viaje = await ViajeAsync(coordinador, "MT-2026-0418", unidades);
        foreach (var (vin, _) in unidades)
        {
            await SalidaAsync(viaje, vin, inspector.UsuarioId, cerrada: true);
        }

        var r = await ConsultarAsync(coordinador, "/api/reportes/adopcion", CONSULTA);

        // ESC-02: 5 carros en Norte y 3 en Sur, cada uno en su destino.
        var carro = Medida(r, "por_carro");
        Assert.Equal(8, carro.GetProperty("programados").GetInt32());
        Assert.Equal(5, Destino(carro, "Concesionario Norte").GetProperty("programados").GetInt32());
        Assert.Equal(3, Destino(carro, "Concesionario Sur").GetProperty("programados").GetInt32());
        Assert.Equal(100m, Destino(carro, "Concesionario Sur").GetProperty("adopcion").GetDecimal());
        // Por camion el viaje cuenta una sola vez y, como va a dos destinos, sale sin destino unico.
        var camion = Medida(r, "por_camion");
        Assert.Equal(1, camion.GetProperty("programados").GetInt32());
        var unico = Assert.Single(camion.GetProperty("por_destino").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, unico.GetProperty("lugar_id").ValueKind);
        Assert.Equal(JsonValueKind.Null, unico.GetProperty("lugar").ValueKind);
        // Las dos medidas traen numerador y denominador para explicarlas por separado.
        foreach (var medida in new[] { camion, carro })
        {
            Assert.Equal(["programados", "con_salida_digital", "adopcion", "por_destino"], medida.EnumerateObject().Select(p => p.Name).ToList());
            Assert.Equal(["lugar_id", "lugar", "programados", "con_salida_digital", "adopcion"], medida.GetProperty("por_destino")[0].EnumerateObject().Select(p => p.Name).ToList());
        }
    }

    [Fact]
    public async Task Los_camiones_se_agrupan_por_el_destino_de_su_viaje_y_un_viaje_cancelado_no_cuenta_en_ninguna_de_las_dos()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var inspector = await SesionAsync(clsRol.INSPECTOR);
        var norte = await LugarRealAsync(Cliente(coordinador.Token), "Concesionario Norte", "concesionario", "Bogota");
        var sur = await LugarRealAsync(Cliente(coordinador.Token), "Concesionario Sur", "concesionario", "Cali");
        var aNorte = await ViajeAsync(coordinador, "MT-A", ("VIN-ADO-C1", norte), ("VIN-ADO-C2", norte));
        var aSur = await ViajeAsync(coordinador, "MT-B", ("VIN-ADO-D1", sur));
        var cancelado = await ViajeAsync(coordinador, "MT-C", ("VIN-ADO-E1", sur), ("VIN-ADO-E2", sur));
        await SalidaAsync(aNorte, "VIN-ADO-C1", inspector.UsuarioId, cerrada: true);
        await SalidaAsync(aSur, "VIN-ADO-D1", inspector.UsuarioId, cerrada: false);
        await SalidaAsync(cancelado, "VIN-ADO-E1", inspector.UsuarioId, cerrada: true);
        Assert.Equal(HttpStatusCode.OK, (await Cliente(coordinador.Token).PatchAsync($"/api/viajes/{cancelado}/estado", Cuerpo(new { estado = "cancelado" }))).StatusCode);

        var r = await ConsultarAsync(coordinador, "/api/reportes/adopcion", CONSULTA);

        // CA-5: el cancelado no cuenta (3 carros y 2 camiones, no 5 y 3).
        var camion = Medida(r, "por_camion");
        var carro = Medida(r, "por_carro");
        Assert.Equal(2, camion.GetProperty("programados").GetInt32());
        Assert.Equal(1, camion.GetProperty("con_salida_digital").GetInt32());
        Assert.Equal(3, carro.GetProperty("programados").GetInt32());
        Assert.Equal(1, carro.GetProperty("con_salida_digital").GetInt32());
        // CA-2: el camion al norte esta adoptado (alguna salida cerrada) y el del sur no (solo un borrador).
        Assert.Equal(1, Destino(camion, "Concesionario Norte").GetProperty("con_salida_digital").GetInt32());
        Assert.Equal(0, Destino(camion, "Concesionario Sur").GetProperty("con_salida_digital").GetInt32());
        Assert.Equal(1, Destino(camion, "Concesionario Sur").GetProperty("programados").GetInt32());
        Assert.Equal(2, Destino(carro, "Concesionario Norte").GetProperty("programados").GetInt32());
    }

    [Fact]
    public async Task Un_viaje_antiguo_sin_fecha_programada_sigue_contando_por_su_fecha_de_creacion_y_sus_unidades_sin_destino_salen_aparte()
    {
        var coordinador = await SesionAsync(clsRol.COORDINADOR_CALIDAD);
        var inspector = await SesionAsync(clsRol.INSPECTOR);
        var norte = await LugarRealAsync(Cliente(coordinador.Token), "Concesionario Norte", "concesionario", "Bogota");
        var viaje = await ViajeAsync(coordinador, "MT-VIEJO", ("VIN-ADO-V1", norte), ("VIN-ADO-V2", norte));
        await SalidaAsync(viaje, "VIN-ADO-V1", inspector.UsuarioId, cerrada: true);
        // Como los de antes: sin fecha programada (cuenta por su creacion) y sin destino por unidad.
        await Sembrador.EjecutarAsync(
            "UPDATE dbo.tblViajes SET FechaProgramada = NULL, CreatedAt = '2026-11-18T10:00:00-05:00' WHERE Id = @v; UPDATE dbo.tblViajeVehiculos SET LugarDestinoId = NULL WHERE ViajeId = @v", ("@v", viaje));

        var r = await ConsultarAsync(coordinador, "/api/reportes/adopcion", CONSULTA);

        // CA-6: cuenta el dia de su creacion; sus unidades sin destino van en la fila sin lugar.
        var carro = Medida(r, "por_carro");
        Assert.Equal(2, carro.GetProperty("programados").GetInt32());
        Assert.Equal(1, carro.GetProperty("con_salida_digital").GetInt32());
        var sinDestino = carro.GetProperty("por_destino").EnumerateArray().Single(d => d.GetProperty("lugar_id").ValueKind == JsonValueKind.Null);
        Assert.Equal(2, sinDestino.GetProperty("programados").GetInt32());
        Assert.Equal(1, Medida(r, "por_camion").GetProperty("programados").GetInt32());
        Assert.Equal(JsonValueKind.Null, Medida(r, "por_camion").GetProperty("por_destino")[0].GetProperty("lugar_id").ValueKind);
    }
}
