namespace ProjectM.Api.Infrastructure.Storage;

/// <summary>Almacenamiento de archivos. v1: disco local (volumen Docker); preparado para Blob/S3.</summary>
public interface IFileStorage
{
    Task<string> SaveAsync(Stream content, CancellationToken ct);
    Task<Stream> OpenReadAsync(string key, CancellationToken ct);
    Task DeleteAsync(string key, CancellationToken ct);
}

public class StorageOptions
{
    public const string Section = "Storage";
    /// <summary>"Local" (disco/volumen Docker) o "S3" (Supabase Storage u otro compatible).</summary>
    public string Provider { get; set; } = "Local";
    public S3StorageOptions S3 { get; set; } = new();
    public string AttachmentsPath { get; set; } = "/data/attachments";
    public int MaxFileSizeMb { get; set; } = 20;
    public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;

    /// <summary>Extensiones permitidas (lista blanca).</summary>
    public static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods",
        ".txt", ".csv", ".md", ".png", ".jpg", ".jpeg", ".gif", ".webp", ".zip"
    };
}

public class LocalFileStorage(Microsoft.Extensions.Options.IOptions<StorageOptions> options) : IFileStorage
{
    private readonly string _root = options.Value.AttachmentsPath;

    public async Task<string> SaveAsync(Stream content, CancellationToken ct)
    {
        // La clave es un GUID: el nombre original nunca toca el sistema de archivos.
        var key = Guid.NewGuid().ToString("N");
        var path = PathFor(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await using var file = File.Create(path);
        await content.CopyToAsync(file, ct);
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (!File.Exists(path)) throw new FileNotFoundException("Archivo no encontrado", key);
        return Task.FromResult<Stream>(File.OpenRead(path));
    }

    public Task DeleteAsync(string key, CancellationToken ct)
    {
        var path = PathFor(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }

    private string PathFor(string key)
    {
        if (key.Length != 32 || !key.All(Uri.IsHexDigit)) throw new ArgumentException("Clave inválida", nameof(key));
        return Path.Combine(_root, key[..2], key);
    }
}
