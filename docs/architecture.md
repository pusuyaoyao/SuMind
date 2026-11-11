# 第一阶段架构与边界

SuMind.App 是 WPF 控制中心；SuMind.Shared 定义组件描述、独立的安装/启用/运行状态及生命周期接口。目前的列表仅代表计划中的功能，不代表发现或安装了组件。新增条目不受固定数量限制。任务栏项目今后可实现接口或通过独立进程适配；原生代码、进程隔离、接口版本及故障恢复的协议需要进一步设计，目前接口不承诺二进制兼容。

目前已加入实验性 `.suminds` 包安装器、签名校验、GitHub 目录服务和桌面导航独立进程接口；发布公钥尚未配置，GitHub 上已有的拓展包仅为不可安装的开发预览，安装入口保持禁用。Windows 10 与 Windows 11 的任务栏效果仍需实机验证，不应将构建成功视为功能验收。签名密钥管理、更新回滚和发布流程还需端到端验证。

配置位于 `%LOCALAPPDATA%\SuMind\settings.json`，错误日志位于同目录的 `error.log`。关闭窗口默认隐藏到托盘，设置中可改为退出。

各拓展的用户数据独立放在 `%LOCALAPPDATA%\SuMind\extension-data\<拓展ID>`，与程序安装目录分离；桌面导航设置保存在其中的 `settings.json`，卸载时可选择保留或删除。升级不会移除数据，旧版主设置中的任务栏外观首次读取时会迁移。当前尚无主程序安装器或 Windows 卸载注册，主程序卸载入口处于禁用状态；后续安装器需要提供确认、可选保留数据及自定义拓展目录的处理。

安装 .NET 10 SDK 后运行 `dotnet build src/SuMind.App/SuMind.App.csproj`，或 `dotnet run --project src/SuMind.App`。正式发布时需要选择运行时及部署方式。项目声明 Per-Monitor V2 DPI 感知，多屏、缩放和高对比度仍需在实际设备测试。

本机 SDK 安装在 `%LOCALAPPDATA%\SuMind\dotnet`，未修改 PATH。可使用 `& "$env:LOCALAPPDATA\SuMind\dotnet\dotnet.exe" build src\SuMind.App\SuMind.App.csproj` 构建。

拓展页面将桌面导航、壁纸引擎和系统工具分别展示为一个拓展。桌面导航包含任务栏设置和未来的 Dock，作为同一个拓展。拓展包版本目录位于 `%LOCALAPPDATA%\SuMind\extensions`；壁纸引擎和系统工具仍是静态规划项。


