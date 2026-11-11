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

namespace SuMind;

public partial class MainWindow : Window
{
    private AppSettings settings = new();
    private Forms.NotifyIcon? tray;
    private System.Drawing.Icon? trayIcon;
    private System.IO.Stream? trayIconStream;
    private bool ready;
    private bool exiting;
    private bool sidebarCollapsed;

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

        ModuleEntry Planned(string id, string name, string description) => new(
            new ModuleDescriptor(id, name, description),
            new ModuleStatus(InstallationState.NotInstalled, EnablementState.Unavailable,
                RuntimeState.NotRunning, "尚未提供"));
        ModuleList.ItemsSource = new[]
        {
            Planned("desktop-navigation", "桌面导航", "管理原生任务栏和 Dock。"),
            Planned("wallpaper", "壁纸引擎", "管理桌面壁纸，计划支持动态壁纸。"),
            Planned("system-optimization", "系统工具", "查看和管理系统设置与维护工具。")
        };
        (settings.Theme switch { "Light" => LightTheme, "Dark" => DarkTheme, _ => SystemTheme }).IsChecked = true;
        CloseToTrayBox.IsChecked = settings.CloseToTray;
        ApplyTheme();
        UpdateThemeIndicator(false);
        UpdateNavigation(false);
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
        SidebarColumn.Width = new GridLength(sidebarCollapsed ? 68 : 140);
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
            var releases = await GitHubReleaseService.GetLatestReleaseAsync();
            GitHubStatus.Text = releases is null
                ? "未找到公开的 GitHub 发布版本，拓展安装暂不可用。"
                : $"GitHub 最新版本：{releases}。拓展安装功能尚未接入。";
        }
        catch (Exception ex) when (ex is System.Net.Http.HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            SettingsStore.Log(ex);
            GitHubStatus.Text = "无法读取 GitHub 发布信息，请检查网络后重试。";
        }
        finally
        {
            RefreshReleasesButton.IsEnabled = true;
        }
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





















