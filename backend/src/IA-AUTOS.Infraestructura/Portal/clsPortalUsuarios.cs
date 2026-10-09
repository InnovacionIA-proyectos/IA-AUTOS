using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Dominio.Comun;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IAAutos.Infraestructura.Portal;

/// <summary>
/// Base de usuarios del portal de administracion (TI), seccion PortalTi de la configuracion. SOLO LECTURA: nunca se escribe
/// ahi ni se lee la contrasena del portal.
/// </summary>
public sealed class clsOpcionesPortalTi
{
    public const string SECCION = "PortalTi";

    /// <summary>
    /// Nombre de la base con las tablas del portal (tblSAPUsuariosApp, tblSAPUsuarioRoles, tblSAPRoles, tblSAPOpcionesRoles,
    /// tblSAPOpciones): dbSolucionesTI en produccion, dbSolucionesTI_Prueba en pruebas. Vacio = se usa la base de la propia
    /// ConnectionString de TI. Sin ninguna de las dos no hay verificacion: login-portal solo acepta ids ya vinculados.
    /// </summary>
    public string? BaseDatos { get; set; }

    /// <summary>
    /// Cadena de conexion a TI (usuario de solo lectura). Si se deja vacia se usa la conexion de la aplicacion, para cuando
    /// las tablas del portal estan en el mismo servidor y base accesibles con sus credenciales.
    /// </summary>
    public string? ConnectionString { get; set; }

    public bool Configurado => !string.IsNullOrWhiteSpace(BaseDatos);
}

internal sealed class clsPortalUsuarios : IPortalUsuarios
{
    private readonly string _conexion;
    private readonly string? _consulta;
    private readonly ILogger<clsPortalUsuarios> _registro;

    public clsPortalUsuarios(IOptions<clsOpcionesPortalTi> opciones, IConfiguration configuracion, ILogger<clsPortalUsuarios> registro)
    {
        _registro = registro;
        var o = opciones.Value;
        _conexion = string.IsNullOrWhiteSpace(o.ConnectionString)
            ? configuracion.GetConnectionString(clsDependenciasInfraestructura.NOMBRE_CADENA_CONEXION) ?? string.Empty
            : o.ConnectionString;
        var baseDatos = string.IsNullOrWhiteSpace(o.BaseDatos) ? BaseDeLaCadena(o.ConnectionString) : o.BaseDatos;
        _consulta = string.IsNullOrWhiteSpace(baseDatos) ? null : ConstruirConsulta(baseDatos);
    }

    // Si no se indica PortalTi:BaseDatos se usa la base de la propia cadena de conexion de TI (Database=...).
    private static string? BaseDeLaCadena(string? cadena)
    {
        if (string.IsNullOrWhiteSpace(cadena))
        {
            return null;
        }

        try
        {
            var base1 = new SqlConnectionStringBuilder(cadena).InitialCatalog;
            return string.IsNullOrWhiteSpace(base1) ? null : base1;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public bool Disponible => _consulta is not null && !string.IsNullOrWhiteSpace(_conexion);

    // El nombre de la base es un valor de configuracion fijado en el despliegue (nunca un dato del usuario), pero no se puede
    // parametrizar en una referencia de tres partes: se escapa entre corchetes. No se selecciona la columna CLAVE.
    private static string ConstruirConsulta(string baseDatos)
    {
        var b = "[" + baseDatos.Trim().Replace("]", "]]") + "]";
        return $"""
            SET NOCOUNT ON;
            SELECT u.ID, u.NOMBREUSUARIO, u.EMAIL, u.DESCRIPCIONUSUARIO, x.Nombre
            FROM {b}.dbo.tblSAPUsuariosApp u
            LEFT JOIN (
                -- Roles de la persona (por ejemplo AdminIAAUTOS)...
                SELECT ur.IDUSUARIO, r.DESCRIPCION AS Nombre
                  FROM {b}.dbo.tblSAPUsuarioRoles ur
                  JOIN {b}.dbo.tblSAPRoles r ON r.IDROLES = ur.IDROLES
                UNION
                -- ...y las opciones de permiso de esos roles (el portal nombra las opciones <Rol><App>, por ejemplo AdminNEXO).
                SELECT ur.IDUSUARIO, o.DESCRIPCION
                  FROM {b}.dbo.tblSAPUsuarioRoles ur
                  JOIN {b}.dbo.tblSAPOpcionesRoles orl ON orl.IDROLES = ur.IDROLES
                  JOIN {b}.dbo.tblSAPOpciones o ON o.IDOPCIONES = orl.IDOPCIONES
            ) x ON x.IDUSUARIO = u.ID
            WHERE u.ID = @id;
            """;
    }

    public async Task<clsUsuarioPortal?> ObtenerAsync(long id, CancellationToken ct = default)
    {
        if (!Disponible)
        {
            throw new clsServicioNoDisponibleException("La verificación contra el portal no está configurada.");
        }

        try
        {
            await using var conexion = new SqlConnection(_conexion);
            await conexion.OpenAsync(ct);
            await using var comando = new SqlCommand(_consulta, conexion) { CommandTimeout = 15 };
            comando.Parameters.AddWithValue("@id", id);
            await using var lector = await comando.ExecuteReaderAsync(ct);

            string? nombre = null;
            string? email = null;
            string? descripcion = null;
            var roles = new List<string>();
            var encontrado = false;
            while (await lector.ReadAsync(ct))
            {
                if (!encontrado)
                {
                    encontrado = true;
                    nombre = lector.IsDBNull(1) ? null : lector.GetString(1);
                    email = lector.IsDBNull(2) ? null : lector.GetString(2);
                    descripcion = lector.IsDBNull(3) ? null : lector.GetString(3);
                }

                // LEFT JOIN: sin roles llega una unica fila con el rol nulo.
                if (!lector.IsDBNull(4))
                {
                    roles.Add(lector.GetString(4));
                }
            }

            return encontrado && !string.IsNullOrWhiteSpace(nombre)
                ? new clsUsuarioPortal(id, nombre.Trim(), email, descripcion, roles)
                : null;
        }
        catch (Exception ex) when (ex is SqlException or InvalidOperationException or TimeoutException)
        {
            // Fail-closed: si TI no responde no se concede acceso. El detalle solo queda en el log del servidor.
            _registro.LogWarning(ex, "No se pudo consultar la base de usuarios del portal para el id {IdPortal}", id);
            throw new clsServicioNoDisponibleException("No se pudo verificar tu identidad en el portal. Intenta de nuevo en unos minutos.");
        }
    }
}
