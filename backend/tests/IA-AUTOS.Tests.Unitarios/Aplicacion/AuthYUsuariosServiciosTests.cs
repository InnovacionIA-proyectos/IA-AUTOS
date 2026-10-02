using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Auth.DTOAuth;
using IAAutos.Aplicacion.Auth.Validators;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Usuarios;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Aplicacion.Usuarios.Validators;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Infraestructura.Seguridad;
using IAAutos.Tests.Unitarios.Falsos;
using Microsoft.Extensions.Options;

namespace IAAutos.Tests.Unitarios.Aplicacion;

internal sealed class clsContexto
{
    public static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    public clsContexto()
    {
        UnidadDeTrabajo = new clsUnidadDeTrabajoFalsa();
        Usuarios = new clsUsuarioRepositoryFalso(UnidadDeTrabajo);
        Auditoria = new clsAuditoriaRepositoryFalso(UnidadDeTrabajo);
    }

    public clsUsuarioRepositoryFalso Usuarios { get; }

    public clsAuditoriaRepositoryFalso Auditoria { get; }

    public clsUnidadDeTrabajoFalsa UnidadDeTrabajo { get; }

    public clsUsuarioActualFalso Actual { get; } = new();

    public clsRelojFijo Reloj { get; } = new(AHORA);

    public clsServicioContrasenas Contrasenas { get; } = new(Options.Create(new clsOpcionesContrasenas { Iteraciones = 1000 }));

    private readonly IOptions<clsOpcionesSeguridad> _seguridad = Options.Create(new clsOpcionesSeguridad { PasswordMinimo = 8 });

    public clsAuthService Auth() => new(Usuarios, Contrasenas, new clsEmisorTokenFalso(), UnidadDeTrabajo,
        new clsAuditor(Auditoria, Actual, Reloj), Reloj, new LoginRequestValidator());

    public clsUsuariosService ServicioUsuarios() => new(Usuarios, Contrasenas, UnidadDeTrabajo,
        new clsAuditor(Auditoria, Actual, Reloj), Actual, Reloj,
        new CrearUsuarioRequestValidator(_seguridad), new ActualizarUsuarioRequestValidator(_seguridad));

    public clsUsuario Sembrar(string email, string password, string rol = clsRol.INSPECTOR, bool activo = true, string? hash = null)
    {
        var usuario = clsUsuario.Crear("Usuario " + email, email, rol, null, hash ?? Contrasenas.Hashear(password), AHORA);
        if (!activo)
        {
            usuario.Retirar(AHORA);
        }

        Usuarios.Agregar(usuario);
        return usuario;
    }
}

public class AuthServiceTests
{
    [Fact]
    public async Task Login_correcto_devuelve_token_y_usuario_y_audita()
    {
        var c = new clsContexto();
        var u = c.Sembrar("ana@ejemplo.com", "Clave-Segura-1", clsRol.ADMIN);

        var respuesta = await c.Auth().LoginAsync(new LoginRequest("  ANA@ejemplo.com ", "Clave-Segura-1"));

        Assert.Equal($"token-de-{u.Id}", respuesta.Token);
        Assert.Equal(new UsuarioAutenticadoDto(u.Id, u.Nombre, "ana@ejemplo.com", clsRol.ADMIN), respuesta.Usuario);
        var registro = Assert.Single(c.Auditoria.Registros);
        Assert.Equal("auth.login", registro.Accion);
        Assert.Equal(u.Id, registro.UsuarioId);
        Assert.Equal(1, c.UnidadDeTrabajo.Confirmaciones);
    }

    [Theory]
    [InlineData("ana@ejemplo.com", "incorrecta")]
    [InlineData("noexiste@ejemplo.com", "Clave-Segura-1")]
    public async Task Credenciales_invalidas_dan_el_mismo_mensaje_generico_y_no_auditan(string email, string password)
    {
        var c = new clsContexto();
        c.Sembrar("ana@ejemplo.com", "Clave-Segura-1");

        var ex = await Assert.ThrowsAsync<clsNoAutenticadoException>(() => c.Auth().LoginAsync(new LoginRequest(email, password)));

        Assert.Equal("Correo o contraseña incorrectos", ex.Message);
        Assert.Equal(clsCodigosError.UNAUTHORIZED, ex.Codigo);
        Assert.Empty(c.Auditoria.Registros);
        Assert.Equal(0, c.UnidadDeTrabajo.Confirmaciones);
    }

