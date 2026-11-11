using System.ComponentModel;
using System.Runtime.InteropServices;

namespace SuMind.DesktopNavigation;

public enum TaskbarAppearance { Native, Transparent, Glass }

/// <summary>Experimental Windows 10 classic-taskbar adapter. This does not support the Windows 11 XAML taskbar.</summary>
public static class TaskbarAppearanceController
{
    public static void Apply(TaskbarAppearance appearance)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19045) || OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            throw new PlatformNotSupportedException("此实验性适配器仅支持 Windows 10 22H2；Windows 11 需要单独的任务栏适配。 ");
        if (!Enum.IsDefined(appearance)) throw new ArgumentOutOfRangeException(nameof(appearance));
        var handles = new List<IntPtr>();
        EnumWindows((handle, _) =>
        {
            var name = new char[256];
            int length = GetClassName(handle, name, name.Length);
            string className = new(name, 0, length);
            if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") handles.Add(handle);
            return true;
        }, IntPtr.Zero);
        if (handles.Count == 0) throw new InvalidOperationException("没有找到 Windows 任务栏。");
        var policy = new AccentPolicy
        {
            State = appearance switch { TaskbarAppearance.Native => 0, TaskbarAppearance.Transparent => 2, _ => 4 },
            GradientColor = appearance == TaskbarAppearance.Glass ? 0x99000000u : 0u
        };
        IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf<AccentPolicy>());
        try
        {
            Marshal.StructureToPtr(policy, buffer, false);
            var data = new WindowCompositionAttributeData { Attribute = 19, Data = buffer, Size = Marshal.SizeOf<AccentPolicy>() };
            foreach (var handle in handles)
                if (SetWindowCompositionAttribute(handle, ref data) == 0)
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "无法设置任务栏效果。");
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct AccentPolicy { public int State, Flags; public uint GradientColor; public int AnimationId; }
    [StructLayout(LayoutKind.Sequential)] private struct WindowCompositionAttributeData { public int Attribute; public IntPtr Data; public int Size; }
    private delegate bool EnumWindowsCallback(IntPtr handle, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr handle, [Out] char[] className, int maxCount);
    [DllImport("user32.dll", SetLastError = true)] private static extern int SetWindowCompositionAttribute(IntPtr handle, ref WindowCompositionAttributeData data);
}

