# 桌面导航拓展（开发中）

`DesktopNavigation.exe apply Native|Transparent|Glass` 为主程序提供一次性调用接口，标准输出为一行 JSON：`{"ok":true|false,"message":"..."}`，退出码分别为 0 / 1。仅在 Windows 10 22H2 调用实验性经典任务栏 API；该实现尚未在 Windows 10 实机验证，不保证原生样式恢复，也不会在 Explorer 重启后自动重新应用。接口中的成功只表示 API 返回成功，不等于效果已视觉验收。

Windows 11 所有选项目前直接返回不支持，不启动后端或修改任务栏。Windows 11 26200 上，TranslucentTB 2026.2 便携后端在测试中仅创建了 `TTB_WorkerWindow`，未创建用于正常关闭和恢复的 `TrayWindow`，不能确认可靠恢复。已停止测试且该进程未保留；Explorer 模块列表中未发现 ExplorerTAP/ExplorerHooks，但没有完成任务栏视觉恢复验证。若要继续，必须先在隔离环境验证启动、恢复及 Explorer 生命周期。`TranslucentBackend.cs` 是未启用的适配试验，不应被发布为已验收功能。

实验后端参考了 [TranslucentTB](https://github.com/TranslucentTB/TranslucentTB) 2026.2 的便携版和配置；上游为 GPL-3.0。`prepare-backend.ps1` 可以下载固定哈希的官方包，但主程序现阶段不调用它。若将后端随产品分发，必须审查 GPL 合规、提供完整对应源代码及许可，并验证 Microsoft.UI.Xaml 2.8、VCLibs、操作系统架构与版本。不能仅凭路径或哈希视为签名验证。

Windows 11、任务栏恢复、多屏、系统主题、Explorer 重启、后台运行、`.suminds` 安装和更新均尚未验收；本组件不应作为稳定成品发布。