    [Fact]
    public async Task Usuario_retirado_no_ingresa_aunque_la_clave_sea_correcta()
    {
        var c = new clsContexto();
        c.Sembrar("retirado@ejemplo.com", "Clave-Segura-1", activo: false);

        var ex = await Assert.ThrowsAsync<clsNoAutenticadoException>(() => c.Auth().LoginAsync(new LoginRequest("retirado@ejemplo.com", "Clave-Segura-1")));

        Assert.Equal("Usuario retirado: no tiene acceso. Contacta al administrador.", ex.Message);
    }

    [Theory]
    [InlineData(null, "x")]
    [InlineData("", "x")]
    [InlineData("a@b.co", null)]
    [InlineData("a@b.co", "")]
    public async Task Correo_o_clave_vacios_son_error_de_validacion_con_el_mensaje_original(string? email, string? password)
    {
        var c = new clsContexto();

        var ex = await Assert.ThrowsAsync<clsValidacionException>(() => c.Auth().LoginAsync(new LoginRequest(email, password)));

        Assert.Equal("Correo y contraseña son obligatorios", ex.Message);
        Assert.Null(ex.Detalles);
    }

    [Fact]
    public async Task Hash_scrypt_heredado_se_acepta_y_se_regenera_en_el_formato_vigente()
    {
        var c = new clsContexto();
        const string HASH_NODE = "scrypt:0102030405060708090a0b0c0d0e0f10:936c45fb5f22fb4ecda5400f1bbfa07b387f01b202839ac365449e69b750093f640ad4b15bf730c4e021efb6c3649f22a95482b9353ff6096229d8aaa3b552f5";
        var u = c.Sembrar("viejo@ejemplo.com", "x", hash: HASH_NODE);

        await c.Auth().LoginAsync(new LoginRequest("viejo@ejemplo.com", "x"));

        Assert.StartsWith("pbkdf2-sha256$", u.PasswordHash);
        Assert.True(c.Contrasenas.Verificar("x", u.PasswordHash));
    }

    [Fact]
    public async Task Perfil_de_un_usuario_inexistente_es_sesion_no_valida()
    {
        var c = new clsContexto();

        var ex = await Assert.ThrowsAsync<clsNoAutenticadoException>(() => c.Auth().ObtenerPerfilAsync(Guid.NewGuid()));

        Assert.Equal("Sesión no válida", ex.Message);
    }

    [Fact]
    public async Task Perfil_devuelve_los_datos_del_usuario()
    {
        var c = new clsContexto();
        var u = c.Sembrar("ana@ejemplo.com", "Clave-Segura-1", clsRol.CONDUCTOR);

        var perfil = await c.Auth().ObtenerPerfilAsync(u.Id);

        Assert.Equal(clsRol.CONDUCTOR, perfil.Rol);
    }
}

public class UsuariosServiceTests
{
    [Fact]
    public async Task Crear_normaliza_hashea_y_audita()
    {
        var c = new clsContexto();
        c.Actual.Id = Guid.NewGuid();

        var dto = await c.ServicioUsuarios().CrearAsync(new CrearUsuarioRequest("  Luis  ", " LUIS@Ejemplo.com ", clsRol.CONDUCTOR, null, "Clave-Segura-1"));

        Assert.Equal("Luis", dto.Nombre);
        Assert.Equal("luis@ejemplo.com", dto.Email);
        Assert.True(dto.Activo);
        var guardado = Assert.Single(c.Usuarios.Usuarios);
        Assert.NotEqual("Clave-Segura-1", guardado.PasswordHash);
        Assert.True(c.Contrasenas.Verificar("Clave-Segura-1", guardado.PasswordHash));
        Assert.Equal("usuario.crear", Assert.Single(c.Auditoria.Registros).Accion);
        Assert.Equal(c.Actual.Id, c.Auditoria.Registros[0].UsuarioId);
    }

    [Fact]
    public async Task Crear_con_datos_invalidos_devuelve_los_errores_por_campo_como_la_api_original()
    {
        var c = new clsContexto();

        var ex = await Assert.ThrowsAsync<clsValidacionException>(() =>
            c.ServicioUsuarios().CrearAsync(new CrearUsuarioRequest(" ", "no-es-correo", "gerente", "raro", "corta")));

        Assert.Equal("Datos inválidos", ex.Message);
        var detalle = Assert.IsType<Dictionary<string, object>>(ex.Detalles);
        var campos = Assert.IsType<Dictionary<string, string[]>>(detalle["fieldErrors"]);
        Assert.Equal(["nombre", "email", "rol", "subtipo_inspector", "password"], campos.Keys.ToArray());
        Assert.Equal("La contraseña debe tener al menos 8 caracteres", campos["password"][0]);
        Assert.Empty(c.Usuarios.Usuarios);
        Assert.Equal(0, c.UnidadDeTrabajo.Confirmaciones);
    }

