using IAAutos.Aplicacion.Inspecciones;

namespace IAAutos.Aplicacion.Viajes;

/// <summary>Un puesto de la niñera con el VIN de la unidad que lo ocupa (nulo si esta libre).</summary>
public sealed record PuestoNinera(int Numero, string Etiqueta, string? Vin);

/// <summary>Un daño de un carro: su zona, clase y tamaño; el nivel y la nota solo van para quien puede verlos (nunca para el conductor).</summary>
public sealed record DanoNinera(string Zona, string? Clase, string Tamano, string? Nivel, string? Nota, string Informe);

/// <summary>
/// Un carro de la niñera: su puesto (nulo si no tiene), si no sale del viaje y por que, y los daños de sus informes vigentes del cargue. <c>CargueSinInforme</c> es verdadero si el carro aun no tiene ningun informe
/// del cargue.
/// </summary>
public sealed record CarroNinera(string Vin, string Marca, string? Modelo, string? Color, int? Puesto, bool NoSale, string? MotivoNoSale, IReadOnlyList<DanoNinera> Danos, bool CargueSinInforme);

/// <summary>Una firma del cargue de la niñera: quien firmo, la hora y el GPS del momento en que firmo, cuando llego al servidor y el trazo (como imagen del almacen o como ruta SVG).</summary>
public sealed record FirmaNinera(string Persona, DateTimeOffset? Momento, double? Lat, double? Lng, double? PrecisionM, DateTimeOffset? LlegoAt, string? Trazo, byte[]? Imagen);

/// <summary>La constancia de no firma de toda la niñera: la parte que se nego, el motivo, el testigo y la hora del momento.</summary>
public sealed record ConstanciaNinera(string ParteQueSeNego, string Motivo, string? Testigo, DateTimeOffset? Momento);

/// <summary>
/// Todo lo que lleva el PDF del informe de la niñera: el viaje, los puestos con su carro, los daños de cada carro, las fotos de la niñera, si la acomodacion esta cerrada y la firma unica del cargue (o su constancia de
/// no firma). <c>MostrarNivel</c> es falso para el conductor.
/// </summary>
public sealed record DatosNinera(
    string? CodigoMtrack,
    string? Placa,
    string? Trailer,
    string? Unidad,
    string? Conductor,
    string? Inspector,
    string? LugarCargue,
    string Estado,
    int Capacidad,
    IReadOnlyList<PuestoNinera> Puestos,
    IReadOnlyList<CarroNinera> Carros,
    IReadOnlyList<FotoInforme> Fotos,
    int FotosOmitidas,
    bool AcomodacionCerrada,
    FirmaNinera? Entrega,
    FirmaNinera? Recibe,
    ConstanciaNinera? Constancia,
    bool MostrarNivel,
    DateTimeOffset GeneradaAt);

/// <summary>Genera el informe de la niñera de un viaje en PDF, con las fotos y las firmas incrustadas.</summary>
public interface IGeneradorPdfNinera
{
    byte[] Generar(DatosNinera datos);
}
