using System.Diagnostics;
using IAAutos.Aplicacion.Comun;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IAAutos.Infraestructura.Persistencia;

internal sealed class clsSaludBaseDatos(clsAppDbContext contexto, ILogger<clsSaludBaseDatos> registro) : ISaludBaseDatos
{
    public async Task<EstadoBaseDatos> VerificarAsync(CancellationToken ct = default)
    {
        var cronometro = Stopwatch.StartNew();
        try
        {
            await contexto.Database.ExecuteSqlRawAsync("SELECT 1", ct);
            return new EstadoBaseDatos(true, cronometro.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            // El detalle solo se registra en el servidor; la respuesta publica no expone el error del motor.
            registro.LogError(ex, "La verificacion de salud de la base de datos fallo");
            return new EstadoBaseDatos(false, null);
        }
    }
}
