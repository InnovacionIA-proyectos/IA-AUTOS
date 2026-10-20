using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.EntornoReal;

/// <summary>Verifica la base de datos real con consultas de solo lectura (nada se inserta, actualiza ni borra).</summary>
public sealed class BaseDatosRealTests
{
    private static readonly string[] TABLAS =
    [
        "tblUsuarios", "tblVehiculos", "tblPuntosCustodia", "tblEtapas", "tblInspecciones", "tblHallazgos",
        "tblEvidencias", "tblTransferenciasCustodia", "tblViajes", "tblViajeVehiculos", "tblOrdenesSofasa", "tblAuditLog", "tblRefreshTokens", "tblEtapasCatalogo", "tblTiposUnidadTransportadora",
        "tblClasesDano", "tblTamanosDano", "tblNivelesSeveridad", "tblVistasFoto", "tblCuadrantes", "tblCuadranteVistas", "tblClientes",
    ];

    private static readonly string[] TRIGGERS =
    [
        "TR_tblInspecciones_Inmutable", "TR_tblHallazgos_NoEnCerrada", "TR_tblEvidencias_NoEnCerrada",
    ];

    private static async Task<SqlConnection> AbrirAsync()
    {
        var conexion = new SqlConnection(clsEntornoReal.Exigir(clsEntornoReal.CADENA_CONEXION));
        await conexion.OpenAsync();
        return conexion;
    }

    private static async Task<T?> EscalarAsync<T>(SqlConnection conexion, string sql, params (string Nombre, object Valor)[] parametros)
    {
        await using var comando = new SqlCommand(sql, conexion) { CommandTimeout = 30 };
        foreach (var (nombre, valor) in parametros)
        {
            comando.Parameters.AddWithValue(nombre, valor);
        }

        var resultado = await comando.ExecuteScalarAsync();
        return resultado is null or DBNull ? default : (T)resultado;
    }

    [FactEntornoReal]
    public async Task La_base_de_datos_responde()
    {
        await using var conexion = await AbrirAsync();
        Assert.Equal(1, await EscalarAsync<int>(conexion, "SELECT 1"));
    }

    [FactEntornoReal]
    public async Task Las_tablas_de_la_aplicacion_resuelven_con_el_esquema_por_defecto_del_usuario()
    {
        // La API consulta las tablas sin esquema (tblUsuarios, no dbo.tblUsuarios): OBJECT_ID con nombre sin calificar
        // las busca en el esquema por defecto del usuario, igual que la API. Si falla, el usuario de la aplicacion no tiene
        // como esquema por defecto el que contiene las tablas (ALTER USER ... WITH DEFAULT_SCHEMA = <esquema>).
        await using var conexion = await AbrirAsync();
        var ausentes = new List<string>();
        foreach (var tabla in TABLAS)
        {
            var id = await EscalarAsync<int?>(conexion, "SELECT OBJECT_ID(@nombre, N'U')", ("@nombre", tabla));
            if (id is null)
            {
                ausentes.Add(tabla);
            }
        }

        Assert.True(ausentes.Count == 0, $"Tablas que el usuario no resuelve: {string.Join(", ", ausentes)}");
    }

    [FactEntornoReal]
    public async Task Los_disparadores_de_integridad_existen()
    {
        await using var conexion = await AbrirAsync();
        var ausentes = new List<string>();
        foreach (var disparador in TRIGGERS)
        {
            var existe = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.triggers WHERE name = @nombre", ("@nombre", disparador));
            if (existe == 0)
            {
                ausentes.Add(disparador);
            }
        }

        Assert.True(ausentes.Count == 0, $"Disparadores ausentes: {string.Join(", ", ausentes)}");
    }

    [FactEntornoReal]
    public async Task El_usuario_de_la_aplicacion_puede_leer_las_tablas_de_negocio()
    {
        await using var conexion = await AbrirAsync();
        foreach (var tabla in TABLAS)
        {
            // COUNT sobre cada tabla: falla con un error de permisos si el usuario no puede leerla.
            var filas = await EscalarAsync<int>(conexion, $"SELECT COUNT(*) FROM {tabla}");
            Assert.True(filas >= 0, tabla);
        }
    }

