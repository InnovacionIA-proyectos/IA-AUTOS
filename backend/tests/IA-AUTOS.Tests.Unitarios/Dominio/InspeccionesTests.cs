using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;

namespace IAAutos.Tests.Unitarios.Dominio;

public class InspeccionesTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid VIAJE = Guid.NewGuid();

    private static readonly IReadOnlyList<string> SIN_FALTANTES = [];

    private static clsResumenCierre Resumen(int evidencias = 0, int sinUbicacion = 0, int hallazgos = 0, int hallazgosSinFoto = 0) =>
        new(evidencias, sinUbicacion, hallazgos, hallazgosSinFoto, []);

    private static clsInspeccion NuevaInspeccion(string momento = clsMomento.SALIDA, double? lat = 4.6, double? lng = -74.1, string? notas = null) =>
        clsInspeccion.Crear(Guid.NewGuid(), VIAJE, Guid.NewGuid(), momento, AHORA, lat, lng, null, notas, AHORA);

    [Fact]
    public void Crear_valida_estado_borrador_por_defecto_y_limpia_textos()
    {
        var insp = NuevaInspeccion();

        Assert.Equal(clsEstadoInspeccion.BORRADOR, insp.Estado);
        Assert.False(insp.EstaCerrada);
        Assert.Equal(VIAJE, insp.ViajeId);
        // La placa, el trailer y el conductor ya no se capturan: salen del viaje.
        Assert.Null(insp.ConductorNombre);
        Assert.Null(insp.Placa);
        Assert.Null(insp.Trailer);
        Assert.NotEqual(Guid.Empty, insp.Id);
    }

    [Fact]
    public void Crear_registra_la_hora_de_inicio_y_nace_en_version_uno_sin_cierre()
    {
        var insp = NuevaInspeccion();

        Assert.Equal(AHORA, insp.IniciadaAt);
        Assert.Null(insp.CerradaAt);
        Assert.Equal(1, insp.Version);
        Assert.Null(insp.CorrigeInspeccionId);
    }

    [Theory]
    [InlineData("salida", "Inspección de salida")]
    [InlineData("llegada", "Inspección de llegada")]
    public void Crear_acepta_salida_y_llegada_con_su_nombre_en_pantalla(string momento, string nombre)
    {
        var insp = NuevaInspeccion(momento);

        Assert.Equal(momento, insp.Momento);
        Assert.Equal(nombre, clsMomento.Nombre(momento));
    }

    [Theory]
    [InlineData("check_in")]
    [InlineData("check_out")]
    [InlineData("informe")]
    [InlineData("otro")]
    public void Crear_rechaza_los_momentos_retirados_y_cualquier_otro(string momento)
    {
        var ex = Assert.Throws<clsValidacionException>(() => NuevaInspeccion(momento));
        Assert.Equal(clsCodigosError.VALIDATION_ERROR, ex.Codigo);
    }

    [Theory]
    [InlineData(91, 0)]
    [InlineData(-91, 0)]
    [InlineData(0, 181)]
    [InlineData(0, -181)]
    public void Crear_rechaza_coordenadas_fuera_de_rango(double lat, double lng)
    {
        Assert.Throws<clsValidacionException>(() => NuevaInspeccion(lat: lat, lng: lng));
    }

    [Fact]
    public void Crear_rechaza_notas_de_mas_de_1000_caracteres()
    {
        Assert.Throws<clsValidacionException>(() => NuevaInspeccion(notas: new string('x', 1001)));
        Assert.NotNull(NuevaInspeccion(notas: new string('x', 1000)).Notas);
    }

    [Fact]
    public void Cerrar_con_todas_las_vistas_y_sin_novedades_es_valido_y_registra_la_hora_de_cierre()
    {
        var insp = NuevaInspeccion();

        insp.Cerrar(Resumen(), SIN_FALTANTES, exigeUbicacionPropia: false, AHORA.AddMinutes(5));

        Assert.True(insp.EstaCerrada);
        Assert.Equal(AHORA.AddMinutes(5), insp.UpdatedAt);
        Assert.Equal(AHORA.AddMinutes(5), insp.CerradaAt);
    }

    [Fact]
    public void Cerrar_sin_todas_las_vistas_obligatorias_nombra_cada_vista_que_falta()
    {
        var insp = NuevaInspeccion();

        var ex = Assert.Throws<clsValidacionException>(() => insp.Cerrar(Resumen(), ["Frontal", "Superior"], false, AHORA));

        var errores = Assert.IsAssignableFrom<IReadOnlyList<string>>(ex.Detalles);
        Assert.Equal(2, errores.Count);
        Assert.Contains("Frontal", errores[0]);
        Assert.Contains("Superior", errores[1]);
        Assert.False(insp.EstaCerrada);
        Assert.Null(insp.CerradaAt);
    }

    [Fact]
    public void Cerrar_exige_foto_propia_en_cada_hallazgo()
    {
        var insp = NuevaInspeccion();

        var ex = Assert.Throws<clsValidacionException>(() => insp.Cerrar(Resumen(evidencias: 7, hallazgos: 2, hallazgosSinFoto: 1), SIN_FALTANTES, false, AHORA));

        var errores = Assert.IsAssignableFrom<IReadOnlyList<string>>(ex.Detalles);
        Assert.Single(errores);
        Assert.Contains("hallazgo", errores[0]);
        Assert.False(insp.EstaCerrada);
    }

    [Fact]
    public void Cerrar_exige_ubicacion_en_toda_foto()
    {
        var insp = NuevaInspeccion();

        var ex = Assert.Throws<clsValidacionException>(() => insp.Cerrar(Resumen(evidencias: 3, sinUbicacion: 2), SIN_FALTANTES, false, AHORA));

        var errores = Assert.IsAssignableFrom<IReadOnlyList<string>>(ex.Detalles);
        Assert.Contains("2 foto(s) sin ubicación", errores[0]);
    }

    [Fact]
    public void Cerrar_acumula_todos_los_errores()
    {
        var insp = NuevaInspeccion();

        var ex = Assert.Throws<clsValidacionException>(() => insp.Cerrar(Resumen(sinUbicacion: 1, hallazgos: 1, hallazgosSinFoto: 1), ["Frontal"], false, AHORA));

        Assert.Equal(3, Assert.IsAssignableFrom<IReadOnlyList<string>>(ex.Detalles).Count);
    }

    [Fact]
    public void En_transporte_el_registro_exige_su_propia_ubicacion_pero_no_las_vistas()
    {
        var sinGps = NuevaInspeccion(lat: null, lng: null);
        var ex = Assert.Throws<clsValidacionException>(() => sinGps.Cerrar(Resumen(), SIN_FALTANTES, exigeUbicacionPropia: true, AHORA));
        Assert.Contains("ubicación", Assert.IsAssignableFrom<IReadOnlyList<string>>(ex.Detalles)[0]);

        var conGps = NuevaInspeccion();
        conGps.Cerrar(Resumen(), SIN_FALTANTES, exigeUbicacionPropia: true, AHORA);
        Assert.True(conGps.EstaCerrada);
    }

    [Fact]
    public void Una_inspeccion_cerrada_es_inmutable_y_no_se_cierra_dos_veces()
    {
        var insp = NuevaInspeccion();
        insp.Cerrar(Resumen(evidencias: 1, hallazgos: 1), SIN_FALTANTES, false, AHORA);

        Assert.Throws<clsConflictoException>(insp.AsegurarEditable);
        Assert.Throws<clsConflictoException>(() => insp.Cerrar(Resumen(), SIN_FALTANTES, false, AHORA));
    }

    [Fact]
    public void Una_inspeccion_en_borrador_es_editable()
    {
        NuevaInspeccion().AsegurarEditable();
    }

    [Fact]
    public void Corregir_crea_una_version_nueva_que_referencia_a_la_anterior_y_no_la_modifica()
    {
        var original = NuevaInspeccion();
        original.Cerrar(Resumen(), SIN_FALTANTES, false, AHORA.AddMinutes(5));
        var usuario = Guid.NewGuid();

        var nueva = original.Corregir(usuario, AHORA.AddHours(1), 5.0, -75.0, "Corrección", AHORA.AddHours(1));

        Assert.Equal(2, nueva.Version);
        Assert.Equal(original.Id, nueva.CorrigeInspeccionId);
        Assert.Equal(original.EtapaId, nueva.EtapaId);
        Assert.Equal(original.ViajeId, nueva.ViajeId);
        Assert.Equal(original.Momento, nueva.Momento);
        Assert.Equal(usuario, nueva.UsuarioId);
        Assert.Equal(clsEstadoInspeccion.BORRADOR, nueva.Estado);
        Assert.Null(nueva.CerradaAt);
        Assert.NotEqual(original.Id, nueva.Id);
        // La original no cambia.
        Assert.True(original.EstaCerrada);
        Assert.Equal(1, original.Version);
        Assert.Equal(AHORA.AddMinutes(5), original.CerradaAt);
        Assert.Equal(4.6, original.Lat);
    }

    [Fact]
    public void Las_correcciones_se_encadenan_y_cada_version_sube_de_numero()
    {
        var v1 = NuevaInspeccion();
        v1.Cerrar(Resumen(), SIN_FALTANTES, false, AHORA);
        var v2 = v1.Corregir(Guid.NewGuid(), AHORA, null, null, null, AHORA);
        v2.Cerrar(Resumen(), SIN_FALTANTES, false, AHORA);

        var v3 = v2.Corregir(Guid.NewGuid(), AHORA, null, null, null, AHORA);

        Assert.Equal(3, v3.Version);
        Assert.Equal(v2.Id, v3.CorrigeInspeccionId);
    }

    [Fact]
    public void Solo_se_corrige_una_inspeccion_cerrada()
    {
        var enBorrador = NuevaInspeccion();

        Assert.Throws<clsConflictoException>(() => enBorrador.Corregir(Guid.NewGuid(), AHORA, null, null, null, AHORA));
    }
}

public class HallazgosYEvidenciasTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid VISTA = Guid.NewGuid();
    private static readonly string HASH = new('a', 64);

    private static clsEvidencia Foto(
        Guid? hallazgo = null,
        Guid? vista = null,
        string ruta = "a.jpg",
        double? lat = 4.6,
        double? lng = -74.1,
        double? precision = null,
        string? dispositivo = null,
        string? hash = null,
        string? nota = null) =>
        clsEvidencia.Crear(Guid.NewGuid(), hallazgo, hallazgo is null ? vista ?? VISTA : vista, ruta, AHORA, lat, lng, precision, dispositivo, hash, nota, AHORA.AddSeconds(3));

    private static readonly Guid ZONA = Guid.NewGuid();

    private static clsHallazgo HallazgoDeInspector(
        string clase = "golpe",
        string tamano = "5_10cm",
        string? nivel = "v2",
        string? nota = null,
        Guid? origen = null,
        Guid? confirmadoPor = null) =>
        clsHallazgo.Crear(Guid.NewGuid(), ZONA, clase, tamano, nivel, clsOrigenHallazgo.INSPECTOR, nota, origen, confirmadoPor, AHORA);

    [Fact]
    public void Hallazgo_del_inspector_guarda_zona_clase_tamano_y_nivel_y_nace_pendiente_y_nuevo()
    {
        var h = HallazgoDeInspector(clase: "  golpe ", nota: "  con nota  ");

        Assert.Equal(ZONA, h.ZonaId);
        Assert.Equal("golpe", h.ClaseDano);
        Assert.Equal("5_10cm", h.TamanoDano);
        Assert.Equal("v2", h.NivelSeveridad);
        Assert.Equal(clsOrigenHallazgo.INSPECTOR, h.Origen);
        Assert.Equal("con nota", h.Nota);
        Assert.Equal(clsEstadoSeguimiento.PENDIENTE, h.EstadoSeguimiento);
        Assert.False(h.EsPreexistente);
        Assert.Null(h.HallazgoOrigenId);
        Assert.Null(h.ConfirmadoPor);
        Assert.Null(h.ParteVehiculo);
        Assert.Null(h.CoordsDiagrama);
    }

    [Fact]
    public void Hallazgo_preexistente_apunta_a_su_origen_y_guarda_quien_lo_confirmo()
    {
        var origen = Guid.NewGuid();
        var inspector = Guid.NewGuid();

        var h = HallazgoDeInspector(origen: origen, confirmadoPor: inspector);

        Assert.True(h.EsPreexistente);
        Assert.Equal(origen, h.HallazgoOrigenId);
        Assert.Equal(inspector, h.ConfirmadoPor);
    }

    [Fact]
    public void Hallazgo_nuevo_no_guarda_quien_confirmo()
    {
        var h = HallazgoDeInspector(confirmadoPor: Guid.NewGuid());

        Assert.Null(h.ConfirmadoPor);
    }

    [Theory]
    [InlineData("", "5_10cm", "clase_dano")]
    [InlineData("golpe", "", "tamano_dano")]
    public void Hallazgo_rechaza_clase_o_tamano_vacios(string clase, string tamano, string campo)
    {
        var ex = Assert.Throws<clsValidacionException>(() => HallazgoDeInspector(clase, tamano));

        Assert.Contains(campo, ex.Message);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Hallazgo_del_inspector_exige_nivel_de_severidad(string? nivel)
    {
        var ex = Assert.Throws<clsValidacionException>(() => HallazgoDeInspector(nivel: nivel));

        Assert.Contains("nivel_severidad", ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys);
    }

    [Fact]
    public void Hallazgo_del_conductor_se_guarda_sin_nivel_y_no_admite_que_lo_envie()
    {
        var h = clsHallazgo.Crear(Guid.NewGuid(), ZONA, "golpe", "5_10cm", null, clsOrigenHallazgo.CONDUCTOR, null, null, null, AHORA);

        Assert.Equal(clsOrigenHallazgo.CONDUCTOR, h.Origen);
        Assert.Null(h.NivelSeveridad);
        Assert.Throws<clsValidacionException>(
            () => clsHallazgo.Crear(Guid.NewGuid(), ZONA, "golpe", "5_10cm", "v1", clsOrigenHallazgo.CONDUCTOR, null, null, null, AHORA));
    }

    [Fact]
    public void Hallazgo_rechaza_un_origen_desconocido()
    {
        Assert.Throws<clsValidacionException>(
            () => clsHallazgo.Crear(Guid.NewGuid(), ZONA, "golpe", "5_10cm", "v1", "coordinador", null, null, null, AHORA));
    }

    [Fact]
    public void Hallazgo_rechaza_una_nota_de_mas_de_500_caracteres()
    {
        Assert.Throws<clsValidacionException>(() => HallazgoDeInspector(nota: new string('n', clsHallazgo.NOTA_MAX + 1)));
        Assert.Equal(clsHallazgo.NOTA_MAX, HallazgoDeInspector(nota: new string('n', clsHallazgo.NOTA_MAX)).Nota!.Length);
    }

    [Fact]
    public void Los_lados_validos_son_izquierdo_y_derecho_y_la_regla_de_si_un_cuadrante_lleva_lado_la_da_el_catalogo()
    {
        Assert.Equal(["derecho", "izquierdo"], clsLado.VALORES.Order().ToList());
        Assert.True(clsCuadrante.Crear(null, 4, "Guardafango delantero", null, null, AHORA).LlevaLado);
        Assert.False(clsCuadrante.Crear(null, 54, "Parabrisas", null, null, AHORA, llevaLado: false).LlevaLado);
    }

    [Theory]
    [InlineData(-0.1, 0.5)]
    [InlineData(0.5, 1.1)]
    public void Coordenadas_del_diagrama_deben_estar_normalizadas(double x, double y)
    {
        Assert.Throws<clsValidacionException>(() => new clsCoordsDiagrama(x, y));
    }

    [Fact]
    public void Evidencia_requiere_ruta_y_valida_rango_del_sello()
    {
        Assert.Throws<clsValidacionException>(() => Foto(ruta: " "));
        Assert.Throws<clsValidacionException>(() => Foto(lat: 100, lng: 1));
        Assert.Throws<clsValidacionException>(() => Foto(lat: 1, lng: 181));
    }

    [Fact]
    public void Evidencia_guarda_cada_sello_el_del_dispositivo_y_el_del_servidor()
    {
        var f = Foto(precision: 5.5, dispositivo: " tablet-7 ", hash: HASH.ToUpperInvariant(), nota: " Falta una pieza ");

        Assert.Equal(AHORA, f.SelloFecha);
        Assert.Equal(AHORA.AddSeconds(3), f.SelloServidorAt);
        Assert.Equal(4.6, f.SelloLat);
        Assert.Equal(-74.1, f.SelloLng);
        Assert.Equal(5.5, f.Precision);
        Assert.Equal("tablet-7", f.DispositivoId);
        Assert.Equal(HASH, f.HashSha256);
        Assert.Equal("Falta una pieza", f.Nota);
        Assert.False(f.SinUbicacion);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(4.6, null)]
    [InlineData(null, -74.1)]
    public void Una_foto_sin_ubicacion_completa_queda_marcada_sin_ubicacion(double? lat, double? lng)
    {
        var f = Foto(lat: lat, lng: lng);

        Assert.True(f.SinUbicacion);
        Assert.Equal(0, f.SelloLat);
        Assert.Equal(0, f.SelloLng);
    }

    [Fact]
    public void Toda_foto_es_de_una_vista_o_de_un_hallazgo()
    {
        Assert.Throws<clsValidacionException>(() =>
            clsEvidencia.Crear(Guid.NewGuid(), null, null, "a.jpg", AHORA, 1, 1, null, null, null, null, AHORA));
        Assert.Equal(VISTA, Foto().VistaId);
        var deHallazgo = Foto(hallazgo: Guid.NewGuid());
        Assert.Null(deHallazgo.VistaId);
        Assert.NotNull(deHallazgo.HallazgoId);
    }

    [Theory]
    [InlineData("no-es-un-hash")]
    [InlineData("abc")]
    public void El_hash_debe_ser_sha256_hexadecimal(string hash)
    {
        Assert.Throws<clsValidacionException>(() => Foto(hash: hash));
    }

    [Fact]
    public void Evidencia_valida_precision_dispositivo_y_nota()
    {
        Assert.Throws<clsValidacionException>(() => Foto(precision: -1));
        Assert.Throws<clsValidacionException>(() => Foto(precision: double.NaN));
        Assert.Throws<clsValidacionException>(() => Foto(dispositivo: new string('d', 101)));
        Assert.Throws<clsValidacionException>(() => Foto(nota: new string('n', 501)));
        Assert.NotNull(Foto(nota: new string('n', 500)).Nota);
    }

    [Theory]
    [InlineData("cargue", true)]
    [InlineData("descargue", true)]
    [InlineData("transporte", false)]
    [InlineData("otra_etapa", false)]
    [InlineData(null, false)]
    public void Solo_cargue_y_descargue_exigen_las_vistas_obligatorias(string? etapa, bool esperado)
    {
        Assert.Equal(esperado, clsEtapaCatalogo.ExigeVistas(etapa));
    }
}
