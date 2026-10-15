using System.Text.Json;
using IAAutos.Dominio.Comun;

namespace IAAutos.Dominio.Catalogos;

/// <summary>Etapa del proceso (cargue, transporte, descargue). Es el "proceso" de cada tramo de la ruta de un vehiculo.</summary>
public sealed class clsEtapaCatalogo : clsCatalogoCodificado
{
    private clsEtapaCatalogo()
    {
    }

    public static clsEtapaCatalogo Crear(string? codigo, string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var etapa = new clsEtapaCatalogo();
        etapa.Iniciar(codigo, nombre, orden, activo, ahora);
        return etapa;
    }
}

/// <summary>Punto de custodia = lugar fisico donde se hace una etapa (CEDI 1, Origen, Certificadora, Destino, CEDI 2, Concesionario).</summary>
public sealed class clsPuntoCustodia : clsCatalogoCodificado
{
    private clsPuntoCustodia()
    {
    }

    public static clsPuntoCustodia Crear(string? codigo, string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var lugar = new clsPuntoCustodia();
        lugar.Iniciar(codigo, nombre, orden, activo, ahora);
        return lugar;
    }
}

/// <summary>Tipo de unidad transportadora (ninera, nodriza, quintinaria, planchon grua).</summary>
public sealed class clsTipoUnidadTransportadora : clsCatalogoCodificado
{
    private clsTipoUnidadTransportadora()
    {
    }

    public static clsTipoUnidadTransportadora Crear(string? codigo, string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var tipo = new clsTipoUnidadTransportadora();
        tipo.Iniciar(codigo, nombre, orden, activo, ahora);
        return tipo;
    }
}

/// <summary>Clase de dano de un hallazgo (deformacion, rotura, golpe...). tblHallazgos guarda el codigo.</summary>
public sealed class clsClaseDano : clsCatalogoCodificado
{
    private clsClaseDano()
    {
    }

    public static clsClaseDano Crear(string? codigo, string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var clase = new clsClaseDano();
        clase.Iniciar(codigo, nombre, orden, activo, ahora);
        return clase;
    }
}

/// <summary>Tamano de dano de un hallazgo. tblHallazgos guarda el codigo.</summary>
public sealed class clsTamanoDano : clsCatalogoCodificado
{
    /// <summary>
    /// Codigo con el que el analisis de patrones cuenta los danos que obligan a reemplazar la pieza. Si se retira o se cambia su
    /// codigo, esa sugerencia deja de calcularse.
    /// </summary>
    public const string REQUIERE_REEMPLAZO = "requiere_reemplazo";

    private clsTamanoDano()
    {
    }

    public static clsTamanoDano Crear(string? codigo, string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var tamano = new clsTamanoDano();
        tamano.Iniciar(codigo, nombre, orden, activo, ahora);
        return tamano;
    }
}

/// <summary>Nivel de severidad (V1+, V1, V2, V3) con la accion que le corresponde.</summary>
public sealed class clsNivelSeveridad : clsCatalogoCodificado
{
    public const int ACCION_MAX = 500;

    private clsNivelSeveridad()
    {
    }

    /// <summary>Que debe hacerse con una unidad de este nivel. Opcional.</summary>
    public string? Accion { get; private set; }

    public static clsNivelSeveridad Crear(string? codigo, string? nombre, string? accion, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var nivel = new clsNivelSeveridad { Accion = AccionValida(accion) };
        nivel.Iniciar(codigo, nombre, orden, activo, ahora);
        return nivel;
    }

    /// <summary>Cambia la accion. Un texto vacio la borra.</summary>
    public void CambiarAccion(string? accion, DateTimeOffset ahora)
    {
        Accion = AccionValida(accion);
        Tocar(ahora);
    }

    private static string? AccionValida(string? accion)
    {
        var limpia = clsValidador.Opcional(accion);
        return limpia is { Length: > ACCION_MAX }
            ? throw clsErroresCampos.De("accion", $"La acción admite máximo {ACCION_MAX} caracteres")
            : limpia;
    }
}

/// <summary>
/// Vista de foto del vehiculo. Las obligatorias se toman siempre; las adicionales (piezas en el baul, cojineria) solo cuando hay
/// faltante o dano. TipoVehiculo nulo = aplica a cualquier tipo de vehiculo.
/// </summary>
public sealed class clsVistaFoto : clsCatalogoCodificado
{
    public const int TIPO_VEHICULO_MAX = 30;

    private clsVistaFoto()
    {
    }

    public bool Obligatoria { get; private set; }

    public string? TipoVehiculo { get; private set; }

    public static clsVistaFoto Crear(
        string? codigo,
        string? nombre,
        bool? obligatoria,
        string? tipoVehiculo,
        int? orden,
        bool? activo,
        DateTimeOffset ahora)
    {
        var vista = new clsVistaFoto { Obligatoria = obligatoria ?? true, TipoVehiculo = TipoVehiculoValido(tipoVehiculo) };
        vista.Iniciar(codigo, nombre, orden, activo, ahora);
        return vista;
    }

    /// <summary>Cambia lo propio de la vista; <paramref name="cambiaTipoVehiculo"/> distingue "no enviado" de "enviado en nulo".</summary>
    public void CambiarVista(bool? obligatoria, string? tipoVehiculo, bool cambiaTipoVehiculo, DateTimeOffset ahora)
    {
        if (obligatoria is not null)
        {
            Obligatoria = obligatoria.Value;
        }

        if (cambiaTipoVehiculo)
        {
            TipoVehiculo = TipoVehiculoValido(tipoVehiculo);
        }

        Tocar(ahora);
    }

    internal static string? TipoVehiculoValido(string? tipo)
    {
        var limpio = clsValidador.Opcional(tipo);
        return limpio is { Length: > TIPO_VEHICULO_MAX }
            ? throw clsErroresCampos.De("tipo_vehiculo", $"El tipo de vehículo admite máximo {TIPO_VEHICULO_MAX} caracteres")
            : limpio;
    }
}

/// <summary>Cliente (empresa) al que pertenecen los usuarios y los vehiculos. Cada usuario ve solo lo de su cliente.</summary>
public sealed class clsCliente : clsCatalogoCodificado
{
    /// <summary>Cliente de las ordenes que llegan por la ingesta de Sofasa.</summary>
    public const string CODIGO_SOFASA = "sofasa";

    private clsCliente()
    {
    }

    public static clsCliente Crear(string? codigo, string? nombre, int? orden, bool? activo, DateTimeOffset ahora)
    {
        var cliente = new clsCliente();
        cliente.Iniciar(codigo, nombre, orden, activo, ahora);
        return cliente;
    }
}
