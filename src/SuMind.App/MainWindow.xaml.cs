using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using SuMind.Shared;
using Forms = System.Windows.Forms;
using Brush = System.Windows.Media.Brush;
using SuMind.Packages;
using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace SuMind;

public partial class MainWindow : Window
{
    private AppSettings settings = new();
    private DesktopNavigationSettings desktopSettings = new();
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private System.IO.Stream? trayIconStream;
    private bool ready;
    private bool exiting;
    private bool sidebarCollapsed;
    private ExtensionCatalogItem? desktopCatalogItem;
    private InstalledPackage? desktopPackage;
    private bool updatingDesktopControls;
    private bool packageOperation;
    private const string DesktopId = "desktop-navigation";
    private string ExtensionRoot => string.IsNullOrWhiteSpace(settings.ExtensionDirectory) ? Path.Combine(SettingsStore.DirectoryPath, "extensions") : settings.ExtensionDirectory;
    // Publisher public key only. The signing private key remains outside the repository.
    private const string TrustedPublisherPublicKey = """
-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEAz/oIpScuNWjj+t/Sq4G1
j7dqdU7t6gsFqeM3fx2cQ0hT7WcvuwdQrSvprMSoQ3igA1x/DWVNsSoS/YCja3Zg
AErGfBkq1v4yDtpYTBEixx/N8mmcdikNK8cYaf8p4a7QpzdEMIOzJoLngV9C1zuE
PZcOK7eOxVriUR3P7zVNMpFGUth6yx41WhrnCn66jbkMwJvsz0nvnN9dkNnA2jKt
QB6O9G8GIXT6dMzVe4DaRbWB2U9kBpkY8Gk9RmrCi31FrnUMV4RY6b8sfmKsZPT/
ylvZdMzJTqrY5O8sgGPuL37XTRCUIHmceLPV3sBxOEnLABT6cYF53mx1Pj6wTmrv
Im5sRQ6SyO7pvLmNwfo2AOJVuk0HksENNaIuzMxYBJvEvDajIJhGtNOBud3Qiqu7
wMoXgrkNkBIlL8czbp2VUYUe067KXJ3wEs+y1vllEETX2rIbu9074/i5i/xSuf0r
LbdllcO4JZbSOPw0QZkTs4L8LamcB4yK7qlQMW/GfJBJAgMBAAE=
-----END PUBLIC KEY-----
""";

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ApplyTheme();

        try
        {
            settings = SettingsStore.Load();
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            SettingsNotice.Text = "配置读取失败，已使用默认设置。请检查本地配置文件。";
        }
        try
        {
            if (!ExtensionSettingsStore.DesktopExists && settings.TaskbarAppearance is not null)
            {
                desktopSettings.TaskbarAppearance = settings.TaskbarAppearance;
                ExtensionSettingsStore.SaveDesktop(desktopSettings);
            }
            desktopSettings = ExtensionSettingsStore.LoadDesktop();
            if (settings.TaskbarAppearance is not null)
            {
                settings.TaskbarAppearance = null;
                SettingsStore.Save(settings);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            SettingsStore.Log(ex);
            SettingsNotice.Text = "拓展配置读取失败，请检查本地配置。";
        }

        ModuleList.ItemsSource = Array.Empty<CatalogRow>();
        (settings.Theme switch { "Light" => LightTheme, "Dark" => DarkTheme, _ => SystemTheme }).IsChecked = true;
        CloseToTrayBox.IsChecked = settings.CloseToTray;
        ExtensionPathText.Text = ExtensionRoot;
        ApplyTheme();
        UpdateThemeIndicator(false);
        UpdateNavigation(false);
        RefreshDesktopState();
        Loaded += async (_, _) => { UpdateNavigation(false); await RefreshGitHubReleasesAsync(); };

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 SuMind", null, (_, _) => ShowWindow());
        menu.Items.Add("退出", null, (_, _) => ExitApp());
        var iconResource = System.Windows.Application.GetResourceStream(
            new Uri("pack://application:,,,/SuMind.App;component/Assets/SuMind-logo.ico"));
        trayIconStream = iconResource?.Stream ?? throw new InvalidOperationException("无法加载应用图标。");
        trayIcon = new System.Drawing.Icon(trayIconStream);
        tray = new Forms.NotifyIcon
        {
            Text = "SuMind",
            Icon = trayIcon,
            ContextMenuStrip = menu,
            Visible = true
        };
        tray.DoubleClick += (_, _) => ShowWindow();
        SystemEvents.UserPreferenceChanged += SystemPreferenceChanged;
        ready = true;
    }

