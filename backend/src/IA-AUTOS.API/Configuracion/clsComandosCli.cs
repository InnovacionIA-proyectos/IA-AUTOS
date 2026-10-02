using IAAutos.Aplicacion.Usuarios;
using IAAutos.Aplicacion.Usuarios.DTOUsuarios;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Usuarios;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.API.Configuracion;

/// <summary>
/// Comandos de operacion que se ejecutan con la propia imagen y terminan (no levantan el servidor).
/// <c>crear-superadmin &lt;correo&gt; &lt;nombre&gt;</c> crea el primer super administrador en una base nueva, o restablece la
/// contrasena (y reactiva) a uno existente. La contrasena se lee de la variable SUPERADMIN_PASSWORD, nunca de un argumento,
/// para que no quede en el historial del shell ni en la lista de procesos.
/// </summary>
public static class clsComandosCli
{
    public const string CREAR_SUPERADMIN = "crear-superadmin";
    public const string VARIABLE_PASSWORD = "SUPERADMIN_PASSWORD";

    public const int OK = 0;
    public const int ERROR = 1;
    public const int USO_INCORRECTO = 2;

    public static async Task<int> CrearSuperAdminAsync(
        IServiceProvider servicios,
        string[] argumentos,
        TextWriter salida,
        Func<string, string?> variableDeEntorno)
    {
        if (argumentos.Length < 3)
        {
            await salida.WriteLineAsync($"Uso: {CREAR_SUPERADMIN} <correo> <nombre completo>   (contraseña en la variable {VARIABLE_PASSWORD})");
            return USO_INCORRECTO;
        }

        var password = variableDeEntorno(VARIABLE_PASSWORD);
        if (string.IsNullOrEmpty(password))
        {
            await salida.WriteLineAsync($"Falta la variable de entorno {VARIABLE_PASSWORD} con la contraseña del super administrador.");
            return USO_INCORRECTO;
        }

        var email = argumentos[1].Trim();
        var nombre = string.Join(' ', argumentos.Skip(2)).Trim();

        try
        {
            var repositorio = servicios.GetRequiredService<IUsuarioRepository>();
            var servicio = servicios.GetRequiredService<clsUsuariosService>();
            var existente = await repositorio.ObtenerPorEmailAsync(email);
            if (existente is null)
            {
                var creado = await servicio.CrearAsync(new CrearUsuarioRequest(nombre, email, clsRol.SUPER_ADMIN, null, password));
                await salida.WriteLineAsync($"Super administrador creado: {creado.Email} ({creado.Id})");
            }
            else
            {
                await servicio.ActualizarAsync(
                    existente.Id,
                    new ActualizarUsuarioRequest(nombre, null, clsRol.SUPER_ADMIN, null, password, true));
                await salida.WriteLineAsync($"Super administrador actualizado (contraseña restablecida y cuenta activa): {existente.Email}");
            }

            return OK;
        }
        catch (clsExcepcionDominio ex)
        {
            await salida.WriteLineAsync($"No se pudo completar: {ex.Message}");
            if (ex.Detalles is Dictionary<string, object> detalle
                && detalle.TryGetValue("fieldErrors", out var campos)
                && campos is Dictionary<string, string[]> porCampo)
            {
                foreach (var (campo, mensajes) in porCampo)
                {
                    await salida.WriteLineAsync($"  {campo}: {string.Join("; ", mensajes)}");
                }
            }

            return ERROR;
        }
    }
}
