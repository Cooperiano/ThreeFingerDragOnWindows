# Touchpad Control — Julian's adaptation

Windows Precision Touchpad 操控改造，基于 Clément Grennerat 的
[ThreeFingerDragOnWindows 2.0.7](https://github.com/ClementGre/ThreeFingerDragOnWindows/tree/2.0.7)。
保留 [MIT 许可](LICENSE)、版权和公开历史；[原版说明](README.upstream.md)以 UTF-8 保存。

由 [Julian Cooper / Cooperiano](https://github.com/Cooperiano) 主导需求、交互方向、套件整合与实机反馈，Codex 辅助实现。
主桌面框架是 [Seelen Desktop Suite](https://github.com/Cooperiano/Seelen-UI)，本仓库是独立触控板组件，不是新内核驱动，也不包含 Apple 的私有算法。

## 来源与公开范围

- `ThreeFingerDragOnWindows/`：WinUI 主程序、输入接收、设置、原生跟踪适配与套件桥接。
- `ThreeFingerDrag.Core/`：手势状态、接触帧、拖动按键生命周期、归一化与本地化。
- `ThreeFingerDragEngine.Tests/`：纯回归测试，不运行实际接收器或注入指针。
- `WindowOverview/`：本项目增加的总览辅助程序和几何测试源；跨私人工作区引用已改为仓库内路径。
- `ThreeFingerDragElevator/`：原项目的辅助模块；构建源码不表示会提权或安装服务。

Julian 的工作包括推动整体触控板操控方向、统一速度/加速入口、三指起步与拖动安全、窗口总览交互、双语设置、启动恢复及套件整合。上游与 Apple 的工作不算作我们的原创，见 [NOTICE](NOTICE.md)。

这是本机 `2.2.0.48` 开发线的源码快照，基础为上游公开提交
`658e18dfdbf71827865a1051d07cabaef59c7fcc`（2.0.7）。`touchpad-control` 是改造主线；上游 `main` 历史另行保留，未声称自动采用之后全部新功能。版本号不代表本次重新安装或字节一致重建已装包。

## 构建与验证

Windows、.NET 10 SDK；WinUI 构建还需兼容 Windows SDK/MSVC 和 NuGet 网络访问。

```powershell
dotnet test ThreeFingerDragEngine.Tests/ThreeFingerDragEngine.Tests.csproj -c Debug
dotnet build WindowOverview/MissionControlPrototype.csproj -c Debug
dotnet msbuild ThreeFingerDragOnWindows/ThreeFingerDragOnWindows.csproj /restore /p:Configuration=Debug /p:Platform=x64 /p:RuntimeIdentifier=win-x64 /p:UnpackagedPreview=true /p:GenerateAppxPackageOnBuild=false /p:AppxPackageSigningEnabled=false /p:GenerateTemporaryStoreCertificate=false
```

本次发布副本 301 项核心测试、总览辅助程序 Debug 构建及 WinUI x64 非打包构建通过。
GitHub CI另行检查核心/辅助程序。编译和模拟验证不代表真实注入、手感、视觉或开机登录验收。

自建总览运行负载时先执行：

```powershell
dotnet publish WindowOverview/MissionControlPrototype.csproj -c Debug -r win-x64 --self-contained true -o ThreeFingerDragOnWindows/MissionControl
```

生成目录忽略，不提交。打包身份沿用该开发线，不代表你拥有上游 Store 身份或本机签名证书。
公开二进制分发前还需自己的身份、签名、安装和恢复流程；此处不提供安装器。

## 边界

- 一个共享 Windows 跟踪速度/加速入口，由内部适配手势，不给每种手指数独立倍率，也不承诺复刻 macOS 曲线。
- 用户确认三指能拖动，但曲线手感仍需改进；实际定位、拖窗、选字和抬手测试不可由单测替代。
- 四指总览可关闭保留 Windows 手势，不等于完整接管原生任务视图。
- 不要与已装输入接收器并行运行，不自动安装证书/驱动、改登录任务或盲调 Windows 指针设置。
- 偏好、原始 HID/输入日志、截图、转储、签名材料及安装包均不发布；NuGet 依赖保留原许可。

## English

An experimental adaptation led by Julian Cooper with Codex-assisted implementation, based on
**Clément Grennerat's ThreeFingerDragOnWindows 2.0.7 (MIT)**. Public ancestry and copyright remain.
Original additions and the window-overview helper are MIT; dependencies retain their own terms.

This publishes the WinUI component, gesture engine, helper source and tests for the Seelen-based
desktop scheme. It is not a kernel driver, official Store release or port of Apple's private code.
Passing 301 pure tests does not establish hardware feel or installed-package acceptance.
