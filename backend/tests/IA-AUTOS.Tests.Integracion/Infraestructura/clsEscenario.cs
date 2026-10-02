using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Infraestructura;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Tests.Integracion.Infraestructura;

/// <summary>Construye el contenedor de dependencias de infraestructura apuntando a la base de pruebas.</summary>
public static class clsEscenario
{
    public static readonly DateTimeOffset AHORA = new(2026, 7, 17, 12, 30, 15, 123, TimeSpan.Zero);

    public static ServiceProvider CrearProveedor(string cadenaConexion)
    {
        var configuracion = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{clsDependenciasInfraestructura.NOMBRE_CADENA_CONEXION}"] = cadenaConexion,
            })
            .Build();
        return new ServiceCollection()
            .AddInfraestructura(configuracion)
            .BuildServiceProvider(validateScopes: true);
    }

    public static clsUsuario NuevoUsuario(string email = "uno@ejemplo.com", string rol = clsRol.INSPECTOR) =>
        clsUsuario.Crear("Usuario " + email, email, rol, null, "scrypt:aa:bb", AHORA);

    public static clsVehiculo NuevoVehiculo(string vin = "9FBHSR4E8PM000123") =>
        clsVehiculo.Crear(vin, "Renault", "Sandero", "Rojo", clsOperacion.NACIONAL, AHORA);
}
