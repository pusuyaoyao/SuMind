using System.IO;
using System.Text.Json;
namespace SuMind;
public sealed class AppSettings { public string Theme { get; set; } = "System"; public bool CloseToTray { get; set; } = true; }
public static class SettingsStore {
 public static readonly string DirectoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SuMind");
 public static readonly string FilePath = Path.Combine(DirectoryPath, "settings.json");
 public static AppSettings Load() => File.Exists(FilePath) ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new() : new();
 public static void Save(AppSettings settings) { Directory.CreateDirectory(DirectoryPath); var temp = FilePath + ".tmp"; File.WriteAllText(temp, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, FilePath, true); }
 public static void Log(Exception error) { try { Directory.CreateDirectory(DirectoryPath); File.AppendAllText(Path.Combine(DirectoryPath, "error.log"), $"{DateTimeOffset.Now:O} {error}{Environment.NewLine}"); } catch { } }
}

