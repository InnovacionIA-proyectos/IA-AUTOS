using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;

namespace IAAutos.Tests.EntornoReal;

/// <summary>Verifica el MinIO real con una lectura (listar como maximo un objeto). No sube ni borra nada.</summary>
public sealed class AlmacenMinioRealTests
{
    [FactEntornoReal]
    public async Task El_bucket_de_imagenes_existe_y_las_credenciales_pueden_leerlo()
    {
        using var cliente = new AmazonS3Client(
            new BasicAWSCredentials(clsEntornoReal.Exigir(clsEntornoReal.S3_ACCESS_KEY), clsEntornoReal.Exigir(clsEntornoReal.S3_SECRET_KEY)),
            new AmazonS3Config
            {
                ServiceURL = clsEntornoReal.Exigir(clsEntornoReal.S3_ENDPOINT),
                ForcePathStyle = true,
                AuthenticationRegion = clsEntornoReal.Leer(clsEntornoReal.S3_REGION) ?? "us-east-1",
                RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
                ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
            });

        var respuesta = await cliente.ListObjectsV2Async(new ListObjectsV2Request
        {
            BucketName = clsEntornoReal.Exigir(clsEntornoReal.S3_BUCKET),
            MaxKeys = 1,
        });

        Assert.Equal(System.Net.HttpStatusCode.OK, respuesta.HttpStatusCode);
    }
}
