using SuMind;

Directory.CreateDirectory(SettingsStore.DirectoryPath);
var first = new DesktopNavigationSettings { TaskbarAppearance = "Glass" };
ExtensionSettingsStore.SaveDesktop(first);
if (ExtensionSettingsStore.LoadDesktop().TaskbarAppearance != "Glass") throw new Exception("保存后无法读取拓展配置。");
// Simulated removal of extension binaries must not affect extension data.
string binaries = Path.Combine(SettingsStore.DirectoryPath, "extensions", "desktop-navigation");
Directory.CreateDirectory(binaries);
File.WriteAllText(Path.Combine(binaries, "program"), "test");
Directory.Delete(binaries, true);
if (ExtensionSettingsStore.LoadDesktop().TaskbarAppearance != "Glass") throw new Exception("保留配置失败。");
string sibling = Path.Combine(SettingsStore.DirectoryPath, "extension-data", "other");
Directory.CreateDirectory(sibling);
File.WriteAllText(Path.Combine(sibling, "data"), "unrelated");
ExtensionSettingsStore.DeleteDesktopData();
if (ExtensionSettingsStore.DesktopExists || !File.Exists(Path.Combine(sibling, "data"))) throw new Exception("清除配置影响了其他拓展。");
if (ExtensionSettingsStore.LoadDesktop().TaskbarAppearance != "Native") throw new Exception("配置删除后未恢复默认值。");
Console.WriteLine("Extension data retention and deletion checks passed.");

namespace SuMind
{
    public static class SettingsStore
    {
        public static readonly string DirectoryPath = Path.Combine(Path.GetTempPath(), "sumind-config-check-" + Guid.NewGuid().ToString("N"));
    }
}
