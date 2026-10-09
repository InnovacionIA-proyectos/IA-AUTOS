using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Dominio.Usuarios;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.EntornoReal;

/// <summary>
/// Prueba contra la base de usuarios del portal (TI), solo lectura. En local se omite si falta PortalTi__ConnectionString; en
/// GitHub Actions falla si falta.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class FactPortalTiRealAttribute : FactAttribute
{
    public const string VARIABLE = "PortalTi__ConnectionString";

    public FactPortalTiRealAttribute()
    {
        var faltantes = clsEntornoReal.Faltantes().Append(clsEntornoReal.Leer(VARIABLE) is null ? VARIABLE : null).Where(n => n is not null).ToArray();
        if (faltantes.Length > 0 && !clsEntornoReal.EsCi)
        {
            Skip = $"Base de TI no configurada (faltan: {string.Join(", ", faltantes)}).";
        }
    }
}

public sealed class PortalTiRealTests
{
    private static string Conexion => clsEntornoReal.Exigir(FactPortalTiRealAttribute.VARIABLE);

    private static async Task<T?> EscalarAsync<T>(string sql)
    {
        await using var conexion = new SqlConnection(Conexion);
        await conexion.OpenAsync();
        await using var comando = new SqlCommand(sql, conexion) { CommandTimeout = 30 };
        var resultado = await comando.ExecuteScalarAsync();
        return resultado is null or DBNull ? default : (T)Convert.ChangeType(resultado, typeof(T));
    }

    [FactPortalTiReal]
    public async Task El_usuario_de_TI_es_de_solo_lectura()
    {
        // La API solo consulta: si este usuario pudiera escribir en las tablas del portal, la configuracion seria demasiado amplia.
        var escritura = await EscalarAsync<int>("""
            SELECT HAS_PERMS_BY_NAME('dbo.tblSAPUsuariosApp', 'OBJECT', 'INSERT')
                 + HAS_PERMS_BY_NAME('dbo.tblSAPUsuariosApp', 'OBJECT', 'UPDATE')
                 + HAS_PERMS_BY_NAME('dbo.tblSAPUsuariosApp', 'OBJECT', 'DELETE')
                 + HAS_PERMS_BY_NAME('dbo.tblSAPRoles', 'OBJECT', 'INSERT')
                 + HAS_PERMS_BY_NAME('dbo.tblSAPUsuarioRoles', 'OBJECT', 'INSERT')
                 + HAS_PERMS_BY_NAME('dbo.tblSAPOpciones', 'OBJECT', 'INSERT')
            """);

        Assert.Equal(0, escritura);
    }

    [FactPortalTiReal]
    public async Task La_API_consulta_a_TI_una_persona_existente_y_devuelve_null_para_una_inexistente()
    {
        await using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        using var alcance = fabrica.Services.CreateScope();
        var portal = alcance.ServiceProvider.GetRequiredService<IPortalUsuarios>();
        Assert.True(portal.Disponible);

        var idExistente = await EscalarAsync<long>("SELECT TOP 1 CAST(ID AS bigint) FROM dbo.tblSAPUsuariosApp ORDER BY ID");
        var existente = await portal.ObtenerAsync(idExistente);
        var inexistente = await portal.ObtenerAsync(-1);

        Assert.NotNull(existente);
        Assert.Equal(idExistente, existente.Id);
        Assert.False(string.IsNullOrWhiteSpace(existente.NombreUsuario));
        Assert.NotNull(existente.Roles);
        Assert.Null(inexistente);
    }

