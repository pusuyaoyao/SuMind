using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace SuMind.Packages;

public sealed record PackageFile(string Path, long Size, string Sha256);
public sealed record PackageManifest(string Id, string Version, PackageFile[] Files);

/// <summary>Reader for the preliminary, uncompressed SUMINDS1 container. This verifies integrity, not publisher identity.</summary>
public static class PackageReader
{
    private const int MaxManifestBytes = 1024 * 1024;
    private const long MaxFileBytes = 128L * 1024 * 1024;
    private const long MaxPackageBytes = 512L * 1024 * 1024;
    private const int MaxFiles = 1024;
    private static readonly byte[] Magic = "SUMINDS1"u8.ToArray();

    public static PackageManifest Extract(Stream input, string destination, string expectedId, string expectedSha256)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!input.CanRead || !input.CanSeek) throw new ArgumentException("包必须是可读取和定位的流。", nameof(input));
        if (input.Length > MaxPackageBytes || input.Length < 12) throw new InvalidDataException("包大小无效。");
        if (expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit)) throw new InvalidDataException("包哈希无效。");
        input.Position = 0;
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(input), Convert.FromHexString(expectedSha256)))
            throw new InvalidDataException("包的 SHA-256 校验失败。");
        input.Position = 0;
        using var reader = new BinaryReader(input, Encoding.UTF8, leaveOpen: true);
        if (!reader.ReadBytes(8).SequenceEqual(Magic)) throw new InvalidDataException("不是受支持的 SuMind 拓展包。");
        int manifestSize = reader.ReadInt32();
        if (manifestSize <= 0 || manifestSize > MaxManifestBytes || manifestSize > input.Length - input.Position)
            throw new InvalidDataException("拓展清单大小无效。");
        var manifest = JsonSerializer.Deserialize<PackageManifest>(reader.ReadBytes(manifestSize), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidDataException("拓展清单无效。");
        if (string.IsNullOrWhiteSpace(manifest.Id) || !string.Equals(manifest.Id, expectedId, StringComparison.Ordinal)
            || !Version.TryParse(manifest.Version, out _) || manifest.Files is null || manifest.Files.Length is 0 or > MaxFiles)
            throw new InvalidDataException("拓展标识、版本或文件列表无效。");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var file in manifest.Files)
        {
            if (file is null || !IsSafePath(file.Path) || !names.Add(file.Path.Replace('/', '\\')) || file.Size < 0 || file.Size > MaxFileBytes
                || file.Sha256 is null || file.Sha256.Length != 64 || !file.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("拓展文件信息无效。");
            total = checked(total + file.Size);
            if (total > MaxPackageBytes) throw new InvalidDataException("拓展文件总大小超出限制。");
        }
        if (input.Length - input.Position != total) throw new InvalidDataException("拓展包数据长度与清单不符。");
        string root = Path.GetFullPath(destination);
        if (Directory.Exists(root) || File.Exists(root)) throw new IOException("目标目录已存在。");
        Directory.CreateDirectory(root);
        try
        {
            foreach (var file in manifest.Files)
            {
                string path = Path.GetFullPath(Path.Combine(root, file.Path.Replace('/', Path.DirectorySeparatorChar)));
                if (!path.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("拓展路径无效。");
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                byte[] buffer = new byte[64 * 1024];
                long remaining = file.Size;
                while (remaining > 0)
                {
                    int count = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (count == 0) throw new EndOfStreamException();
                    hash.AppendData(buffer, 0, count);
                    output.Write(buffer, 0, count);
                    remaining -= count;
                }
                if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(file.Sha256)))
                    throw new InvalidDataException("拓展文件校验失败。");
            }
            return manifest;
        }
        catch { Directory.Delete(root, recursive: true); throw; }
    }

    private static bool IsSafePath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 240 || path.Contains('\\') || path.StartsWith('/') || path.Contains(':')) return false;
        return path.Split('/').All(part => part.Length > 0 && part is not "." and not ".."
            && !part.EndsWith('.') && !part.EndsWith(' ') && part.All(c => c >= 32 && "<>\"|?*".IndexOf(c) < 0)
            && !IsReservedName(part));
    }

    private static bool IsReservedName(string part)
    {
        string stem = part.Split('.')[0];
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase)
            || stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase)
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) && stem[3] is >= '1' and <= '9');
    }
}
