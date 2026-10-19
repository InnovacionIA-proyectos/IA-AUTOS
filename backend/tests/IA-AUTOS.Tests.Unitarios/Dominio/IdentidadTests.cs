using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;

namespace IAAutos.Tests.Unitarios.Dominio;

public class PinTests
{
    [Theory]
    [InlineData("4829")]
    [InlineData("  730194 ")]
    [InlineData("84920173")]
    public void Acepta_pin_numerico_de_4_a_8_digitos(string pin) => Assert.Equal(pin.Trim(), clsPin.Validar(pin));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("123456789")]
    [InlineData("48a9")]
    [InlineData("48 29")]
    public void Rechaza_pin_vacio_corto_largo_o_con_letras(string? pin) =>
        Assert.Throws<clsValidacionException>(() => clsPin.Validar(pin));

    [Theory]
    [InlineData("1111")]
    [InlineData("000000")]
    [InlineData("1234")]
    [InlineData("987654")]
    public void Rechaza_pin_con_digitos_repetidos_o_en_secuencia(string pin) =>
        Assert.Throws<clsValidacionException>(() => clsPin.Validar(pin));

    [Fact]
    public void El_pin_generado_tiene_6_digitos_y_no_es_trivial()
    {
        for (var i = 0; i < 200; i++)
        {
            var pin = clsPin.Generar();
            Assert.Equal(clsPin.LONGITUD_GENERADO, pin.Length);
            Assert.Equal(pin, clsPin.Validar(pin));
        }
    }
}

