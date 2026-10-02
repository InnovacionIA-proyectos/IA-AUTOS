namespace IAAutos.Aplicacion.Comun;

/// <summary>Fuente de la hora actual (UTC). Permite fijar el tiempo en las pruebas.</summary>
public interface IReloj
{
    DateTimeOffset Ahora { get; }
}
