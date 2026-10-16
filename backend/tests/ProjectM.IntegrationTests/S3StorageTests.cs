using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using FluentAssertions;
using Microsoft.Extensions.Options;
using ProjectM.Api.Infrastructure.Storage;

namespace ProjectM.IntegrationTests;

/// <summary>Solo se ejecuta si hay un S3 de pruebas (TEST_S3_URL, TEST_S3_KEY, TEST_S3_SECRET), p. ej. MinIO.</summary>
public sealed class S3FactAttribute : FactAttribute
{
    public S3FactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TEST_S3_URL")))
            Skip = "Sin S3 de pruebas (define TEST_S3_URL/TEST_S3_KEY/TEST_S3_SECRET).";
    }
}

public class S3StorageTests
{
    private static StorageOptions Options() => new()
    {
        Provider = "S3",
        S3 = new S3StorageOptions
        {
            ServiceUrl = Environment.GetEnvironmentVariable("TEST_S3_URL")!,
            AccessKeyId = Environment.GetEnvironmentVariable("TEST_S3_KEY")!,
            SecretAccessKey = Environment.GetEnvironmentVariable("TEST_S3_SECRET")!,
            Bucket = "projectm-test",
            Region = "us-east-1"
        }
    };

    [S3Fact]
    public async Task Guarda_lee_y_borra_archivos()
    {
        var opts = Options();
        using (var admin = new AmazonS3Client(new BasicAWSCredentials(opts.S3.AccessKeyId, opts.S3.SecretAccessKey),
                   new AmazonS3Config { ServiceURL = opts.S3.ServiceUrl, ForcePathStyle = true, AuthenticationRegion = "us-east-1" }))
        {
            try { await admin.PutBucketAsync(opts.S3.Bucket); } catch (AmazonS3Exception) { /* ya existe */ }
        }

        using var storage = new S3FileStorage(Microsoft.Extensions.Options.Options.Create(opts));
        var key = await storage.SaveAsync(new MemoryStream(Encoding.UTF8.GetBytes("hola desde S3")), default);

        await using (var stream = await storage.OpenReadAsync(key, default))
        using (var reader = new StreamReader(stream))
            (await reader.ReadToEndAsync()).Should().Be("hola desde S3");

        await storage.DeleteAsync(key, default);
        var read = () => storage.OpenReadAsync(key, default);
        await read.Should().ThrowAsync<FileNotFoundException>();
    }
}
