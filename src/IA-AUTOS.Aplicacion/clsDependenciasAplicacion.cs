using FluentValidation;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Asignaciones;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Aplicacion.Sofasa;
using IAAutos.Aplicacion.Usuarios;
using IAAutos.Aplicacion.Vehiculos;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Aplicacion;

public static class clsDependenciasAplicacion
{
    /// <summary>Registra los casos de uso, validadores y servicios de la capa de Aplicacion.</summary>
    public static IServiceCollection AddAplicacion(this IServiceCollection servicios, IConfiguration configuracion)
    {
        servicios.Configure<clsOpcionesSeguridad>(configuracion.GetSection(clsOpcionesSeguridad.SECCION));
        servicios.Configure<clsOpcionesAlmacenamiento>(configuracion.GetSection(clsOpcionesAlmacenamiento.SECCION));
        servicios.AddValidatorsFromAssemblyContaining<clsAuthService>(ServiceLifetime.Singleton);
        servicios.AddScoped<IAuditor, clsAuditor>();
        servicios.AddScoped<clsAuthService>();
        servicios.AddScoped<clsUsuariosService>();
        servicios.AddScoped<clsVehiculosService>();
        servicios.AddScoped<clsAsignacionesService>();
        servicios.AddScoped<clsEtapasService>();
        servicios.AddScoped<clsSofasaService>();
        servicios.AddScoped<clsReportesService>();
        servicios.AddScoped<clsInspeccionesService>();
        servicios.AddScoped<clsArchivosService>();
        return servicios;
    }
}
