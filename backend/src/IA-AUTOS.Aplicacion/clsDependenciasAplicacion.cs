using FluentValidation;
using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Auth;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Aplicacion.Sofasa;
using IAAutos.Aplicacion.Usuarios;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Aplicacion.Viajes;
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
        servicios.Configure<clsOpcionesSesion>(configuracion.GetSection(clsOpcionesSesion.SECCION));
        servicios.Configure<clsOpcionesPortalAplicacion>(configuracion.GetSection(clsOpcionesPortalAplicacion.SECCION));
        servicios.Configure<clsOpcionesCatalogos>(configuracion.GetSection(clsOpcionesCatalogos.SECCION));
        servicios.AddScoped<clsCatalogosService>();
        servicios.AddScoped<ICatalogosLectura>(proveedor => proveedor.GetRequiredService<clsCatalogosService>());
        servicios.AddScoped<clsAuthService>();
        servicios.AddScoped<clsSesionPortalService>();
        servicios.AddScoped<clsUsuariosService>();
        servicios.AddScoped<clsCargaConductoresService>();
        servicios.AddScoped<clsVehiculosService>();
        servicios.AddScoped<clsEnsamblajeViajes>();
        servicios.AddScoped<clsViajesService>();
        servicios.AddScoped<clsCargaViajesService>();
        servicios.AddScoped<clsSofasaService>();
        servicios.AddScoped<clsReportesService>();
        servicios.AddScoped<clsInspeccionesService>();
        servicios.AddScoped<clsArchivosService>();
        return servicios;
    }
}
