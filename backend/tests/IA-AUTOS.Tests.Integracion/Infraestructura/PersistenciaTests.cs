using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Catalogos;
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

[Collection(clsColecciones.BASE_DE_DATOS_3)]
public sealed class PersistenciaTests(clsBaseDatosFixture bd) : IAsyncLifetime
{
    public Task InitializeAsync() => bd.LimpiarAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private ServiceProvider Proveedor() => CrearProveedor(bd.CadenaConexion);

    /// <summary>Crea usuario, vehiculo con su ruta estandar y devuelve los ids para armar inspecciones.</summary>
    /// <summary>La zona del cuadrante 50 «Frente» (sin lado, del dibujo base de las pruebas): una zona que siempre existe para los hallazgos de prueba.</summary>
    private static Task<Guid> ZonaFrenteAsync(IServiceProvider proveedor) =>
        proveedor.GetRequiredService<clsAppDbContext>().ZonasDano.Where(z => z.Numero == 50).Select(z => z.Id).FirstAsync();

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
        var etapas = clsRutaCustodia.CrearEtapasEstandar(viaje.Id, vehiculo.Id, ruta, null, null, AHORA);

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
        Guid zonaId;

        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, 4.60971, -74.08175, null,
                "nota con \"comillas\"", AHORA);
            var hallazgo = clsHallazgo.Crear(
                inspeccion.Id, await ZonaFrenteAsync(s), "golpe", "5_10cm", "v2", clsOrigenHallazgo.INSPECTOR, "con nota", null, null, AHORA);
            var evidencia = clsEvidencia.Crear(inspeccion.Id, hallazgo.Id, null, "foto.jpg", AHORA, 4.6, -74.1, 7.5, "tablet-1", new string('b', 64), "nota", AHORA.AddSeconds(2));
            var repo = s.GetRequiredService<IInspeccionRepository>();
            repo.Agregar(inspeccion);
            repo.Agregar(hallazgo);
            repo.Agregar(evidencia);
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            inspeccionId = inspeccion.Id;
            hallazgoId = hallazgo.Id;
            zonaId = hallazgo.ZonaId!.Value;
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
        Assert.Equal(zonaId, h.ZonaId);
        Assert.Equal("v2", h.NivelSeveridad);
        Assert.Equal(clsOrigenHallazgo.INSPECTOR, h.Origen);
        Assert.Equal("con nota", h.Nota);
        Assert.Equal(clsEstadoSeguimiento.PENDIENTE, h.EstadoSeguimiento);
        Assert.False(h.EsPreexistente);
        Assert.Null(h.ParteVehiculo);
        Assert.Null(h.CoordsDiagrama);
        var ev = await contexto.Evidencias.SingleAsync(x => x.InspeccionId == inspeccionId);
        Assert.Equal(hallazgoId, ev.HallazgoId);
        Assert.Equal(AHORA, ev.SelloFecha);
        Assert.Equal(AHORA.AddSeconds(2), ev.SelloServidorAt);
        Assert.Equal(7.5, ev.Precision);
        Assert.Equal("tablet-1", ev.DispositivoId);
        Assert.Equal(new string('b', 64), ev.HashSha256);
        Assert.Equal("nota", ev.Nota);
        Assert.Equal(AHORA, leida.IniciadaAt);
        Assert.Null(leida.CerradaAt);
        Assert.Equal(1, leida.Version);
    }

    [Fact]
    public async Task Resumen_de_cierre_cuenta_fotos_sin_ubicacion_hallazgos_sin_foto_y_vistas_fotografiadas()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;
        var vistas = await s.GetRequiredService<clsAppDbContext>().VistasFoto.OrderBy(v => v.Orden).Select(v => v.Id).Take(3).ToListAsync();
        var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, null, null, null, null, AHORA);
        var repo = s.GetRequiredService<IInspeccionRepository>();
        repo.Agregar(inspeccion);
        var zonaId = await ZonaFrenteAsync(s);
        var conFoto = clsHallazgo.Crear(inspeccion.Id, zonaId, "golpe", "1_5cm", "v1", clsOrigenHallazgo.INSPECTOR, null, null, null, AHORA);
        repo.Agregar(conFoto);
        repo.Agregar(clsHallazgo.Crear(inspeccion.Id, zonaId, "mancha", "1_5cm", "v3", clsOrigenHallazgo.INSPECTOR, null, null, null, AHORA));
        repo.Agregar(clsEvidencia.Crear(inspeccion.Id, conFoto.Id, null, "h.jpg", AHORA, 4.6, -74.1, null, null, null, null, AHORA));
        // Vista 0: primero sin ubicacion y luego repetida con ubicacion (la ultima reemplaza a la anterior).
        repo.Agregar(clsEvidencia.Crear(inspeccion.Id, null, vistas[0], "a1.jpg", AHORA, null, null, null, null, null, null, AHORA));
        repo.Agregar(clsEvidencia.Crear(inspeccion.Id, null, vistas[0], "a2.jpg", AHORA, 4.6, -74.1, null, null, null, null, AHORA.AddSeconds(5)));
        // Vista 1: con ubicacion; vista 2: sin ubicacion y sin repetir.
        repo.Agregar(clsEvidencia.Crear(inspeccion.Id, null, vistas[1], "b.jpg", AHORA, 4.6, -74.1, null, null, null, null, AHORA));
        repo.Agregar(clsEvidencia.Crear(inspeccion.Id, null, vistas[2], "c.jpg", AHORA, 4.6, null, null, null, null, null, AHORA));
        await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();

        var resumen = await repo.ObtenerResumenCierreAsync(inspeccion.Id);

        Assert.Equal(5, resumen.TotalEvidencias);
        Assert.Equal(1, resumen.EvidenciasSinUbicacion);
        Assert.Equal(2, resumen.TotalHallazgos);
        Assert.Equal(1, resumen.HallazgosSinEvidencia);
        Assert.Equal(3, resumen.VistasFotografiadas.Count);
        Assert.All(vistas, v => Assert.Contains(v, resumen.VistasFotografiadas));
    }

    [Fact]
    public async Task Las_correcciones_forman_una_cadena_y_la_base_impide_corregir_dos_veces_la_misma_version()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        Guid originalId;
        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var repo = s.GetRequiredService<IInspeccionRepository>();
            var original = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, null, null, null, null, AHORA);
            repo.Agregar(original);
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            original.Cerrar(await repo.ObtenerResumenCierreAsync(original.Id), [], false, AHORA.AddMinutes(1));
            repo.Agregar(original.Corregir(usuarioId, AHORA.AddHours(1), null, null, null, AHORA.AddHours(1)));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            originalId = original.Id;
        }

        using var proveedor2 = Proveedor();
        using var alcance2 = proveedor2.CreateScope();
        var s2 = alcance2.ServiceProvider;
        var repo2 = s2.GetRequiredService<IInspeccionRepository>();
        var version2 = await repo2.ObtenerCorreccionIdAsync(originalId);
        Assert.NotNull(version2);
        var leida = await repo2.ObtenerPorIdAsync(originalId);
        Assert.True(leida!.EstaCerrada);
        Assert.NotNull(leida.CerradaAt);

        // Una segunda correccion de la misma version no entra (cada version se corrige una sola vez).
        repo2.Agregar(leida.Corregir(usuarioId, AHORA.AddHours(2), null, null, null, AHORA.AddHours(2)));
        var ex = await Assert.ThrowsAsync<clsConflictoException>(() => s2.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());
        Assert.Equal("Esa inspección ya fue corregida: corrija la última versión", ex.Message);
    }

    [Fact]
    public async Task La_etapa_tiene_llegada_cerrada_solo_si_su_llegada_esta_cerrada()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var s = alcance.ServiceProvider;
        var repo = s.GetRequiredService<IInspeccionRepository>();
        var uow = s.GetRequiredService<IUnidadDeTrabajo>();
        var salida = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, null, null, null, null, AHORA);
        var llegada = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.LLEGADA, AHORA, null, null, null, null, AHORA);
        repo.Agregar(salida);
        repo.Agregar(llegada);
        await uow.GuardarCambiosAsync();
        Assert.False(await repo.TieneLlegadaCerradaAsync(etapas[0].Id));

        salida.Cerrar(await repo.ObtenerResumenCierreAsync(salida.Id), [], false, AHORA);
        await uow.GuardarCambiosAsync();
        Assert.False(await repo.TieneLlegadaCerradaAsync(etapas[0].Id));

        llegada.Cerrar(await repo.ObtenerResumenCierreAsync(llegada.Id), [], false, AHORA);
        await uow.GuardarCambiosAsync();
        Assert.True(await repo.TieneLlegadaCerradaAsync(etapas[0].Id));
        Assert.False(await repo.TieneLlegadaCerradaAsync(etapas[1].Id));
    }

    [Fact]
    public async Task La_etapa_previa_es_la_de_mayor_orden_menor_del_mismo_vin_y_viaje()
    {
        var (_, vehiculoId, etapas) = await SembrarRutaAsync();
        using var proveedor = Proveedor();
        using var alcance = proveedor.CreateScope();
        var repo = alcance.ServiceProvider.GetRequiredService<IEtapaRepository>();

        Assert.Null(await repo.ObtenerPreviaAsync(etapas[0].ViajeId, vehiculoId, 1));
        Assert.Equal(etapas[0].Id, (await repo.ObtenerPreviaAsync(etapas[0].ViajeId, vehiculoId, 2))!.Id);
        Assert.Equal(etapas[1].Id, (await repo.ObtenerPreviaAsync(etapas[0].ViajeId, vehiculoId, 3))!.Id);
        Assert.Null(await repo.ObtenerPreviaAsync(Guid.NewGuid(), vehiculoId, 3));
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
            s.GetRequiredService<IEtapaRepository>().Agregar(clsEtapa.Crear(vehiculoId, etapas[0].ViajeId, etapas[0].EtapaCatalogoId, null, 1, true, AHORA));
            var ex = await Assert.ThrowsAsync<clsConflictoException>(() => s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());
            Assert.Equal("Ya existe una etapa con ese orden para el vehículo en el viaje", ex.Message);
        }

        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var repo = s.GetRequiredService<IInspeccionRepository>();
            repo.Agregar(clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, null, null, null, null, AHORA));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        }

        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            s.GetRequiredService<IInspeccionRepository>().Agregar(
                clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, null, null, null, null, AHORA));
            var ex = await Assert.ThrowsAsync<clsConflictoException>(() => s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());
            Assert.Equal("Esta etapa ya tiene una inspección de ese momento: para cambiarla cree una corrección", ex.Message);
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
            clsInspeccion.Crear(Guid.NewGuid(), etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, null, null, null, null, AHORA));

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
            var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, null, null, null, null, AHORA);
            repo.Agregar(inspeccion);
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            inspeccion.Cerrar(await repo.ObtenerResumenCierreAsync(inspeccion.Id), [], false, AHORA.AddMinutes(1));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            inspeccionId = inspeccion.Id;
        }

        using var proveedor2 = Proveedor();
        Guid vistaId;
        Guid zonaId;
        using (var alcanceVista = proveedor2.CreateScope())
        {
            vistaId = await alcanceVista.ServiceProvider.GetRequiredService<clsAppDbContext>().VistasFoto.Select(v => v.Id).FirstAsync();
            zonaId = await ZonaFrenteAsync(alcanceVista.ServiceProvider);
        }

        foreach (var agregar in new Action<IInspeccionRepository>[]
        {
            r => r.Agregar(clsHallazgo.Crear(inspeccionId, zonaId, "golpe", "1_5cm", "v1", clsOrigenHallazgo.INSPECTOR, null, null, null, AHORA)),
            r => r.Agregar(clsEvidencia.Crear(inspeccionId, null, vistaId, "x.jpg", AHORA, 4.6, -74.1, null, null, null, null, AHORA)),
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
        var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, null, null, null, null, AHORA);
        repo.Agregar(inspeccion);
        await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        inspeccion.Cerrar(await repo.ObtenerResumenCierreAsync(inspeccion.Id), [], false, AHORA.AddMinutes(1));
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
                clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, null, null, null, null, AHORA));
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
    public async Task Transferencia_se_completa_en_dos_firmas_con_su_fecha_e_ip_y_solo_hay_una_por_inspeccion()
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
            var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.SALIDA, AHORA, null, null, null, null, AHORA);
            s.GetRequiredService<IInspeccionRepository>().Agregar(inspeccion);
            var transferencia = clsTransferenciaCustodia.Crear(inspeccion.Id, usuarioId, receptor.Id, AHORA);
            transferencia.Firmar(usuarioId, "entrega.png", "10.0.0.1", AHORA);
            s.GetRequiredService<ITransferenciaCustodiaRepository>().Agregar(transferencia);
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
            Assert.Null(t.TipoCierre);
            Assert.Equal("10.0.0.1", t.FirmaEntregaIp);
            t.Firmar(receptorId, "recibe.png", "10.0.0.2", AHORA.AddMinutes(2));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        }

        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var t = await s.GetRequiredService<ITransferenciaCustodiaRepository>().ObtenerPorInspeccionAsync(inspeccionId);
            Assert.True(t!.EstaCompleta);
            Assert.Equal(AHORA.AddMinutes(2), t.FirmadaAt);
            Assert.Equal(AHORA, t.FirmaEntregaAt);
            Assert.Equal(AHORA.AddMinutes(2), t.FirmaRecibeAt);
            Assert.Equal("10.0.0.2", t.FirmaRecibeIp);
            Assert.Equal(clsTipoCierre.FIRMADA, t.TipoCierre);

            s.GetRequiredService<ITransferenciaCustodiaRepository>().Agregar(clsTransferenciaCustodia.Crear(inspeccionId, usuarioId, receptorId, AHORA));
            await Assert.ThrowsAsync<clsConflictoException>(() => s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync());
        }
    }

    [Fact]
    public async Task Una_transferencia_sin_tipo_de_cierre_bloquea_la_etapa_y_con_reservas_la_libera()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        Guid receptorId;
        Guid inspeccionId;
        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var receptor = NuevoUsuario("reservas@ejemplo.com", clsRol.CONDUCTOR);
            s.GetRequiredService<IUsuarioRepository>().Agregar(receptor);
            var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.LLEGADA, AHORA, null, null, null, null, AHORA);
            s.GetRequiredService<IInspeccionRepository>().Agregar(inspeccion);
            var transferencia = clsTransferenciaCustodia.Crear(inspeccion.Id, usuarioId, receptor.Id, AHORA);
            transferencia.Firmar(usuarioId, "entrega.png", null, AHORA);
            s.GetRequiredService<ITransferenciaCustodiaRepository>().Agregar(transferencia);
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            receptorId = receptor.Id;
            inspeccionId = inspeccion.Id;
        }

        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            Assert.True(await s.GetRequiredService<IInspeccionRepository>().TieneTransferenciaSinCierreAsync(etapas[0].Id));
            Assert.False(await s.GetRequiredService<IInspeccionRepository>().TieneTransferenciaSinCierreAsync(etapas[1].Id));

            var t = await s.GetRequiredService<ITransferenciaCustodiaRepository>().ObtenerPorInspeccionAsync(inspeccionId);
            t!.FirmarConReservas(receptorId, "recibe.png", "El espejo está rayado", "10.0.0.7", AHORA.AddMinutes(1));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
        }

        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            Assert.False(await s.GetRequiredService<IInspeccionRepository>().TieneTransferenciaSinCierreAsync(etapas[0].Id));
            var t = await s.GetRequiredService<ITransferenciaCustodiaRepository>().ObtenerPorInspeccionAsync(inspeccionId);
            Assert.Equal(clsTipoCierre.CON_RESERVAS, t!.TipoCierre);
            Assert.Equal("El espejo está rayado", t.ReservasTexto);
        }
    }

    [Fact]
    public async Task La_constancia_de_no_firma_se_guarda_y_su_foto_se_admite_aunque_la_inspeccion_este_cerrada()
    {
        var (usuarioId, _, etapas) = await SembrarRutaAsync();
        Guid receptorId;
        Guid testigoId;
        Guid inspeccionId;
        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var receptor = NuevoUsuario("niega@ejemplo.com", clsRol.CONDUCTOR);
            var testigo = NuevoUsuario("testigo@ejemplo.com", clsRol.INSPECTOR);
            s.GetRequiredService<IUsuarioRepository>().Agregar(receptor);
            s.GetRequiredService<IUsuarioRepository>().Agregar(testigo);
            var repo = s.GetRequiredService<IInspeccionRepository>();
            var inspeccion = clsInspeccion.Crear(etapas[0].Id, etapas[0].ViajeId, usuarioId, clsMomento.LLEGADA, AHORA, null, null, null, null, AHORA);
            repo.Agregar(inspeccion);
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            inspeccion.Cerrar(await repo.ObtenerResumenCierreAsync(inspeccion.Id), [], false, AHORA.AddMinutes(1));
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            receptorId = receptor.Id;
            testigoId = testigo.Id;
            inspeccionId = inspeccion.Id;
        }

        Guid fotoId;
        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var repo = s.GetRequiredService<IInspeccionRepository>();
            var foto = clsEvidencia.CrearConstancia(inspeccionId, "constancia.jpg", AHORA, 4.6, -74.1, null, "tablet-1", new string('c', 64), null, AHORA.AddMinutes(2));
            repo.Agregar(foto);
            var transferencia = clsTransferenciaCustodia.Crear(inspeccionId, usuarioId, receptorId, AHORA);
            transferencia.RegistrarNoFirma(usuarioId, clsParteFirmante.RECIBE, "Se niega a firmar", testigoId, foto.Id, AHORA.AddMinutes(2));
            s.GetRequiredService<ITransferenciaCustodiaRepository>().Agregar(transferencia);
            await s.GetRequiredService<IUnidadDeTrabajo>().GuardarCambiosAsync();
            fotoId = foto.Id;
        }

        using (var proveedor = Proveedor())
        using (var alcance = proveedor.CreateScope())
        {
            var s = alcance.ServiceProvider;
            var t = await s.GetRequiredService<ITransferenciaCustodiaRepository>().ObtenerPorInspeccionAsync(inspeccionId);
            Assert.Equal(clsTipoCierre.NO_FIRMA, t!.TipoCierre);
            Assert.Equal(testigoId, t.TestigoUsuarioId);
            Assert.Equal(fotoId, t.EvidenciaConstanciaId);
            Assert.True((await s.GetRequiredService<IInspeccionRepository>().ObtenerEvidenciaAsync(fotoId))!.EsConstancia);
            Assert.Equal(0, (await s.GetRequiredService<IInspeccionRepository>().ObtenerResumenCierreAsync(inspeccionId)).TotalEvidencias);
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
