using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Sofasa;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using IAAutos.Infraestructura.Persistencia;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static IAAutos.Tests.Integracion.Infraestructura.clsEscenario;

namespace IAAutos.Tests.Integracion.Infraestructura;

[Collection(clsColeccionBaseDatos.NOMBRE)]
public sealed class PersistenciaTests(clsBaseDatosFixture bd) : IAsyncLifetime
{
    public Task InitializeAsync() => bd.LimpiarAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private ServiceProvider Proveedor() => CrearProveedor(bd.CadenaConexion);

    /// <summary>Crea usuario, vehiculo con su ruta estandar y devuelve los ids para armar inspecciones.</summary>
    private async Task<(Guid UsuarioId, Guid VehiculoId, IReadOnlyList<clsEtapa> Etapas)> SembrarRutaAsync(string email = "uno@ejemplo.com", string vin = "9FBHSR4E8PM000123")
    {
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;
        var usuario = NuevoUsuario(email);
        var contexto = s.GetRequiredService<clsAppDbContext>();
        var vehiculo = NuevoVehiculo(await contexto.Clientes.Select(c => c.Id).FirstAsync(), vin);
        var ruta = await contexto.EtapasCatalogo.OrderBy(e => e.Orden).Select(e => new clsEtapaDeRuta(e.Id, e.Codigo)).ToListAsync();
        var tipoUnidadId = await contexto.TiposUnidad.Select(t => t.Id).FirstAsync();
        var viaje = clsViaje.Crear("MTK-" + vin, tipoUnidadId, "ABC123", null, null, null, null, null, usuario.Id, AHORA);
        var etapas = clsRutaCustodia.CrearEtapasEstandar(viaje.Id, vehiculo.Id, ruta, null, null, false, AHORA);

        s.GetRequiredService<IUsuarioRepository>().Agregar(usuario);
        s.GetRequiredService<IVehiculoRepository>().Agregar(vehiculo);
        s.GetRequiredService<IViajeRepository>().Agregar(viaje);
        s.GetRequiredService<IViajeRepository>().Agregar(clsViajeVehiculo.Crear(viaje.Id, vehiculo.Id, AHORA));
        foreach (var etapa in etapas)
        {
            s.GetRequiredService<IEtapaRepository>().Agregar(etapa);
        }

        await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        return (usuario.Id, vehiculo.Id, etapas);
    }

    [Fact]
    public async Task Vehiculo_y_ruta_se_guardan_en_una_sola_confirmacion_respetando_las_claves_foraneas()
    {
        var (_, vehiculoId, etapas) = await SembrarRutaAsync();

        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var contexto = alcance.ServiceProvider.GetRequiredService<clsAppDbContext>();
        Assert.Equal(3, await contexto.Etapas.CountAsync(e => e.VehiculoId == vehiculoId));
        Assert.Equal([1, 2, 3], await contexto.Etapas.Where(e => e.VehiculoId == vehiculoId).OrderBy(e => e.Orden).Select(e => e.Orden).ToListAsync());
    }