    private void SystemPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (settings.Theme == "System" && !Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(ApplyTheme);
    }

    private void ShowWindow()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitApp()
    {
        if (exiting) return;
        exiting = true;
        SystemEvents.UserPreferenceChanged -= SystemPreferenceChanged;
        tray?.Dispose();
        trayIcon?.Dispose();
        trayIconStream?.Dispose();
        System.Windows.Application.Current.Shutdown();
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (!exiting && settings.CloseToTray)
        {
            e.Cancel = true;
            Hide();
        }
        else if (!exiting)
        {
            ExitApp();
        }
    }

    private void ToggleSidebar_Click(object sender, RoutedEventArgs e)
    {
        sidebarCollapsed = !sidebarCollapsed;
        SidebarColumn.Width = new GridLength(sidebarCollapsed ? 60 : 140);
        ModulesNavContent.Width = sidebarCollapsed ? 40 : 120;
        SettingsNavContent.Width = sidebarCollapsed ? 40 : 120;
        ModulesLabel.Visibility = sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SettingsLabel.Visibility = sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;

        UpdateNavigation();
    }

    private void Modules_Click(object sender, RoutedEventArgs e)
    {
        ModulesPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;
        UpdateNavigation();
    }

    private async void RefreshReleases_Click(object sender, RoutedEventArgs e)
    {
        await RefreshGitHubReleasesAsync();
    }