    [FactPortalTiReal]
    public async Task Las_consultas_de_roles_y_opciones_corren_sin_error_para_todas_las_personas_del_portal()
    {
        // Ejecuta la misma consulta de la API sobre TODOS los ids: detecta datos que la rompan (nulos, tipos inesperados).
        await using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        using var alcance = fabrica.Services.CreateScope();
        var portal = alcance.ServiceProvider.GetRequiredService<IPortalUsuarios>();
        var ids = new List<long>();
        await using (var conexion = new SqlConnection(Conexion))
        {
            await conexion.OpenAsync();
            await using var comando = new SqlCommand("SELECT TOP 25 CAST(ID AS bigint) FROM dbo.tblSAPUsuariosApp ORDER BY ID DESC", conexion);
            await using var lector = await comando.ExecuteReaderAsync();
            while (await lector.ReadAsync())
            {
                ids.Add(lector.GetInt64(0));
            }
        }

        foreach (var id in ids)
        {
            Assert.NotNull(await portal.ObtenerAsync(id));
        }
    }

    /// <summary>Nombre del rol en el portal (TI) que debe existir para cada rol de IA Autos que entra por el portal.</summary>
    private static readonly (string Rol, string NombreEnPortal)[] ROLES_ESPERADOS =
    [
        (clsRol.SUPER_ADMIN, "SuperAdminIAAUTOS"),
        (clsRol.ADMIN, "AdminIAAUTOS"),
        (clsRol.COORDINADOR_CALIDAD, "CoordinadorCalidadIAAUTOS"),
        (clsRol.COMERCIAL, "ComercialIAAUTOS"),
        (clsRol.INSPECTOR, "InspectorIAAUTOS"),
    ];

    [FactPortalTiReal]
    public async Task Cada_rol_de_IA_Autos_esta_registrado_en_TI_para_al_menos_una_persona()
    {
        // Recorre TI de punta a punta: busca a quien tenga un rol u opcion cuyo nombre contenga IAAUTOS, la consulta como lo hace
        // la API y resuelve el rol. Falla mientras TI no haya registrado los roles (y a alguien con cada uno).
        var ids = new List<long>();
        await using (var conexion = new SqlConnection(Conexion))
        {
            await conexion.OpenAsync();
            await using var comando = new SqlCommand("""
                SELECT CAST(ur.IDUSUARIO AS bigint)
                  FROM dbo.tblSAPUsuarioRoles ur
                  JOIN dbo.tblSAPRoles r ON r.IDROLES = ur.IDROLES
                 WHERE REPLACE(REPLACE(REPLACE(r.DESCRIPCION, ' ', ''), '-', ''), '_', '') LIKE '%IAAUTOS%'
                UNION
                SELECT CAST(ur.IDUSUARIO AS bigint)
                  FROM dbo.tblSAPUsuarioRoles ur
                  JOIN dbo.tblSAPOpcionesRoles orl ON orl.IDROLES = ur.IDROLES
                  JOIN dbo.tblSAPOpciones o ON o.IDOPCIONES = orl.IDOPCIONES
                 WHERE REPLACE(REPLACE(REPLACE(o.DESCRIPCION, ' ', ''), '-', ''), '_', '') LIKE '%IAAUTOS%'
                """, conexion);
            await using var lector = await comando.ExecuteReaderAsync();
            while (await lector.ReadAsync())
            {
                ids.Add(lector.GetInt64(0));
            }
        }

        await using var fabrica = new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.UseEnvironment("Testing"));
        using var alcance = fabrica.Services.CreateScope();
        var portal = alcance.ServiceProvider.GetRequiredService<IPortalUsuarios>();
        var cubiertos = new HashSet<string>();
        foreach (var id in ids)
        {
            var persona = await portal.ObtenerAsync(id);
            foreach (var rol in persona is null ? [] : clsRolesPortal.Coincidencias(persona.Roles))
            {
                cubiertos.Add(rol);
            }
        }

        var faltantes = ROLES_ESPERADOS.Where(r => !cubiertos.Contains(r.Rol)).Select(r => r.NombreEnPortal).ToArray();
        Assert.True(
            faltantes.Length == 0,
            "Faltan roles de IA Autos en el portal (TI) o nadie los tiene asignados: " + string.Join(", ", faltantes));
    }
}
