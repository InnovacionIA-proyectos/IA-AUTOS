using IAAutos.Aplicacion.Comun;

namespace IAAutos.Infraestructura;

internal sealed class clsRelojSistema : IReloj
{
    public DateTimeOffset Ahora => DateTimeOffset.UtcNow;
}
