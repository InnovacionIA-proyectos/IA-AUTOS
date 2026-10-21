using IAAutos.Aplicacion.Archivos;
using IAAutos.Aplicacion.Auth.Services;
using IAAutos.Aplicacion.Catalogos;
using IAAutos.Aplicacion.Inspecciones;
using IAAutos.Aplicacion.Reportes;
using IAAutos.Aplicacion.Comun;
using IAAutos.Aplicacion.Sofasa;
using IAAutos.Aplicacion.Vehiculos;
using IAAutos.Dominio.Catalogos;
using IAAutos.Dominio.Auditoria;
using IAAutos.Dominio.Comun;
using IAAutos.Dominio.Custodia;
using IAAutos.Dominio.Inspecciones;
using IAAutos.Dominio.Sofasa;
using IAAutos.Dominio.Usuarios;
using IAAutos.Dominio.Vehiculos;
using IAAutos.Dominio.Viajes;
using IAAutos.Aplicacion.Viajes;
using IAAutos.Infraestructura.Almacenamiento;
using IAAutos.Infraestructura.Catalogos;
using IAAutos.Infraestructura.Persistencia;
using IAAutos.Infraestructura.Portal;
using IAAutos.Infraestructura.Reportes;
using IAAutos.Infraestructura.Persistencia.Consultas;
using IAAutos.Infraestructura.Persistencia.Repositorios;
using IAAutos.Infraestructura.Seguridad;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace IAAutos.Infraestructura;

public static class clsDependenciasInfraestructura
{
    public const string NOMBRE_CADENA_CONEXION = "DefaultConnection";

    /// <summary>Registra persistencia (SQL Server 2019), repositorios y servicios de infraestructura.</summary>
    public static IServiceCollection AddInfraestructura(this IServiceCollection servicios, IConfiguration configuracion)
    {
        var cadena = configuracion.GetConnectionString(NOMBRE_CADENA_CONEXION);
        if (string.IsNullOrWhiteSpace(cadena))
        {
            throw new InvalidOperationException(
                $"Falta la cadena de conexion ConnectionStrings:{NOMBRE_CADENA_CONEXION} (variable de entorno ConnectionStrings__{NOMBRE_CADENA_CONEXION}).");
        }

        servicios.AddDbContext<clsAppDbContext>(opciones => opciones.UseSqlServer(
            cadena,
            sql =>
            {
                sql.CommandTimeout(30);
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(2), errorNumbersToAdd: null);
            }));

        servicios.Configure<clsOpcionesJwt>(configuracion.GetSection(clsOpcionesJwt.SECCION));
        servicios.Configure<clsOpcionesContrasenas>(configuracion.GetSection(clsOpcionesContrasenas.SECCION));
        servicios.AddSingleton<IReloj, clsRelojSistema>();
        servicios.AddSingleton<IServicioContrasenas, clsServicioContrasenas>();
        servicios.AddSingleton<IEmisorToken, clsEmisorTokenJwt>();
        servicios.AddScoped<ISaludBaseDatos, clsSaludBaseDatos>();
        servicios.AddScoped<IUnidadDeTrabajo, clsUnidadDeTrabajo>();
        servicios.AddScoped<IConsultasVehiculos, clsConsultasVehiculos>();
        servicios.AddScoped<IConsultasSofasa, clsConsultasSofasa>();
        servicios.AddScoped<IConsultasViajes, clsConsultasViajes>();
        servicios.AddScoped<IConsultasInspecciones, clsConsultasInspecciones>();
        servicios.AddScoped<IDescriptorHallazgos, clsDescriptorHallazgos>();
        servicios.AddScoped<IConsultasHallazgos, clsConsultasHallazgos>();
        servicios.AddScoped<IConsultasReportes, clsConsultasReportes>();
        servicios.AddSingleton<IGeneradorPdfInventario, clsGeneradorPdfInventario>();
        servicios.AddSingleton<IGeneradorPdfConstancia, clsGeneradorPdfConstancia>();

