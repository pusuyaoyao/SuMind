namespace SuMind.Shared;
public enum InstallationState { NotInstalled, Installed, Updating, Removing, Failed }
public enum EnablementState { Unavailable, Disabled, Enabled }
public enum RuntimeState { NotRunning, Starting, Running, Stopping, Failed }
public record ModuleDescriptor(string Id, string Name, string Description);
public record ModuleStatus(InstallationState Installation, EnablementState Enablement, RuntimeState Runtime, string Detail);
public record ModuleEntry(ModuleDescriptor Descriptor, ModuleStatus Status) {
 public string Icon => Descriptor.Id switch { "desktop-navigation" => "\uE7F4", "taskbar" => "\uE7F4", "dock" => "\uE71D", "wallpaper" => "\uEB9F", "system-optimization" => "\uE713", _ => "\uE74C" };
 public string Name => Descriptor.Name; public string Description => Descriptor.Description;
 public string InstallationText => Status.Installation switch { InstallationState.NotInstalled => "未安装", InstallationState.Installed => "已安装", InstallationState.Updating => "更新中", InstallationState.Removing => "移除中", _ => "操作失败" };
 public string EnablementText => Status.Enablement switch { EnablementState.Unavailable => "不可启用", EnablementState.Disabled => "已关闭", _ => "已启用" };
 public string RuntimeText => Status.Runtime switch { RuntimeState.NotRunning => "未运行", RuntimeState.Starting => "启动中", RuntimeState.Running => "运行中", RuntimeState.Stopping => "停止中", _ => "运行失败" };
 public string Detail => Status.Detail;
}
// Initial contract; binary compatibility and native-process integration remain to be designed.
public interface ISuMindModule { ModuleDescriptor Descriptor { get; } Task StartAsync(CancellationToken cancellationToken); Task StopAsync(CancellationToken cancellationToken); }




