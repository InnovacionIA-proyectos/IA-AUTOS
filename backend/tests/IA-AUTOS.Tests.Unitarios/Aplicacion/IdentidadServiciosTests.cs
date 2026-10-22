using System.Text.Json;
using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Tests.Unitarios.Falsos;

namespace IAAutos.Tests.Unitarios.Aplicacion;

public class LoginConductorTests
{
    private const string CEDULA = "1234567";
    private const string PIN = "482915";

    [Fact]
    public async Task Cedula_y_pin_correctos_dan_un_token_con_el_rol_del_conductor_y_dejan_auditoria()
    {
        var c = new clsContexto();
        var u = c.SembrarConPin(CEDULA, PIN);

        var respuesta = await c.Auth().LoginConductorAsync(new LoginConductorRequest("1.234.567", PIN));

        Assert.Equal($"token-de-{u.Id}", respuesta.Token);
        Assert.Equal(clsRol.CONDUCTOR, respuesta.Usuario.Rol);
        Assert.Equal(CEDULA, respuesta.Usuario.Cedula);
        Assert.Null(respuesta.Usuario.Email);
        Assert.False(respuesta.Usuario.DebeCambiarPin);
        Assert.Equal(clsContexto.AHORA, u.UltimoAccesoAt);
        Assert.Equal("auth.login_conductor", Assert.Single(c.Auditoria.Registros).Accion);
    }

    [Fact]
    public async Task Un_inspector_sin_correo_tambien_entra_con_cedula_y_pin()
    {
        var c = new clsContexto();
        c.SembrarConPin(CEDULA, PIN, clsRol.INSPECTOR);

        var respuesta = await c.Auth().LoginConductorAsync(new LoginConductorRequest(CEDULA, PIN));

        Assert.Equal(clsRol.INSPECTOR, respuesta.Usuario.Rol);
    }

    [Fact]
    public async Task Un_pin_temporal_entra_pero_avisa_que_debe_cambiarse()
    {
        var c = new clsContexto();
        c.SembrarConPin(CEDULA, PIN, debeCambiar: true);

        var respuesta = await c.Auth().LoginConductorAsync(new LoginConductorRequest(CEDULA, PIN));

        Assert.True(respuesta.Usuario.DebeCambiarPin);
    }

    [Theory]
    [InlineData("1234567", "000999")]
    [InlineData("7654321", "482915")]
    [InlineData("no es cedula!", "482915")]
    public async Task Cedula_o_pin_incorrectos_dan_el_mismo_mensaje_exista_o_no_la_cedula(string cedula, string pin)
    {
        var c = new clsContexto();
        c.SembrarConPin(CEDULA, PIN);

        var ex = await Assert.ThrowsAsync<clsNoAutenticadoException>(() => c.Auth().LoginConductorAsync(new LoginConductorRequest(cedula, pin)));

        Assert.Equal("Cédula o PIN incorrectos", ex.Message);
    }

