# SuMind

SuMind 是面向 Windows 的模块化桌面控制中心，目前处于基础开发阶段。桌面导航、壁纸引擎和系统工具仍在规划中。

## 临时启动

双击项目根目录的 **启动 SuMind.lnk**。它会先构建最新源码，再启动程序；首次构建或还原可能需要一些时间。窗口已运行时请先通过托盘退出，再重新启动，避免旧进程占用构建文件。构建失败时命令窗口会保留错误信息。

快捷方式是本机生成的，不纳入 Git。也可以双击 `scripts/start-preview.cmd`；脚本优先使用 `%LOCALAPPDATA%\SuMind\dotnet` 下的 .NET SDK，再尝试 PATH 中的 dotnet。需要 .NET 10 SDK 和 Windows 桌面运行环境。运行配置和错误日志保存在 `%LOCALAPPDATA%\SuMind`，不在项目目录中。

## 目录

- `src/SuMind.App/`：WPF 主程序、窗口与配置保存。
- `src/SuMind.Shared/`：组件描述、状态和初步接口。
- `src/Modules/`：未来组件项目所在位置；组件目前尚未实现。
- `assets/`：项目共用图标和图片。
- `docs/`：架构边界和界面开发约定。
- `scripts/`：开发阶段的辅助脚本。
- `installer/`：未来安装包项目的位置，目前尚未实现。
- `AGENTS.md`：本项目的协作约定。

`bin/`、`obj/` 是可重新生成的构建文件，不纳入 Git。构建、兼容性及当前限制见 [架构说明](docs/architecture.md)，界面规则见 [界面开发约定](docs/ui-guidelines.md)。空目录不会被 Git 跟踪。