    private async Task RefreshGitHubReleasesAsync()
    {
        RefreshReleasesButton.IsEnabled = false;
        GitHubStatus.Text = "正在连接 GitHub…";
        try
        {
            var catalog = await ExtensionCatalogService.GetCatalogAsync();
            var architecture = RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "win-x64", Architecture.Arm64 => "win-arm64", Architecture.X86 => "win-x86", _ => "unsupported" };
            var entries = catalog?.Extensions.GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.FirstOrDefault(item => item.Architecture == architecture) ?? group.First())
                .ToArray() ?? Array.Empty<ExtensionCatalogItem>();
            desktopCatalogItem = entries.FirstOrDefault(item => item.Id == DesktopId);
            var rows = entries.Where(item => item.Id != DesktopId).Select(item => new CatalogRow(item)).ToArray();
            ModuleList.ItemsSource = rows;
            CatalogListBorder.Visibility = entries.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            CatalogEmpty.Visibility = entries.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var row in rows) _ = LoadCatalogIconAsync(row);
            if (desktopCatalogItem is not null) _ = LoadDesktopIconAsync(desktopCatalogItem);
            GitHubStatus.Text = catalog is null ? "尚未发布拓展目录。" : "拓展目录已更新。";
            RefreshDesktopState();
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or IOException or InvalidOperationException)
        {
            SettingsStore.Log(ex);
            GitHubStatus.Text = "无法读取 GitHub 发布信息，请检查网络后重试。";
            RefreshDesktopState();
        }
        finally
        {
            RefreshReleasesButton.IsEnabled = true;
        }
    }

    private void RefreshDesktopState()
    {
        DesktopCard.Visibility = desktopCatalogItem is null ? Visibility.Collapsed : Visibility.Visible;
        if (desktopCatalogItem is not null)
        {
            DesktopName.Text = desktopCatalogItem.Name;
            DesktopDescription.Text = desktopCatalogItem.Description;
            DesktopMetadata.Text = desktopCatalogItem.Metadata;
        }
        try { desktopPackage = PackageInstaller.GetInstalled(ExtensionRoot, DesktopId); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            SettingsStore.Log(ex);
            desktopPackage = null;
            DesktopStatus.Text = "拓展安装状态异常，请检查日志。";
            DesktopInstallButton.IsEnabled = false;
            DesktopOptions.Visibility = Visibility.Collapsed;
            return;
        }
        bool installed = desktopPackage is not null;
        bool trusted = !string.IsNullOrEmpty(TrustedPublisherPublicKey);
        bool updateAvailable = installed && desktopCatalogItem is not null && Version.TryParse(desktopCatalogItem.Version, out var available) && Version.TryParse(desktopPackage!.Version, out var current) && available > current;
        bool preview = desktopCatalogItem?.IsPreview == true;
        bool compatible = desktopCatalogItem?.Architecture == (RuntimeInformation.ProcessArchitecture switch { Architecture.X64 => "win-x64", Architecture.Arm64 => "win-arm64", Architecture.X86 => "win-x86", _ => "unsupported" });
        bool signed = !string.IsNullOrEmpty(desktopCatalogItem?.Signature);
        if (desktopCatalogItem is not null) DesktopVersion.Text = installed ? $"v{desktopPackage!.Version}" : desktopCatalogItem.VersionLabel;
        DesktopStatus.Text = installed ? (updateAvailable ? $"可更新至 v{desktopCatalogItem!.Version}" : "") : desktopCatalogItem is null ? "尚无可安装版本" : !compatible ? "当前系统架构不受支持" : !trusted || !signed ? "发布签名尚未配置" : preview ? "测试版：Windows 11 效果暂不可用" : "";
        DesktopInstallButton.Content = installed ? updateAvailable ? "更新" : "已安装" : "安装";
        DesktopInstallButton.IsEnabled = (!installed || updateAvailable) && desktopCatalogItem is not null && trusted && signed && compatible;
        DesktopRemoveButton.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        DesktopOptions.Visibility = installed ? Visibility.Visible : Visibility.Collapsed;
        if (installed && preview) DesktopNotice.Text = "测试版：Windows 11 任务栏效果尚不可用，可测试安装和卸载。";
        updatingDesktopControls = true;
        (desktopSettings.TaskbarAppearance switch { "Transparent" => TransparentEffect, "Glass" => GlassEffect, _ => NativeEffect }).IsChecked = true;
        updatingDesktopControls = false;
    }

    private async void DesktopInstall_Click(object sender, RoutedEventArgs e)
    {
        var item = desktopCatalogItem;
        if (item is null || packageOperation || string.IsNullOrEmpty(TrustedPublisherPublicKey) || string.IsNullOrEmpty(item.Signature)) return;
        if (!Version.TryParse(item.MinHostVersion, out var minimum) || minimum > (typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0)))
        { DesktopStatus.Text = "需要更新 SuMind 才能安装此拓展。"; return; }
        DesktopInstallButton.IsEnabled = false;
        packageOperation = true;
        LocalInstallButton.IsEnabled = false;
        DesktopRemoveButton.IsEnabled = false;
        DesktopOptions.IsEnabled = false;
        DesktopStatus.Text = "正在下载并验证拓展…";
        string temp = Path.Combine(Path.GetTempPath(), "sumind-download-" + Guid.NewGuid().ToString("N") + ".suminds");
        try
        {
            await ExtensionCatalogService.DownloadPackageAsync(item, temp);
            await InstallDesktopPackageAsync(item, temp);
            RefreshDesktopState();
            DesktopNotice.Text = item.IsPreview ? "测试版安装完成；Windows 11 任务栏效果尚不可用。" : "安装完成，选择任务栏外观。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException or System.Security.Cryptography.CryptographicException or ArgumentException or FormatException or InvalidOperationException or Win32Exception or JsonException or KeyNotFoundException)
        { SettingsStore.Log(ex); RefreshDesktopState(); DesktopStatus.Text = "安装失败，请检查网络、版本和拓展签名。"; }
        finally { packageOperation = false; LocalInstallButton.IsEnabled = true; DesktopRemoveButton.IsEnabled = true; DesktopOptions.IsEnabled = true; try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    }

    private void DesktopGitHub_Click(object sender, RoutedEventArgs e)
    {
        OpenCatalogRelease(desktopCatalogItem);
    }

    private void CatalogGitHub_Click(object sender, RoutedEventArgs e)
    {
        if (((System.Windows.Controls.Button)sender).DataContext is CatalogRow row) OpenCatalogRelease(row.Item);
    }

    private void OpenCatalogRelease(ExtensionCatalogItem? item)
    {
        try
        {
            string url = "https://github.com/pusuyaoyao/SuMind/releases";
            if (item is not null)
            {
                var asset = new Uri(item.PackageUrl);
                var segments = asset.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length >= 6 && segments[2] == "releases" && segments[3] == "download")
                    url = "https://github.com/pusuyaoyao/SuMind/releases/tag/" + segments[4];
            }
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or System.Security.SecurityException)
        { SettingsStore.Log(ex); GitHubStatus.Text = "无法打开 GitHub，请检查默认浏览器。"; }
    }

    private sealed class CatalogRow : INotifyPropertyChanged
    {
        public CatalogRow(ExtensionCatalogItem item) => Item = item;
        public ExtensionCatalogItem Item { get; }
        public string Name => Item.Name;
        public string Description => Item.Description;
        public string Metadata => Item.Metadata;
        public string VersionLabel => Item.VersionLabel;
        private ImageSource? icon;
        public ImageSource? Icon { get => icon; set { icon = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Icon))); } }
        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private async Task<ImageSource?> DownloadCatalogIconAsync(ExtensionCatalogItem item)
    {
        if (string.IsNullOrEmpty(item.IconUrl)) return null;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var bytes = await ExtensionCatalogService.DownloadIconAsync(item, timeout.Token);
            if (bytes is null) return null;
            using var input = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count != 1 || decoder.Frames[0].PixelWidth > 1024 || decoder.Frames[0].PixelHeight > 1024)
                throw new InvalidDataException("拓展图标尺寸无效。");
            var image = decoder.Frames[0];
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException or OperationCanceledException or NotSupportedException or ArgumentException or System.Security.Cryptography.CryptographicException)
        { SettingsStore.Log(ex); return null; }
    }

    private async Task LoadCatalogIconAsync(CatalogRow row) => row.Icon = await DownloadCatalogIconAsync(row.Item);
    private async Task LoadDesktopIconAsync(ExtensionCatalogItem item)
    {
        DesktopIcon.Source = null;
        DesktopIconPlaceholder.Visibility = Visibility.Visible;
        var icon = await DownloadCatalogIconAsync(item);
        if (desktopCatalogItem == item) { DesktopIcon.Source = icon; DesktopIconPlaceholder.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed; }
    }

    private async void LocalInstall_Click(object sender, RoutedEventArgs e)
    {
        if (packageOperation) return;
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "选择 SuMind 拓展包", Filter = "SuMind 拓展包 (*.suminds)|*.suminds", CheckFileExists = true, Multiselect = false };
        if (dialog.ShowDialog(this) != true) return;
        if (string.IsNullOrEmpty(TrustedPublisherPublicKey))
        { GitHubStatus.Text = "尚未配置发布签名，暂时不能验证和安装本地拓展。"; return; }
        var item = desktopCatalogItem;
        if (item is null)
        { GitHubStatus.Text = "无法获取匹配的发布信息，请连接网络并刷新后重试。"; return; }
        if (string.IsNullOrEmpty(item.Signature))
        { GitHubStatus.Text = "拓展包尚未提供签名，暂不可安装。"; return; }
        if (!Version.TryParse(item.MinHostVersion, out var minimum) || minimum > (typeof(MainWindow).Assembly.GetName().Version ?? new Version(0, 0)))
        { GitHubStatus.Text = "需要更新 SuMind 才能安装此拓展。"; return; }
        if (desktopPackage is not null && Version.TryParse(desktopPackage.Version, out var installedVersion) && Version.TryParse(item.Version, out var candidateVersion) && candidateVersion <= installedVersion)
        { GitHubStatus.Text = "当前版本已安装，或本地包不是更新版本。"; return; }
        packageOperation = true;
        LocalInstallButton.IsEnabled = false;
        DesktopInstallButton.IsEnabled = false;
        DesktopRemoveButton.IsEnabled = false;
        DesktopOptions.IsEnabled = false;
        GitHubStatus.Text = "正在验证本地拓展…";
        try
        {
            if (!await ExtensionCatalogService.MatchesLocalPackageAsync(item, dialog.FileName))
                throw new InvalidDataException("拓展包与当前发布版本不匹配。");
            await InstallDesktopPackageAsync(item, dialog.FileName);
            RefreshDesktopState();
            GitHubStatus.Text = "本地拓展安装完成。";
            DesktopNotice.Text = item.IsPreview ? "测试版安装完成；Windows 11 任务栏效果尚不可用。" : "安装完成，选择任务栏外观。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException or ArgumentException or FormatException or InvalidOperationException or Win32Exception or JsonException or OperationCanceledException or KeyNotFoundException)
        { SettingsStore.Log(ex); RefreshDesktopState(); GitHubStatus.Text = "本地安装失败：" + ex.Message; }
        finally { packageOperation = false; LocalInstallButton.IsEnabled = true; DesktopRemoveButton.IsEnabled = true; DesktopOptions.IsEnabled = true; }
    }

    private void ChooseExtensionDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (packageOperation) { SettingsNotice.Text = "请等待拓展操作完成后再更换目录。"; return; }
        try
        {
            if (Directory.Exists(ExtensionRoot) && Directory.EnumerateDirectories(ExtensionRoot).Any())
            { SettingsNotice.Text = "当前目录已有拓展或安装残留，请先卸载并处理残留后再更换目录。"; return; }
            using var dialog = new Forms.FolderBrowserDialog { Description = "选择一个空目录用于存放 SuMind 拓展", ShowNewFolderButton = true, UseDescriptionForTitle = true };
            if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
            string selected = Path.GetFullPath(dialog.SelectedPath).TrimEnd(Path.DirectorySeparatorChar);
            if (!Path.IsPathFullyQualified(selected) || selected.StartsWith(@"\\", StringComparison.Ordinal) || selected.Length <= Path.GetPathRoot(selected)!.Length)
                throw new IOException("请选择本地磁盘上的普通目录。");
            for (var current = new DirectoryInfo(selected); current is not null; current = current.Parent)
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("拓展目录及其上级目录不能是链接。");
            if (Directory.EnumerateFileSystemEntries(selected).Any()) throw new IOException("请选择空目录，以免影响已有文件。");
            string? previous = settings.ExtensionDirectory;
            settings.ExtensionDirectory = selected;
            try { SettingsStore.Save(settings); } catch { settings.ExtensionDirectory = previous; throw; }
            ExtensionPathText.Text = selected;
            SettingsNotice.Text = "拓展目录已保存。";
            RefreshDesktopState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or System.Security.SecurityException)
        { SettingsStore.Log(ex); SettingsNotice.Text = "无法更换目录：" + ex.Message; }
    }

    private async Task InstallDesktopPackageAsync(ExtensionCatalogItem item, string packagePath)
    {
            var previous = desktopPackage;
            if (previous is not null) await ApplyDesktopEffectAsync(previous, "Native");
            InstalledPackage installed;
            try
            {
                installed = await Task.Run(() => PackageInstaller.Install(packagePath, ExtensionRoot, item.Id, item.Version, item.Sha256, Convert.FromBase64String(item.Signature!), TrustedPublisherPublicKey, new PackageMetadata(item.Name, item.Description, item.Author, item.MinHostVersion, item.Architecture, item.IconSha256)));
            }
            catch
            {
                if (previous is not null && desktopSettings.TaskbarAppearance != "Native")
                    try { await ApplyDesktopEffectAsync(previous, desktopSettings.TaskbarAppearance); } catch (Exception restoreError) { SettingsStore.Log(restoreError); }
                throw;
            }
            if (previous is not null && desktopSettings.TaskbarAppearance != "Native")
            {
                try { await ApplyDesktopEffectAsync(installed, desktopSettings.TaskbarAppearance); }
                catch
                {
                    PackageInstaller.ActivateVersion(ExtensionRoot, DesktopId, previous.Version);
                    try { await ApplyDesktopEffectAsync(previous, desktopSettings.TaskbarAppearance); } catch (Exception restoreError) { SettingsStore.Log(restoreError); }
                    throw;
                }
            }
    }

    private async void DesktopRemove_Click(object sender, RoutedEventArgs e)
    {
        if (desktopPackage is null || packageOperation) return;
        var (confirmed, keepData) = ConfirmExtensionRemoval();
        if (!confirmed) return;
        packageOperation = true;
        LocalInstallButton.IsEnabled = false;
        DesktopInstallButton.IsEnabled = false;
        DesktopOptions.IsEnabled = false;
        DesktopRemoveButton.IsEnabled = false;
        bool removed = false;
        try
        {
            DesktopNotice.Text = "正在恢复原生效果…";
            if (desktopSettings.TaskbarAppearance != "Native")
                await ApplyDesktopEffectAsync(desktopPackage, "Native");
            PackageInstaller.Uninstall(ExtensionRoot, DesktopId);
            removed = true;
            if (!keepData)
            {
                ExtensionSettingsStore.DeleteDesktopData();
                desktopSettings = new DesktopNavigationSettings();
            }
            RefreshDesktopState();
            GitHubStatus.Text = keepData ? "桌面导航已卸载，配置已保留。" : "桌面导航及其配置已移除。";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or JsonException or OperationCanceledException or KeyNotFoundException)
        { SettingsStore.Log(ex); GitHubStatus.Text = (removed ? "拓展已移除，但配置未能删除：" : "卸载未完成：") + ex.Message; }
        finally { packageOperation = false; LocalInstallButton.IsEnabled = true; DesktopRemoveButton.IsEnabled = true; DesktopOptions.IsEnabled = true; RefreshDesktopState(); }
    }

    private (bool Confirmed, bool KeepData) ConfirmExtensionRemoval()
    {
        var dialog = new Window
        {
            Owner = this, Title = "卸载桌面导航", Width = 430, SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false, FontFamily = FontFamily, Icon = Icon,
            WindowStyle = WindowStyle.SingleBorderWindow,
            Background = System.Windows.Media.Brushes.Transparent, Foreground = (Brush)FindResource("TextBrush")
        };
        System.Windows.Shell.WindowChrome.SetWindowChrome(dialog, new System.Windows.Shell.WindowChrome
        {
            CaptionHeight = 40, ResizeBorderThickness = new Thickness(0), GlassFrameThickness = new Thickness(0, 40, 0, 0),
            CornerRadius = new CornerRadius(10)
        });
        dialog.SourceInitialized += (_, _) =>
        {
            bool dark = settings.Theme == "Dark" || (settings.Theme != "Light" && SystemUsesDarkTheme());
            ApplyNativeCaptionTheme(new WindowInteropHelper(dialog).Handle, dark);
        };
        foreach (string key in new[] { "FrameBrush", "PageBrush", "CardBrush", "TextBrush", "MutedBrush", "BorderBrush", "HoverBrush", "SwitchOffBrush", "SwitchOnBrush" })
            dialog.Resources[key] = FindResource(key);
        var layout = new StackPanel { Margin = new Thickness(24) };
        layout.Children.Add(new TextBlock { Text = "卸载桌面导航？", FontSize = 18, FontWeight = FontWeights.SemiBold });
        layout.Children.Add(new TextBlock { Text = "卸载会移除拓展程序，并尝试恢复系统任务栏外观。", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 18), Foreground = (Brush)FindResource("MutedBrush") });
        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 22), LastChildFill = true };
        var keep = new System.Windows.Controls.CheckBox { IsChecked = true, Style = (Style)FindResource("CapsuleSwitch"), VerticalAlignment = VerticalAlignment.Center };
        System.Windows.Automation.AutomationProperties.SetName(keep, "保留桌面导航配置数据");
        DockPanel.SetDock(keep, Dock.Right);
        row.Children.Add(keep);
        var labels = new StackPanel { Margin = new Thickness(0, 0, 16, 0) };
        labels.Children.Add(new TextBlock { Text = "保留拓展配置", FontWeight = FontWeights.SemiBold });
        labels.Children.Add(new TextBlock { Text = "重新安装后可继续使用原来的设置", Foreground = (Brush)FindResource("MutedBrush"), FontSize = 12, Margin = new Thickness(0, 5, 0, 0), TextWrapping = TextWrapping.Wrap });
        row.Children.Add(labels);
        layout.Children.Add(row);
        var actions = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right };
        var cancel = new System.Windows.Controls.Button { Content = "取消", Style = (Style)FindResource("SecondaryButton"), IsCancel = true, MinWidth = 70 };
        var remove = new System.Windows.Controls.Button { Content = "确认卸载", Style = (Style)FindResource("SecondaryButton"), MinWidth = 88, Margin = new Thickness(8, 0, 0, 0) };
        remove.Click += (_, _) => dialog.DialogResult = true;
        actions.Children.Add(cancel); actions.Children.Add(remove); layout.Children.Add(actions);
        var root = new Grid { Background = (Brush)FindResource("FrameBrush"), ClipToBounds = true };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(40) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, IsHitTestVisible = false };
        header.Children.Add(new System.Windows.Controls.Image { Source = TryFindResource("TitleLogo") as ImageSource ?? Icon, Width = 18, Height = 25, Margin = new Thickness(0, 0, 5, 0), Stretch = Stretch.Uniform });
        header.Children.Add(new TextBlock { Text = "SuMind", FontSize = 13, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        root.Children.Add(header);
        var close = new System.Windows.Controls.Button { Content = "\uE8BB", FontFamily = new System.Windows.Media.FontFamily("Segoe MDL2 Assets"), FontSize = 10, Width = 46, Height = 40, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Style = (Style)FindResource("CloseButton") };
        System.Windows.Automation.AutomationProperties.SetName(close, "关闭");
        System.Windows.Shell.WindowChrome.SetIsHitTestVisibleInChrome(close, true);
        close.Click += (_, _) => dialog.Close();
        root.Children.Add(close);
        var surface = new Border { Background = (Brush)FindResource("PageBrush"), BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1, 1, 1, 0), CornerRadius = new CornerRadius(10, 10, 0, 0), Child = layout };
        Grid.SetRow(surface, 1);
        root.Children.Add(surface);
        dialog.Content = root;
        return (dialog.ShowDialog() == true, keep.IsChecked == true);
    }

    private async void TaskbarEffect_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready || updatingDesktopControls || desktopPackage is null) return;
        string effect = ((System.Windows.Controls.RadioButton)sender).Tag.ToString()!;
        DesktopOptions.IsEnabled = false;
        DesktopNotice.Text = "正在应用效果…";
        try
        {
            string message = await ApplyDesktopEffectAsync(desktopPackage, effect);
            string previousAppearance = desktopSettings.TaskbarAppearance;
            desktopSettings.TaskbarAppearance = effect;
            try { ExtensionSettingsStore.SaveDesktop(desktopSettings); }
            catch
            {
                desktopSettings.TaskbarAppearance = previousAppearance;
                try { await ApplyDesktopEffectAsync(desktopPackage, previousAppearance); }
                catch (Exception restoreError) { SettingsStore.Log(restoreError); }
                throw;
            }
            DesktopNotice.Text = message;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception or JsonException or OperationCanceledException or KeyNotFoundException)
        {
            SettingsStore.Log(ex);
            DesktopNotice.Text = "无法应用：" + ex.Message;
            updatingDesktopControls = true;
            (desktopSettings.TaskbarAppearance switch { "Transparent" => TransparentEffect, "Glass" => GlassEffect, _ => NativeEffect }).IsChecked = true;
            updatingDesktopControls = false;
        }
        finally { DesktopOptions.IsEnabled = true; }
    }

    private static async Task<string> ApplyDesktopEffectAsync(InstalledPackage package, string effect)
    {
        var start = new ProcessStartInfo(Path.Combine(package.Directory, "DesktopNavigation.exe"))
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = package.Directory };
        var runtimeDirectory = new DirectoryInfo(RuntimeEnvironment.GetRuntimeDirectory());
        var dotnetRoot = runtimeDirectory.Parent?.Parent?.Parent?.FullName;
        if (dotnetRoot is not null && File.Exists(Path.Combine(dotnetRoot, "dotnet.exe"))) start.Environment["DOTNET_ROOT"] = dotnetRoot;
        start.ArgumentList.Add("apply"); start.ArgumentList.Add(effect);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("无法启动桌面导航。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) { try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { } throw; }
        string output = await stdout;
        string error = await stderr;
        using var result = JsonDocument.Parse(output);
        bool success = process.ExitCode == 0 && result.RootElement.GetProperty("ok").GetBoolean();
        string message = result.RootElement.TryGetProperty("message", out var msg) ? msg.GetString() ?? "" : "";
        if (!success) throw new InvalidOperationException(string.IsNullOrEmpty(message) ? error : message);
        return message;
    }
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        ModulesPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
        UpdateNavigation();
    }

    private void UpdateNavigation(bool animate = true)
    {
        bool modulesSelected = ModulesPage.Visibility == Visibility.Visible;
        TextBlock label = modulesSelected ? ModulesLabel : SettingsLabel;
        label.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
        double targetWidth = sidebarCollapsed ? 40 : Math.Min(120, 58 + label.DesiredSize.Width);
        double targetY = modulesSelected ? 0 : 44;
        double currentY = NavIndicatorTransform.Y;
        double currentWidth = NavIndicator.ActualWidth > 0 ? NavIndicator.ActualWidth : NavIndicator.Width;
        NavIndicatorTransform.BeginAnimation(TranslateTransform.YProperty, null);
        NavIndicator.BeginAnimation(WidthProperty, null);
        NavIndicatorTransform.Y = targetY;
        NavIndicator.Width = targetWidth;
        if (!animate) return;
        var easing = new BackEase { Amplitude = 0.25, EasingMode = EasingMode.EaseOut };
        NavIndicatorTransform.BeginAnimation(TranslateTransform.YProperty,
            new DoubleAnimation(currentY, targetY, TimeSpan.FromMilliseconds(260)) { EasingFunction = easing });
        NavIndicator.BeginAnimation(WidthProperty,
            new DoubleAnimation(currentWidth, targetWidth, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
    }

    private void Theme_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.Theme = ((System.Windows.Controls.RadioButton)sender).Tag.ToString()!;
        UpdateThemeIndicator();
        ApplyTheme();
        SaveSettings();
    }

    private void UpdateThemeIndicator(bool animate = true)
    {
        double targetX = settings.Theme switch { "Light" => 80, "Dark" => 136, _ => 0 };
        double targetWidth = settings.Theme == "System" || (settings.Theme != "Light" && settings.Theme != "Dark") ? 80 : 56;
        double currentX = ThemeIndicatorTransform.X;
        double currentWidth = ThemeIndicator.ActualWidth > 0 ? ThemeIndicator.ActualWidth : ThemeIndicator.Width;
        ThemeIndicatorTransform.BeginAnimation(TranslateTransform.XProperty, null);
        ThemeIndicator.BeginAnimation(WidthProperty, null);
        ThemeIndicatorTransform.X = targetX;
        ThemeIndicator.Width = targetWidth;
        if (!animate) return;
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };
        ThemeIndicatorTransform.BeginAnimation(TranslateTransform.XProperty,
            new DoubleAnimation(currentX, targetX, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
        ThemeIndicator.BeginAnimation(WidthProperty,
            new DoubleAnimation(currentWidth, targetWidth, TimeSpan.FromMilliseconds(220)) { EasingFunction = easing });
    }
    private void CloseToTray_Changed(object sender, RoutedEventArgs e)
    {
        if (!ready) return;
        settings.CloseToTray = CloseToTrayBox.IsChecked == true;
        SaveSettings();
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(settings);
            SettingsNotice.Text = "";
        }
        catch (Exception ex)
        {
            SettingsStore.Log(ex);
            SettingsNotice.Text = "设置保存失败，请检查本地目录权限。";
            System.Windows.MessageBox.Show(SettingsNotice.Text, "SuMind", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void ApplyTheme()
    {
        bool dark = settings.Theme == "Dark" || (settings.Theme != "Light" && SystemUsesDarkTheme());
        SetBrush("FrameBrush", dark ? "#242321" : "#F3F0EF");
        SetBrush("PageBrush", dark ? "#1B1B1A" : "#FCFBFA");
        SetBrush("CardBrush", dark ? "#242423" : "#FFFFFF");
        SetBrush("TextBrush", dark ? "#EDEBE8" : "#292827");
        SetBrush("MutedBrush", dark ? "#A9A5A0" : "#777370");
        SetBrush("BorderBrush", dark ? "#3B3936" : "#E8E4E1");
        SetBrush("HoverBrush", dark ? "#3B3936" : "#E9E5E2");
        SetBrush("SelectedBrush", dark ? "#44413E" : "#E6E1DE");
        SetBrush("ChoiceBrush", dark ? "#494541" : "#FFFFFF");
        SetBrush("SwitchOffBrush", dark ? "#595652" : "#C8C3BF");
        SetBrush("SwitchOnBrush", dark ? "#9B948D" : "#53504D");
        ApplyNativeCaptionTheme(dark);
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private void ApplyNativeCaptionTheme(bool dark)
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        ApplyNativeCaptionTheme(handle, dark);
    }

    private static void ApplyNativeCaptionTheme(IntPtr handle, bool dark)
    {
        if (handle == IntPtr.Zero) return;
        int darkMode = dark ? 1 : 0;
        DwmSetWindowAttribute(handle, 20, ref darkMode, sizeof(int));
        int captionColor = dark ? 0x212324 : 0xEFF0F3;
        DwmSetWindowAttribute(handle, 35, ref captionColor, sizeof(int));
    }
    private static bool SystemUsesDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or System.IO.IOException)
        {
            SettingsStore.Log(ex);
            return false;
        }
    }

    private void SetBrush(string key, string color)
    {
        Resources[key] = new SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(color));
    }
}






