    [Fact]
    public async Task Un_pin_errado_cuenta_el_intento_y_lo_audita_pero_una_cedula_desconocida_no_deja_rastro()
    {
        var c = new clsContexto();
        var u = c.SembrarConPin(CEDULA, PIN);

        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => c.Auth().LoginConductorAsync(new LoginConductorRequest(CEDULA, "000999")));
        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => c.Auth().LoginConductorAsync(new LoginConductorRequest("9999999", "000999")));

        Assert.Equal(1, u.IntentosFallidos);
        var registro = Assert.Single(c.Auditoria.Registros);
        Assert.Equal("auth.login_conductor_fallido", registro.Accion);
        Assert.Equal(u.Id, registro.UsuarioId);
    }

    [Fact]
    public async Task Cinco_pines_errados_bloquean_la_cuenta_y_cada_intento_queda_en_la_auditoria()
    {
        var c = new clsContexto();
        var u = c.SembrarConPin(CEDULA, PIN);
        var auth = c.Auth();

        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<clsNoAutenticadoException>(() => auth.LoginConductorAsync(new LoginConductorRequest(CEDULA, "000999")));
        }

        var bloqueo = await Assert.ThrowsAsync<clsCuentaBloqueadaException>(() => auth.LoginConductorAsync(new LoginConductorRequest(CEDULA, "000999")));

        Assert.Equal(clsCodigosError.ACCOUNT_LOCKED, bloqueo.Codigo);
        var detalles = Assert.IsType<Dictionary<string, object>>(bloqueo.Detalles);
        Assert.Equal(clsContexto.AHORA.AddMinutes(15).UtcDateTime, detalles["bloqueado_hasta"]);
        Assert.Equal(5, c.Auditoria.Registros.Count(r => r.Accion == "auth.login_conductor_fallido"));
        Assert.Equal(1, c.Auditoria.Registros.Count(r => r.Accion == "auth.cuenta_bloqueada"));
        Assert.True(u.EstaBloqueado(clsContexto.AHORA));
    }

    [Fact]
    public async Task Bloqueada_la_cuenta_rechaza_hasta_el_pin_correcto_y_vuelve_a_servir_pasado_el_tiempo()
    {
        var c = new clsContexto();
        c.SembrarConPin(CEDULA, PIN);
        var auth = c.Auth();
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAnyAsync<clsExcepcionDominio>(() => auth.LoginConductorAsync(new LoginConductorRequest(CEDULA, "000999")));
        }

        await Assert.ThrowsAsync<clsCuentaBloqueadaException>(() => auth.LoginConductorAsync(new LoginConductorRequest(CEDULA, PIN)));

        c.Reloj.Ahora = clsContexto.AHORA.AddMinutes(16);
        var respuesta = await auth.LoginConductorAsync(new LoginConductorRequest(CEDULA, PIN));
        Assert.Equal(clsRol.CONDUCTOR, respuesta.Usuario.Rol);
    }

    [Fact]
    public async Task Un_ingreso_correcto_reinicia_el_contador_de_intentos()
    {
        var c = new clsContexto();
        var u = c.SembrarConPin(CEDULA, PIN);
        var auth = c.Auth();
        for (var i = 0; i < 4; i++)
        {
            await Assert.ThrowsAsync<clsNoAutenticadoException>(() => auth.LoginConductorAsync(new LoginConductorRequest(CEDULA, "000999")));
        }

        await auth.LoginConductorAsync(new LoginConductorRequest(CEDULA, PIN));

        Assert.Equal(0, u.IntentosFallidos);
        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => auth.LoginConductorAsync(new LoginConductorRequest(CEDULA, "000999")));
        Assert.Equal(1, u.IntentosFallidos);
    }

    [Fact]
    public async Task Un_usuario_retirado_no_ingresa_y_sus_pines_errados_no_se_cuentan()
    {
        var c = new clsContexto();
        var u = c.SembrarConPin(CEDULA, PIN, activo: false);

        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => c.Auth().LoginConductorAsync(new LoginConductorRequest(CEDULA, "000999")));
        var ex = await Assert.ThrowsAsync<clsNoAutenticadoException>(() => c.Auth().LoginConductorAsync(new LoginConductorRequest(CEDULA, PIN)));

        Assert.StartsWith("Usuario retirado", ex.Message);
        Assert.Equal(0, u.IntentosFallidos);
    }

    [Fact]
    public async Task Quien_entra_con_contrasena_no_puede_entrar_por_cedula_y_quien_entra_con_pin_no_puede_por_correo()
    {
        var c = new clsContexto();
        var conClave = clsUsuario.Crear("Ines", "ines@ejemplo.com", clsRol.INSPECTOR, null, c.Contrasenas.Hashear("Clave-Segura-1"), clsContexto.AHORA, "7654321");
        c.Usuarios.Agregar(conClave);
        var conPin = clsUsuario.CrearConPin("Luis", "luis@ejemplo.com", clsRol.CONDUCTOR, null, CEDULA, c.Contrasenas.Hashear(PIN), clsContexto.AHORA);
        c.Usuarios.Agregar(conPin);

        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => c.Auth().LoginConductorAsync(new LoginConductorRequest("7654321", "Clave-Segura-1")));
        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => c.Auth().LoginAsync(new LoginRequest("luis@ejemplo.com", PIN)));
    }

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "x")]
    [InlineData("1234567", null)]
    [InlineData("1234567", "")]
    public async Task Cedula_o_pin_vacios_son_error_de_validacion(string? cedula, string? pin)
    {
        var c = new clsContexto();

        var ex = await Assert.ThrowsAsync<clsValidacionException>(() => c.Auth().LoginConductorAsync(new LoginConductorRequest(cedula, pin)));

        Assert.Equal("Cédula y PIN son obligatorios", ex.Message);
    }
}

public class CambiarPinTests
{
    private const string CEDULA = "1234567";
    private const string PIN = "482915";

    [Fact]
    public async Task Cambiar_el_pin_temporal_lo_libera_y_el_nuevo_pin_es_el_que_sirve()
    {
        var c = new clsContexto();
        var u = c.SembrarConPin(CEDULA, PIN, debeCambiar: true);

        await c.Auth().CambiarPinAsync(u.Id, new CambiarPinRequest(PIN, "730194"));

        Assert.False(u.DebeCambiarPin);
        Assert.True(c.Contrasenas.Verificar("730194", u.PinHash));
        Assert.False(c.Contrasenas.Verificar(PIN, u.PinHash));
        Assert.Equal("auth.cambiar_pin", Assert.Single(c.Auditoria.Registros).Accion);
    }

