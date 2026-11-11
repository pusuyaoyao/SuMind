using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SuMind.DesktopNavigation;

/// <summary>Adapter for an unmodified, privately deployed TranslucentTB 2026.2 portable build.</summary>
internal static class TranslucentBackend
{
    public static string Apply(TaskbarAppearance appearance)
    {
        using var mutex = new Mutex(false, @"Local\SuMind.DesktopNavigation.Backend");
        bool acquired;
        try { acquired = mutex.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException("另一项任务栏操作正在进行，请稍后重试。");
        try
        {
            // Mutex ownership is thread-affine: keep the owning thread until release.
            return ApplyCore(appearance);
        }
        finally { mutex.ReleaseMutex(); }
    }

    private static string ApplyCore(TaskbarAppearance appearance)
    {
        string backend = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "backend", "TranslucentTB.exe"));
        Process? owned = null;
        foreach (var process in Process.GetProcessesByName("TranslucentTB"))
        {
            try
            {
                string? path;
                try { path = process.MainModule?.FileName; }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
                { throw new InvalidOperationException("无法确认正在运行的 TranslucentTB 是否属于 SuMind，已停止操作。", ex); }
                if (path is null || !string.Equals(Path.GetFullPath(path), backend, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("检测到其他 TranslucentTB 正在运行；请先自行退出该程序，再使用桌面导航。");
                if (owned is not null) throw new InvalidOperationException("检测到多个任务栏后端实例，已停止操作。");
                owned = process;
            }
            catch { process.Dispose(); owned?.Dispose(); throw; }
        }
        using (owned)
        {
            if (appearance == TaskbarAppearance.Native && owned is null)
                return "SuMind 任务栏后端未运行；未修改其他软件的任务栏效果。";
            if (!File.Exists(backend)) throw new FileNotFoundException("桌面导航缺少 Windows 11 后端，请重新安装拓展。", backend);
            string accent = appearance switch { TaskbarAppearance.Native => "normal", TaskbarAppearance.Transparent => "clear", _ => "acrylic" };
            var config = new
            {
                desktop_appearance = new { accent, color = appearance == TaskbarAppearance.Glass ? "#00000099" : "#00000000", show_line = appearance == TaskbarAppearance.Native },
                visible_window_appearance = new { enabled = false }, maximized_window_appearance = new { enabled = false },
                start_opened_appearance = new { enabled = false }, search_opened_appearance = new { enabled = false },
                task_view_opened_appearance = new { enabled = false }, battery_saver_appearance = new { enabled = false },
                hide_tray = true, disable_saving = true
            };
            string configPath = Path.Combine(Path.GetDirectoryName(backend)!, "settings.json");
            string tempPath = configPath + ".sumindstmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(config));
            File.Move(tempPath, configPath, true);
            if (appearance == TaskbarAppearance.Native)
            {
                // Give the upstream 200 ms configuration watcher time to restore normal
                // before requesting graceful shutdown; never kill another vendor's process.
                Thread.Sleep(600);
                IntPtr hwnd = FindWindow("TrayWindow", "TranslucentTB");
                if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out uint id) == 0 || id != owned!.Id)
                    throw new InvalidOperationException("无法确认桌面导航后端的窗口，未强行关闭；请稍后重试。");
                if (!PostMessage(hwnd, 0x0010, IntPtr.Zero, IntPtr.Zero) || !owned.WaitForExit(5000))
                    throw new InvalidOperationException("任务栏后端未能正常退出；请稍后重试。");
                return "已请求恢复系统任务栏，并关闭 SuMind 后端。";
            }
            if (owned is null)
            {
                using var started = Process.Start(new ProcessStartInfo(backend) { WorkingDirectory = Path.GetDirectoryName(backend)!, UseShellExecute = false, CreateNoWindow = true })
                    ?? throw new InvalidOperationException("无法启动任务栏后端。");
                if (started.WaitForExit(2500)) throw new InvalidOperationException($"任务栏后端启动后退出（退出码 {started.ExitCode}），请检查 Windows 组件依赖。");
                IntPtr hwnd = FindWindow("TrayWindow", "TranslucentTB");
                if (hwnd == IntPtr.Zero || GetWindowThreadProcessId(hwnd, out uint id) == 0 || id != started.Id)
                {
                    try { started.Kill(); } catch (InvalidOperationException) { }
                    throw new InvalidOperationException("任务栏后端尚未就绪，或缺少系统组件；请检查后重试。");
                }
            }
            else Thread.Sleep(500);
            return "已向任务栏后端发送效果请求；实际效果取决于 Windows 版本和系统透明设置。";
        }
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string windowName);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
}
