using System.Text.Json;
using SuMind.DesktopNavigation;

// A deliberately small protocol: this process returns after the request; the
// Windows 11 backend runs independently and owns its own taskbar lifecycle.
try
{
    if (args.Length != 2 || args[0] != "apply" || !Enum.TryParse<TaskbarAppearance>(args[1], false, out var mode) || !Enum.IsDefined(mode))
        throw new ArgumentException("用法：DesktopNavigation.exe apply Native|Transparent|Glass");
    // A Win11 XAML backend must be validated for graceful restoration before it
    // can touch Explorer. The 2026.2 portable backend did not reach its ready
    // window in the test environment, so fail closed rather than leave changes.
    if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
        throw new PlatformNotSupportedException("Windows 11 任务栏适配尚未通过恢复验证，暂时无法应用效果。");
    string message = ApplyWindows10(mode);
    Console.WriteLine(JsonSerializer.Serialize(new { ok = true, message }));
    return 0;
}
catch (Exception ex)
{
    Console.WriteLine(JsonSerializer.Serialize(new { ok = false, message = ex.Message }));
    return 1;
}

static string ApplyWindows10(TaskbarAppearance mode)
{
    TaskbarAppearanceController.Apply(mode);
    return "已请求 Windows 10 更改任务栏外观；Explorer 重启后需重新应用。";
}

