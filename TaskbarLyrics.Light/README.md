# TaskbarLyrics Light

原生 WPF 轻量版（当前 **v1.1.0**）。与原版共享 `TaskbarLyrics.Core` 业务逻辑，歌词与设置页均用纯 WPF 渲染，去除 WebView2、WPF-UI 依赖，显著降低运行时进程数与内存占用。

## 与原版对比

### 架构

| 项目 | 原版 (`TaskbarLyrics.App`) | 轻量版 (`TaskbarLyrics.Light`) |
|------|---------------------------|-------------------------------|
| 歌词渲染 | WebView2 + HTML/CSS/JS | 原生 WPF `LyricsDisplayControl` |
| 设置页 | WebView2 加载 `settings.html` | 原生 WPF + `SettingsTheme.xaml` |
| UI 框架 | WPF + WPF-UI | 纯 WPF |
| 默认字体 | 内置 Source Han Sans SC（约 33 MB） | 内置 Source Han Sans SC（约 33 MB，默认） |
| WebView2 Runtime | 必须 | 不需要 |
| 业务逻辑 | `TaskbarLyrics.Core` | 共享 `TaskbarLyrics.Core` |
| 歌词缓存 / 歌曲映射 | `%APPDATA%\TaskbarLyrics\` | 与原版共享 |
| 单实例 | 支持 | 支持（独立锁文件与激活管道） |

### 性能（实测）

测试环境：Windows 11 x64（22631），`Release` 发布，`win-x64` 框架依赖（`--self-contained false`），启动后静置 **20 s** 取样；内存为**主进程及其子进程树**合计（原版含 WebView2）。测试日期：**2026-07-01**；版本：原版 **v1.1.1** / 轻量版 **v1.1.0**。

| 指标 | 原版 v1.1.1 | 轻量版 v1.1.0 | 变化 |
|------|-------------|---------------|------|
| 发布包体积 | 75.7 MB（54 文件） | **67.6 MB**（38 文件） | **约 −11%** |
| 进程数（含子进程） | **7**（1 主进程 + 6× WebView2） | **1** | **单进程** |
| 工作集内存 | **~669 MB** | **~340 MB** | **约 −49%** |
| 专用内存 Private | **~398 MB** | **~234 MB** | **约 −41%** |

> **默认启动差异**：原版默认**显示**歌词窗（WebView2 随即加载）；轻量版默认开启「播放器关闭时隐藏」，启动时**不显示**歌词窗。若强制轻量版启动即显示歌词窗，20 s 后仍约 **338 MB / 233 MB Private**（单进程），优势不变。
>
> 轻量版在包含思源黑体后，磁盘体积与原版接近，但**无 Chromium 多进程栈**、无 `ExecuteScriptAsync` 跨进程开销，空闲内存显著更低。若去掉内置字体改用系统字体，发布包可降至约 **35 MB**。

轻量版去掉的主要运行时开销：**Chromium 多进程栈**、**每帧 `ExecuteScriptAsync` 跨进程调用**、以及 WPF-UI 依赖。

### 性能优化（轻量版内置）

- **延迟初始化**：SQLite / EF Core 与歌词同步服务在首次需要检索歌词时才加载
- **Provider 懒加载**：各在线歌词源在首次被调用时才实例化
- **合并帧定时器**：16 ms 统一调度歌词（约 64 ms）与频谱（约 32 ms）刷新
- **频谱按需采集**：仅在需要显示频谱时启动 WASAPI 环回采集线程（纯音乐，或开启「未找到歌词时显示频谱」）
- **WPF 渲染缓存**：行高 `FormattedText` 测量缓存、画刷 `Freeze`
- **设置页**：字体列表延迟枚举、下拉列表虚拟化
- **Release 发布**：框架依赖发布时剥离 PDB（`DebugType=none`）；因托盘使用 WinForms，`PublishTrimmed` 与当前 SDK 不兼容

### 运行时路径差异

- **原版**：C# 定时器 → JSON 序列化 → `ExecuteScriptAsync` → Chromium 渲染（歌词约 60 ms/次，频谱约 33 ms/次）
- **轻量版**：C# 定时器 → 直接更新 WPF 控件属性；频谱在控件内以 16 ms `DispatcherTimer` 插值，切歌动画走 `CompositionTarget.Rendering`

## 功能

### 与原版对齐（v1.1.0）

- 双行歌词滚动与 560 ms 平滑切换动画
- SMTC 专辑封面异步加载、交叉淡入；本地音乐目录封面回退（同目录图片 / 内嵌图）
- 本地歌词（`.lrc` / `.qrc` / `.krc`）与多源在线歌词检索
- 纯音乐 24 条实时频谱；频谱总开关；未检索到歌词时可显示频谱
- 单实例防多开；二次启动激活已有实例并打开设置
- 系统托盘、原生 WPF 设置页、关于页与更新检查（对接主仓库 Release）
- 运行日志与频谱调试诊断面板
- 歌词窗口强制置顶开关；从 Alt+Tab 隐藏（`WS_EX_TOOLWINDOW`）
- SMTC 时间轴调试窗口、频谱调参窗口

### 轻量版独有

- **开机自启动**（写入当前用户注册表 `Run` 项，默认开启）
- **播放器联动**（默认开启）：
  - 检测到已启用播放器开始播放时自动显示歌词
  - 全部播放器停止后自动隐藏歌词
  - 开启「播放器关闭时隐藏」时，**启动阶段默认不显示歌词窗**（避免无播放时闪一下）；若同时开启「播放器打开时显示」，检测到播放后会自动弹出
- **默认字体**：`Source Han Sans SC`（旧配置缺字段时自动补全）

### 轻量版设置页（原生 WPF）额外支持

- 窗口宽度 / 高度随歌词内容自动适配（可关）
- 行距自动适配字号（可关）

### 与原版仍存在的差异

| 能力 | 原版 | 轻量版 |
|------|------|--------|
| 显示歌词翻译 | 支持（设置项可开关） | **不支持**（仅显示原文） |
| 设置页实现 | WebView2 HTML | 原生 WPF |
| 开机自启动 | 支持 | 支持（独立注册表项 `TaskbarLyrics.Light`） |
| 播放器打开/关闭联动 | 无 | 支持 |
| 当前版本 | 1.1.1 | **1.1.0** |

## 系统要求

- Windows 10/11 x64
- 从源码运行或框架依赖发布时，需安装 [.NET 8 Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
- **不需要** Microsoft Edge WebView2 Runtime

## 运行

```bash
dotnet run --project TaskbarLyrics.Light/TaskbarLyrics.Light.App
```

## 发布

框架依赖（需目标机器已装 .NET 8，含内置字体约 **68 MB**）：

```bash
dotnet publish TaskbarLyrics.Light/TaskbarLyrics.Light.App -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o publish/light
```

独立发布（无需预装运行时）：

```bash
dotnet publish TaskbarLyrics.Light/TaskbarLyrics.Light.App -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o publish/light-standalone
```

发布包请完整解压后运行 `TaskbarLyrics.Light.exe`，需保留 `Assets` 等目录。

## 配置与数据目录

| 用途 | 路径 |
|------|------|
| 轻量版设置 | `%APPDATA%\TaskbarLyrics.Light\settings.json` |
| 单实例锁 | `%LOCALAPPDATA%\TaskbarLyrics.Light\TaskbarLyrics.Light.lock` |
| 运行日志 | 程序目录下 `Logs\app_debug.log`（开启 SMTC 监视或出现警告/错误时写入更完整） |
| 歌词缓存 | `%APPDATA%\TaskbarLyrics\cache`（与原版共享） |
| 歌曲映射数据库 | `%APPDATA%\TaskbarLyrics\database\song_maps.db`（与原版共享） |

两版可同时安装；设置文件与单实例锁独立，缓存与数据库共用。

## 选用建议

- **优先轻量版**：日常挂任务栏、在意内存与进程数、不想装 WebView2、需要开机自启与播放器联动、只需原文歌词
- **优先原版**：需要歌词翻译、偏好 WebView2 设置页，或希望与主仓库 Release 版本号完全同步

## 版本记录

### 1.1.0

跟进原版 1.1.x 核心能力：单实例、本地歌词与封面、频谱增强、日志与诊断、关于页与更新检查、置顶与任务视图隐藏；保留轻量版播放器联动与原生 WPF 设置体验。