    [Fact]
    public async Task Actualizar_cambia_solo_lo_enviado_y_rehashea_la_clave()
    {
        var c = new clsContexto();
        var u = c.Sembrar("ana@ejemplo.com", "Clave-Segura-1", clsRol.INSPECTOR);
        var hashAnterior = u.PasswordHash;

        var dto = await c.ServicioUsuarios().ActualizarAsync(u.Id, new ActualizarUsuarioRequest(null, null, clsRol.ADMIN, null, "Otra-Clave-22", false));

        Assert.Equal(clsRol.ADMIN, dto.Rol);
        Assert.False(dto.Activo);
        Assert.Equal("ana@ejemplo.com", dto.Email);
        Assert.NotEqual(hashAnterior, u.PasswordHash);
        Assert.True(c.Contrasenas.Verificar("Otra-Clave-22", u.PasswordHash));
    }

    [Fact]
    public async Task Actualizar_un_usuario_inexistente_es_no_encontrado()
    {
        var c = new clsContexto();

        var ex = await Assert.ThrowsAsync<clsEntidadNoEncontradaException>(() =>
            c.ServicioUsuarios().ActualizarAsync(Guid.NewGuid(), new ActualizarUsuarioRequest("X", null, null, null, null, null)));

        Assert.Equal("Usuario no encontrado", ex.Message);
    }

    [Fact]
    public async Task No_se_puede_eliminar_el_propio_usuario()
    {
        var c = new clsContexto();
        var u = c.Sembrar("ana@ejemplo.com", "Clave-Segura-1", clsRol.SUPER_ADMIN);
        c.Actual.Id = u.Id;

        var ex = await Assert.ThrowsAsync<clsConflictoException>(() => c.ServicioUsuarios().EliminarAsync(u.Id));

        Assert.Equal("No puedes eliminar tu propio usuario", ex.Message);
        Assert.Contains(u, c.Usuarios.Usuarios);
    }

    [Fact]
    public async Task Usuario_sin_historial_se_elimina_de_verdad()
    {
        var c = new clsContexto();
        var u = c.Sembrar("libre@ejemplo.com", "Clave-Segura-1");
        c.Actual.Id = Guid.NewGuid();

        var respuesta = await c.ServicioUsuarios().EliminarAsync(u.Id);

        Assert.Equal("eliminado", respuesta.Modo);
        Assert.Empty(c.Usuarios.Usuarios);
        Assert.Equal("usuario.eliminar", Assert.Single(c.Auditoria.Registros).Accion);
    }

    [Fact]
    public async Task Usuario_con_historial_se_retira_para_conservar_sus_operaciones()
    {
        var c = new clsContexto();
        var u = c.Sembrar("activo@ejemplo.com", "Clave-Segura-1");
        c.Usuarios.ConHistorial.Add(u.Id);
        c.Actual.Id = Guid.NewGuid();

        var respuesta = await c.ServicioUsuarios().EliminarAsync(u.Id);

        Assert.Equal("retirado", respuesta.Modo);
        Assert.False(u.Activo);
        Assert.Contains(u, c.Usuarios.Usuarios);
        Assert.Equal("usuario.retirar", Assert.Single(c.Auditoria.Registros).Accion);
    }

    [Fact]
    public async Task Si_aparece_historial_durante_el_borrado_se_cae_a_la_baja_logica()
    {
        var c = new clsContexto();
        var u = c.Sembrar("carrera@ejemplo.com", "Clave-Segura-1");
        c.Actual.Id = Guid.NewGuid();
        c.UnidadDeTrabajo.FallarEnLaProximaConfirmacion(new clsConflictoException("El registro tiene operaciones asociadas y no puede eliminarse"));

        var respuesta = await c.ServicioUsuarios().EliminarAsync(u.Id);

        Assert.Equal("retirado", respuesta.Modo);
        Assert.False(u.Activo);
        Assert.Equal(1, c.UnidadDeTrabajo.Descartes);
    }

    [Fact]
    public async Task Listar_devuelve_los_usuarios_ordenados_por_nombre()
    {
        var c = new clsContexto();
        c.Sembrar("zeta@ejemplo.com", "Clave-Segura-1");
        c.Sembrar("alfa@ejemplo.com", "Clave-Segura-1");

        var lista = await c.ServicioUsuarios().ListarAsync();

        Assert.Equal(["alfa@ejemplo.com", "zeta@ejemplo.com"], lista.Select(u => u.Email));
    }
}
