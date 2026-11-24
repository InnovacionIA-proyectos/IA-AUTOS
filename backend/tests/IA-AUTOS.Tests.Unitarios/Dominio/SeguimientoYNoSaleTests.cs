using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

/// <summary>La evidencia completa de un hallazgo, el historial de seguimiento, la foto del sello y la unidad que no sale.</summary>
public class SeguimientoYNoSaleTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 30, 15, TimeSpan.Zero);

    // ------------------------------------------------------------------------------- evidencia completa

    [Theory]
    [InlineData(true, clsTipoCierre.FIRMADA, true)]
    [InlineData(true, clsTipoCierre.CON_RESERVAS, true)]
    [InlineData(true, clsTipoCierre.NO_FIRMA, false)]
    [InlineData(true, null, false)]
    [InlineData(false, clsTipoCierre.FIRMADA, false)]
    [InlineData(false, null, false)]
    public void La_evidencia_esta_completa_con_foto_propia_y_transferencia_firmada_o_con_reservas(bool foto, string? cierre, bool completa)
    {
        Assert.Equal(completa, clsEvidenciaHallazgo.EstaCompleta(foto, cierre));
    }

    [Fact]
    public void Lo_que_falta_se_dice_en_el_orden_de_la_foto_y_el_cierre()
    {
        Assert.Equal([], clsEvidenciaHallazgo.Faltante(true, clsTipoCierre.FIRMADA));
        Assert.Equal([clsEvidenciaHallazgo.FOTO_HALLAZGO, clsEvidenciaHallazgo.TRANSFERENCIA_SIN_CIERRE], clsEvidenciaHallazgo.Faltante(false, null));
        Assert.Equal([clsEvidenciaHallazgo.TRANSFERENCIA_NO_FIRMA], clsEvidenciaHallazgo.Faltante(true, clsTipoCierre.NO_FIRMA));
        Assert.Equal([clsEvidenciaHallazgo.FOTO_HALLAZGO, clsEvidenciaHallazgo.TRANSFERENCIA_NO_FIRMA], clsEvidenciaHallazgo.Faltante(false, clsTipoCierre.NO_FIRMA));
    }

    [Fact]
    public void Un_recibo_sin_entrega_deja_la_evidencia_incompleta_porque_solo_firma_una_parte()
    {
        Assert.Equal([clsEvidenciaHallazgo.TRANSFERENCIA_SIN_ENTREGA], clsEvidenciaHallazgo.Faltante(true, clsTipoCierre.RECIBO_SIN_ENTREGA));
        Assert.Equal([clsEvidenciaHallazgo.FOTO_HALLAZGO, clsEvidenciaHallazgo.TRANSFERENCIA_SIN_ENTREGA], clsEvidenciaHallazgo.Faltante(false, clsTipoCierre.RECIBO_SIN_ENTREGA));
        Assert.False(clsEvidenciaHallazgo.EstaCompleta(true, clsTipoCierre.RECIBO_SIN_ENTREGA));
    }

    // ------------------------------------------------------------------------------------- historial

    [Theory]
    [InlineData("validado")]
    [InlineData("rechazado")]
    [InlineData("recuperado")]
    [InlineData("derogado")]
    public void Un_cambio_de_seguimiento_guarda_los_estados_quien_lo_hizo_y_su_nota(string estado)
    {
        var hallazgo = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var sello = Guid.NewGuid();

        var fila = clsHallazgoSeguimiento.CrearCambioDeEstado(hallazgo, clsEstadoSeguimiento.PENDIENTE, estado, usuario, "  una nota  ", sello, AHORA);

        Assert.Equal(clsAccionSeguimiento.SEGUIMIENTO, fila.Accion);
        Assert.Equal(hallazgo, fila.HallazgoId);
        Assert.Equal(clsEstadoSeguimiento.PENDIENTE, fila.EstadoAnterior);
        Assert.Equal(estado, fila.EstadoNuevo);
        Assert.Equal(usuario, fila.UsuarioId);
        Assert.Equal("una nota", fila.Nota);
        Assert.Equal(sello, fila.EvidenciaSelloId);
        Assert.Equal(AHORA, fila.CreatedAt);
    }

    [Theory]
    [InlineData("pendiente")]
    [InlineData("archivado")]
    [InlineData("")]
    public void El_estado_nuevo_debe_ser_validado_rechazado_recuperado_o_derogado(string estado)
    {
        Assert.Throws<clsValidacionException>(
            () => clsHallazgoSeguimiento.CrearCambioDeEstado(Guid.NewGuid(), clsEstadoSeguimiento.PENDIENTE, estado, Guid.NewGuid(), null, null, AHORA));
    }

    [Fact]
    public void La_nota_del_seguimiento_admite_hasta_1000_caracteres()
    {
        Assert.Throws<clsValidacionException>(
            () => clsHallazgoSeguimiento.CrearCambioDeEstado(
                Guid.NewGuid(), clsEstadoSeguimiento.PENDIENTE, clsEstadoSeguimiento.RECHAZADO, Guid.NewGuid(), new string('n', 1001), null, AHORA));
        var justa = clsHallazgoSeguimiento.CrearCambioDeEstado(
            Guid.NewGuid(), clsEstadoSeguimiento.PENDIENTE, clsEstadoSeguimiento.RECHAZADO, Guid.NewGuid(), new string('n', 1000), null, AHORA);
        Assert.Equal(1000, justa.Nota!.Length);
    }

    [Fact]
    public void Un_no_sale_queda_en_el_historial_con_el_motivo_y_sin_estados()
    {
        var fila = clsHallazgoSeguimiento.CrearNoSale(Guid.NewGuid(), Guid.NewGuid(), "Golpe en el frente", AHORA);

        Assert.Equal(clsAccionSeguimiento.NO_SALE, fila.Accion);
        Assert.Equal("Golpe en el frente", fila.Nota);
        Assert.Null(fila.EstadoAnterior);
        Assert.Null(fila.EstadoNuevo);
    }

    // -------------------------------------------------------------------------------- foto del sello

    [Fact]
    public void La_foto_del_sello_es_de_la_inspeccion_y_no_de_una_vista_ni_de_un_hallazgo()
    {
        var inspeccion = Guid.NewGuid();

        var sello = clsEvidencia.CrearSello(inspeccion, "sello.png", AHORA, 6.2, -75.5, 4.5, "tablet-9", new string('a', 64), null, AHORA);

        Assert.True(sello.EsSello);
        Assert.False(sello.EsConstancia);
        Assert.Equal(inspeccion, sello.InspeccionId);
        Assert.Null(sello.HallazgoId);
        Assert.Null(sello.VistaId);
    }

    [Fact]
    public void Las_demas_evidencias_no_son_sello()
    {
        var foto = clsEvidencia.Crear(Guid.NewGuid(), Guid.NewGuid(), null, "h.png", AHORA, 4.6, -74.1, null, null, null, null, AHORA);
        var constancia = clsEvidencia.CrearConstancia(Guid.NewGuid(), "c.png", AHORA, 4.6, -74.1, null, null, null, null, AHORA);

        Assert.False(foto.EsSello);
        Assert.False(constancia.EsSello);
    }

    // ---------------------------------------------------------------------------- unidad que no sale

    [Fact]
    public void Una_unidad_nueva_esta_activa_sin_datos_de_no_sale()
    {
        var unidad = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);

        Assert.Equal(clsEstadoUnidad.ACTIVA, unidad.EstadoUnidad);
        Assert.False(unidad.NoSale);
        Assert.Null(unidad.NoSaleMotivo);
        Assert.Null(unidad.NoSaleHallazgoId);
    }

    [Fact]
    public void Marcar_que_no_sale_guarda_el_motivo_el_hallazgo_quien_la_marco_cuando_y_el_reemplazo()
    {
        var unidad = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);
        var hallazgo = Guid.NewGuid();
        var usuario = Guid.NewGuid();
        var reemplazo = Guid.NewGuid();
        var luego = AHORA.AddHours(2);

        unidad.MarcarNoSale("dano_v1_plus", "  Golpe estructural  ", hallazgo, usuario, reemplazo, luego);

        Assert.True(unidad.NoSale);
        Assert.Equal(clsEstadoUnidad.NO_SALE, unidad.EstadoUnidad);
        Assert.Equal("dano_v1_plus", unidad.NoSaleMotivoCodigo);
        Assert.Equal("Golpe estructural", unidad.NoSaleMotivo);
        Assert.Equal(hallazgo, unidad.NoSaleHallazgoId);
        Assert.Equal(usuario, unidad.NoSaleMarcadoPor);
        Assert.Equal(luego, unidad.NoSaleAt);
        Assert.Equal(reemplazo, unidad.ReemplazoVehiculoId);
    }

    [Fact]
    public void El_reemplazo_es_opcional_y_una_unidad_que_ya_no_sale_no_se_marca_de_nuevo()
    {
        var unidad = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);

        unidad.MarcarNoSale("dano_v1_plus", "Golpe", Guid.NewGuid(), Guid.NewGuid(), null, AHORA);

        Assert.Null(unidad.ReemplazoVehiculoId);
        Assert.Throws<clsConflictoException>(() => unidad.MarcarNoSale("dano_v1_plus", "Otra vez", Guid.NewGuid(), Guid.NewGuid(), null, AHORA));
    }

    [Fact]
    public void Un_no_sale_no_exige_hallazgo_ni_texto_pero_el_texto_admite_hasta_300_caracteres()
    {
        var unidad = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);

        Assert.Throws<clsValidacionException>(() => unidad.MarcarNoSale("otro", new string('m', 301), null, Guid.NewGuid(), null, AHORA));
        Assert.False(unidad.NoSale);
        unidad.MarcarNoSale("no_llego_al_patio", null, null, Guid.NewGuid(), null, AHORA);

        Assert.True(unidad.NoSale);
        Assert.Equal("no_llego_al_patio", unidad.NoSaleMotivoCodigo);
        Assert.Null(unidad.NoSaleMotivo);
        Assert.Null(unidad.NoSaleHallazgoId);
    }

    [Fact]
    public void Vuelve_a_salir_deja_la_unidad_activa_sin_posicion_y_sin_la_marca_y_solo_si_no_sale()
    {
        var unidad = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);
        Assert.Throws<clsConflictoException>(() => unidad.VolverASalir(AHORA));
        unidad.AsignarPosicion(3, AHORA);
        unidad.MarcarNoSale("dano_v1_plus", "Golpe", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), AHORA);

        unidad.VolverASalir(AHORA.AddMinutes(20));

        Assert.True(unidad.EstaActiva);
        Assert.Null(unidad.Posicion);
        Assert.Null(unidad.NoSaleMotivoCodigo);
        Assert.Null(unidad.NoSaleMotivo);
        Assert.Null(unidad.NoSaleHallazgoId);
        Assert.Null(unidad.NoSaleMarcadoPor);
        Assert.Null(unidad.NoSaleAt);
        Assert.Null(unidad.ReemplazoVehiculoId);
    }

    [Fact]
    public void Bajar_guarda_cuando_quien_y_por_que_libera_el_puesto_y_una_unidad_que_no_sale_tambien_se_baja()
    {
        var usuario = Guid.NewGuid();
        var activa = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);
        activa.AsignarPosicion(2, AHORA);
        var noSale = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);
        noSale.MarcarNoSale("no_esta_listo", null, null, Guid.NewGuid(), null, AHORA);

        activa.Bajar("  Cambio de pedido  ", usuario, AHORA.AddHours(1));
        noSale.Bajar("Cambio de pedido", usuario, AHORA.AddHours(1));

        Assert.True(activa.Bajado);
        Assert.False(activa.EstaActiva);
        Assert.Null(activa.Posicion);
        Assert.Equal("Cambio de pedido", activa.BajadoMotivo);
        Assert.Equal(usuario, activa.BajadoPor);
        Assert.Equal(AHORA.AddHours(1), activa.BajadoAt);
        Assert.True(noSale.Bajado);
        // Conserva los datos de su «no sale».
        Assert.Equal("no_esta_listo", noSale.NoSaleMotivoCodigo);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Bajar_exige_el_motivo_y_admite_hasta_300_caracteres_y_una_unidad_bajada_no_se_baja_ni_se_marca(string? motivo)
    {
        var unidad = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);

        Assert.Throws<clsValidacionException>(() => unidad.Bajar(motivo, Guid.NewGuid(), AHORA));
        Assert.Throws<clsValidacionException>(() => unidad.Bajar(new string('m', 301), Guid.NewGuid(), AHORA));
        Assert.False(unidad.Bajado);
        unidad.Bajar(new string('m', 300), Guid.NewGuid(), AHORA);

        Assert.Throws<clsConflictoException>(() => unidad.Bajar("Otra vez", Guid.NewGuid(), AHORA));
        Assert.Throws<clsConflictoException>(() => unidad.MarcarNoSale("no_esta_listo", null, null, Guid.NewGuid(), null, AHORA));
        Assert.Throws<clsConflictoException>(() => unidad.AsignarPosicion(1, AHORA));
        Assert.Throws<clsConflictoException>(() => unidad.CambiarLugarDestino(Guid.NewGuid(), AHORA));
    }

    // ------------------------------------------------------------------------------------ capacidades

    [Theory]
    [InlineData(clsRol.COORDINADOR_CALIDAD, true)]
    [InlineData(clsRol.COMERCIAL, true)]
    [InlineData(clsRol.ADMIN, true)]
    [InlineData(clsRol.SUPER_ADMIN, true)]
    [InlineData(clsRol.INSPECTOR, false)]
    [InlineData(clsRol.CONDUCTOR, false)]
    [InlineData(null, false)]
    public void Gestionar_el_seguimiento_es_del_coordinador_comercial_y_administradores(string? rol, bool tiene)
    {
        Assert.Equal(tiene, clsCapacidades.De(rol).Contains(clsCapacidades.GESTIONAR_SEGUIMIENTO));
    }
}