public class UsuarioIdentidadTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid CLIENTE = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");

    private static clsUsuario ConductorConPin(string? email = null) =>
        clsUsuario.CrearConPin("Luis Gomez", email, clsRol.CONDUCTOR, null, "1.234.567", "hash-pin", AHORA, CLIENTE);

    [Fact]
    public void Un_conductor_con_pin_no_necesita_correo_y_su_pin_nace_temporal()
    {
        var u = ConductorConPin();

        Assert.Null(u.Email);
        Assert.Equal("1234567", u.Cedula);
        Assert.Equal(clsTipoCredencial.PIN, u.TipoCredencial);
        Assert.True(u.DebeCambiarPin);
        Assert.Equal(0, u.IntentosFallidos);
        Assert.Null(u.BloqueadoHasta);
        Assert.Equal(CLIENTE, u.ClienteId);
    }

    [Fact]
    public void Con_pin_la_cedula_es_obligatoria_y_solo_conductor_o_inspector()
    {
        Assert.Throws<clsValidacionException>(() => clsUsuario.CrearConPin("X", null, clsRol.CONDUCTOR, null, null, "h", AHORA));
        Assert.Throws<clsValidacionException>(() => clsUsuario.CrearConPin("X", null, clsRol.ADMIN, null, "1234567", "h", AHORA));
        Assert.Throws<clsValidacionException>(() => clsUsuario.CrearConPin("X", "no-es-correo", clsRol.INSPECTOR, null, "1234567", "h", AHORA));
        Assert.Equal(clsRol.INSPECTOR, clsUsuario.CrearConPin("X", null, clsRol.INSPECTOR, "nacional", "1234567", "h", AHORA).Rol);
    }

    [Fact]
    public void Con_contrasena_el_correo_es_obligatorio_y_el_conductor_exige_cedula()
    {
        Assert.Throws<clsValidacionException>(() => clsUsuario.Crear("X", "", clsRol.ADMIN, null, "h", AHORA));
        Assert.Throws<clsValidacionException>(() => clsUsuario.Crear("X", "x@y.co", clsRol.CONDUCTOR, null, "h", AHORA));
        Assert.Equal(clsTipoCredencial.PASSWORD, clsUsuario.Crear("X", "x@y.co", clsRol.CONDUCTOR, null, "h", AHORA, "1234567").TipoCredencial);
    }

    [Fact]
    public void El_super_administrador_no_pertenece_a_ningun_cliente()
    {
        var u = clsUsuario.Crear("Root", "root@ejemplo.com", clsRol.SUPER_ADMIN, null, "h", AHORA, clienteId: CLIENTE);

        Assert.Null(u.ClienteId);
    }

    [Fact]
    public void Cinco_pines_errados_bloquean_quince_minutos_y_reinician_el_contador()
    {
        var u = ConductorConPin();

        for (var i = 0; i < 4; i++)
        {
            Assert.False(u.RegistrarFalloDePin(AHORA, 5, 15));
        }

        Assert.Equal(4, u.IntentosFallidos);
        Assert.False(u.EstaBloqueado(AHORA));

        Assert.True(u.RegistrarFalloDePin(AHORA, 5, 15));

        Assert.Equal(AHORA.AddMinutes(15), u.BloqueadoHasta);
        Assert.Equal(0, u.IntentosFallidos);
        Assert.True(u.EstaBloqueado(AHORA.AddMinutes(14)));
        Assert.False(u.EstaBloqueado(AHORA.AddMinutes(15)));
    }

    [Fact]
    public void Pasado_el_bloqueo_el_contador_empieza_de_cero()
    {
        var u = ConductorConPin();
        for (var i = 0; i < 5; i++)
        {
            u.RegistrarFalloDePin(AHORA, 5, 15);
        }

        var despues = AHORA.AddMinutes(20);
        Assert.False(u.RegistrarFalloDePin(despues, 5, 15));

        Assert.Equal(1, u.IntentosFallidos);
        Assert.Null(u.BloqueadoHasta);
    }

    [Fact]
    public void Un_ingreso_correcto_limpia_intentos_y_bloqueo_y_marca_el_ultimo_acceso()
    {
        var u = ConductorConPin();
        u.RegistrarFalloDePin(AHORA, 5, 15);

        u.RegistrarAcceso(AHORA.AddMinutes(1));

        Assert.Equal(0, u.IntentosFallidos);
        Assert.Equal(AHORA.AddMinutes(1), u.UltimoAccesoAt);
    }

    [Fact]
    public void Asignar_pin_levanta_el_bloqueo_y_obliga_a_cambiarlo_y_cambiarlo_lo_libera()
    {
        var u = ConductorConPin();
        u.CambiarPin("hash-nuevo", AHORA);
        Assert.False(u.DebeCambiarPin);
        for (var i = 0; i < 5; i++)
        {
            u.RegistrarFalloDePin(AHORA, 5, 15);
        }

        u.AsignarPin("hash-reiniciado", debeCambiar: true, AHORA);

        Assert.True(u.DebeCambiarPin);
        Assert.Null(u.BloqueadoHasta);
        Assert.Equal("hash-reiniciado", u.PinHash);
    }

    [Fact]
    public void Forzar_cambio_de_pin_solo_aplica_a_quien_entra_con_pin()
    {
        var conContrasena = clsUsuario.Crear("X", "x@y.co", clsRol.INSPECTOR, null, "h", AHORA);
        Assert.Throws<clsValidacionException>(() => conContrasena.ForzarCambioDePin(AHORA));

        var u = ConductorConPin();
        u.CambiarPin("h2", AHORA);
        u.ForzarCambioDePin(AHORA);
        Assert.True(u.DebeCambiarPin);
    }

    [Fact]
    public void Asignar_pin_a_un_inspector_con_contrasena_lo_pasa_a_entrar_con_pin_y_descarta_la_contrasena()
    {
        var u = clsUsuario.Crear("Ines", "ines@ejemplo.com", clsRol.INSPECTOR, null, "hash-pass", AHORA, "7654321");

        u.AsignarPin("hash-pin", true, AHORA);

        Assert.Equal(clsTipoCredencial.PIN, u.TipoCredencial);
        Assert.Null(u.PasswordHash);
        Assert.Equal("ines@ejemplo.com", u.Email);
    }

    [Fact]
    public void Asignar_pin_sin_cedula_o_a_un_administrador_es_error()
    {
        var sinCedula = clsUsuario.Crear("X", "x@y.co", clsRol.INSPECTOR, null, "h", AHORA);
        Assert.Throws<clsValidacionException>(() => sinCedula.AsignarPin("p", true, AHORA));

        var admin = clsUsuario.Crear("A", "a@y.co", clsRol.ADMIN, null, "h", AHORA, "7654321");
        Assert.Throws<clsValidacionException>(() => admin.AsignarPin("p", true, AHORA));
    }

    [Fact]
    public void Modificar_distingue_no_enviado_de_enviado_en_nulo_para_el_subtipo()
    {
        var u = clsUsuario.Crear("Ines", "ines@ejemplo.com", clsRol.INSPECTOR, "nacional", "h", AHORA);

        u.Modificar("Ines Perez", false, null, null, false, null, null, null, false, null, false, null, AHORA);
        Assert.Equal("nacional", u.SubtipoInspector);

        u.Modificar(null, false, null, null, true, null, null, null, false, null, false, null, AHORA);
        Assert.Null(u.SubtipoInspector);
        Assert.Equal("Ines Perez", u.Nombre);
    }

    [Fact]
    public void Cambiar_a_un_rol_que_no_es_inspector_limpia_el_subtipo()
    {
        var u = clsUsuario.Crear("Ines", "ines@ejemplo.com", clsRol.INSPECTOR, "importacion", "h", AHORA);

        u.Modificar(null, false, null, clsRol.COMERCIAL, false, null, null, null, false, null, false, null, AHORA);

        Assert.Null(u.SubtipoInspector);
    }

    [Fact]
    public void No_se_puede_quitar_el_correo_a_quien_entra_con_contrasena_ni_la_cedula_al_conductor_o_al_de_pin()
    {
        var conContrasena = clsUsuario.Crear("X", "x@y.co", clsRol.ADMIN, null, "h", AHORA);
        Assert.Throws<clsValidacionException>(() => conContrasena.Modificar(null, true, null, null, false, null, null, null, false, null, false, null, AHORA));

        Assert.Throws<clsValidacionException>(() => ConductorConPin("c@y.co").Modificar(null, false, null, null, false, null, null, null, true, null, false, null, AHORA));

        var conPin = ConductorConPin("c@y.co");
        conPin.Modificar(null, true, null, null, false, null, null, null, false, null, false, null, AHORA);
        Assert.Null(conPin.Email);
    }

    [Fact]
    public void Una_contrasena_nueva_pasa_al_usuario_a_entrar_con_contrasena_y_descarta_el_pin()
    {
        var u = ConductorConPin("c@y.co");

        u.Modificar(null, false, null, null, false, null, "hash-pass", null, false, null, false, null, AHORA);

        Assert.Equal(clsTipoCredencial.PASSWORD, u.TipoCredencial);
        Assert.Null(u.PinHash);
        Assert.False(u.DebeCambiarPin);
    }

    [Fact]
    public void Un_conductor_anterior_sin_cedula_se_puede_retirar_sin_exigirle_la_cedula()
    {
        // Datos anteriores a la cedula: se crea por la ruta de contrasena con rol inspector y luego se cambia el rol en la base.
        var u = clsUsuario.Crear("Viejo", "viejo@ejemplo.com", clsRol.INSPECTOR, null, "h", AHORA);
        typeof(clsUsuario).GetProperty(nameof(clsUsuario.Rol))!.SetValue(u, clsRol.CONDUCTOR);

        u.Modificar(null, false, null, null, false, null, null, false, false, null, false, null, AHORA);

        Assert.False(u.Activo);
    }

    [Fact]
    public void Las_capacidades_salen_del_rol()
    {
        Assert.Contains(clsCapacidades.GESTIONAR_USUARIOS, clsCapacidades.De(clsRol.COORDINADOR_CALIDAD));
        Assert.Contains(clsCapacidades.GESTIONAR_USUARIOS, clsCapacidades.De(clsRol.ADMIN));
        Assert.Contains(clsCapacidades.REGISTRAR_INSPECCIONES, clsCapacidades.De(clsRol.CONDUCTOR));
        Assert.DoesNotContain(clsCapacidades.GESTIONAR_USUARIOS, clsCapacidades.De(clsRol.INSPECTOR));
        Assert.DoesNotContain(clsCapacidades.GESTIONAR_VIAJES, clsCapacidades.De(clsRol.CONDUCTOR));
        Assert.DoesNotContain(clsCapacidades.REGISTRAR_INSPECCIONES, clsCapacidades.De(clsRol.COMERCIAL));
        Assert.Contains(clsCapacidades.CARGAR_VEHICULOS, clsCapacidades.De(clsRol.SUPER_ADMIN));
        Assert.Empty(clsCapacidades.De(null));
    }
}
