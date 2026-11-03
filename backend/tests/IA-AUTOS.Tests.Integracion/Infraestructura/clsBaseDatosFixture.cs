using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>
/// Crea SU PROPIA base en el SQL Server compartido (<see cref="clsServidorSql"/>) con los scripts del proyecto (database/00-crear-base.sql y database/ddl.sql). Se comparte entre las pruebas de una
/// coleccion, que corren una tras otra; las colecciones corren en paralelo, cada una con su base, asi que no hay estado compartido entre ellas.
/// </summary>
public sealed class clsBaseDatosFixture : IAsyncLifetime
{
    private const string BASE = "IAAutos";

    /// <summary>Cadena de conexion a la base IAAutos del contenedor.</summary>
    public string CadenaConexion { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        CadenaConexion = await clsServidorSql.CrearBaseAsync(BASE);
        await clsServidorSql.EjecutarScriptAsync(CadenaConexion, Path.Combine(AppContext.BaseDirectory, "database", "ddl.sql"));
        await EjecutarAsync(DIBUJO_BASE);
    }

    /// <summary>
    /// El dibujo minimo de las pruebas (una base nueva no trae dibujo: lo siembra el guion 34 desde el archivo): cinco paneles y dos cuadrantes sin lado, 50 «Frente» y 51 «Llanta», que dan a los hallazgos
    /// de prueba una zona que siempre existe. Los cuadrantes con lado los crea cada prueba que los necesita.
    /// </summary>
    public const string DIBUJO_BASE = """
        INSERT INTO dbo.tblPanelesDano (Id, Codigo, Nombre, Orden)
        SELECT NEWID(), v.Codigo, v.Nombre, v.Orden
        FROM (VALUES ('superior', N'Superior', 1), ('perfil_posterior_izquierdo', N'Perfil posterior izquierdo', 2), ('perfil_posterior_derecho', N'Perfil posterior derecho', 3),
                     ('costado_izquierdo', N'Costado izquierdo', 4), ('costado_derecho', N'Costado derecho', 5)) AS v (Codigo, Nombre, Orden)
        WHERE NOT EXISTS (SELECT 1 FROM dbo.tblPanelesDano p WHERE p.Codigo = v.Codigo);
        INSERT INTO dbo.tblCuadrantes (Id, TipoVehiculo, Numero, Nombre, Orden, LlevaLado)
        SELECT NEWID(), NULL, v.Numero, v.Nombre, v.Numero, 0
        FROM (VALUES (50, N'Frente'), (51, N'Llanta')) AS v (Numero, Nombre)
        WHERE NOT EXISTS (SELECT 1 FROM dbo.tblCuadrantes c WHERE c.TipoVehiculo IS NULL AND c.Numero = v.Numero);
        """;

    public async Task DisposeAsync() => await clsServidorSql.EliminarBaseAsync(CadenaConexion);

