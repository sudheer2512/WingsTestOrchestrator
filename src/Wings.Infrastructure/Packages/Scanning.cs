using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Options;
using Wings.Application.Testing;

namespace Wings.Infrastructure.Packages;

/// <summary>Scans one file's content. Implementations must never execute the content.</summary>
public interface IContentScanner
{
    string Name { get; }
    /// <summary>False when the scanner is not configured; the report then says that this scan did not run.</summary>
    bool IsEnabled { get; }
    Task<IReadOnlyList<ValidationFinding>> ScanAsync(string path, byte[] content, CancellationToken ct);
}

/// <summary>
/// Always-on checks: the EICAR test signature, native executable headers (PE/ELF/Mach-O) and blocked file types.
/// This does not replace an antivirus engine; configure ClamAV for signature-based malware scanning.
/// </summary>
public sealed class BuiltInContentScanner : IContentScanner
{
    // Assembled at runtime so this binary never contains the contiguous test signature (antivirus would flag it).
    public static readonly byte[] Eicar = System.Text.Encoding.ASCII.GetBytes(string.Join("", @"X5O!P%@AP[4\PZX54(P^)7CC)7}$", "EICAR-STANDARD-ANTIVIRUS-TEST-FILE!$H+H*"));

    public static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".dll", ".so", ".dylib", ".msi", ".msp", ".com", ".scr", ".pif", ".cpl", ".sys", ".drv", ".ocx",
        ".bat", ".cmd", ".vbs", ".vbe", ".wsf", ".wsh", ".hta", ".jar", ".apk", ".app", ".lnk", ".reg",
        ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".cab", ".iso", ".nupkg",
    };

    public static readonly HashSet<string> ReviewExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ps1", ".psm1", ".sh", ".bash" };

    public string Name => "Built-in signature and file-type checks";
    public bool IsEnabled => true;

    public Task<IReadOnlyList<ValidationFinding>> ScanAsync(string path, byte[] content, CancellationToken ct)
    {
        var findings = new List<ValidationFinding>();
        var ext = Path.GetExtension(path);
        if (BlockedExtensions.Contains(ext))
            findings.Add(new("error", $"'{ext}' files are not allowed in test packages (executables and nested archives cannot be reviewed or scanned).", path));
        else if (ReviewExtensions.Contains(ext))
            findings.Add(new("warning", "Shell script included. Reviewers should confirm what it does before approval.", path));

        if (content.AsSpan().IndexOf(Eicar) >= 0)
            findings.Add(new("error", "EICAR anti-malware test signature detected.", path));
        if (IsNativeExecutable(content))
            findings.Add(new("error", "Native executable content detected (PE/ELF/Mach-O header).", path));
        return Task.FromResult<IReadOnlyList<ValidationFinding>>(findings);
    }

    private static bool IsNativeExecutable(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 64 && b[0] == 'M' && b[1] == 'Z')
        {
            var peOffset = BitConverter.ToInt32(b.Slice(0x3C, 4));
            if (peOffset > 0 && peOffset + 4 <= b.Length && b[peOffset] == 'P' && b[peOffset + 1] == 'E' && b[peOffset + 2] == 0 && b[peOffset + 3] == 0) return true;
        }
        if (b.Length >= 4 && b[0] == 0x7F && b[1] == 'E' && b[2] == 'L' && b[3] == 'F') return true;
        if (b.Length >= 4)
        {
            var magic = BitConverter.ToUInt32(b[..4]);
            if (magic is 0xFEEDFACE or 0xFEEDFACF or 0xCEFAEDFE or 0xCFFAEDFE) return true;
        }
        return false;
    }
}

public sealed class ClamAvOptions
{
    /// <summary>clamd host. Leave empty to disable ClamAV scanning.</summary>
    public string? Host { get; set; }
    public int Port { get; set; } = 3310;
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>Streams content to clamd using the INSTREAM protocol.</summary>
public sealed class ClamAvScanner(IOptions<ClamAvOptions> options) : IContentScanner
{
    public string Name => "ClamAV";
    public bool IsEnabled => !string.IsNullOrWhiteSpace(options.Value.Host);

    public async Task<IReadOnlyList<ValidationFinding>> ScanAsync(string path, byte[] content, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(options.Value.TimeoutSeconds));
        using var client = new TcpClient();
        await client.ConnectAsync(options.Value.Host!, options.Value.Port, cts.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync("zINSTREAM\0"u8.ToArray(), cts.Token);
        const int chunk = 64 * 1024;
        for (var offset = 0; offset < content.Length; offset += chunk)
        {
            var len = Math.Min(chunk, content.Length - offset);
            var size = BitConverter.GetBytes(System.Net.IPAddress.HostToNetworkOrder(len));
            await stream.WriteAsync(size, cts.Token);
            await stream.WriteAsync(content.AsMemory(offset, len), cts.Token);
        }
        await stream.WriteAsync(new byte[4], cts.Token);
        using var reader = new StreamReader(stream, Encoding.ASCII);
        var reply = (await reader.ReadToEndAsync(cts.Token)).TrimEnd('\0', '\n');
        if (reply.EndsWith("OK", StringComparison.Ordinal)) return [];
        if (reply.EndsWith("FOUND", StringComparison.Ordinal)) return [new("error", $"ClamAV: {reply.Replace("stream:", "").Trim()}", path)];
        throw new InvalidOperationException($"Unexpected ClamAV reply: {reply}");
    }
}
