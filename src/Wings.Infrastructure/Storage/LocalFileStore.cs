using System.Security.Cryptography;
using Microsoft.Extensions.Options;
using Wings.Application.Abstractions;

namespace Wings.Infrastructure.Storage;

public sealed class StorageOptions
{
    /// <summary>Local folder or UNC network share. Relative paths resolve against the content root.</summary>
    public string RootPath { get; set; } = "App_Data/storage";
}

/// <summary>
/// Content-addressed blob store on a local disk or network share: blobs are written once under their SHA-256 and never
/// modified, which gives immutable artifacts and free de-duplication. An S3-compatible store can implement <see cref="IFileStore"/>.
/// </summary>
public sealed class LocalFileStore(IOptions<StorageOptions> options) : IFileStore
{
    private readonly string _root = Path.GetFullPath(options.Value.RootPath);

    public async Task<StoredBlob> PutAsync(Stream content, CancellationToken ct = default)
    {
        Directory.CreateDirectory(Path.Combine(_root, "tmp"));
        var temp = Path.Combine(_root, "tmp", Guid.NewGuid().ToString("N"));
        string sha;
        long size;
        await using (var file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        using (var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
        {
            var buffer = new byte[81920];
            int read;
            while ((read = await content.ReadAsync(buffer, ct)) > 0)
            {
                hasher.AppendData(buffer, 0, read);
                await file.WriteAsync(buffer.AsMemory(0, read), ct);
            }
            size = file.Length;
            sha = Convert.ToHexStringLower(hasher.GetHashAndReset());
        }

        var key = $"sha256/{sha[..2]}/{sha}";
        var target = Resolve(key);
        if (File.Exists(target)) File.Delete(temp);
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try { File.Move(temp, target); }
            catch (IOException) when (File.Exists(target)) { File.Delete(temp); }
        }
        return new StoredBlob(key, sha, size);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken ct = default)
    {
        var path = Resolve(key);
        if (!File.Exists(path)) throw new FileNotFoundException($"Blob '{key}' is missing from the file store.");
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true));
    }

    public Task<bool> ExistsAsync(string key, CancellationToken ct = default) => Task.FromResult(File.Exists(Resolve(key)));

    private string Resolve(string key)
    {
        var full = Path.GetFullPath(Path.Combine(_root, key));
        if (!full.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new InvalidOperationException("Invalid storage key.");
        return full;
    }
}
