namespace StockOrder.Infrastructure.Files;

/// <summary>NTFS folder outside the web root (section 2, 17); files are served back through the API, never directly.</summary>
public interface IFileStore
{
    /// <returns>The path to store in platform.Attachment.StoragePath, relative to the configured root.</returns>
    Task<string> SaveAsync(string relativeFolder, string fileName, Stream content, CancellationToken ct);

    Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct);

    Task DeleteAsync(string storagePath, CancellationToken ct);
}

public sealed class FileStoreOptions
{
    public const string SectionName = "Files";

    public string RootPath { get; set; } = string.Empty;
}
