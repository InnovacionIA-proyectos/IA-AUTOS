using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Infraestructura.Seguridad;
using IAAutos.Tests.Unitarios.Falsos;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IAAutos.Tests.Unitarios.Aplicacion;

public sealed class SesionPortalTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly ContextoCliente CLIENTE = new("10.0.0.5", "Mozilla/5.0 (pruebas)");

    private readonly clsUnidadDeTrabajoFalsa _uow = new();
    private readonly clsRelojFijo _reloj = new(AHORA);
    private readonly clsRefreshTokenRepositoryFalso _refrescos = new();
    private readonly clsPortalUsuariosFalso _portal = new();
    private readonly clsOpcionesSesion _sesion = new();
    private readonly clsOpcionesPortalAplicacion _opcionesPortal = new();
    private readonly clsUsuarioRepositoryFalso _usuarios;
    private readonly clsAuditoriaRepositoryFalso _auditoria;
    private readonly clsServicioContrasenas _contrasenas = new(Options.Create(new clsOpcionesContrasenas { Iteraciones = 1000 }));

    public SesionPortalTests()
    {
        _usuarios = new clsUsuarioRepositoryFalso(_uow);
        _auditoria = new clsAuditoriaRepositoryFalso(_uow);
    }

    private clsSesionPortalService Servicio() => new(
        _usuarios,
        _refrescos,
        _portal,
        _contrasenas,
        new clsEmisorTokenFalso(),
        _uow,
        new clsAuditor(_auditoria, new clsUsuarioActualFalso(), _reloj),
        _reloj,
        Options.Create(_sesion),
        Options.Create(_opcionesPortal),
        NullLogger<clsSesionPortalService>.Instance);

    private static List<LoginPortalUsuarioDto> Solicitud(long id, string? rolReclamado = null) =>
        [new(id, "jperez", rolReclamado is null ? null : [new LoginPortalPermisoDto("53", rolReclamado, "56", rolReclamado)])];

    private void PortalConUsuario(long id, string? email, params string[] roles) =>
        _portal.Usuarios[id] = new clsUsuarioPortal(id, "jperez", email, "Juan Pérez", roles);

    private clsUsuario SembrarLocal(string email, string rol = clsRol.INSPECTOR, long? portalId = null, bool activo = true)
    {
        var usuario = clsUsuario.Crear("Local " + email, email, rol, null, "hash", AHORA);
        if (portalId is not null)
        {
            usuario.VincularPortal(portalId.Value, AHORA);
        }

        if (!activo)
        {
            usuario.Retirar(AHORA);
        }

        _usuarios.Usuarios.Add(usuario);
        return usuario;
    }

    // ------------------------------------------------------------------------------------------- login-portal

    [Fact]
    public async Task Un_usuario_vinculado_con_rol_de_IA_Autos_entra_y_el_refresh_solo_se_guarda_como_hash()
    {
        var local = SembrarLocal("jperez@empresa.com", clsRol.ADMIN, portalId: 123);
        PortalConUsuario(123, "jperez@empresa.com", "AdminIAAUTOS");

        var sesion = await Servicio().LoginPortalAsync(Solicitud(123), CLIENTE);

        Assert.Equal(local.Id, sesion.User.Id);
        Assert.Equal(clsRol.ADMIN, sesion.User.Rol);
        Assert.False(string.IsNullOrEmpty(sesion.Access));
        var guardado = Assert.Single(_refrescos.Tokens);
        Assert.NotEqual(sesion.RefreshPlano, guardado.TokenHash);
        Assert.Equal(clsTokensRefresh.Hash(sesion.RefreshPlano), guardado.TokenHash);
        Assert.Equal(AHORA.AddDays(30), sesion.RefreshExpiraAt);
        Assert.Equal("10.0.0.5", guardado.Ip);
        Assert.Contains(_auditoria.Registros, r => r.Accion == "auth.login_portal");
    }

    [Fact]
    public async Task Los_permisos_que_envia_el_cliente_se_ignoran_y_el_rol_sale_de_TI()
    {
        SembrarLocal("jperez@empresa.com", clsRol.INSPECTOR, portalId: 123);
        PortalConUsuario(123, "jperez@empresa.com", "InspectorIAAUTOS");

        // El navegador dice que es AdminIAAUTOS; TI dice que es inspector.
        var sesion = await Servicio().LoginPortalAsync(Solicitud(123, rolReclamado: "AdminIAAUTOS"), CLIENTE);

        Assert.Equal(clsRol.INSPECTOR, sesion.User.Rol);
        Assert.Equal(1, _portal.Consultas);
    }

    [Fact]
    public async Task Si_no_existe_en_IA_Autos_pero_tiene_rol_en_el_portal_se_registra_con_ese_rol()
    {
        PortalConUsuario(321, "Nuevo@Empresa.com", "ComercialIAAUTOS");

        var sesion = await Servicio().LoginPortalAsync(Solicitud(321), CLIENTE);

        var creado = Assert.Single(_usuarios.Usuarios);
        Assert.Equal("nuevo@empresa.com", creado.Email);
        Assert.Equal(clsRol.COMERCIAL, creado.Rol);
        Assert.Equal(321, creado.PortalUsuarioId);
        Assert.Equal("Juan Pérez", creado.Nombre);
        Assert.Equal(creado.Id, sesion.User.Id);
        Assert.NotNull(creado.PasswordHash);
        Assert.Contains(_auditoria.Registros, r => r.Accion == "usuario.crear_desde_portal");
    }

    [Fact]
    public async Task Una_cuenta_existente_con_el_mismo_correo_se_vincula_y_no_se_duplica()
    {
        var existente = SembrarLocal("jperez@empresa.com", clsRol.INSPECTOR);
        PortalConUsuario(123, "JPerez@Empresa.com", "AdminIAAUTOS");

        var sesion = await Servicio().LoginPortalAsync(Solicitud(123), CLIENTE);

        Assert.Single(_usuarios.Usuarios);
        Assert.Equal(existente.Id, sesion.User.Id);
        Assert.Equal(123, existente.PortalUsuarioId);
        Assert.Equal(clsRol.ADMIN, existente.Rol);
    }

    [Fact]
    public async Task Si_el_rol_del_portal_cambio_el_rol_local_se_sincroniza_en_el_siguiente_ingreso()
    {
        var local = SembrarLocal("jperez@empresa.com", clsRol.INSPECTOR, portalId: 123);
        PortalConUsuario(123, "jperez@empresa.com", "SuperAdminIAAUTOS");

        var sesion = await Servicio().LoginPortalAsync(Solicitud(123), CLIENTE);

        Assert.Equal(clsRol.SUPER_ADMIN, local.Rol);
        Assert.Equal(clsRol.SUPER_ADMIN, sesion.User.Rol);
    }

    [Fact]
    public async Task Con_varios_roles_de_IA_Autos_gana_el_de_mayor_privilegio()
    {
        PortalConUsuario(5, "multi@empresa.com", "InspectorIAAUTOS", "AdminIAAUTOS", "ComercialIAAUTOS");

        var sesion = await Servicio().LoginPortalAsync(Solicitud(5), CLIENTE);

        Assert.Equal(clsRol.ADMIN, sesion.User.Rol);
    }

    [Theory]
    [InlineData("AdminEmbarQA")]
    [InlineData("ConductorIAAUTOS")]
    [InlineData("IAAUTOS")]
    [InlineData("PerfilDesconocidoIAAUTOS")]
    public async Task Sin_un_rol_de_IA_Autos_responde_acceso_no_habilitado_y_no_crea_nada(string rolPortal)
    {
        PortalConUsuario(9, "otro@empresa.com", rolPortal);

        var error = await Assert.ThrowsAsync<clsPortalUsuarioNoVinculadoException>(() => Servicio().LoginPortalAsync(Solicitud(9), CLIENTE));

        Assert.Equal(clsCodigosError.PORTAL_USER_NOT_LINKED, error.Codigo);
        Assert.Empty(_usuarios.Usuarios);
        Assert.Empty(_refrescos.Tokens);
        Assert.Contains(_auditoria.Registros, r => r.Accion == "auth.login_portal_rechazado");
    }

    [Fact]
    public async Task Un_id_que_no_existe_en_el_portal_responde_acceso_no_habilitado()
    {
        await Assert.ThrowsAsync<clsPortalUsuarioNoVinculadoException>(() => Servicio().LoginPortalAsync(Solicitud(999), CLIENTE));
    }

    [Fact]
    public async Task Un_usuario_retirado_en_IA_Autos_no_vuelve_a_entrar_aunque_siga_vigente_en_el_portal()
    {
        SembrarLocal("jperez@empresa.com", clsRol.ADMIN, portalId: 123, activo: false);
        PortalConUsuario(123, "jperez@empresa.com", "AdminIAAUTOS");

        await Assert.ThrowsAsync<clsPortalUsuarioNoVinculadoException>(() => Servicio().LoginPortalAsync(Solicitud(123), CLIENTE));
        Assert.Empty(_refrescos.Tokens);
    }

    [Fact]
    public async Task Una_cuenta_vinculada_a_otra_persona_del_portal_no_se_entrega_aunque_coincida_el_correo()
    {
        SembrarLocal("jperez@empresa.com", clsRol.ADMIN, portalId: 111);
        PortalConUsuario(222, "jperez@empresa.com", "AdminIAAUTOS");

        await Assert.ThrowsAsync<clsPortalUsuarioNoVinculadoException>(() => Servicio().LoginPortalAsync(Solicitud(222), CLIENTE));
    }

    [Fact]
    public async Task Sin_correo_en_el_portal_no_se_crea_la_cuenta_salvo_que_haya_dominio_configurado()
    {
        PortalConUsuario(40, null, "AdminIAAUTOS");
        await Assert.ThrowsAsync<clsPortalUsuarioNoVinculadoException>(() => Servicio().LoginPortalAsync(Solicitud(40), CLIENTE));

        _opcionesPortal.DominioCorreo = "empresa.com";
        var sesion = await Servicio().LoginPortalAsync(Solicitud(40), CLIENTE);

        Assert.Equal("jperez@empresa.com", sesion.User.Email);
    }

    [Fact]
    public async Task Si_TI_no_responde_no_se_concede_acceso_y_se_informa_el_servicio_no_disponible()
    {
        SembrarLocal("jperez@empresa.com", clsRol.ADMIN, portalId: 123);
        _portal.Falla = true;

        var error = await Assert.ThrowsAsync<clsServicioNoDisponibleException>(() => Servicio().LoginPortalAsync(Solicitud(123), CLIENTE));

        Assert.Equal(clsCodigosError.DB_UNAVAILABLE, error.Codigo);
        Assert.Empty(_refrescos.Tokens);
    }

    [Fact]
    public async Task Sin_base_de_TI_configurada_solo_entran_los_ids_ya_vinculados()
    {
        _portal.Disponible = false;
        SembrarLocal("jperez@empresa.com", clsRol.ADMIN, portalId: 123);

        var sesion = await Servicio().LoginPortalAsync(Solicitud(123), CLIENTE);
        Assert.Equal(clsRol.ADMIN, sesion.User.Rol);
        Assert.Equal(0, _portal.Consultas);

        await Assert.ThrowsAsync<clsPortalUsuarioNoVinculadoException>(() => Servicio().LoginPortalAsync(Solicitud(456), CLIENTE));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0L)]
    public async Task Un_cuerpo_sin_id_valido_es_un_error_de_validacion(long? id)
    {
        await Assert.ThrowsAsync<clsValidacionException>(() => Servicio().LoginPortalAsync([new(id, "x", null)], CLIENTE));
        await Assert.ThrowsAsync<clsValidacionException>(() => Servicio().LoginPortalAsync([], CLIENTE));
        await Assert.ThrowsAsync<clsValidacionException>(() => Servicio().LoginPortalAsync(null, CLIENTE));
    }

    // ----------------------------------------------------------------------------------------------- refresh

    private async Task<SesionEmitida> IniciarSesionAsync()
    {
        SembrarLocal("jperez@empresa.com", clsRol.ADMIN, portalId: 123);
        PortalConUsuario(123, "jperez@empresa.com", "AdminIAAUTOS");
        return await Servicio().LoginPortalAsync(Solicitud(123), CLIENTE);
    }

    [Fact]
    public async Task El_refresh_rota_el_token_y_el_anterior_queda_revocado_apuntando_al_nuevo()
    {
        var inicial = await IniciarSesionAsync();
        _reloj.Ahora = AHORA.AddMinutes(61);

        var renovada = await Servicio().RefrescarAsync(inicial.RefreshPlano, CLIENTE);

        Assert.NotEqual(inicial.RefreshPlano, renovada.RefreshPlano);
        Assert.Equal(2, _refrescos.Tokens.Count);
        var anterior = _refrescos.Tokens.Single(t => t.TokenHash == clsTokensRefresh.Hash(inicial.RefreshPlano));
        var nuevo = _refrescos.Tokens.Single(t => t.TokenHash == clsTokensRefresh.Hash(renovada.RefreshPlano));
        Assert.NotNull(anterior.RevocadoAt);
        Assert.Equal(nuevo.Id, anterior.ReemplazadoPorId);
        Assert.Equal(anterior.FamiliaId, nuevo.FamiliaId);
        Assert.Null(nuevo.RevocadoAt);
    }

    [Fact]
    public async Task Reusar_un_refresh_ya_rotado_responde_401_y_pasada_la_gracia_revoca_toda_la_sesion()
    {
        var inicial = await IniciarSesionAsync();
        var renovada = await Servicio().RefrescarAsync(inicial.RefreshPlano, CLIENTE);
        var familia = _refrescos.Tokens[0].FamiliaId;
        _reloj.Ahora = AHORA.AddMinutes(5);

        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => Servicio().RefrescarAsync(inicial.RefreshPlano, CLIENTE));

        // El token nuevo tambien murio: se asumio robo de la sesion.
        Assert.False(await _refrescos.FamiliaActivaAsync(familia, _reloj.Ahora));
        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => Servicio().RefrescarAsync(renovada.RefreshPlano, CLIENTE));
        Assert.Contains(_auditoria.Registros, r => r.Accion == "auth.refresh_reuso");
    }

    [Fact]
    public async Task Dentro_de_la_gracia_un_refresh_rotado_da_401_pero_no_revoca_la_sesion()
    {
        var inicial = await IniciarSesionAsync();
        var renovada = await Servicio().RefrescarAsync(inicial.RefreshPlano, CLIENTE);
        var familia = _refrescos.Tokens[0].FamiliaId;
        _reloj.Ahora = AHORA.AddSeconds(3);

        // Dos pestañas refrescaron a la vez con la misma cookie: la perdedora recibe 401 sin castigar a la ganadora.
        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => Servicio().RefrescarAsync(inicial.RefreshPlano, CLIENTE));

        Assert.True(await _refrescos.FamiliaActivaAsync(familia, _reloj.Ahora));
        var siguiente = await Servicio().RefrescarAsync(renovada.RefreshPlano, CLIENTE);
        Assert.False(string.IsNullOrEmpty(siguiente.Access));
    }

    [Fact]
    public async Task Un_refresh_vencido_o_desconocido_responde_401_y_uno_ausente_es_un_error_de_validacion()
    {
        var inicial = await IniciarSesionAsync();
        await Assert.ThrowsAsync<clsValidacionException>(() => Servicio().RefrescarAsync(null, CLIENTE));
        await Assert.ThrowsAsync<clsValidacionException>(() => Servicio().RefrescarAsync("  ", CLIENTE));
        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => Servicio().RefrescarAsync("no-existe", CLIENTE));

        _reloj.Ahora = AHORA.AddDays(31);
        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => Servicio().RefrescarAsync(inicial.RefreshPlano, CLIENTE));
    }

    [Fact]
    public async Task Si_el_usuario_fue_retirado_el_refresh_no_renueva_y_la_sesion_se_revoca()
    {
        var inicial = await IniciarSesionAsync();
        _usuarios.Usuarios.Single().Retirar(AHORA);

        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => Servicio().RefrescarAsync(inicial.RefreshPlano, CLIENTE));

        Assert.False(await _refrescos.FamiliaActivaAsync(_refrescos.Tokens[0].FamiliaId, AHORA));
    }

    [Fact]
    public async Task Los_tiempos_de_la_sesion_son_configurables()
    {
        _sesion.RefreshDias = 7;
        var sesion = await IniciarSesionAsync();

        Assert.Equal(AHORA.AddDays(7), sesion.RefreshExpiraAt);
    }

    // ------------------------------------------------------------------------------------------------ logout

    [Fact]
    public async Task El_logout_revoca_la_sesion_completa_por_la_cookie()
    {
        var inicial = await IniciarSesionAsync();
        var renovada = await Servicio().RefrescarAsync(inicial.RefreshPlano, CLIENTE);
        var familia = _refrescos.Tokens[0].FamiliaId;

        await Servicio().CerrarSesionAsync(renovada.RefreshPlano, familiaDelAccess: null);

        Assert.False(await _refrescos.FamiliaActivaAsync(familia, AHORA));
        await Assert.ThrowsAsync<clsNoAutenticadoException>(() => Servicio().RefrescarAsync(renovada.RefreshPlano, CLIENTE));
        Assert.Contains(_auditoria.Registros, r => r.Accion == "auth.logout");
    }

    [Fact]
    public async Task El_logout_tambien_revoca_por_la_sesion_del_access_token()
    {
        await IniciarSesionAsync();
        var familia = _refrescos.Tokens[0].FamiliaId;

        await Servicio().CerrarSesionAsync(refreshPlano: null, familiaDelAccess: familia);

        Assert.False(await _refrescos.FamiliaActivaAsync(familia, AHORA));
    }

    [Fact]
    public async Task El_logout_sin_sesion_no_falla()
    {
        await Servicio().CerrarSesionAsync(null, null);
        await Servicio().CerrarSesionAsync("desconocido", Guid.NewGuid());
    }
}

