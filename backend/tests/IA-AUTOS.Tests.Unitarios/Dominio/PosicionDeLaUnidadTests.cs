using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Viajes;

namespace IAAutos.Tests.Unitarios.Dominio;

public class PosicionDeLaUnidadTests
{
    private static readonly DateTimeOffset AHORA = new(2026, 11, 3, 6, 0, 0, TimeSpan.FromHours(-5));

    private static string CampoDelError(clsValidacionException ex) =>
        ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"]).Keys.Single();

    private static clsViajeVehiculo Unidad() => clsViajeVehiculo.Crear(Guid.NewGuid(), Guid.NewGuid(), AHORA);

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    [InlineData(7)]
    public void Una_posicion_de_1_a_7_se_registra(int posicion)
    {
        var unidad = Unidad();

        unidad.AsignarPosicion(posicion, AHORA.AddMinutes(1));

        Assert.Equal((byte)posicion, unidad.Posicion);
        Assert.Equal(AHORA.AddMinutes(1), unidad.UpdatedAt);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(-1)]
    [InlineData(255)]
    public void Una_posicion_fuera_de_rango_se_rechaza_con_su_mensaje(int posicion)
    {
        var unidad = Unidad();
        unidad.AsignarPosicion(3, AHORA);

        var ex = Assert.Throws<clsValidacionException>(() => unidad.AsignarPosicion(posicion, AHORA));

        Assert.Equal("posicion", CampoDelError(ex));
        Assert.Contains("La posición debe estar entre 1 y 7", ((Dictionary<string, string[]>)((Dictionary<string, object>)ex.Detalles!)["fieldErrors"])["posicion"]);
        Assert.Equal((byte)3, unidad.Posicion);
    }

    [Fact]
    public void Una_posicion_nula_libera_la_que_tenia()
    {
        var unidad = Unidad();
        unidad.AsignarPosicion(5, AHORA);

        unidad.AsignarPosicion(null, AHORA.AddMinutes(2));

        Assert.Null(unidad.Posicion);
    }

    [Fact]
    public void Marcar_la_unidad_que_no_sale_libera_su_posicion_y_ya_no_admite_otra()
    {
        var unidad = Unidad();
        unidad.AsignarPosicion(5, AHORA);

        unidad.MarcarNoSale("Golpe en el capó", Guid.NewGuid(), Guid.NewGuid(), null, AHORA.AddMinutes(5));

        Assert.Null(unidad.Posicion);
        Assert.Throws<clsConflictoException>(() => unidad.AsignarPosicion(2, AHORA.AddMinutes(6)));
        unidad.AsignarPosicion(null, AHORA.AddMinutes(7));
        Assert.Null(unidad.Posicion);
    }

    [Fact]
    public void El_cierre_suma_los_faltantes_de_la_acomodacion_a_los_demas_requisitos()
    {
        var inspeccion = clsInspeccion.Crear(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), clsMomento.SALIDA, AHORA, 4.6, -74.1, null, null, AHORA);
        var resumen = new clsResumenCierre(0, 0, 0, 0, []);

        var ex = Assert.Throws<clsValidacionException>(() => inspeccion.Cerrar(resumen, [], true, AHORA, ["Falta la posición de la unidad VIN-1", "Faltan fotos de la acomodación: se exige al menos 1 y hay 0"]));

        var detalles = (IEnumerable<string>)ex.Detalles!;
        Assert.Equal(["Falta la posición de la unidad VIN-1", "Faltan fotos de la acomodación: se exige al menos 1 y hay 0"], detalles.ToList());
        Assert.False(inspeccion.EstaCerrada);

        inspeccion.Cerrar(resumen, [], true, AHORA, []);
        Assert.True(inspeccion.EstaCerrada);
    }

    [Fact]
    public void La_foto_sin_vista_ni_hallazgo_solo_nace_como_foto_de_la_acomodacion()
    {
        var inspeccion = Guid.NewGuid();

        var ex = Assert.Throws<clsValidacionException>(() => clsEvidencia.Crear(inspeccion, null, null, "a.jpg", AHORA, 4.6, -74.1, null, null, null, null, AHORA));
        var foto = clsEvidencia.CrearDeAcomodacion(inspeccion, "a.jpg", AHORA, 4.6, -74.1, null, "tablet-1", null, "nota", AHORA);

        Assert.Equal("vista_id", CampoDelError(ex));
        Assert.Null(foto.VistaId);
        Assert.Null(foto.HallazgoId);
        Assert.False(foto.EsConstancia);
        Assert.False(foto.EsSello);
    }
}