        servicios.Configure<clsOpcionesAlmacenamiento>(configuracion.GetSection(clsOpcionesAlmacenamiento.SECCION));
        servicios.Configure<clsOpcionesS3>(configuracion.GetSection(clsOpcionesS3.SECCION));
        var s3 = configuracion.GetSection(clsOpcionesS3.SECCION).Get<clsOpcionesS3>() ?? new clsOpcionesS3();
        if (s3.Configurado)
        {
            servicios.AddSingleton<IAlmacenArchivos, clsAlmacenS3>();
        }
        else
        {
            servicios.AddSingleton<IAlmacenArchivos, clsAlmacenDisco>();
        }

        servicios.AddScoped<IUsuarioRepository, clsUsuarioRepository>();
        servicios.AddScoped<IRefreshTokenRepository, clsRefreshTokenRepository>();
        servicios.Configure<clsOpcionesPortalTi>(configuracion.GetSection(clsOpcionesPortalTi.SECCION));
        servicios.AddScoped<IPortalUsuarios, clsPortalUsuarios>();
        servicios.AddScoped<IVehiculoRepository, clsVehiculoRepository>();
        RegistrarCatalogos(servicios);
        servicios.AddScoped<IEtapaRepository, clsEtapaRepository>();
        servicios.AddScoped<IInspeccionRepository, clsInspeccionRepository>();
        servicios.AddScoped<ITransferenciaCustodiaRepository, clsTransferenciaCustodiaRepository>();
        servicios.AddScoped<IViajeRepository, clsViajeRepository>();
        servicios.AddScoped<IOrdenSofasaRepository, clsOrdenSofasaRepository>();
        servicios.AddScoped<IAuditoriaRepository, clsAuditoriaRepository>();
        return servicios;
    }

    /// <summary>Catalogos maestros: cada uno se registra como ICatalogo y el servicio de aplicacion los administra y los cachea.</summary>
    private static void RegistrarCatalogos(IServiceCollection servicios)
    {
        servicios.AddMemoryCache();
        servicios.AddScoped<ICatalogo>(p => new clsCatalogoSimple<clsEtapaCatalogo>(
            p.GetRequiredService<clsAppDbContext>(), p.GetRequiredService<IReloj>(), clsNombresCatalogo.ETAPAS, clsEtapaCatalogo.Crear));
        servicios.AddScoped<ICatalogo>(p => new clsCatalogoSimple<clsPuntoCustodia>(
            p.GetRequiredService<clsAppDbContext>(), p.GetRequiredService<IReloj>(), clsNombresCatalogo.PUNTOS_CUSTODIA, clsPuntoCustodia.Crear));
        servicios.AddScoped<ICatalogo>(p => new clsCatalogoSimple<clsTipoUnidadTransportadora>(
            p.GetRequiredService<clsAppDbContext>(), p.GetRequiredService<IReloj>(), clsNombresCatalogo.TIPOS_UNIDAD, clsTipoUnidadTransportadora.Crear));
        servicios.AddScoped<ICatalogo>(p => new clsCatalogoSimple<clsCliente>(
            p.GetRequiredService<clsAppDbContext>(), p.GetRequiredService<IReloj>(), clsNombresCatalogo.CLIENTES, clsCliente.Crear));
        servicios.AddScoped<ICatalogo>(p => new clsCatalogoSimple<clsClaseDano>(
            p.GetRequiredService<clsAppDbContext>(), p.GetRequiredService<IReloj>(), clsNombresCatalogo.CLASES_DANO, clsClaseDano.Crear));
        servicios.AddScoped<ICatalogo>(p => new clsCatalogoSimple<clsTamanoDano>(
            p.GetRequiredService<clsAppDbContext>(), p.GetRequiredService<IReloj>(), clsNombresCatalogo.TAMANOS_DANO, clsTamanoDano.Crear));
        servicios.AddScoped<ICatalogo, clsCatalogoNiveles>();
        servicios.AddScoped<ICatalogo, clsCatalogoVistas>();
        servicios.AddScoped<ICatalogo, clsCatalogoCuadrantes>();
        servicios.AddScoped<ICatalogo, clsCatalogoPanelesDano>();
        servicios.AddScoped<ICatalogo, clsCatalogoCuadranteRegiones>();
        servicios.AddScoped<IImagenesPanelDano, clsImagenesPanelDano>();
    }
}
