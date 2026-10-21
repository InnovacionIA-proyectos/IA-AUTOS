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

        unidad.MarcarNoSale("  Golpe estructural  ", hallazgo, usuario, reemplazo, luego);

        Assert.True(unidad.NoSale);
        Assert.Equal(clsEstadoUnidad.NO_SALE, unidad.EstadoUnidad);
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

        unidad.MarcarNoSale("Golpe", Guid.NewGuid(), Guid.NewGuid(), null, AHORA);

        Assert.Null(unidad.ReemplazoVehiculoId);
        Assert.Throws<clsConflictoException>(() => unidad.MarcarNoSale("Otra vez", Guid.NewGuid(), Guid.NewGuid(), null, AHORA));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void El_motivo_es_obligatorio(string? motivo)
    {
        var unidad = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);

        Assert.Throws<clsValidacionException>(() => unidad.MarcarNoSale(motivo, Guid.NewGuid(), Guid.NewGuid(), null, AHORA));
        Assert.False(unidad.NoSale);
    }

    [Fact]
    public void El_motivo_admite_hasta_500_caracteres()
    {
        var unidad = clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);

        Assert.Throws<clsValidacionException>(() => unidad.MarcarNoSale(new string('m', 501), Guid.NewGuid(), Guid.NewGuid(), null, AHORA));
        unidad.MarcarNoSale(new string('m', 500), Guid.NewGuid(), Guid.NewGuid(), null, AHORA);
        Assert.Equal(500, unidad.NoSaleMotivo!.Length);
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