public sealed class RolesPortalTests
{
    [Theory]
    [InlineData("AdminIAAUTOS", "admin")]
    [InlineData("SuperAdminIAAUTOS", "super_admin")]
    [InlineData("InspectorIAAUTOS", "inspector")]
    [InlineData("CoordinadorCalidadIAAUTOS", "coordinador_calidad")]
    [InlineData("ComercialIAAUTOS", "comercial")]
    [InlineData("admin iaautos", "admin")]
    [InlineData("Super_Admin-IAAUTOS", "super_admin")]
    [InlineData("  Coordinador Calidad IA-AUTOS  ", "coordinador_calidad")]
    public void Reconoce_el_nombre_del_rol_sin_importar_mayusculas_tildes_ni_separadores(string rolPortal, string esperado)
    {
        Assert.Equal(esperado, clsRolesPortal.Resolver([rolPortal]));
    }

    [Theory]
    [InlineData("AdminEmbarQA")]
    [InlineData("ConductorIAAUTOS")]
    [InlineData("Embarcador")]
    [InlineData("IAAUTOSAdmin")]
    [InlineData("")]
    [InlineData(null)]
    public void Ignora_los_roles_que_no_son_de_IA_Autos(string? rolPortal)
    {
        Assert.Null(clsRolesPortal.Resolver([rolPortal]));
    }

    [Fact]
    public void La_precedencia_es_super_admin_admin_coordinador_comercial_inspector()
    {
        var todos = new[] { "InspectorIAAUTOS", "ComercialIAAUTOS", "CoordinadorCalidadIAAUTOS", "AdminIAAUTOS", "SuperAdminIAAUTOS" };

        Assert.Equal(
            ["super_admin", "admin", "coordinador_calidad", "comercial", "inspector"],
            clsRolesPortal.Coincidencias(todos));
        Assert.Equal("super_admin", clsRolesPortal.Resolver(todos));
    }

    [Fact]
    public void Los_tokens_de_refresh_son_aleatorios_largos_y_solo_se_guardan_como_hash()
    {
        var (plano1, hash1) = clsTokensRefresh.Generar();
        var (plano2, _) = clsTokensRefresh.Generar();

        Assert.NotEqual(plano1, plano2);
        Assert.True(plano1.Length >= 43);
        Assert.DoesNotContain('=', plano1);
        Assert.Equal(64, hash1.Length);
        Assert.Equal(hash1, clsTokensRefresh.Hash(plano1));
    }
}
