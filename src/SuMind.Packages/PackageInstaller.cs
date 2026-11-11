using System.Text.Json;

namespace SuMind.Packages;

public sealed record InstalledPackage(string Id, string Version, string Directory);
public sealed record PackageMetadata(string Name, string Description, string Author, string MinHostVersion, string Architecture, string? IconSha256 = null);

/// <summary>Stages verified packages under a private per-user root and atomically changes their active version pointer.</summary>
public static class PackageInstaller
{
    public static InstalledPackage Install(string packagePath, string installRoot, string expectedId,
        string expectedVersion, string expectedSha256, byte[] signature, string trustedPublicKeyPem, PackageMetadata? expectedMetadata = null)
    {
        ValidateId(expectedId);
        ValidateVersion(expectedVersion);
        string root = PrepareRoot(installRoot);
        using var gate = AcquireGate(root);
        string moduleRoot = Path.Combine(root, expectedId);
        EnsureNormalDirectory(moduleRoot);
        string versions = Path.Combine(moduleRoot, "versions");
        EnsureNormalDirectory(versions);
        string final = Path.Combine(versions, expectedVersion);
        if (Directory.Exists(final) || File.Exists(final)) throw new IOException("此版本已安装。");
        string staging = Path.Combine(versions, ".staging-" + Guid.NewGuid().ToString("N"));
        bool moved = false;
        try
        {
            using var input = new FileStream(packagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            PackageSignature.Verify(input, signature, trustedPublicKeyPem);
            var manifest = PackageReader.Extract(input, staging, expectedId, expectedSha256);
            if (!string.Equals(manifest.Version, expectedVersion, StringComparison.Ordinal))
                throw new InvalidDataException("拓展包版本与目录不一致。");
            if (expectedMetadata is not null && (manifest.Name != expectedMetadata.Name || manifest.Description != expectedMetadata.Description
                || manifest.Author != expectedMetadata.Author || manifest.MinHostVersion != expectedMetadata.MinHostVersion || manifest.Architecture != expectedMetadata.Architecture))
                throw new InvalidDataException("拓展包信息与目录不一致。");
            if (!string.IsNullOrEmpty(manifest.IconPath) && (!manifest.IconPath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || !manifest.Files.Any(file => file.Path == manifest.IconPath)))
                throw new InvalidDataException("拓展包图标信息无效。");
            if (expectedMetadata is not null && !string.Equals(expectedMetadata.IconSha256,
                manifest.Files.FirstOrDefault(file => file.Path == manifest.IconPath)?.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("拓展图标与目录不一致。");
            Directory.Move(staging, final);
            moved = true;
            WriteActiveVersion(moduleRoot, expectedVersion);
            return new InstalledPackage(expectedId, expectedVersion, final);
        }
        catch
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, true);
            if (moved && Directory.Exists(final)) Directory.Delete(final, true);
            throw;
        }
    }

    public static InstalledPackage? GetInstalled(string installRoot, string id)
    {
        ValidateId(id);
        string root = Path.GetFullPath(installRoot);
        string moduleRoot = Path.Combine(root, id);
        string pointer = Path.Combine(moduleRoot, "current.json");
        if (!File.Exists(pointer)) return null;
        EnsureExistingNormalDirectory(root);
        EnsureExistingNormalDirectory(moduleRoot);
        EnsureExistingNormalDirectory(Path.Combine(moduleRoot, "versions"));
        if ((File.GetAttributes(pointer) & FileAttributes.ReparsePoint) != 0) throw new IOException("安装状态不能是链接。");
        using var input = new FileStream(pointer, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (input.Length > 4096) throw new InvalidDataException("安装状态无效。");
        var state = JsonSerializer.Deserialize<ActiveVersion>(input) ?? throw new InvalidDataException("安装状态无效。");
        ValidateVersion(state.Version);
        string directory = Path.Combine(moduleRoot, "versions", state.Version);
        EnsureExistingNormalDirectory(directory);
        return new InstalledPackage(id, state.Version, directory);
    }

    public static InstalledPackage ActivateVersion(string installRoot, string id, string version)
    {
        ValidateId(id);
        ValidateVersion(version);
        string root = PrepareRoot(installRoot);
        using var gate = AcquireGate(root);
        string moduleRoot = Path.Combine(root, id);
        EnsureExistingNormalDirectory(moduleRoot);
        EnsureExistingNormalDirectory(Path.Combine(moduleRoot, "versions"));
        string directory = Path.Combine(moduleRoot, "versions", version);
        EnsureExistingNormalDirectory(directory);
        WriteActiveVersion(moduleRoot, version);
        return new InstalledPackage(id, version, directory);
    }

    /// <summary>Remove only this extension's installation tree. The caller must stop its process first.</summary>
    public static bool Uninstall(string installRoot, string id)
    {
        ValidateId(id);
        string root = Path.GetFullPath(installRoot);
        if (!Directory.Exists(root)) return false;
        EnsureExistingNormalDirectory(root);
        using var gate = AcquireGate(root);
        string moduleRoot = Path.Combine(root, id);
        if (!Directory.Exists(moduleRoot)) return false;
        EnsureExistingNormalDirectory(moduleRoot);
        string tombstone = Path.Combine(root, ".remove-" + Guid.NewGuid().ToString("N"));
        Directory.Move(moduleRoot, tombstone);
        Directory.Delete(tombstone, recursive: true);
        return true;
    }

    private static string PrepareRoot(string installRoot)
    {
        string root = Path.GetFullPath(installRoot);
        EnsureNormalDirectory(root);
        return root;
    }

    private static FileStream AcquireGate(string root) => new(Path.Combine(root, ".install.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);

    private static void WriteActiveVersion(string moduleRoot, string version)
    {
        string temporary = Path.Combine(moduleRoot, ".current-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, new ActiveVersion(version));
                file.Flush(flushToDisk: true);
            }
            File.Move(temporary, Path.Combine(moduleRoot, "current.json"), overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void ValidateId(string id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64 || !id.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '-') || id[0] == '-' || id[^1] == '-')
            throw new ArgumentException("拓展 ID 无效。", nameof(id));
    }

    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrEmpty(version) || version.Length > 40 || !Version.TryParse(version, out var parsed) || parsed.ToString() != version)
            throw new ArgumentException("拓展版本无效。", nameof(version));
    }

    private static void EnsureNormalDirectory(string path)
    {
        if (File.Exists(path)) throw new IOException("安装目录被文件占用。");
        Directory.CreateDirectory(path);
        EnsureExistingNormalDirectory(path);
    }

    private static void EnsureExistingNormalDirectory(string path)
    {
        if (!Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("安装目录不存在或是链接。");
    }

    private sealed record ActiveVersion(string Version);
}
