using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Options;

namespace ProjectM.Api.Infrastructure.Storage;

/// <summary>Configuración de un almacenamiento compatible con S3 (Supabase Storage, AWS S3, Cloudflare R2, MinIO).</summary>
public class S3StorageOptions
{
    /// <summary>Endpoint S3. Supabase: https://&lt;proyecto&gt;.supabase.co/storage/v1/s3</summary>
    public string ServiceUrl { get; set; } = "";
    /// <summary>Región del proyecto (Supabase la muestra en Storage → S3 Connection).</summary>
    public string Region { get; set; } = "us-east-1";
    public string Bucket { get; set; } = "attachments";
    public string AccessKeyId { get; set; } = "";
    public string SecretAccessKey { get; set; } = "";
}

/// <summary>
/// Adjuntos en un bucket S3 privado. La descarga siempre pasa por la API, que valida permisos;
/// el bucket nunca se expone públicamente.
/// </summary>
public sealed class S3FileStorage : IFileStorage, IDisposable
{
    private readonly AmazonS3Client _client;
    private readonly string _bucket;

    public S3FileStorage(IOptions<StorageOptions> options)
    {
        var o = options.Value.S3;
        if (string.IsNullOrWhiteSpace(o.ServiceUrl) || string.IsNullOrWhiteSpace(o.AccessKeyId) || string.IsNullOrWhiteSpace(o.SecretAccessKey))
            throw new InvalidOperationException("Storage:S3 requiere ServiceUrl, AccessKeyId y SecretAccessKey.");
        _bucket = o.Bucket;
        _client = new AmazonS3Client(new BasicAWSCredentials(o.AccessKeyId, o.SecretAccessKey), new AmazonS3Config
        {
            ServiceURL = o.ServiceUrl,
            AuthenticationRegion = o.Region,
            ForcePathStyle = true,
            // Los servicios compatibles con S3 (Supabase, R2, MinIO) no aceptan siempre los checksums
            // CRC que el SDK agrega por defecto.
            RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED,
            ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED,
        });
    }

    public async Task<string> SaveAsync(Stream content, CancellationToken ct)
    {
        var key = Guid.NewGuid().ToString("N");
        await _client.PutObjectAsync(new PutObjectRequest
        {
            BucketName = _bucket,
            Key = key,
            InputStream = content,
            AutoCloseStream = false,
            UseChunkEncoding = false,
            ContentType = "application/octet-stream"
        }, ct);
        return key;
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        try
        {
            var response = await _client.GetObjectAsync(_bucket, key, ct);
            return new ResponseStream(response);
        }
        catch (AmazonS3Exception e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            throw new FileNotFoundException("Archivo no encontrado", key);
        }
    }

    public Task DeleteAsync(string key, CancellationToken ct) =>
        _client.DeleteObjectAsync(_bucket, key, ct);

    public void Dispose() => _client.Dispose();

    /// <summary>Mantiene viva la respuesta HTTP mientras se lee el contenido.</summary>
    private sealed class ResponseStream(GetObjectResponse response) : Stream
    {
        private readonly Stream _inner = response.ResponseStream;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => response.ContentLength;
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => _inner.ReadAsync(buffer, offset, count, ct);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => _inner.ReadAsync(buffer, ct);
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing)
        {
            if (disposing) response.Dispose();
            base.Dispose(disposing);
        }
    }
}
