using Microsoft.Extensions.Options;

namespace StockOrder.Infrastructure.Files;

internal sealed class NtfsFileStore(IOptions<FileStoreOptions> options) : IFileStore
{
    public async Task<string> SaveAsync(string relativeFolder, string fileName, Stream content, CancellationToken ct)
    {
        var folder = Path.Combine(options.Value.RootPath, SanitizeSegment(relativeFolder));
        Directory.CreateDirectory(folder);

        var storedName = $"{Guid.CreateVersion7()}_{SanitizeSegment(fileName)}";
        var fullPath = Path.Combine(folder, storedName);

        await using var fileStream = File.Create(fullPath);
        await content.CopyToAsync(fileStream, ct);

        return Path.Combine(relativeFolder, storedName).Replace('\\', '/');
    }

    public Task<Stream> OpenReadAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = ResolveFullPath(storagePath);
        return Task.FromResult<Stream>(File.OpenRead(fullPath));
    }

    public Task DeleteAsync(string storagePath, CancellationToken ct)
    {
        var fullPath = ResolveFullPath(storagePath);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string ResolveFullPath(string storagePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(options.Value.RootPath, storagePath));
        var rootFullPath = Path.GetFullPath(options.Value.RootPath);
        if (!fullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new UnauthorizedAccessException("Storage path escapes the configured file root.");
        }

        return fullPath;
    }

    private static string SanitizeSegment(string segment)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(segment.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
    }
}
