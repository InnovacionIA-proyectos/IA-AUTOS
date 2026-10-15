using IAAutos.Aplicacion.Comun;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

public class ViajesTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);

    private static clsViaje NuevoViaje(
        string? codigo = "MTK-001",
        Guid? tipoUnidad = null,
        string? placa = "ABC123",
        string? trailer = null,
        Guid? conductor = null,
        Guid? inspector = null) =>
        clsViaje.Crear(codigo, tipoUnidad ?? Guid.NewGuid(), placa, trailer, conductor, inspector, null, null, Guid.NewGuid(), AHORA);

    private static string CampoDelError(clsValidacionException ex) =>
        ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys.Single();

    [Fact]
    public void Un_viaje_nuevo_nace_programado_con_sus_datos_limpios()
    {
        var conductor = Guid.NewGuid();

        var viaje = NuevoViaje(codigo: "  MTK-001  ", placa: " ABC123 ", trailer: "  T-9 ", conductor: conductor);

        Assert.Equal("MTK-001", viaje.CodigoMtrack);
        Assert.Equal("ABC123", viaje.Placa);
        Assert.Equal("T-9", viaje.Trailer);
        Assert.Equal(conductor, viaje.ConductorId);
        Assert.Equal(clsEstadoViaje.PROGRAMADO, viaje.Estado);
        Assert.NotEqual(Guid.Empty, viaje.Id);
    }

    [Theory]
    [InlineData(null, "codigo_mtrack")]
    [InlineData("  ", "codigo_mtrack")]
    [InlineData("MTK 001", "codigo_mtrack")]
    [InlineData("MTK#1", "codigo_mtrack")]
    public void El_codigo_mtrack_es_requerido_y_admite_letras_numeros_y_separadores(string? codigo, string campo)
    {
        var ex = Assert.Throws<clsValidacionException>(() => NuevoViaje(codigo: codigo));

        Assert.Equal(campo, CampoDelError(ex));
    }

    [Fact]
    public void El_codigo_mtrack_admite_hasta_50_caracteres()
    {
        Assert.Equal(50, NuevoViaje(codigo: new string('A', 50)).CodigoMtrack!.Length);
        Assert.Equal("codigo_mtrack", CampoDelError(Assert.Throws<clsValidacionException>(() => NuevoViaje(codigo: new string('A', 51)))));
    }

    [Fact]
    public void La_placa_y_la_unidad_son_requeridas_y_el_trailer_es_opcional()
    {
        Assert.Equal("placa", CampoDelError(Assert.Throws<clsValidacionException>(() => NuevoViaje(placa: " "))));
        Assert.Equal("placa", CampoDelError(Assert.Throws<clsValidacionException>(() => NuevoViaje(placa: new string('P', 51)))));
        Assert.Equal("tipo_unidad_id", CampoDelError(Assert.Throws<clsValidacionException>(() => clsViaje.Crear("MTK", Guid.Empty, "ABC", null, null, null, null, null, null, AHORA))));
        Assert.Null(NuevoViaje(trailer: "   ").Trailer);
    }

    [Theory]
    [InlineData(clsEstadoViaje.PROGRAMADO, clsEstadoViaje.EN_CURSO, true)]
    [InlineData(clsEstadoViaje.PROGRAMADO, clsEstadoViaje.CANCELADO, true)]
    [InlineData(clsEstadoViaje.EN_CURSO, clsEstadoViaje.FINALIZADO, true)]
    [InlineData(clsEstadoViaje.EN_CURSO, clsEstadoViaje.CANCELADO, true)]
    [InlineData(clsEstadoViaje.PROGRAMADO, clsEstadoViaje.FINALIZADO, false)]
    [InlineData(clsEstadoViaje.EN_CURSO, clsEstadoViaje.PROGRAMADO, false)]
    [InlineData(clsEstadoViaje.FINALIZADO, clsEstadoViaje.EN_CURSO, false)]
    [InlineData(clsEstadoViaje.FINALIZADO, clsEstadoViaje.CANCELADO, false)]
    [InlineData(clsEstadoViaje.CANCELADO, clsEstadoViaje.PROGRAMADO, false)]
    public void Las_transiciones_de_estado_siguen_el_ciclo_del_viaje(string actual, string nuevo, bool permitida)
    {
        Assert.Equal(permitida, clsEstadoViaje.PuedePasar(actual, nuevo));
    }

    [Fact]
    public void Una_transicion_no_permitida_es_un_conflicto_y_un_estado_desconocido_es_un_error_de_validacion()
    {
        var viaje = NuevoViaje();

        Assert.Throws<clsConflictoException>(() => viaje.CambiarEstado(clsEstadoViaje.FINALIZADO, AHORA));
        Assert.Equal("estado", CampoDelError(Assert.Throws<clsValidacionException>(() => viaje.CambiarEstado("volando", AHORA))));
        Assert.Equal(clsEstadoViaje.PROGRAMADO, viaje.Estado);

        viaje.CambiarEstado(clsEstadoViaje.EN_CURSO, AHORA);
        viaje.CambiarEstado(clsEstadoViaje.FINALIZADO, AHORA);

        Assert.Equal(clsEstadoViaje.FINALIZADO, viaje.Estado);
        Assert.Throws<clsConflictoException>(() => viaje.CambiarEstado(clsEstadoViaje.CANCELADO, AHORA));
    }

    [Fact]
    public void La_primera_actividad_pone_en_curso_un_viaje_programado_y_no_toca_a_los_demas()
    {
        var programado = NuevoViaje();
        programado.RegistrarActividad(AHORA);
        Assert.Equal(clsEstadoViaje.EN_CURSO, programado.Estado);

        var finalizado = NuevoViaje();
        finalizado.CambiarEstado(clsEstadoViaje.EN_CURSO, AHORA);
        finalizado.CambiarEstado(clsEstadoViaje.FINALIZADO, AHORA);
        finalizado.RegistrarActividad(AHORA);
        Assert.Equal(clsEstadoViaje.FINALIZADO, finalizado.Estado);
    }

    [Fact]
    public void Un_viaje_finalizado_o_cancelado_no_admite_cambios()
    {
        var viaje = NuevoViaje();
        viaje.CambiarEstado(clsEstadoViaje.CANCELADO, AHORA);

        Assert.Throws<clsConflictoException>(() => viaje.AsegurarActivo());
        Assert.Throws<clsConflictoException>(() =>
            viaje.Actualizar("OTRO", null, null, false, null, false, null, false, null, false, null, false, null, AHORA));
    }

    [Fact]
    public void Actualizar_cambia_solo_lo_enviado_y_distingue_no_enviado_de_enviado_en_nulo()
    {
        var conductor = Guid.NewGuid();
        var viaje = NuevoViaje(trailer: "T-1", conductor: conductor);

        viaje.Actualizar(null, null, "XYZ789", false, null, false, null, false, null, false, null, false, null, AHORA);
        Assert.Equal("XYZ789", viaje.Placa);
        Assert.Equal("T-1", viaje.Trailer);
        Assert.Equal(conductor, viaje.ConductorId);

        viaje.Actualizar(null, null, null, true, null, true, null, false, null, false, null, false, null, AHORA);
        Assert.Null(viaje.Trailer);
        Assert.Null(viaje.ConductorId);
        Assert.Equal("XYZ789", viaje.Placa);
    }

    [Fact]
    public void Participan_el_conductor_y_el_inspector_mientras_el_viaje_no_este_cancelado()
    {
        Guid conductor = Guid.NewGuid(), inspector = Guid.NewGuid(), ajeno = Guid.NewGuid();
        var viaje = NuevoViaje(conductor: conductor, inspector: inspector);

        Assert.True(viaje.Participa(conductor));
        Assert.True(viaje.Participa(inspector));
        Assert.False(viaje.Participa(ajeno));

        viaje.CambiarEstado(clsEstadoViaje.CANCELADO, AHORA);

        Assert.False(viaje.Participa(conductor));
        Assert.False(viaje.Participa(inspector));
    }

    [Theory]
    [InlineData(clsRol.INSPECTOR, true)]
    [InlineData(clsRol.CONDUCTOR, true)]
    [InlineData(clsRol.COORDINADOR_CALIDAD, false)]
    [InlineData(clsRol.COMERCIAL, false)]
    [InlineData(clsRol.ADMIN, false)]
    [InlineData(clsRol.SUPER_ADMIN, false)]
    [InlineData(null, false)]
    public void Solo_inspectores_y_conductores_ven_unicamente_sus_viajes(string? rol, bool restringido)
    {
        Assert.Equal(restringido, clsRol.VeSoloSusViajes(rol));
    }

    [Fact]
    public void La_gestion_de_viajes_es_de_coordinadores_y_administradores()
    {
        Assert.Equal(new[] { clsRol.ADMIN, clsRol.SUPER_ADMIN, clsRol.COORDINADOR_CALIDAD }.Order(), clsRol.GESTION_VIAJES.Order());
    }
}

