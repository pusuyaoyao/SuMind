using System.IO;
using System.Text.Json;

namespace SuMind;

public sealed class DesktopNavigationSettings
{
    public string TaskbarAppearance { get; set; } = "Native";
}

/// <summary>Extension data lives outside versioned binaries and the configurable install directory.</summary>
public static class ExtensionSettingsStore
{
    public static string DataRoot => Path.Combine(SettingsStore.DirectoryPath, "extension-data");
    public static string DesktopDirectory => Path.Combine(DataRoot, "desktop-navigation");
    private static string DesktopFile => Path.Combine(DesktopDirectory, "settings.json");

    public static DesktopNavigationSettings LoadDesktop() => File.Exists(DesktopFile)
        ? JsonSerializer.Deserialize<DesktopNavigationSettings>(File.ReadAllText(DesktopFile)) ?? new()
        : new();

    public static bool DesktopExists => File.Exists(DesktopFile);

    public static string EnsureDesktopDirectory()
    {
        EnsureNormalPath();
        Directory.CreateDirectory(DesktopDirectory);
        EnsureNormalPath();
        return DesktopDirectory;
    }

    public static void SaveDesktop(DesktopNavigationSettings settings)
    {
        EnsureNormalPath();
        Directory.CreateDirectory(DesktopDirectory);
        EnsureNormalPath();
        string temporary = Path.Combine(DesktopDirectory, ".settings-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                JsonSerializer.Serialize(file, settings, new JsonSerializerOptions { WriteIndented = true });
                file.Flush(true);
            }
            File.Move(temporary, DesktopFile, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public static void DeleteDesktopData()
    {
        EnsureNormalPath();
        if (!Directory.Exists(DesktopDirectory)) return;
        // Refuse links anywhere inside before deleting only this fixed extension data directory.
        var pending = new Stack<string>();
        pending.Push(DesktopDirectory);
        while (pending.TryPop(out var directory))
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("拓展数据包含链接，未删除配置。");
                if ((attributes & FileAttributes.Directory) != 0) pending.Push(entry);
            }
        Directory.Delete(DesktopDirectory, recursive: true);
    }

    private static void EnsureNormalPath()
    {
        foreach (string path in new[] { SettingsStore.DirectoryPath, DataRoot, DesktopDirectory })
            if ((Directory.Exists(path) || File.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("拓展数据目录不能是链接。");
    }
}
