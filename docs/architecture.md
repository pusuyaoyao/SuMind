# 第一阶段架构与边界

SuMind.App 是 WPF 控制中心；SuMind.Shared 定义组件描述、独立的安装/启用/运行状态及生命周期接口。目前的列表仅代表计划中的功能，不代表发现或安装了组件。新增条目不受固定数量限制。任务栏项目今后可实现接口或通过独立进程适配；原生代码、进程隔离、接口版本及故障恢复的协议需要进一步设计，目前接口不承诺二进制兼容。

目前没有组件安装器、插件加载器、启用操作或更新机制，也不修改任务栏。未来下载与更新需要设计来源验证、完整性校验、签名、事务和回滚。WiX、C++/WinRT 和视频播放引擎不属于本阶段依赖，需分别验证目标系统和分发许可。

配置位于 `%LOCALAPPDATA%\SuMind\settings.json`，错误日志位于同目录的 `error.log`。关闭窗口默认隐藏到托盘，设置中可改为退出。

安装 .NET 10 SDK 后运行 `dotnet build src/SuMind.App/SuMind.App.csproj`，或 `dotnet run --project src/SuMind.App`。正式发布时需要选择运行时及部署方式。项目声明 Per-Monitor V2 DPI 感知，多屏、缩放和高对比度仍需在实际设备测试。

本机 SDK 安装在 `%LOCALAPPDATA%\SuMind\dotnet`，未修改 PATH。可使用 `& "$env:LOCALAPPDATA\SuMind\dotnet\dotnet.exe" build src\SuMind.App\SuMind.App.csproj` 构建。

拓展页面将桌面导航、壁纸引擎和系统工具分别展示为一个拓展。桌面导航包含任务栏设置和 Dock，作为同一个功能规划。当前条目是静态目录，未来应由组件管理器提供实际状态，再设计安装及生命周期接入。