    [Fact]
    public async Task Inspeccion_hallazgo_y_evidencia_conservan_sus_valores_al_leerse()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        Guid inspeccionId;
        Guid hallazgoId;

        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.INFORME, AHORA, 4.60971, -74.08175, null,
                "nota con \"comillas\"", AHORA);
            var hallazgo = clsHallazgo.Crear(inspeccion.Id, "Capot", "golpe", "5_10cm", new clsCoordsDiagrama(0.25, 0.75), AHORA);
            var evidencia = clsEvidencia.Crear(inspeccion.Id, hallazgo.Id, "foto.jpg", AHORA, 4.6, -74.1, AHORA);
            var repo = s.GetRequiredService<IInspeccionRepository>();
            repo.Agregar(inspeccion);
            repo.Agregar(hallazgo);
            repo.Agregar(evidencia);
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            inspeccionId = inspeccion.Id;
            hallazgoId = hallazgo.Id;
        }

        using var proveedor2 = Proveedor();
        using var alcance2 = proveedor2.CreateScope();
        var s2 = alcance2.ServiceProvider;
        var leida = await s2.GetRequiredService<IInspeccionRepository>().ObtenerPorIdAsync(inspeccionId);
        Assert.NotNull(leida);
        Assert.Equal(AHORA, leida.Fecha);
        Assert.Equal(4.60971, leida.Lat);
        Assert.Equal("nota con \"comillas\"", leida.Notas);
        Assert.Equal(clsEstadoInspeccion.BORRADOR, leida.Estado);

        var contexto = s2.GetRequiredService<clsAppDbContext>();
        var h = await contexto.Hallazgos.SingleAsync(x => x.Id == hallazgoId);
        Assert.Equal(0.25, h.CoordsDiagrama!.X);
        Assert.Equal(0.75, h.CoordsDiagrama.Y);
        var ev = await contexto.Evidencias.SingleAsync(x => x.InspeccionId == inspeccionId);
        Assert.Equal(hallazgoId, ev.HallazgoId);
        Assert.Equal(AHORA, ev.SelloFecha);
    }

    [Fact]
    public async Task Resumen_de_cierre_cuenta_evidencias_sin_geo_y_hallazgos()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;
        var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.INFORME, AHORA, null, null, null, null, AHORA);
        var repo = s.GetRequiredService<IInspeccionRepository>();
        repo.Agregar(inspeccion);
        repo.Agregar(clsHallazgo.Crear(inspeccion.Id, "Capot", "golpe", "1_5cm", null, AHORA));
        repo.Agregar(clsHallazgo.Crear(inspeccion.Id, "Techo", "mancha", "1_5cm", null, AHORA));
        repo.Agregar(clsEvidencia.Crear(inspeccion.Id, null, "a.jpg", AHORA, 4.6, -74.1, AHORA));
        repo.Agregar(clsEvidencia.Crear(inspeccion.Id, null, "b.jpg", AHORA, null, null, AHORA));
        repo.Agregar(clsEvidencia.Crear(inspeccion.Id, null, "c.jpg", AHORA, 4.6, null, AHORA));
        await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();

        var resumen = await repo.ObtenerResumenCierreAsync(inspeccion.Id);

        Assert.Equal(new clsResumenCierre(3, 2, 2), resumen);
    }

    [Fact]
    public async Task Correo_duplicado_se_traduce_a_conflicto_sin_distinguir_mayusculas()
    {
        await SembrarRutaAsync("uno@ejemplo.com");
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;
        s.GetRequiredService<IUsuarioRepository>().Agregar(clsUsuario.Crear("Otro", "UNO@Ejemplo.com", clsRol.ADMIN, null, "h", AHORA));

        var ex = await Assert.ThrowsAsync<clsConflictoException>(() => s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());

        Assert.Equal("Ya existe un usuario con ese correo", ex.Message);
        Assert.Equal(clsCodigosError.CONFLICT, ex.Codigo);
    }

    [Fact]
    public async Task Vin_duplicado_en_otra_capitalizacion_es_conflicto()
    {
        await SembrarRutaAsync(vin: "9FBHSR4E8PM000123");
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;
        s.GetRequiredService<IVehiculoRepository>().Agregar(NuevoVehiculo(await s.GetRequiredService<clsAppDbContext>().Clientes.Select(c => c.Id).FirstAsync(), "9FBHSR4E8PM000123"));

        var ex = await Assert.ThrowsAsync<clsConflictoException>(() => s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());

        Assert.Equal("Ya existe un vehículo con ese VIN", ex.Message);
    }

    [Fact]
    public async Task Etapa_con_orden_repetido_y_segundo_informe_de_la_etapa_son_conflictos_con_su_mensaje()
    {
        var (usuarioId, vehiculoId, etapas) = await SembrarRutaAsync();
        using var proveedor = Proveedor();

        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            s.GetRequiredService<IEtapaRepository>().Agregar(clsEtapa.Crear(vehiculoId, etapas[0].ViajeId, etapas[0].EtapaCatalogoId, null, 1, true, false, AHORA));
            var ex = await Assert.ThrowsAsync<clsConflictoException>(() => s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());
            Assert.Equal("Ya existe una etapa con ese orden para el vehículo en el viaje", ex.Message);
        }

        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var repo = s.GetRequiredService<IInspeccionRepository>();
            repo.Agregar(clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.INFORME, AHORA, null, null, null, null, AHORA));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        }

        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            s.GetRequiredService<IInspeccionRepository>().Agregar(
                clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.INFORME, AHORA, null, null, null, null, AHORA));
            var ex = await Assert.ThrowsAsync<clsConflictoException>(() => s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());
            Assert.Equal("Esta etapa ya tiene un informe registrado", ex.Message);
        }
    }

    [Fact]
    public async Task Referencias_inexistentes_se_traducen_a_no_encontrado()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;
        s.GetRequiredService<IInspeccionRepository>().Agregar(
            clsInspeccion.Crear(Guid.NewGuid(), etapas[0].ViajeId, usuarioId, clsMomento.INFORME, AHORA, null, null, null, null, AHORA));

        var ex = await Assert.ThrowsAsync<clsEntidadNoEncontradaException>(() => s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());

        Assert.Equal("La etapa o el usuario no existen", ex.Message);
        Assert.Equal(clsCodigosError.NOT_FOUND, ex.Codigo);
    }

    [Fact]
    public async Task El_trigger_de_la_base_impide_agregar_hallazgos_y_evidencias_a_una_inspeccion_cerrada()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        Guid inspeccionId;
        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var repo = s.GetRequiredService<IInspeccionRepository>();
            var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.INFORME, AHORA, null, null, null, null, AHORA);
            repo.Agregar(inspeccion);
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            inspeccion.Cerrar(await repo.ObtenerResumenCierreAsync(inspeccion.Id), AHORA.AddMinutes(1));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            inspeccionId = inspeccion.Id;
        }

        using var proveedor2 = Proveedor();
        foreach (var agregar in new Action<IInspeccionRepository>[]
        {
            r => r.Agregar(clsHallazgo.Crear(inspeccionId, "Capot", "golpe", "1_5cm", null, AHORA)),
            r => r.Agregar(clsEvidencia.Crear(inspeccionId, null, "x.jpg", AHORA, null, null, AHORA)),
        })
        {
            using var alcance2 = proveedor2.CreateScope();
            var s2 = alcance2.ServiceProvider;
            agregar(s2.GetRequiredService<IInspeccionRepository>());
            var ex = await Assert.ThrowsAsync<clsConflictoException>(() => s2.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());
            Assert.Contains("inmutable", ex.Message);
        }
    }

    [Fact]
    public async Task Cerrar_persiste_el_estado_y_una_inspeccion_cerrada_no_vuelve_a_modificarse_en_base_de_datos()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;
        var repo = s.GetRequiredService<IInspeccionRepository>();
        var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.INFORME, AHORA, null, null, null, null, AHORA);
        repo.Agregar(inspeccion);
        await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        inspeccion.Cerrar(await repo.ObtenerResumenCierreAsync(inspeccion.Id), AHORA.AddMinutes(1));
        await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();

        var contexto = s.GetRequiredService<clsAppDbContext>();
        var estado = await contexto.Database
            .SqlQuery<string>($"SELECT Estado AS Value FROM dbo.tblInspecciones WHERE Id = {inspeccion.Id}")
            .SingleAsync();
        Assert.Equal(clsEstadoInspeccion.CERRADA, estado);

        var ex = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            contexto.Database.ExecuteSqlAsync($"UPDATE dbo.tblInspecciones SET Notas = N'cambio' WHERE Id = {inspeccion.Id}"));
        Assert.Equal(51000, ex.Number);
    }

    [Fact]
    public async Task Usuario_sin_historial_se_elimina_y_con_historial_se_detecta()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync("con.historial@ejemplo.com");
        Guid libreId;
        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var repoU = s.GetRequiredService<IUsuarioRepository>();
            var libre = NuevoUsuario("libre@ejemplo.com");
            repoU.Agregar(libre);
            s.GetRequiredService<IInspeccionRepository>().Agregar(
                clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.INFORME, AHORA, null, null, null, null, AHORA));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            libreId = libre.Id;
        }

        using var proveedor2 = Proveedor();
        using var alcance2 = proveedor2.CreateScope();
        var s2 = alcance2.ServiceProvider;
        var repo = s2.GetRequiredService<IUsuarioRepository>();
        Assert.True(await repo.TieneHistorialAsync(usuarioId));
        Assert.False(await repo.TieneHistorialAsync(libreId));

        var libreEntidad = await repo.ObtenerPorIdAsync(libreId);
        repo.Eliminar(libreEntidad!);
        await s2.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        Assert.Null(await repo.ObtenerPorIdAsync(libreId));
    }

    [Fact]
    public async Task Busqueda_por_correo_y_por_vines_no_distingue_mayusculas()
    {
        await SembrarRutaAsync("Mixto@Ejemplo.com", "ABC123XYZ");
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;

        Assert.NotNull(await s.GetRequiredService<IUsuarioRepository>().ObtenerPorEmailAsync("  MIXTO@ejemplo.COM "));
        Assert.NotNull(await s.GetRequiredService<IVehiculoRepository>().ObtenerPorVinAsync("abc123xyz"));
        var lote = await s.GetRequiredService<IVehiculoRepository>().ObtenerPorVinesAsync(["abc123xyz", "no-existe"]);
        Assert.Single(lote);
        Assert.True(lote.ContainsKey("ABC123XYZ"));
    }

    [Fact]
    public async Task Transferencia_se_completa_en_dos_firmas_y_solo_hay_una_por_inspeccion()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        Guid receptorId;
        Guid inspeccionId;
        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var receptor = NuevoUsuario("recibe@ejemplo.com", clsRol.CONDUCTOR);
            s.GetRequiredService<IUsuarioRepository>().Agregar(receptor);
            var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.INFORME, AHORA, null, null, null, null, AHORA);
            s.GetRequiredService<IInspeccionRepository>().Agregar(inspeccion);
            s.GetRequiredService<ITransferenciaCustodiaRepository>().Agregar(
                clsTransferenciaCustodia.Crear(inspeccion.Id, usuarioId, receptor.Id, "entrega.png", null, AHORA));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            receptorId = receptor.Id;
            inspeccionId = inspeccion.Id;
        }

        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var repo = s.GetRequiredService<ITransferenciaCustodiaRepository>();
            var t = await repo.ObtenerPorInspeccionAsync(inspeccionId);
            Assert.NotNull(t);
            Assert.Null(t.FirmadaAt);
            t.AplicarFirma(usuarioId, receptorId, null, "recibe.png", AHORA.AddMinutes(2));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        }

        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var t = await s.GetRequiredService<ITransferenciaCustodiaRepository>().ObtenerPorInspeccionAsync(inspeccionId);
            Assert.True(t!.EstaCompleta);
            Assert.Equal(AHORA.AddMinutes(2), t.FirmadaAt);

            s.GetRequiredService<ITransferenciaCustodiaRepository>().Agregar(
                clsTransferenciaCustodia.Crear(inspeccionId, usuarioId, receptorId, "a.png", "b.png", AHORA));
            await Assert.ThrowsAsync<clsConflictoException>(() => s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());
        }
    }

    [Fact]
    public async Task Orden_sofasa_conserva_decimales_fechas_y_se_actualiza_por_source_id()
    {
        using var proveedor = Proveedor();
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var datos = new clsDatosOrdenSofasa(900001, "EN_RUTA", AHORA, "OC-1", null, null, "VINSOFASA00000001", "Renault",
                "Duster \"Oroch\"", null, "Bogotá", "Calle 1\nBodega 'Norte'", null, null, null, 1234.5678m);
            s.GetRequiredService<IOrdenSofasaRepository>().Agregar(clsOrdenSofasa.Crear(datos, null, AHORA));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        }

        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var repo = s.GetRequiredService<IOrdenSofasaRepository>();
            var orden = await repo.ObtenerPorSourceIdAsync(900001);
            Assert.NotNull(orden);
            Assert.Equal(1234.5678m, orden.Weight);
            Assert.Equal(AHORA, orden.DatetimeEvent);
            Assert.Equal("Calle 1\nBodega 'Norte'", orden.DestinationAddress);
            orden.Aplicar(new clsDatosOrdenSofasa(900001, "ENTREGADA", null, null, null, null, null, null, null, null, null, null, null, null, null, null), null, AHORA.AddHours(1));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        }

        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var orden = await s.GetRequiredService<IOrdenSofasaRepository>().ObtenerPorSourceIdAsync(900001);
            Assert.Equal("ENTREGADA", orden!.StatusCode);
            Assert.Null(orden.Weight);
            Assert.Equal(AHORA.AddHours(1), orden.SyncedAt);
        }
    }

    [Fact]
    public async Task La_auditoria_acepta_el_usuario_del_sistema_nulo()
    {
        var (usuarioId, vehiculoId, _) = await SembrarRutaAsync();
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;
        s.GetRequiredService<IAuditoriaRepository>().Agregar(clsAuditLog.Crear(null, "sofasa.ingesta", "ordenes", null, AHORA));
        s.GetRequiredService<IAuditoriaRepository>().Agregar(clsAuditLog.Crear(usuarioId, "vehiculos.cargar", "vehiculos", vehiculoId, AHORA));

        Assert.Equal(2, await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());
        var contexto = s.GetRequiredService<clsAppDbContext>();
        Assert.Equal(2, await contexto.AuditLog.CountAsync());
        Assert.Equal(1, await contexto.AuditLog.CountAsync(a => a.UsuarioId == null));
    }

    [Fact]
    public async Task Descartar_cambios_deja_el_contexto_limpio_tras_un_fallo()
    {
        await SembrarRutaAsync("uno@ejemplo.com");
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;
        var uow = s.GetRequiredService<IUnidadDeTrabajo>();
        var repo = s.GetRequiredService<IUsuarioRepository>();
        repo.Agregar(NuevoUsuario("uno@ejemplo.com"));
        await Assert.ThrowsAsync<clsConflictoException>(() => uow.GuardarCambiosAsync());

        uow.DescartarCambios();
        repo.Agregar(NuevoUsuario("dos@ejemplo.com"));

        Assert.Equal(1, await uow.GuardarCambiosAsync());
        Assert.NotNull(await repo.ObtenerPorEmailAsync("dos@ejemplo.com"));
    }
}