    [FactEntornoReal]
    public async Task El_esquema_de_la_sesion_del_portal_esta_aplicado_en_la_base()
    {
        // Sin la columna PortalUsuarioId ni la tabla de refresh tokens, la API falla en todas las consultas de usuarios: este
        // chequeo (solo lectura) avisa antes de desplegar si falta ejecutar database/cambios-esquema/01-sesion-portal.sql.
        await using var conexion = await AbrirAsync();

        var columna = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'tblUsuarios') AND name = N'PortalUsuarioId'");
        var indice = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.indexes
             WHERE object_id = OBJECT_ID(N'tblUsuarios') AND name = N'UX_tblUsuarios_PortalUsuarioId'
               AND is_unique = 1 AND has_filter = 1
            """);

        Assert.True(columna == 1, "Falta la columna tblUsuarios.PortalUsuarioId (ejecute database/cambios-esquema/01-sesion-portal.sql).");
        Assert.True(indice == 1, "Falta el indice unico filtrado UX_tblUsuarios_PortalUsuarioId.");
    }

    [FactEntornoReal]
    public async Task Los_catalogos_maestros_estan_migrados_con_sus_valores_iniciales()
    {
        // Sin esta migracion la API nueva falla en las consultas de etapas y hallazgos: este chequeo (solo lectura) avisa antes de desplegar
        // si falta ejecutar database/cambios-esquema/02-catalogos-maestros.sql.
        await using var conexion = await AbrirAsync();
        var ayuda = "Ejecute database/cambios-esquema/02-catalogos-maestros.sql.";

        async Task<int> Contar(string tabla, string codigos) =>
            await EscalarAsync<int>(conexion, $"SELECT COUNT(*) FROM {tabla} WHERE Codigo IN ({codigos})");

        Assert.True(await Contar("tblEtapasCatalogo", "'cargue','transporte','descargue'") == 3, $"Faltan etapas del catalogo. {ayuda}");
        Assert.True(await Contar("tblPuntosCustodia", "'cedi_1','origen','certificadora','destino','cedi_2','concesionario'") == 6, $"Faltan lugares. {ayuda}");
        Assert.True(await Contar("tblTiposUnidadTransportadora", "'ninera','nodriza','quintinaria','planchon_grua'") == 4, $"Faltan tipos de unidad. {ayuda}");
        Assert.True(await Contar("tblClasesDano", "'deformacion','rotura','cortado','perforacion','golpe','mancha','raya_pintar','desconchado','particulas','faltante'") == 10, $"Faltan clases de dano. {ayuda}");
        Assert.True(await Contar("tblTamanosDano", "'1_5cm','5_10cm','10_20cm','mas_20cm','requiere_reemplazo','mas_10_particulas'") == 6, $"Faltan tamanos de dano. {ayuda}");
        Assert.True(await Contar("tblNivelesSeveridad", "'v1_plus','v1','v2','v3'") == 4, $"Faltan niveles de severidad. {ayuda}");
        Assert.Equal(9, await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblVistasFoto"));
        Assert.Equal(7, await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblVistasFoto WHERE Obligatoria = 1"));
        Assert.Equal(2, await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblVistasFoto WHERE Obligatoria = 0 AND Codigo IN ('piezas_baul','cojineria')"));
    }

    [FactEntornoReal]
    public async Task Los_cuadrantes_cargados_son_la_ficha_completa_con_geometria_valida()
    {
        // Los cuadrantes son zonas (36 numeros de la ficha; el 4 y el 12 no existen) y cada zona se dibuja en las vistas donde aparece.
        // Se carga con database/cambios-esquema/04-cargar-cuadrantes-ficha.sql.
        await using var conexion = await AbrirAsync();
        var ayuda = "Ejecute database/cambios-esquema/03-cuadrantes-por-zona.sql y luego 04-cargar-cuadrantes-ficha.sql.";

        var zonas = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblCuadrantes");
        var poligonos = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblCuadranteVistas");
        var vistasConPoligonos = await EscalarAsync<int>(conexion, "SELECT COUNT(DISTINCT VistaFotoId) FROM tblCuadranteVistas");
        var sinPoligono = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblCuadrantes c WHERE NOT EXISTS (SELECT 1 FROM tblCuadranteVistas p WHERE p.CuadranteId = c.Id)");
        var enVistaAdicional = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblCuadranteVistas p JOIN tblVistasFoto v ON v.Id = p.VistaFotoId WHERE v.Obligatoria = 0");
        var geometriaInvalida = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM tblCuadranteVistas p
             WHERE ISJSON(p.Geometria) = 0
                OR (SELECT COUNT(*) FROM OPENJSON(p.Geometria, '$.puntos')) < 3
                OR EXISTS (SELECT 1 FROM OPENJSON(p.Geometria, '$.puntos') pt
                            WHERE TRY_CAST(JSON_VALUE(pt.value, '$[0]') AS float) IS NULL OR TRY_CAST(JSON_VALUE(pt.value, '$[0]') AS float) NOT BETWEEN 0 AND 1
                               OR TRY_CAST(JSON_VALUE(pt.value, '$[1]') AS float) IS NULL OR TRY_CAST(JSON_VALUE(pt.value, '$[1]') AS float) NOT BETWEEN 0 AND 1)
            """);

        Assert.True(zonas == 36, $"Se esperaban 36 cuadrantes (zonas) y hay {zonas}. {ayuda}");
        Assert.True(poligonos == 97, $"Se esperaban 97 poligonos por vista y hay {poligonos}. {ayuda}");
        Assert.Equal(5, vistasConPoligonos);
        Assert.Equal(0, sinPoligono);
        Assert.Equal(0, enVistaAdicional);
        Assert.Equal(0, geometriaInvalida);
    }

    [FactEntornoReal]
    public async Task Los_viajes_estan_migrados_y_la_vista_nueva_reemplaza_a_la_anterior()
    {
        // Sin esta migracion la API nueva falla en etapas, inspecciones y listados: este chequeo (solo lectura) avisa antes de
        // desplegar si falta ejecutar database/cambios-esquema/05-viajes.sql.
        await using var conexion = await AbrirAsync();
        var ayuda = "Ejecute database/cambios-esquema/05-viajes.sql.";

        async Task<int> Columnas(string tabla, string columna, bool? obligatoria = null) => await EscalarAsync<int>(
            conexion,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@t, N'U') AND name = @c" + (obligatoria is null ? string.Empty : $" AND is_nullable = {(obligatoria.Value ? 0 : 1)}"),
            ("@t", tabla), ("@c", columna));

        Assert.True(await Columnas("tblEtapas", "ViajeId", obligatoria: true) == 1, $"tblEtapas.ViajeId debe existir y ser obligatoria. {ayuda}");
        Assert.True(await Columnas("tblEtapas", "ConPersonalApoyo") == 1, $"Falta tblEtapas.ConPersonalApoyo. {ayuda}");
        Assert.True(await Columnas("tblInspecciones", "ViajeId", obligatoria: true) == 1, $"tblInspecciones.ViajeId debe existir y ser obligatoria. {ayuda}");
        Assert.True(await Columnas("tblUsuarios", "Cedula") == 1, $"Falta tblUsuarios.Cedula. {ayuda}");
        Assert.True(await Columnas("tblOrdenesSofasa", "ViajeId") == 1, $"Falta tblOrdenesSofasa.ViajeId. {ayuda}");
        Assert.True(await Columnas("tblOrdenesSofasa", "CodigoMtrack") == 1, $"Falta tblOrdenesSofasa.CodigoMtrack. {ayuda}");

        var objetos = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.objects
             WHERE name IN (N'FK_tblEtapas_Viaje', N'FK_tblInspecciones_Viaje', N'FK_tblViajeVehiculos_Viaje', N'FK_tblViajeVehiculos_Vehiculo',
                            N'FK_tblOrdenesSofasa_Viaje', N'UQ_tblEtapas_Viaje_Vehiculo_Orden', N'UQ_tblViajeVehiculos_Viaje_Vehiculo')
            """);
        var indices = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.indexes WHERE name IN (N'UX_tblViajes_CodigoMtrack', N'UX_tblUsuarios_Cedula') AND is_unique = 1 AND has_filter = 1");
        var vistaNueva = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.views WHERE name = N'vwViajesCompletos'");
        var vistaVieja = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.views WHERE name = N'vwVehiculosMovilizados'");
        var asignaciones = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.tables WHERE name = N'tblAsignaciones'");
        var etapasSinViaje = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblEtapas WHERE ViajeId IS NULL");
        var inspeccionesSinViaje = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblInspecciones WHERE ViajeId IS NULL");

        Assert.True(objetos == 7, $"Faltan llaves foraneas o restricciones de unicidad de los viajes. {ayuda}");
        Assert.True(indices == 2, $"Faltan los indices unicos del codigo MTrack y de la cedula. {ayuda}");
        Assert.True(vistaNueva == 1, $"Falta la vista vwViajesCompletos. {ayuda}");
        Assert.True(vistaVieja == 0, "Sigue existiendo la vista vwVehiculosMovilizados: la reemplaza vwViajesCompletos.");
        Assert.True(asignaciones == 0, "Sigue existiendo tblAsignaciones: se retira al migrar a viajes.");
        Assert.Equal(0, etapasSinViaje);
        Assert.Equal(0, inspeccionesSinViaje);
    }

    [FactEntornoReal]
    public async Task La_identidad_por_cedula_y_pin_y_los_clientes_estan_migrados()
    {
        // Sin esta migracion la API nueva falla al leer usuarios (columnas de credencial) y al cargar vehiculos (cliente): este
        // chequeo (solo lectura) avisa antes de desplegar si falta ejecutar database/cambios-esquema/06-identidad.sql.
        await using var conexion = await AbrirAsync();
        var ayuda = "Ejecute database/cambios-esquema/06-identidad.sql.";

        async Task<int> Columnas(string tabla, string columna, bool? obligatoria = null) => await EscalarAsync<int>(
            conexion,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@t, N'U') AND name = @c" + (obligatoria is null ? string.Empty : $" AND is_nullable = {(obligatoria.Value ? 0 : 1)}"),
            ("@t", tabla), ("@c", columna));

        foreach (var columna in new[] { "PinHash", "BloqueadoHasta", "UltimoAccesoAt", "ClienteId" })
        {
            Assert.True(await Columnas("tblUsuarios", columna, obligatoria: false) == 1, $"Falta tblUsuarios.{columna} (opcional). {ayuda}");
        }

        foreach (var columna in new[] { "TipoCredencial", "DebeCambiarPin", "IntentosFallidos" })
        {
            Assert.True(await Columnas("tblUsuarios", columna, obligatoria: true) == 1, $"Falta tblUsuarios.{columna} (obligatoria). {ayuda}");
        }

        Assert.True(await Columnas("tblUsuarios", "Email", obligatoria: false) == 1, $"tblUsuarios.Email debe admitir nulos (quien entra con cedula y PIN puede no tener correo). {ayuda}");
        Assert.True(await Columnas("tblVehiculos", "ClienteId", obligatoria: true) == 1, $"tblVehiculos.ClienteId debe existir y ser obligatoria. {ayuda}");

        var restricciones = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.objects
             WHERE name IN (N'FK_tblUsuarios_Cliente', N'FK_tblVehiculos_Cliente', N'CK_tblUsuarios_TipoCredencial', N'CK_tblUsuarios_Identidad', N'UQ_tblClientes_Codigo')
            """);
        var indiceCorreo = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.indexes WHERE name = N'UX_tblUsuarios_Email' AND is_unique = 1 AND has_filter = 1");
        var restriccionVieja = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.key_constraints WHERE name = N'UQ_tblUsuarios_Email'");
        var clientes = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblClientes WHERE Activo = 1");
        var vehiculosSinCliente = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblVehiculos WHERE ClienteId IS NULL");
        var usuariosSinCliente = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblUsuarios WHERE ClienteId IS NULL AND Rol <> 'super_admin'");

        Assert.True(restricciones == 5, $"Faltan llaves foraneas o restricciones de la identidad y los clientes. {ayuda}");
        Assert.True(indiceCorreo == 1, $"Falta el indice unico del correo entre quienes lo tienen (UX_tblUsuarios_Email). {ayuda}");
        Assert.True(restriccionVieja == 0, $"Sigue existiendo UQ_tblUsuarios_Email: el correo ya no es obligatorio. {ayuda}");
        Assert.True(clientes >= 1, $"No hay ningun cliente activo. {ayuda}");
        Assert.Equal(0, vehiculosSinCliente);
        Assert.Equal(0, usuariosSinCliente);
    }

    [FactEntornoReal]
    public async Task Las_inspecciones_de_salida_y_llegada_y_las_fotos_selladas_estan_migradas()
    {
        // Sin esta migracion la API nueva falla al leer inspecciones y fotos (columnas nuevas) y al crear la salida o la llegada: este
        // chequeo (solo lectura) avisa antes de desplegar si falta ejecutar database/cambios-esquema/07-inspecciones-salida-llegada.sql.
        await using var conexion = await AbrirAsync();
        var ayuda = "Ejecute database/cambios-esquema/07-inspecciones-salida-llegada.sql.";

        async Task<int> Columnas(string tabla, string columna, bool? obligatoria = null) => await EscalarAsync<int>(
            conexion,
            "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@t, N'U') AND name = @c" + (obligatoria is null ? string.Empty : $" AND is_nullable = {(obligatoria.Value ? 0 : 1)}"),
            ("@t", tabla), ("@c", columna));

        foreach (var columna in new[] { "IniciadaAt", "Version" })
        {
            Assert.True(await Columnas("tblInspecciones", columna, obligatoria: true) == 1, $"Falta tblInspecciones.{columna} (obligatoria). {ayuda}");
        }

        foreach (var columna in new[] { "CerradaAt", "CorrigeInspeccionId" })
        {
            Assert.True(await Columnas("tblInspecciones", columna, obligatoria: false) == 1, $"Falta tblInspecciones.{columna} (opcional). {ayuda}");
        }

        foreach (var columna in new[] { "VistaId", "HashSha256", "Precision", "DispositivoId", "Nota" })
        {
            Assert.True(await Columnas("tblEvidencias", columna, obligatoria: false) == 1, $"Falta tblEvidencias.{columna} (opcional). {ayuda}");
        }

        foreach (var columna in new[] { "SelloServidorAt", "SelloLat", "SelloLng" })
        {
            Assert.True(await Columnas("tblEvidencias", columna, obligatoria: true) == 1, $"tblEvidencias.{columna} debe existir y ser obligatoria. {ayuda}");
        }

        var restricciones = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.objects
             WHERE name IN (N'FK_tblInspecciones_Corrige', N'FK_tblEvidencias_Vista', N'CK_tblInspecciones_Momento', N'CK_tblInspecciones_Cierre',
                            N'CK_tblInspecciones_Version', N'CK_tblEvidencias_Precision', N'CK_tblEvidencias_Hash')
            """);
        var indices = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.indexes WHERE name IN (N'UX_tblInspecciones_Etapa_Momento_Original', N'UX_tblInspecciones_Corrige') AND is_unique = 1 AND has_filter = 1");
        var restriccionVieja = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.key_constraints WHERE name = N'UQ_tblInspecciones_Etapa_Momento'");
        var momentosViejos = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblInspecciones WHERE Momento NOT IN ('salida', 'llegada')");
        var cerradasSinHora = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblInspecciones WHERE Estado = 'cerrada' AND CerradaAt IS NULL");
        var triggerInactivo = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.triggers WHERE name = N'TR_tblInspecciones_Inmutable' AND is_disabled = 1");

        Assert.True(restricciones == 7, $"Faltan llaves foraneas o restricciones de las inspecciones y las fotos. {ayuda}");
        Assert.True(indices == 2, $"Faltan los indices unicos de la inspeccion original y de la correccion. {ayuda}");
        Assert.True(restriccionVieja == 0, $"Sigue existiendo UQ_tblInspecciones_Etapa_Momento: la reemplazan los indices filtrados. {ayuda}");
        Assert.Equal(0, momentosViejos);
        Assert.Equal(0, cerradasSinHora);
        Assert.Equal(0, triggerInactivo);
    }

    [FactEntornoReal]
    public async Task Las_transferencias_con_tipo_de_cierre_y_la_foto_de_constancia_estan_migradas()
    {
        // Sin esta migracion la API nueva falla al leer y firmar transferencias (columnas nuevas): este chequeo (solo lectura) avisa antes de
        // desplegar si falta ejecutar database/cambios-esquema/08-transferencias-custodia.sql.
        await using var conexion = await AbrirAsync();
        var ayuda = "Ejecute database/cambios-esquema/08-transferencias-custodia.sql.";

        async Task<int> Columnas(string tabla, string columna) => await EscalarAsync<int>(
            conexion, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@t, N'U') AND name = @c", ("@t", tabla), ("@c", columna));

        foreach (var columna in new[]
        {
            "FirmaEntregaAt", "FirmaEntregaIp", "FirmaRecibeAt", "FirmaRecibeIp", "TipoCierre", "ReservasTexto", "NoFirmaParte", "MotivoNoFirma",
            "TestigoUsuarioId", "EvidenciaConstanciaId",
        })
        {
            Assert.True(await Columnas("tblTransferenciasCustodia", columna) == 1, $"Falta tblTransferenciasCustodia.{columna}. {ayuda}");
        }

        Assert.True(await Columnas("tblEvidencias", "EsConstancia") == 1, $"Falta tblEvidencias.EsConstancia. {ayuda}");

        var restricciones = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.objects
             WHERE name IN (N'FK_tblTransferencias_Testigo', N'FK_tblTransferencias_Constancia', N'CK_tblTransferencias_TipoCierre',
                            N'CK_tblTransferencias_Parte', N'CK_tblTransferencias_Testigo', N'CK_tblTransferencias_Cierre')
            """);
        var disparadorAdmiteConstancia = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.triggers t JOIN sys.sql_modules m ON m.object_id = t.object_id
             WHERE t.name = N'TR_tblEvidencias_NoEnCerrada' AND t.is_disabled = 0 AND m.definition LIKE N'%EsConstancia%'
            """);
        var completasSinCierre = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM tblTransferenciasCustodia
             WHERE TipoCierre IS NULL AND FirmaEntrega IS NOT NULL AND FirmaEntrega <> N'' AND FirmaRecibe IS NOT NULL AND FirmaRecibe <> N''
            """);
        var firmasSinFecha = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM tblTransferenciasCustodia
             WHERE (FirmaEntrega IS NOT NULL AND FirmaEntrega <> N'' AND FirmaEntregaAt IS NULL) OR (FirmaRecibe IS NOT NULL AND FirmaRecibe <> N'' AND FirmaRecibeAt IS NULL)
            """);

        Assert.True(restricciones == 6, $"Faltan llaves foraneas o restricciones de las transferencias. {ayuda}");
        Assert.True(disparadorAdmiteConstancia == 1, $"El trigger de evidencias no admite la foto de una constancia en una inspeccion cerrada. {ayuda}");
        Assert.Equal(0, completasSinCierre);
        Assert.Equal(0, firmasSinFecha);
    }

    [FactEntornoReal]
    public async Task La_evidencia_no_se_borra_y_las_relaciones_ya_no_borran_en_cascada()
    {
        // Sin esta migracion la evidencia se podria borrar de la base (en cascada o a mano): este chequeo (solo lectura) avisa antes de
        // desplegar si falta ejecutar database/cambios-esquema/09-evidencia-sin-borrado.sql.
        await using var conexion = await AbrirAsync();
        var ayuda = "Ejecute database/cambios-esquema/09-evidencia-sin-borrado.sql.";

        var activo = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'tblVehiculos', N'U') AND name = N'Activo' AND is_nullable = 0");
        var indice = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.indexes WHERE name = N'IX_tblVehiculos_Activo' AND object_id = OBJECT_ID(N'tblVehiculos', N'U')");
        var triggers = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.triggers
             WHERE is_instead_of_trigger = 1 AND is_disabled = 0
               AND name IN (N'TR_tblEvidencias_NoBorrar', N'TR_tblHallazgos_NoBorrar', N'TR_tblTransferenciasCustodia_NoBorrar',
                            N'TR_tblAuditLog_NoBorrar', N'TR_tblInspecciones_NoBorrar')
            """);
        var cascadasQuitadas = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.foreign_keys
             WHERE delete_referential_action = 1
               AND name IN (N'FK_tblViajeVehiculos_Vehiculo', N'FK_tblEtapas_Vehiculo', N'FK_tblInspecciones_Etapa', N'FK_tblHallazgos_Inspeccion',
                            N'FK_tblEvidencias_Inspeccion', N'FK_tblTransferencias_Inspeccion')
            """);
        var cascadasConservadas = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.foreign_keys
             WHERE delete_referential_action = 1 AND name IN (N'FK_tblViajeVehiculos_Viaje', N'FK_tblCuadranteVistas_Cuadrante')
            """);

        Assert.True(activo == 1 && indice == 1, $"Falta tblVehiculos.Activo (obligatoria) o su indice. {ayuda}");
        Assert.True(triggers == 5, $"Faltan triggers que impiden borrar la evidencia (o estan deshabilitados). {ayuda}");
        Assert.True(cascadasQuitadas == 0, $"Siguen existiendo relaciones que borran en cascada lo que es evidencia. {ayuda}");
        Assert.True(cascadasConservadas == 2, $"Se perdio una cascada que debia conservarse (viaje a sus vehiculos o cuadrante a sus regiones). {ayuda}");
    }

    [FactEntornoReal]
    public async Task La_zona_del_dano_el_hallazgo_con_nivel_y_el_estado_de_la_unidad_estan_migrados()
    {
        // Sin esta migracion la API no puede leer ni guardar hallazgos con zona: este chequeo (solo lectura) avisa antes de desplegar si falta
        // ejecutar database/cambios-esquema/10-zona-dano-y-hallazgo.sql.
        await using var conexion = await AbrirAsync();
        var ayuda = "Ejecute database/cambios-esquema/10-zona-dano-y-hallazgo.sql.";

        async Task<int> Columnas(string tabla, string columna) => await EscalarAsync<int>(
            conexion, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(@t, N'U') AND name = @c", ("@t", tabla), ("@c", columna));

        foreach (var tabla in new[] { "tblPanelesDano", "tblZonasDano", "tblRegionesDano" })
        {
            Assert.True(await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.tables WHERE name = @t", ("@t", tabla)) == 1, $"Falta la tabla {tabla}. {ayuda}");
        }

        foreach (var columna in new[]
        {
            "ZonaId", "TipoNovedad", "NivelSeveridad", "Origen", "Nota", "EsPreexistente", "HallazgoOrigenId", "ConfirmadoPor", "EstadoSeguimiento",
            "EvidenciaCompleta", "RevisadoPor", "RevisadoAt", "MigracionPendiente",
        })
        {
            Assert.True(await Columnas("tblHallazgos", columna) == 1, $"Falta tblHallazgos.{columna}. {ayuda}");
        }

        foreach (var columna in new[] { "EstadoUnidad", "NoSaleMotivo", "NoSaleHallazgoId", "NoSaleMarcadoPor", "NoSaleAt", "ReemplazoVehiculoId" })
        {
            Assert.True(await Columnas("tblViajeVehiculos", columna) == 1, $"Falta tblViajeVehiculos.{columna}. {ayuda}");
        }

        var paneles = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblPanelesDano WHERE Codigo IN ('superior','perfil_posterior_izquierdo','perfil_posterior_derecho','costado_izquierdo','costado_derecho')");
        var provisionales = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblZonasDano WHERE CuadranteId IS NULL AND Codigo IN ('frente','llanta_delantera_izquierda','llanta_delantera_derecha','llanta_trasera_izquierda','llanta_trasera_derecha')");
        var zonasEsperadas = await EscalarAsync<int>(conexion, "SELECT ISNULL(SUM(CASE WHEN Numero <= 25 THEN 2 ELSE 1 END), 0) FROM tblCuadrantes");
        var zonas = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblZonasDano WHERE CuadranteId IS NOT NULL");
        var restricciones = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.objects
             WHERE name IN (N'FK_tblZonasDano_Cuadrante', N'CK_tblZonasDano_Forma', N'FK_tblRegionesDano_Zona', N'FK_tblHallazgos_Zona', N'FK_tblHallazgos_Nivel',
                            N'FK_tblHallazgos_Origen', N'CK_tblHallazgos_Origen', N'CK_tblHallazgos_Clase', N'CK_tblHallazgos_TipoNovedad',
                            N'CK_tblHallazgos_Seguimiento', N'CK_tblHallazgos_Preexistente', N'FK_tblViajeVehiculos_NoSaleHallazgo', N'CK_tblViajeVehiculos_NoSale')
            """);
        var disparador = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.triggers WHERE name = N'TR_tblCuadrantes_CrearZonas' AND is_disabled = 0");
        var hallazgosSinUbicarSinMarca = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblHallazgos WHERE ZonaId IS NULL AND Origen = 'inspector' AND MigracionPendiente = 0");

        Assert.True(paneles == 5, $"Faltan paneles del dibujo de danos. {ayuda}");
        Assert.True(provisionales == 5, $"Faltan zonas provisionales. {ayuda}");
        Assert.True(zonas == zonasEsperadas, $"Hay {zonas} zonas numeradas y los cuadrantes esperan {zonasEsperadas} (dos por lado del 1 al 25 y una del 26 en adelante). {ayuda}");
        Assert.True(restricciones == 13, $"Faltan relaciones o restricciones de zonas, hallazgos o unidades del viaje. {ayuda}");
        Assert.True(disparador == 1, $"Falta el trigger que crea las zonas de un cuadrante nuevo. {ayuda}");
        Assert.Equal(0, hallazgosSinUbicarSinMarca);
    }

    [FactEntornoReal]
    public async Task El_historial_de_seguimiento_del_hallazgo_y_la_vista_de_patrones_estan_migrados()
    {
        // Sin esta migracion la API no puede registrar el seguimiento de un hallazgo ni calcular los patrones por zona: este chequeo (solo lectura)
        // avisa antes de desplegar si falta ejecutar database/cambios-esquema/11-seguimiento-hallazgo.sql.
        await using var conexion = await AbrirAsync();
        var ayuda = "Ejecute database/cambios-esquema/11-seguimiento-hallazgo.sql.";

        var tabla = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.tables WHERE name = N'tblHallazgoSeguimiento'");
        var vista = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.views WHERE name = N'vwPatronesPorCuadrante'");
        var triggers = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.triggers
             WHERE is_disabled = 0 AND parent_id = OBJECT_ID(N'tblHallazgoSeguimiento', N'U')
               AND name IN (N'TR_tblHallazgoSeguimiento_NoModificar', N'TR_tblHallazgoSeguimiento_NoBorrar', N'TR_tblHallazgoSeguimiento_Sincronizar')
            """);
        var restricciones = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.objects
             WHERE name IN (N'FK_tblHallazgoSeguimiento_Hallazgo', N'FK_tblHallazgoSeguimiento_Usuario', N'FK_tblHallazgoSeguimiento_Sello',
                            N'CK_tblHallazgoSeguimiento_Accion', N'CK_tblHallazgoSeguimiento_Estados', N'CK_tblHallazgoSeguimiento_Cambio', N'CK_tblHallazgoSeguimiento_Detalle')
            """);

        Assert.True(tabla == 1, $"Falta la tabla tblHallazgoSeguimiento. {ayuda}");
        Assert.True(triggers == 3, $"Faltan triggers del historial de seguimiento (o estan deshabilitados). {ayuda}");
        Assert.True(restricciones == 7, $"Faltan relaciones o restricciones del historial de seguimiento. {ayuda}");
        Assert.True(vista == 1, $"Falta la vista vwPatronesPorCuadrante. {ayuda}");

        // La vista se puede consultar (es solo lectura) y no cuenta hallazgos sin zona.
        var sinZona = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM vwPatronesPorCuadrante WHERE ZonaId IS NULL");
        Assert.Equal(0, sinZona);
    }

    [FactEntornoReal]
    public async Task Cada_etapa_e_inspeccion_cuelga_de_un_viaje_coherente()
    {
        await using var conexion = await AbrirAsync();

        var inspeccionDeOtroViaje = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblInspecciones i JOIN tblEtapas e ON e.Id = i.EtapaId WHERE i.ViajeId <> e.ViajeId");
        var etapaFueraDeSuViaje = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblEtapas e WHERE NOT EXISTS (SELECT 1 FROM tblViajeVehiculos vv WHERE vv.ViajeId = e.ViajeId AND vv.VehiculoId = e.VehiculoId)");

        Assert.Equal(0, inspeccionDeOtroViaje);
        Assert.Equal(0, etapaFueraDeSuViaje);
    }

    [FactEntornoReal]
    public async Task Las_etapas_y_los_hallazgos_referencian_los_catalogos_con_llaves_foraneas()
    {
        await using var conexion = await AbrirAsync();

        var etapaNoNula = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'tblEtapas') AND name = N'EtapaCatalogoId' AND is_nullable = 0");
        var lugarOpcional = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.columns WHERE object_id = OBJECT_ID(N'tblEtapas') AND name = N'PuntoCustodiaId' AND is_nullable = 1");
        var fks = await EscalarAsync<int>(conexion, """
            SELECT COUNT(*) FROM sys.foreign_keys
             WHERE name IN (N'FK_tblEtapas_EtapaCatalogo', N'FK_tblEtapas_PuntoCustodia', N'FK_tblHallazgos_ClaseDano', N'FK_tblHallazgos_TamanoDano', N'FK_tblCuadranteVistas_Cuadrante', N'FK_tblCuadranteVistas_Vista')
            """);
        var checksViejos = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM sys.check_constraints WHERE name IN (N'CK_tblHallazgos_ClaseDano', N'CK_tblHallazgos_TamanoDano')");
        var sinEtapa = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblEtapas WHERE EtapaCatalogoId IS NULL");

        Assert.Equal(1, etapaNoNula);
        Assert.Equal(1, lugarOpcional);
        Assert.Equal(6, fks);
        Assert.Equal(0, checksViejos);
        Assert.Equal(0, sinEtapa);
    }

    [FactEntornoReal]
    public async Task Existe_al_menos_un_super_administrador_activo()
    {
        await using var conexion = await AbrirAsync();
        var total = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblUsuarios WHERE Rol = 'super_admin' AND Activo = 1");
        Assert.True(total > 0, "No hay ningun super_admin activo; falta ejecutar el comando crear-superadmin.");
    }
}