    [Fact]
    public async Task Un_pin_actual_errado_es_error_del_campo_y_cuenta_como_intento_fallido()
    {
        var c = new clsContexto();
        var u = c.SembrarConPin(CEDULA, PIN, debeCambiar: true);

        var ex = await Assert.ThrowsAsync<clsValidacionException>(() => c.Auth().CambiarPinAsync(u.Id, new CambiarPinRequest("000999", "730194")));

        Assert.Equal("Datos inválidos", ex.Message);
        Assert.Equal(1, u.IntentosFallidos);
        Assert.True(u.DebeCambiarPin);
    }

    [Fact]
    public async Task El_pin_nuevo_debe_ser_distinto_al_actual_y_no_trivial()
    {
        var c = new clsContexto();
        var u = c.SembrarConPin(CEDULA, PIN, debeCambiar: true);

        await Assert.ThrowsAsync<clsValidacionException>(() => c.Auth().CambiarPinAsync(u.Id, new CambiarPinRequest(PIN, PIN)));
        await Assert.ThrowsAsync<clsValidacionException>(() => c.Auth().CambiarPinAsync(u.Id, new CambiarPinRequest(PIN, "1111")));
        await Assert.ThrowsAsync<clsValidacionException>(() => c.Auth().CambiarPinAsync(u.Id, new CambiarPinRequest(PIN, "12ab")));

        Assert.True(u.DebeCambiarPin);
    }

    [Fact]
    public async Task Quien_entra_con_contrasena_no_tiene_pin_que_cambiar()
    {
        var c = new clsContexto();
        var u = c.Sembrar("ana@ejemplo.com", "Clave-Segura-1");

        await Assert.ThrowsAsync<clsConflictoException>(() => c.Auth().CambiarPinAsync(u.Id, new CambiarPinRequest(PIN, "730194")));
    }

    [Fact]
    public async Task Con_la_cuenta_bloqueada_no_se_puede_cambiar_el_pin()
    {
        var c = new clsContexto();
        var u = c.SembrarConPin(CEDULA, PIN);
        for (var i = 0; i < 5; i++)
        {
            u.RegistrarFalloDePin(clsContexto.AHORA, 5, 15);
        }

        await Assert.ThrowsAsync<clsCuentaBloqueadaException>(() => c.Auth().CambiarPinAsync(u.Id, new CambiarPinRequest(PIN, "730194")));
    }
}

public class PerfilTests
{
    [Fact]
    public async Task El_perfil_trae_cedula_cliente_y_capacidades_del_rol()
    {
        var c = new clsContexto();
        var u = c.SembrarConPin("1234567", "482915", clsRol.CONDUCTOR);

        var perfil = await c.Auth().ObtenerPerfilAsync(u.Id);

        Assert.Equal("1234567", perfil.Cedula);
        Assert.Equal(clsTipoCredencial.PIN, perfil.TipoCredencial);
        Assert.Equal(new ClienteDto(clsCatalogosLecturaFalso.SOFASA, "sofasa", "Sofasa"), perfil.Cliente);
        Assert.Contains(clsCapacidades.REGISTRAR_INSPECCIONES, perfil.Capacidades);
        Assert.DoesNotContain(clsCapacidades.GESTIONAR_USUARIOS, perfil.Capacidades);
    }

    [Fact]
    public async Task El_super_administrador_no_tiene_cliente_y_puede_gestionar_usuarios()
    {
        var c = new clsContexto();
        var u = c.Sembrar("root@ejemplo.com", "Clave-Segura-1", clsRol.SUPER_ADMIN);

        var perfil = await c.Auth().ObtenerPerfilAsync(u.Id);

        Assert.Null(perfil.Cliente);
        Assert.Contains(clsCapacidades.GESTIONAR_USUARIOS, perfil.Capacidades);
    }
}

public class GestionDeUsuariosTests
{
    private static clsContexto ComoActor(string rol, Guid? clienteId = null)
    {
        var c = new clsContexto();
        c.Actual.Id = Guid.NewGuid();
        c.Actual.Rol = rol;
        c.Actual.ClienteId = rol == clsRol.SUPER_ADMIN ? null : clienteId ?? clsCatalogosLecturaFalso.SOFASA;
        return c;
    }