public class CedulaYTextoTests
{
    [Theory]
    [InlineData("1.234.567", "1234567")]
    [InlineData(" 1 234 567 ", "1234567")]
    [InlineData("ce-12345", "CE-12345")]
    public void La_cedula_se_guarda_sin_puntos_ni_espacios_y_en_mayuscula(string entrada, string esperado)
    {
        Assert.Equal(esperado, clsUsuario.NormalizarCedula(entrada));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  .  ")]
    public void Una_cedula_vacia_significa_sin_cedula(string? entrada)
    {
        Assert.Null(clsUsuario.NormalizarCedula(entrada));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("123456789012345678901")]
    [InlineData("12 34$5")]
    public void Una_cedula_demasiado_corta_larga_o_con_simbolos_se_rechaza(string entrada)
    {
        var ex = Assert.Throws<clsValidacionException>(() => clsUsuario.NormalizarCedula(entrada));

        Assert.Equal("cedula", ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys.Single());
    }

    [Fact]
    public void Un_usuario_conserva_su_cedula_normalizada_y_un_texto_vacio_al_actualizar_la_borra()
    {
        var ahora = new DateTimeOffset(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
        var usuario = clsUsuario.Crear("Carlos", "c@e.com", clsRol.CONDUCTOR, null, "hash", ahora, "1.234.567");
        Assert.Equal("1234567", usuario.Cedula);

        usuario.Actualizar(null, null, null, null, null, null, ahora, null);
        Assert.Equal("1234567", usuario.Cedula);

        usuario.Actualizar(null, null, null, null, null, null, ahora, "");
        Assert.Null(usuario.Cedula);
    }

    [Theory]
    [InlineData("Niñera", "ninera")]
    [InlineData("PLANCHÓN GRÚA", "planchon_grua")]
    [InlineData(" Nodriza ", "NODRIZA")]
    public void El_texto_comparable_ignora_mayusculas_tildes_y_separadores(string a, string b)
    {
        Assert.Equal(clsTextoComparable.Normalizar(a), clsTextoComparable.Normalizar(b));
    }
}
