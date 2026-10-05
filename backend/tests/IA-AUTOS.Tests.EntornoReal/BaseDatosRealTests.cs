using Microsoft.Data.SqlClient;

namespace IAAutos.Tests.EntornoReal;

/// <summary>Verifica la base de datos real con consultas de solo lectura (nada se inserta, actualiza ni borra).</summary>
public sealed class BaseDatosRealTests
{
    private static readonly string[] TABLAS =
    [
        "tblUsuarios", "tblVehiculos", "tblPuntosCustodia", "tblEtapas", "tblInspecciones", "tblHallazgos",
        "tblEvidencias", "tblTransferenciasCustodia", "tblAsignaciones", "tblOrdenesSofasa", "tblAuditLog",
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
    public async Task Existe_al_menos_un_super_administrador_activo()
    {
        await using var conexion = await AbrirAsync();
        var total = await EscalarAsync<int>(conexion, "SELECT COUNT(*) FROM tblUsuarios WHERE Rol = 'super_admin' AND Activo = 1");
        Assert.True(total > 0, "No hay ningun super_admin activo; falta ejecutar el comando crear-superadmin.");
    }
}