    private static Dictionary<string, JsonElement> Campos(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    [Theory]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    public async Task Admin_y_coordinador_crean_un_conductor_con_cedula_y_pin_temporal_en_su_cliente(string rolActor)
    {
        var c = ComoActor(rolActor);

        var dto = await c.ServicioUsuarios().CrearAsync(new CrearUsuarioRequest("Luis Gomez", null, clsRol.CONDUCTOR, null, null, "1.234.567"));

        Assert.Null(dto.Email);
        Assert.Equal("1234567", dto.Cedula);
        Assert.Equal(clsTipoCredencial.PIN, dto.TipoCredencial);
        Assert.True(dto.DebeCambiarPin);
        Assert.Equal(clsCatalogosLecturaFalso.SOFASA, dto.ClienteId);
        Assert.Equal(clsPin.LONGITUD_GENERADO, dto.PinTemporal!.Length);
        var guardado = Assert.Single(c.Usuarios.Usuarios);
        Assert.True(c.Contrasenas.Verificar(dto.PinTemporal, guardado.PinHash));
        Assert.Equal("usuario.crear", Assert.Single(c.Auditoria.Registros).Accion);
    }

    [Fact]
    public async Task Con_un_pin_enviado_no_se_devuelve_y_aun_asi_debe_cambiarse()
    {
        var c = ComoActor(clsRol.ADMIN);

        var dto = await c.ServicioUsuarios().CrearAsync(new CrearUsuarioRequest("Ines", null, clsRol.INSPECTOR, "nacional", null, "7654321", "730194"));

        Assert.Null(dto.PinTemporal);
        Assert.True(dto.DebeCambiarPin);
        Assert.True(c.Contrasenas.Verificar("730194", c.Usuarios.Usuarios.Single().PinHash));
    }

    [Fact]
    public async Task El_inspector_con_correo_y_contrasena_se_crea_como_siempre()
    {
        var c = ComoActor(clsRol.COORDINADOR_CALIDAD);

        var dto = await c.ServicioUsuarios().CrearAsync(new CrearUsuarioRequest("Ines", "ines@ejemplo.com", clsRol.INSPECTOR, null, "Clave-Segura-1"));

        Assert.Equal(clsTipoCredencial.PASSWORD, dto.TipoCredencial);
        Assert.Null(dto.PinTemporal);
        Assert.False(dto.DebeCambiarPin);
    }

    [Theory]
    [InlineData(clsRol.ADMIN, null, "Cédula")]
    [InlineData(clsRol.COMERCIAL, null, "contraseña")]
    public async Task Los_validadores_piden_cedula_o_correo_segun_el_rol(string rol, string? password, string campoEnMensaje)
    {
        var c = ComoActor(clsRol.ADMIN);

        var ex = await Assert.ThrowsAsync<clsValidacionException>(() => c.ServicioUsuarios().CrearAsync(new CrearUsuarioRequest("X", null, rol, null, password)));

        var campos = Assert.IsType<Dictionary<string, string[]>>(Assert.IsType<Dictionary<string, object>>(ex.Detalles)["fieldErrors"]);
        Assert.Contains(campos.Values.SelectMany(m => m), m => m.Contains(campoEnMensaje, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Un_conductor_exige_cedula_aunque_tenga_contrasena()
    {
        var c = ComoActor(clsRol.ADMIN);

        await Assert.ThrowsAsync<clsValidacionException>(() =>
            c.ServicioUsuarios().CrearAsync(new CrearUsuarioRequest("Luis", "luis@ejemplo.com", clsRol.CONDUCTOR, null, "Clave-Segura-1")));
    }

    [Theory]
    [InlineData(clsRol.ADMIN)]
    [InlineData(clsRol.COORDINADOR_CALIDAD)]
    public async Task Solo_el_super_administrador_asigna_el_nivel_super_administrador(string rolActor)
    {
        var c = ComoActor(rolActor);
        var solicitud = new CrearUsuarioRequest("Root", "root@ejemplo.com", clsRol.SUPER_ADMIN, null, "Clave-Segura-1");

        var ex = await Assert.ThrowsAsync<clsProhibidoException>(() => c.ServicioUsuarios().CrearAsync(solicitud));

        Assert.Equal("Solo un super administrador puede asignar el nivel super administrador", ex.Message);
        Assert.Empty(c.Usuarios.Usuarios);

        var super = ComoActor(clsRol.SUPER_ADMIN);
        var creado = await super.ServicioUsuarios().CrearAsync(solicitud);
        Assert.Equal(clsRol.SUPER_ADMIN, creado.Rol);
        Assert.Null(creado.ClienteId);
    }

    [Fact]
    public async Task Un_admin_no_puede_ascender_a_nadie_a_super_administrador_ni_ve_a_uno()
    {
        var c = ComoActor(clsRol.ADMIN);
        var inspector = c.Sembrar("ines@ejemplo.com", "x", clsRol.INSPECTOR);
        inspector.AsignarCliente(clsCatalogosLecturaFalso.SOFASA, clsContexto.AHORA);
        var super = c.Sembrar("root@ejemplo.com", "x", clsRol.SUPER_ADMIN);

        await Assert.ThrowsAsync<clsProhibidoException>(() => c.ServicioUsuarios().ActualizarAsync(inspector.Id, Campos("{\"rol\":\"super_admin\"}")));
        // El super administrador no pertenece a ningun cliente, asi que para los demas ni siquiera existe.
        await Assert.ThrowsAsync<clsEntidadNoEncontradaException>(() => c.ServicioUsuarios().ActualizarAsync(super.Id, Campos("{\"nombre\":\"Otro\"}")));
        await Assert.ThrowsAsync<clsEntidadNoEncontradaException>(() => c.ServicioUsuarios().EliminarAsync(super.Id));
    }

    [Fact]
    public async Task Quien_no_es_super_administrador_no_ve_ni_modifica_a_los_de_otro_cliente()
    {
        var c = ComoActor(clsRol.ADMIN);
        var ajeno = c.Sembrar("ajeno@ejemplo.com", "x", clsRol.INSPECTOR);
        ajeno.AsignarCliente(clsCatalogosLecturaFalso.OTRO, clsContexto.AHORA);
        var propio = c.Sembrar("propio@ejemplo.com", "x", clsRol.INSPECTOR);
        propio.AsignarCliente(clsCatalogosLecturaFalso.SOFASA, clsContexto.AHORA);

        var lista = await c.ServicioUsuarios().ListarAsync(new FiltrosUsuariosRequest(null, null, null, null, null, null));
        var basica = await c.ServicioUsuarios().ListarBasicoAsync();

        Assert.Equal(["propio@ejemplo.com"], lista.Items.Select(u => u.Email));
        Assert.Equal([propio.Id], basica.Select(u => u.Id));
        await Assert.ThrowsAsync<clsEntidadNoEncontradaException>(() => c.ServicioUsuarios().ActualizarAsync(ajeno.Id, Campos("{\"nombre\":\"X\"}")));
        await Assert.ThrowsAsync<clsEntidadNoEncontradaException>(() => c.ServicioUsuarios().EliminarAsync(ajeno.Id));
        await Assert.ThrowsAsync<clsEntidadNoEncontradaException>(() => c.ServicioUsuarios().AsignarPinAsync(ajeno.Id, new AsignarPinRequest(null)));
    }

    [Fact]
    public async Task Los_demas_no_pueden_crear_en_otro_cliente_pero_el_super_administrador_si_elige_el_cliente()
    {
        var admin = ComoActor(clsRol.ADMIN);
        await Assert.ThrowsAsync<clsProhibidoException>(() => admin.ServicioUsuarios().CrearAsync(
            new CrearUsuarioRequest("Luis", null, clsRol.CONDUCTOR, null, null, "1234567", null, clsCatalogosLecturaFalso.OTRO)));

        var super = ComoActor(clsRol.SUPER_ADMIN);
        var dto = await super.ServicioUsuarios().CrearAsync(
            new CrearUsuarioRequest("Luis", null, clsRol.CONDUCTOR, null, null, "1234567", null, clsCatalogosLecturaFalso.OTRO));
        Assert.Equal(clsCatalogosLecturaFalso.OTRO, dto.ClienteId);

        await Assert.ThrowsAsync<clsValidacionException>(() => super.ServicioUsuarios().CrearAsync(
            new CrearUsuarioRequest("Ana", null, clsRol.CONDUCTOR, null, null, "7654321", null, Guid.NewGuid())));
    }

    [Fact]
    public async Task Patch_con_subtipo_nulo_lo_limpia_y_sin_el_campo_lo_conserva()
    {
        var c = ComoActor(clsRol.ADMIN);
        var u = clsUsuario.Crear("Ines", "ines@ejemplo.com", clsRol.INSPECTOR, "nacional", "h", clsContexto.AHORA, clienteId: clsCatalogosLecturaFalso.SOFASA);
        c.Usuarios.Agregar(u);

        var sinCampo = await c.ServicioUsuarios().ActualizarAsync(u.Id, Campos("{\"nombre\":\"Ines Perez\"}"));
        Assert.Equal("nacional", sinCampo.SubtipoInspector);

        var conNulo = await c.ServicioUsuarios().ActualizarAsync(u.Id, Campos("{\"subtipo_inspector\":null}"));
        Assert.Null(conNulo.SubtipoInspector);
        Assert.Equal("Ines Perez", conNulo.Nombre);
    }

    [Fact]
    public async Task Patch_rechaza_nulos_donde_no_caben_y_campos_desconocidos()
    {
        var c = ComoActor(clsRol.ADMIN);
        var u = clsUsuario.Crear("Ines", "ines@ejemplo.com", clsRol.INSPECTOR, null, "h", clsContexto.AHORA, clienteId: clsCatalogosLecturaFalso.SOFASA);
        c.Usuarios.Agregar(u);

        await Assert.ThrowsAsync<clsValidacionException>(() => c.ServicioUsuarios().ActualizarAsync(u.Id, Campos("{\"nombre\":null}")));
        await Assert.ThrowsAsync<clsValidacionException>(() => c.ServicioUsuarios().ActualizarAsync(u.Id, Campos("{\"activo\":null}")));
        await Assert.ThrowsAsync<clsValidacionException>(() => c.ServicioUsuarios().ActualizarAsync(u.Id, Campos("{\"pin_hash\":\"x\"}")));
        await Assert.ThrowsAsync<clsValidacionException>(() => c.ServicioUsuarios().ActualizarAsync(u.Id, Campos("{\"password\":\"corta\"}")));
    }

    [Fact]
    public async Task Solo_el_super_administrador_cambia_el_cliente_de_un_usuario()
    {
        var admin = ComoActor(clsRol.ADMIN);
        var u = clsUsuario.Crear("Ines", "ines@ejemplo.com", clsRol.INSPECTOR, null, "h", clsContexto.AHORA, clienteId: clsCatalogosLecturaFalso.SOFASA);
        admin.Usuarios.Agregar(u);

        await Assert.ThrowsAsync<clsProhibidoException>(() =>
            admin.ServicioUsuarios().ActualizarAsync(u.Id, Campos($"{{\"cliente_id\":\"{clsCatalogosLecturaFalso.OTRO}\"}}")));

        var super = ComoActor(clsRol.SUPER_ADMIN);
        super.Usuarios.Usuarios.Add(u);
        var dto = await super.ServicioUsuarios().ActualizarAsync(u.Id, Campos($"{{\"cliente_id\":\"{clsCatalogosLecturaFalso.OTRO}\"}}"));
        Assert.Equal(clsCatalogosLecturaFalso.OTRO, dto.ClienteId);
        await Assert.ThrowsAsync<clsValidacionException>(() => super.ServicioUsuarios().ActualizarAsync(u.Id, Campos("{\"cliente_id\":null}")));
    }

    [Fact]
    public async Task No_se_puede_desactivar_el_propio_usuario()
    {
        var c = ComoActor(clsRol.ADMIN);
        var yo = clsUsuario.Crear("Yo", "yo@ejemplo.com", clsRol.ADMIN, null, "h", clsContexto.AHORA, clienteId: clsCatalogosLecturaFalso.SOFASA);
        c.Usuarios.Agregar(yo);
        c.Actual.Id = yo.Id;

        await Assert.ThrowsAsync<clsConflictoException>(() => c.ServicioUsuarios().ActualizarAsync(yo.Id, Campos("{\"activo\":false}")));
    }

    [Fact]
    public async Task La_busqueda_filtra_por_texto_rol_y_estado_y_pagina()
    {
        var c = ComoActor(clsRol.SUPER_ADMIN);
        for (var i = 1; i <= 5; i++)
        {
            c.SembrarConPin($"100000{i}", "482915", clsRol.CONDUCTOR);
        }

        c.Sembrar("ines@ejemplo.com", "x", clsRol.INSPECTOR);
        c.SembrarConPin("2000001", "482915", clsRol.CONDUCTOR, activo: false);

        var servicio = c.ServicioUsuarios();
        var porCedula = await servicio.ListarAsync(new FiltrosUsuariosRequest("1000003", null, null, null, null, null));
        var conductoresActivos = await servicio.ListarAsync(new FiltrosUsuariosRequest(null, "conductor", "true", null, null, null));
        var paginaDos = await servicio.ListarAsync(new FiltrosUsuariosRequest(null, "conductor", null, null, "2", "4"));
        var porCorreo = await servicio.ListarAsync(new FiltrosUsuariosRequest("ines@", null, null, null, null, null));

        Assert.Equal(["1000003"], porCedula.Items.Select(u => u.Cedula));
        Assert.Equal(5, conductoresActivos.Total);
        Assert.Equal(6, paginaDos.Total);
        Assert.Equal(2, paginaDos.Items.Count);
        Assert.Equal(2, paginaDos.Pagina);
        Assert.Equal(4, paginaDos.Tamano);
        Assert.Single(porCorreo.Items);
    }

    [Fact]
    public async Task Los_filtros_invalidos_son_error_de_validacion_y_el_tamano_de_pagina_se_acota()
    {
        var c = ComoActor(clsRol.SUPER_ADMIN);
        var servicio = c.ServicioUsuarios();

        await Assert.ThrowsAsync<clsValidacionException>(() => servicio.ListarAsync(new FiltrosUsuariosRequest(null, "gerente", "tal", "no-guid", null, null)));

        var enorme = await servicio.ListarAsync(new FiltrosUsuariosRequest(null, null, null, null, "-3", "99999"));
        Assert.Equal(1, enorme.Pagina);
        Assert.Equal(clsUsuariosServiceLimites.MAXIMO, enorme.Tamano);
    }

    [Fact]
    public async Task Asignar_pin_genera_uno_temporal_obliga_a_cambiarlo_y_levanta_el_bloqueo()
    {
        var c = ComoActor(clsRol.ADMIN);
        var u = c.SembrarConPin("1234567", "482915");
        for (var i = 0; i < 5; i++)
        {
            u.RegistrarFalloDePin(clsContexto.AHORA, 5, 15);
        }

        var dto = await c.ServicioUsuarios().AsignarPinAsync(u.Id, new AsignarPinRequest(null));

        Assert.True(dto.DebeCambiarPin);
        Assert.Null(dto.BloqueadoHasta);
        Assert.True(c.Contrasenas.Verificar(dto.PinTemporal!, u.PinHash));
        Assert.Equal("usuario.pin_asignar", Assert.Single(c.Auditoria.Registros).Accion);
    }

    [Fact]
    public async Task Asignar_un_pin_a_un_administrador_o_sin_cedula_es_error_y_un_pin_trivial_tambien()
    {
        var c = ComoActor(clsRol.ADMIN);
        var admin = clsUsuario.Crear("A", "a@ejemplo.com", clsRol.COMERCIAL, null, "h", clsContexto.AHORA, "7654321", clsCatalogosLecturaFalso.SOFASA);
        c.Usuarios.Agregar(admin);
        var conductor = c.SembrarConPin("1234567", "482915");

        await Assert.ThrowsAsync<clsValidacionException>(() => c.ServicioUsuarios().AsignarPinAsync(admin.Id, new AsignarPinRequest(null)));
        await Assert.ThrowsAsync<clsValidacionException>(() => c.ServicioUsuarios().AsignarPinAsync(conductor.Id, new AsignarPinRequest("1111")));
    }

    [Fact]
    public async Task Forzar_el_cambio_de_pin_y_desbloquear_dejan_auditoria()
    {
        var c = ComoActor(clsRol.COORDINADOR_CALIDAD);
        var u = c.SembrarConPin("1234567", "482915");
        for (var i = 0; i < 5; i++)
        {
            u.RegistrarFalloDePin(clsContexto.AHORA, 5, 15);
        }

        var forzado = await c.ServicioUsuarios().ForzarCambioDePinAsync(u.Id);
        var desbloqueado = await c.ServicioUsuarios().DesbloquearAsync(u.Id);

        Assert.True(forzado.DebeCambiarPin);
        Assert.Null(desbloqueado.BloqueadoHasta);
        Assert.Equal(["usuario.pin_forzar_cambio", "usuario.desbloquear"], c.Auditoria.Registros.Select(r => r.Accion));
    }

    [Fact]
    public async Task Pasar_a_un_usuario_de_super_administrador_a_otro_rol_le_asigna_un_cliente()
    {
        var c = ComoActor(clsRol.SUPER_ADMIN);
        var u = c.Sembrar("root@ejemplo.com", "x", clsRol.SUPER_ADMIN);

        var dto = await c.ServicioUsuarios().ActualizarAsync(u.Id, Campos("{\"rol\":\"admin\"}"));

        Assert.Equal(clsCatalogosLecturaFalso.SOFASA, dto.ClienteId);
    }
}

/// <summary>Limites de pagina del servicio de usuarios (para no repetir el numero en las pruebas).</summary>
internal static class clsUsuariosServiceLimites
{
    public const int MAXIMO = IAAutos.Aplicacion.Comun.clsPaginacion.TAMANO_MAXIMO;
}

public class CargaConductoresTests
{
    private static clsContexto ComoAdmin()
    {
        var c = new clsContexto();
        c.Actual.Id = Guid.NewGuid();
        c.Actual.Rol = clsRol.ADMIN;
        c.Actual.ClienteId = clsCatalogosLecturaFalso.SOFASA;
        return c;
    }

    private static CargarConductoresRequest Filas(params (string? nombre, string? cedula, string? email)[] filas) =>
        new(filas.Select((f, i) => new FilaCargaConductorRequest(i + 2, f.nombre, f.cedula, f.email)).ToList());

    [Fact]
    public async Task La_vista_previa_valida_cada_fila_y_no_guarda_nada()
    {
        var c = ComoAdmin();
        c.SembrarConPin("1000001", "482915");

        var r = await c.CargaConductores().PrevisualizarAsync(Filas(
            ("Ana Ruiz", "2000001", null),
            ("", "2000002", null),
            ("Sin Cedula", null, null),
            ("Repetido", "1000001", null),
            ("Correo Malo", "2000003", "no-es-correo"),
            ("Otra Ana", "2.000.001", null)));

        Assert.False(r.Confirmado);
        Assert.Equal(6, r.Total);
        Assert.Equal(1, r.Validas);
        Assert.Equal(5, r.Invalidas);
        Assert.Equal([true, false, false, false, false, false], r.Filas.Select(f => f.Valida));
        Assert.Contains("nombre", r.Filas[1].Errores[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Ya existe", r.Filas[3].Errores[0]);
        Assert.Contains("repetida en el archivo", r.Filas[5].Errores[0]);
        Assert.Single(c.Usuarios.Usuarios);
        Assert.All(r.Filas, f => Assert.Null(f.PinTemporal));
        Assert.Equal(2, r.Filas[0].Fila);
    }

    [Fact]
    public async Task Al_confirmar_crea_un_conductor_por_fila_valida_con_su_pin_temporal_y_su_cliente()
    {
        var c = ComoAdmin();

        var r = await c.CargaConductores().CargarAsync(Filas(
            ("Ana Ruiz", "2000001", "ANA@ejemplo.com"),
            ("Mal", null, null),
            ("Luis Gomez", "2000002", null)));

        Assert.True(r.Confirmado);
        Assert.Equal(2, r.Creados);
        Assert.Equal(1, r.Invalidas);
        Assert.Equal(2, c.Usuarios.Usuarios.Count);
        foreach (var fila in r.Filas.Where(f => f.Valida))
        {
            var usuario = c.Usuarios.Usuarios.Single(u => u.Id == fila.UsuarioId);
            Assert.Equal(clsRol.CONDUCTOR, usuario.Rol);
            Assert.Equal(clsTipoCredencial.PIN, usuario.TipoCredencial);
            Assert.True(usuario.DebeCambiarPin);
            Assert.Equal(clsCatalogosLecturaFalso.SOFASA, usuario.ClienteId);
            Assert.True(c.Contrasenas.Verificar(fila.PinTemporal!, usuario.PinHash));
        }

        Assert.Equal("ana@ejemplo.com", c.Usuarios.Usuarios.Single(u => u.Cedula == "2000001").Email);
        Assert.Equal(2, c.Auditoria.Registros.Count(a => a.Accion == "usuario.crear"));
        Assert.Contains(c.Auditoria.Registros, a => a.Accion == "usuarios.cargar_conductores");
    }

    [Fact]
    public async Task Sin_filas_o_con_demasiadas_es_error_de_validacion()
    {
        var c = ComoAdmin();

        await Assert.ThrowsAsync<clsValidacionException>(() => c.CargaConductores().PrevisualizarAsync(new CargarConductoresRequest([])));
        await Assert.ThrowsAsync<clsValidacionException>(() => c.CargaConductores().PrevisualizarAsync(new CargarConductoresRequest(null)));
        var demasiadas = Enumerable.Range(0, 501).Select(i => new FilaCargaConductorRequest(i + 2, "N", $"30{i:00000}", null)).ToList();
        await Assert.ThrowsAsync<clsValidacionException>(() => c.CargaConductores().CargarAsync(new CargarConductoresRequest(demasiadas)));
    }

    [Fact]
    public async Task Si_una_cedula_choca_justo_al_guardar_esa_fila_queda_con_el_error_y_las_demas_siguen()
    {
        var c = ComoAdmin();
        c.UnidadDeTrabajo.FallarEnLaProximaConfirmacion(new clsConflictoException("Ya existe un usuario con esa cédula"));

        var r = await c.CargaConductores().CargarAsync(Filas(("Ana Ruiz", "2000001", null), ("Luis Gomez", "2000002", null)));

        Assert.Equal(1, r.Creados);
        Assert.Equal("Ya existe un usuario con esa cédula", Assert.Single(r.Filas[0].Errores));
        Assert.True(r.Filas[1].Valida);
        Assert.NotNull(r.Filas[1].UsuarioId);
        Assert.Null(r.Filas[0].UsuarioId);
    }
}
