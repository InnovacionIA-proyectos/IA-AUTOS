using System.Text.Json;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Tests.Unitarios.Falsos;

namespace IAAutos.Tests.Unitarios.Aplicacion;

/// <summary>El registro de auditoria: IP, dispositivo, detalle antes y despues sin secretos, y el viaje y el cliente de lo auditado.</summary>
public sealed class AuditoriaTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 10, 21, 9, 0, 0, TimeSpan.Zero);

    private static readonly Guid CLIENTE_ACTOR = Guid.Parse("aaaaaaaa-0000-0000-0000-0000000000a1");

    private sealed class Montaje
    {
        public Montaje()
        {
            Unidad = new clsUnidadDeTrabajoFalsa();
            Repositorio = new clsAuditoriaRepositoryFalso(Unidad);
            Actual = new clsUsuarioActualFalso { Id = Guid.NewGuid(), Rol = clsRol.ADMIN, ClienteId = CLIENTE_ACTOR };
            Auditor = new clsAuditor(Repositorio, Actual, Contexto, Origen, new clsRelojFijo(AHORA));
        }

        public clsUnidadDeTrabajoFalsa Unidad { get; }

        public clsAuditoriaRepositoryFalso Repositorio { get; }

        public clsUsuarioActualFalso Actual { get; }

        public clsContextoSolicitudFalso Contexto { get; } = new() { Ip = "203.0.113.7", Dispositivo = "tablet-3" };

        public clsOrigenAuditoriaFalso Origen { get; } = new();

        public clsAuditor Auditor { get; }

        public async Task<clsAuditLog> UnicoAsync()
        {
            await Unidad.GuardarCambiosAsync();
            return Assert.Single(Repositorio.Registros);
        }
    }

    // ---------------------------------------------------------------------------------------- el registro

    [Fact]
    public async Task El_auditor_guarda_el_usuario_la_ip_el_dispositivo_el_detalle_y_el_viaje()
    {
        var m = new Montaje();
        var entidad = Guid.NewGuid();
        var viaje = Guid.NewGuid();

        m.Auditor.Registrar("viaje.actualizar", "viajes", entidad, detalle: new DetalleAuditoria(new { placa = "AAA111" }, new { placa = "BBB222" }), viajeId: viaje, clienteId: Guid.Empty);

        var registro = await m.UnicoAsync();
        Assert.Equal(m.Actual.Id, registro.UsuarioId);
        Assert.Equal("viaje.actualizar", registro.Accion);
        Assert.Equal("viajes", registro.Entidad);
        Assert.Equal(entidad, registro.EntidadId);
        Assert.Equal("203.0.113.7", registro.Ip);
        Assert.Equal("tablet-3", registro.Dispositivo);
        Assert.Equal(viaje, registro.ViajeId);
        Assert.Equal(AHORA, registro.CreatedAt);
        using var detalle = JsonDocument.Parse(registro.Detalle!);
        Assert.Equal("AAA111", detalle.RootElement.GetProperty("antes").GetProperty("placa").GetString());
        Assert.Equal("BBB222", detalle.RootElement.GetProperty("despues").GetProperty("placa").GetString());
    }

    [Fact]
    public async Task Sin_cliente_indicado_toma_el_del_usuario_que_actua_y_sin_detalle_queda_nulo()
    {
        var m = new Montaje();

        m.Auditor.Registrar("auth.login");

        var registro = await m.UnicoAsync();
        Assert.Equal(CLIENTE_ACTOR, registro.ClienteId);
        Assert.Null(registro.Detalle);
        Assert.Null(registro.ViajeId);
    }

    [Fact]
    public async Task Una_accion_del_sistema_no_tiene_ip_ni_dispositivo()
    {
        var m = new Montaje();
        m.Contexto.Ip = null;
        m.Contexto.Dispositivo = null;
        m.Actual.Id = null;
        m.Actual.ClienteId = null;

        m.Auditor.Registrar("sofasa.ingesta", "ordenes_sofasa");

        var registro = await m.UnicoAsync();
        Assert.Null(registro.UsuarioId);
        Assert.Null(registro.Ip);
        Assert.Null(registro.Dispositivo);
        Assert.Null(registro.ClienteId);
    }

    [Fact]
    public async Task Una_accion_de_un_viaje_toma_el_cliente_del_viaje_y_no_el_del_usuario_que_actua()
    {
        var m = new Montaje();
        var viaje = Guid.NewGuid();
        var delViaje = Guid.NewGuid();
        m.Origen.ClientesPorViaje[viaje] = delViaje;

        await m.Auditor.RegistrarDeViajeAsync("inspeccion.crear", "inspecciones", Guid.NewGuid(), viaje);

        var registro = await m.UnicoAsync();
        Assert.Equal(viaje, registro.ViajeId);
        Assert.Equal(delViaje, registro.ClienteId);
    }

    [Fact]
    public async Task Un_viaje_sin_cliente_conocido_cae_al_cliente_del_usuario_y_uno_indicado_manda()
    {
        var m = new Montaje();
        var indicado = Guid.NewGuid();

        await m.Auditor.RegistrarDeViajeAsync("viaje.crear", "viajes", Guid.NewGuid(), Guid.NewGuid());
        await m.Auditor.RegistrarDeViajeAsync("viaje.crear", "viajes", Guid.NewGuid(), Guid.NewGuid(), clienteId: indicado);

        await m.Unidad.GuardarCambiosAsync();
        Assert.Equal([CLIENTE_ACTOR, indicado], m.Repositorio.Registros.Select(r => r.ClienteId).ToList());
    }

    [Fact]
    public void El_registro_recorta_la_ip_y_el_dispositivo_a_lo_que_cabe_en_la_base()
    {
        var registro = clsAuditLog.Crear(null, "x", null, null, AHORA, new string('1', 80), new string('d', 400), null);

        Assert.Equal(clsAuditLog.IP_MAX, registro.Ip!.Length);
        Assert.Equal(clsAuditLog.DISPOSITIVO_MAX, registro.Dispositivo!.Length);
        Assert.Null(clsAuditLog.Crear(null, "x", null, null, AHORA, "  ", " ", "  ").Ip);
    }

    // ---------------------------------------------------------------------------------------- detalle sin secretos

    [Fact]
    public void El_detalle_se_escribe_en_snake_case_con_antes_y_despues_y_nulo_el_que_no_aplica()
    {
        var detalle = new DetalleAuditoria(null, new { TipoUnidadId = 7, CodigoMtrack = "MT-1", Vines = new[] { "A", "B" } });

        using var doc = JsonDocument.Parse(detalle.ATexto());

        Assert.Equal(["antes", "despues"], doc.RootElement.EnumerateObject().Select(p => p.Name).ToList());
        Assert.Equal(JsonValueKind.Null, doc.RootElement.GetProperty("antes").ValueKind);
        var despues = doc.RootElement.GetProperty("despues");
        Assert.Equal(7, despues.GetProperty("tipo_unidad_id").GetInt32());
        Assert.Equal("MT-1", despues.GetProperty("codigo_mtrack").GetString());
        Assert.Equal(["A", "B"], despues.GetProperty("vines").EnumerateArray().Select(v => v.GetString()).ToList());
    }

    [Fact]
    public void El_detalle_nunca_incluye_contrasenas_pin_tokens_ni_claves_aunque_se_pasen_por_descuido()
    {
        var detalle = new DetalleAuditoria(
            new { Nombre = "Luis", PasswordHash = "x", Password = "y", Pin = "1234", PinHash = "h", token_fcm = "t", RefreshToken = "r", ClaveSecreta = "c", TipoCredencial = "pin" },
            new Dictionary<string, object?> { ["password"] = "nueva", ["Contrasena"] = "z", ["secreto"] = "s", ["nombre"] = "Luis A.", ["anidado"] = new { pin = "9", ok = 1 } });

        var texto = detalle.ATexto();
        using var doc = JsonDocument.Parse(texto);

        var antes = doc.RootElement.GetProperty("antes");
        Assert.Equal(["nombre", "tipo_credencial"], antes.EnumerateObject().Select(p => p.Name).Order().ToList());
        var despues = doc.RootElement.GetProperty("despues");
        Assert.Equal(["anidado", "nombre"], despues.EnumerateObject().Select(p => p.Name).Order().ToList());
        Assert.Equal(["ok"], despues.GetProperty("anidado").EnumerateObject().Select(p => p.Name).ToList());
        foreach (var secreto in new[] { "1234", "\"y\"", "\"nueva\"", "\"h\"", "\"r\"", "\"s\"" })
        {
            Assert.DoesNotContain(secreto, texto);
        }
    }

    [Fact]
    public void La_marca_que_dice_si_debe_cambiar_el_pin_se_conserva_porque_no_es_el_pin()
    {
        var detalle = new DetalleAuditoria(new { DebeCambiarPin = false }, new { DebeCambiarPin = true, PinNuevo = "1234" });

        using var doc = JsonDocument.Parse(detalle.ATexto());

        Assert.False(doc.RootElement.GetProperty("antes").GetProperty("debe_cambiar_pin").GetBoolean());
        Assert.True(doc.RootElement.GetProperty("despues").GetProperty("debe_cambiar_pin").GetBoolean());
        Assert.False(doc.RootElement.GetProperty("despues").TryGetProperty("pin_nuevo", out _));
    }

    [Fact]
    public void Las_fotos_y_sus_hash_de_evidencia_no_se_confunden_con_secretos()
    {
        var detalle = new DetalleAuditoria(null, new { StoragePath = "a.jpg", HashSha256 = "abc", EsSello = true });

        using var doc = JsonDocument.Parse(detalle.ATexto());

        Assert.Equal("abc", doc.RootElement.GetProperty("despues").GetProperty("hash_sha256").GetString());
        Assert.Equal("a.jpg", doc.RootElement.GetProperty("despues").GetProperty("storage_path").GetString());
    }

    // ---------------------------------------------------------------------------------------- capacidad y baja logica

    [Theory]
    [InlineData(clsRol.ADMIN, true)]
    [InlineData(clsRol.SUPER_ADMIN, true)]
    [InlineData(clsRol.COORDINADOR_CALIDAD, false)]
    [InlineData(clsRol.COMERCIAL, false)]
    [InlineData(clsRol.INSPECTOR, false)]
    [InlineData(clsRol.CONDUCTOR, false)]
    public void Solo_el_administrador_y_el_super_administrador_ven_la_auditoria(string rol, bool esperado) =>
        Assert.Equal(esperado, clsCapacidades.De(rol).Contains(clsCapacidades.VER_AUDITORIA));

    [Fact]
    public void Un_vehiculo_nace_activo_y_la_baja_lo_inactiva_una_sola_vez()
    {
        var vehiculo = clsVehiculo.Crear("VIN-1", "Renault", null, null, clsOperacion.NACIONAL, Guid.NewGuid(), AHORA);
        Assert.True(vehiculo.Activo);

        vehiculo.DarDeBaja(AHORA.AddDays(1));
        var tras = vehiculo.UpdatedAt;
        vehiculo.DarDeBaja(AHORA.AddDays(2));

        Assert.False(vehiculo.Activo);
        Assert.Equal(AHORA.AddDays(1), tras);
        Assert.Equal(tras, vehiculo.UpdatedAt);
    }
}
