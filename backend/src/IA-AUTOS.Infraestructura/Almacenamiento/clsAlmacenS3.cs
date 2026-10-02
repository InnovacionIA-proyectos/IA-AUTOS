using System.Net;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using IAAutos.Aplicacion.Archivos;
using Microsoft.Extensions.Options;

namespace IAAutos.Infraestructura.Almacenamiento;

public sealed class clsOpcionesS3
{
    public const string SECCION = "Storage:S3";

    public string Endpoint { get; set; } = string.Empty;

    public string Bucket { get; set; } = string.Empty;

    public string Region { get; set; } = "us-east-1";

    public string AccessKey { get; set; } = string.Empty;

    public string SecretKey { get; set; } = string.Empty;

    public bool Configurado => !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Bucket);
}

/// <summary>Almacenamiento S3 compatible (MinIO). El bucket se crea si no existe; las imagenes se sirven a traves de la API.</summary>
internal sealed class clsAlmacenS3 : IAlmacenArchivos, IDisposable
{
    private readonly AmazonS3Client _cliente;
    private readonly string _bucket;
    private readonly SemaphoreSlim _candado = new(1, 1);
    private bool _bucketListo;

    public clsAlmacenS3(IOptions<clsOpcionesS3> opciones)
    {
        var o = opciones.Value;
        _bucket = o.Bucket;
        _cliente = new AmazonS3Client(
            new BasicAWSCredentials(o.AccessKey, o.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = o.Endpoint,
                ForcePathStyle = true,
                AuthenticationRegion = o.Region,
                // Los almacenes S3 compatibles (MinIO) no siempre admiten las sumas de verificacion por defecto del SDK nuevo.
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });
    }

    public async Task GuardarAsync(string nombreArchivo, byte[] contenido, string tipoContenido, CancellationToken ct = default)
    {
        await AsegurarBucketAsync(ct);
        using var flujo = new MemoryStream(contenido);
        await _cliente.PutObjectAsync(
            new PutObjectRequest { BucketName = _bucket, Key = nombreArchivo, InputStream = flujo, ContentType = tipoContenido },
            ct);
    }

    public async Task<byte[]?> LeerAsync(string nombreArchivo, CancellationToken ct = default)
    {
        try
        {
            using var respuesta = await _cliente.GetObjectAsync(_bucket, nombreArchivo, ct);
            using var memoria = new MemoryStream();
            await respuesta.ResponseStream.CopyToAsync(memoria, ct);
            return memoria.ToArray();
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _cliente.Dispose();
        _candado.Dispose();
    }

    private async Task AsegurarBucketAsync(CancellationToken ct)
    {
        if (_bucketListo)
        {
            return;
        }

        await _candado.WaitAsync(ct);
        try
        {
            if (_bucketListo)
            {
                return;
            }

            if (!await AmazonS3Util.DoesS3BucketExistV2Async(_cliente, _bucket))
            {
                try
                {
                    await _cliente.PutBucketAsync(new PutBucketRequest { BucketName = _bucket }, ct);
                }
                catch (AmazonS3Exception ex) when (ex.ErrorCode is "BucketAlreadyOwnedByYou" or "BucketAlreadyExists")
                {
                    // Condicion de carrera con otra instancia: ya existe.
                }
            }

            _bucketListo = true;
        }
        finally
        {
            _candado.Release();
        }
    }
}