    /// <summary>
    /// Borra todos los datos de negocio (conserva los catalogos). La base protege la evidencia con triggers de no borrado: se apagan solo
    /// mientras dura la limpieza y se vuelven a encender siempre, aunque falle.
    /// </summary>
    public async Task LimpiarAsync()
    {
        const string SQL = """
            BEGIN TRY
            ALTER TABLE dbo.tblTransferenciasCustodia DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblEvidencias DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgos DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspecciones DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblAuditLog DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgoSeguimiento DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNotificaciones DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNotasAclaratorias DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNovedadesProceso DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblReclamos DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspeccionExtras DISABLE TRIGGER ALL;
            ALTER TABLE dbo.tblPropuestasUbicacion DISABLE TRIGGER ALL;
            DELETE FROM dbo.tblPropuestasUbicacion;
            UPDATE dbo.tblLugares SET UbicacionFijadaPor = NULL;
            DELETE FROM dbo.tblNovedadesProceso;
            DELETE FROM dbo.tblReclamos;
            DELETE FROM dbo.tblInspectorLugares;
            DELETE FROM dbo.tblNotasAclaratorias;
            DELETE FROM dbo.tblNotificaciones;
            DELETE FROM dbo.tblDispositivos;
            DELETE FROM dbo.tblHallazgoSeguimiento;
            DELETE FROM dbo.tblRefreshTokens;
            DELETE FROM dbo.tblAuditLog;
            DELETE FROM dbo.tblOrdenesSofasa;
            DELETE FROM dbo.tblTransferenciasCustodia;
            UPDATE dbo.tblInspecciones SET ConfirmacionEvidenciaId = NULL;
            DELETE FROM dbo.tblEvidencias;
            DELETE FROM dbo.tblInspeccionExtras;
            DELETE FROM dbo.tblViajeVehiculos;
            DELETE FROM dbo.tblHallazgos;
            DELETE FROM dbo.tblInspecciones;
            DELETE FROM dbo.tblEtapas;
            DELETE FROM dbo.tblViajes;
            DELETE FROM dbo.tblVehiculos;
            UPDATE dbo.tblParametros SET ActualizadoPor = NULL;
            DELETE FROM dbo.tblUsuarios;
            ALTER TABLE dbo.tblTransferenciasCustodia ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblEvidencias ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblAuditLog ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgoSeguimiento ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNotasAclaratorias ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNovedadesProceso ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblReclamos ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspeccionExtras ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblPropuestasUbicacion ENABLE TRIGGER ALL;
            END TRY
            BEGIN CATCH
            ALTER TABLE dbo.tblTransferenciasCustodia ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblEvidencias ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgos ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspecciones ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblAuditLog ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblHallazgoSeguimiento ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNotificaciones ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNotasAclaratorias ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblNovedadesProceso ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblReclamos ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblInspeccionExtras ENABLE TRIGGER ALL;
            ALTER TABLE dbo.tblPropuestasUbicacion ENABLE TRIGGER ALL;
            THROW;
            END CATCH

            -- Catalogos: vuelven a su estado inicial (sin valores creados por las pruebas, todos activos y sin cambios de forma).
            DELETE FROM dbo.tblCuadrantes;
            DELETE FROM dbo.tblLugares;
            DELETE FROM dbo.tblEstadosSofasa;
            DELETE FROM dbo.tblTiposExtra WHERE Codigo NOT IN ('barras_techo', 'volco', 'platon', 'otro');
            DELETE FROM dbo.tblMotivosSinInspector WHERE Codigo NOT IN ('inspector_no_llego', 'inspector_no_disponible', 'otro');
            DELETE FROM dbo.tblEtapasCatalogo WHERE Codigo NOT IN ('cargue', 'transporte', 'descargue');
            DELETE FROM dbo.tblPuntosCustodia WHERE Codigo NOT IN ('cedi_1', 'origen', 'certificadora', 'destino', 'cedi_2', 'concesionario');
            DELETE FROM dbo.tblClientes WHERE Codigo <> 'sofasa';
            DELETE FROM dbo.tblTiposUnidadTransportadora WHERE Codigo NOT IN ('ninera', 'nodriza', 'quintinaria', 'planchon_grua');
            DELETE FROM dbo.tblClasesDano WHERE Codigo NOT IN ('deformacion', 'rotura', 'cortado', 'perforacion', 'golpe', 'mancha', 'raya_pintar', 'desconchado', 'particulas', 'faltante');
            DELETE FROM dbo.tblTamanosDano WHERE Codigo NOT IN ('1_5cm', '5_10cm', '10_20cm', 'mas_20cm', 'requiere_reemplazo', 'mas_10_particulas');
            DELETE FROM dbo.tblNivelesSeveridad WHERE Codigo NOT IN ('v1_plus', 'v1', 'v2', 'v3');
            DELETE FROM dbo.tblVistasFoto WHERE Codigo NOT IN ('frontal', 'trasera', 'lateral_derecha', 'lateral_izquierda', 'diagonal_trasera_izquierda', 'diagonal_delantera_derecha', 'superior', 'piezas_baul', 'cojineria');
            UPDATE dbo.tblEtapasCatalogo SET Activo = 1;
            UPDATE dbo.tblPuntosCustodia SET Activo = 1;
            UPDATE dbo.tblTiposUnidadTransportadora SET Activo = 1;
            UPDATE dbo.tblClientes SET Activo = 1, Orden = 1;
            UPDATE dbo.tblMotivosSinInspector SET Activo = 1;
            UPDATE dbo.tblTiposExtra SET Activo = 1;
            UPDATE dbo.tblClasesDano SET Activo = 1;
            UPDATE dbo.tblTamanosDano SET Activo = 1;
            UPDATE dbo.tblNivelesSeveridad SET Activo = 1, Accion = CASE Codigo
                WHEN 'v1_plus' THEN N'Se recupera antes del despacho'
                WHEN 'v1' THEN N'Se rechaza el veh' + NCHAR(237) + N'culo'
                WHEN 'v2' THEN N'Validaci' + NCHAR(243) + N'n por Sofasa'
                ELSE N'Se deroga (se acepta el defecto)' END;
            DELETE FROM dbo.tblParametros WHERE Clave LIKE 'zz%';
            UPDATE dbo.tblParametros SET Valor = CASE Clave
                WHEN 'objetivo_diario_novedad' THEN N'1.2' WHEN 'reporte_diario_hora' THEN N'06:00' WHEN 'horario_operacion_inicio' THEN N'08:00' WHEN 'horario_operacion_fin' THEN N'21:00'
                WHEN 'gps_precision_minima_m' THEN N'30' WHEN 'url_firmada_minutos' THEN N'15' WHEN 'niveles_criticos' THEN N'v1_plus,v1' WHEN 'umbral_patron_cantidad' THEN N'10'
                WHEN 'umbral_patron_ventana_dias' THEN N'30' WHEN 'captura_incompleta_minutos' THEN N'30' WHEN 'transferencia_pendiente_minutos' THEN N'240' WHEN 'reclamo_sin_actividad_minutos' THEN N'30' WHEN 'acomodacion_fotos_minimas' THEN N'1' WHEN 'radio_lugar_cargue_m' THEN N'1000' WHEN 'radio_destino_m' THEN N'500' WHEN 'valvula_precision_m' THEN N'500' WHEN 'cargue_sin_reclamar_minutos' THEN N'30' ELSE NULL END,
                ActualizadoPor = NULL;
            DELETE FROM dbo.tblPanelesDano WHERE Codigo NOT IN ('superior', 'perfil_posterior_izquierdo', 'perfil_posterior_derecho', 'costado_izquierdo', 'costado_derecho');
            UPDATE dbo.tblPanelesDano SET Activo = 1, ImagenPath = NULL;
            UPDATE dbo.tblZonasDano SET Activo = 1 WHERE CuadranteId IS NULL;
            UPDATE dbo.tblVistasFoto SET Activo = 1, TipoVehiculo = NULL, Obligatoria = CASE WHEN Codigo IN ('piezas_baul', 'cojineria') THEN 0 ELSE 1 END;
            """;
        await using var conexion = new SqlConnection(CadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(SQL, conexion);
        await comando.ExecuteNonQueryAsync();
        await EjecutarAsync(DIBUJO_BASE);
    }

    /// <summary>Ejecuta una sentencia suelta sobre la base de la prueba (para forzar estados, por ejemplo un token vencido).</summary>
    public async Task EjecutarAsync(string sql)
    {
        await using var conexion = new SqlConnection(CadenaConexion);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion);
        await comando.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Las colecciones de pruebas que usan una base. xUnit corre las clases de una misma coleccion una tras otra y las colecciones en paralelo: cada coleccion tiene su propia base (ver
/// <see cref="clsBaseDatosFixture"/>), asi que ninguna prueba ve los datos de otra. El reparto de las clases busca que cada coleccion tarde lo mismo (se midio con --logger trx); una clase nueva
/// va a la coleccion que menos tarda.
/// </summary>
public static class clsColecciones
{
    public const string BASE_DE_DATOS_1 = "BaseDatos-1";
    public const string BASE_DE_DATOS_2 = "BaseDatos-2";
    public const string BASE_DE_DATOS_3 = "BaseDatos-3";
    public const string BASE_DE_DATOS_4 = "BaseDatos-4";
}

[CollectionDefinition(clsColecciones.BASE_DE_DATOS_1)]
public sealed class clsColeccionBaseDatos1 : ICollectionFixture<clsBaseDatosFixture>;

[CollectionDefinition(clsColecciones.BASE_DE_DATOS_2)]
public sealed class clsColeccionBaseDatos2 : ICollectionFixture<clsBaseDatosFixture>;

[CollectionDefinition(clsColecciones.BASE_DE_DATOS_3)]
public sealed class clsColeccionBaseDatos3 : ICollectionFixture<clsBaseDatosFixture>;

[CollectionDefinition(clsColecciones.BASE_DE_DATOS_4)]
public sealed class clsColeccionBaseDatos4 : ICollectionFixture<clsBaseDatosFixture>;
