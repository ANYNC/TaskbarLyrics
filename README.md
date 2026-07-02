# TaskbarLyrics
Windows 任务栏歌词工具。它通过 SMTC 识别当前播放歌曲，并从多个在线歌词源并发检索高置信结果。

## 效果
演示字体为Source Han Sans SC（已内置，无需安装）

![效果图](doc/images/preview.gif)

## Light 版
TaskbarLyrics Light 是由 [sorawithcat](https://github.com/sorawithcat) 维护的原生 WPF 轻量版本，目标是不依赖 WebView2 / Chromium，减少进程数、内存占用和发布体积。

- [Light 版 README](https://github.com/sorawithcat/TaskbarLyrics/blob/main/TaskbarLyrics.Light/README.md)
- 维护者：[sorawithcat](https://github.com/sorawithcat)

## 功能
- 跟随系统进行黑白主题切换
- 纯音乐显示频谱
- 双行任务栏歌词与平滑切换动画
- 播放器对应歌词源优先，失败后按质量权重跨源检索
- 支持 QQ 音乐 QRC、酷狗 KRC、网易云/LRCLIB LRC 歌词解析
- 多歌词源检索、相似度校验与缓存
- 多播放器同时运行时的识别顺序与启用开关

## 已支持播放器
- QQ音乐
- 网易云音乐（建议安装 [inflink-rs](https://github.com/apoint123/inflink-rs) 插件，以获得更完整的 SMTC 元数据）
- 酷狗音乐（SMTC信息不完整无法滚动歌词，可以尝试支持较好的第三方酷狗播放器[MoeKoeMusic](https://github.com/MoeKoeMusic/MoeKoeMusic)）
- Spotify
- 所有支持SMTC协议的播放器

## 歌词检索
TaskbarLyrics 会优先使用当前播放器对应的歌词源；如果短时间内没有取得有效歌词，再按配置启用跨源检索。跨源结果会经过标题、歌手、时长相似度校验，并叠加歌词源质量权重后选择最终结果。

歌词源检索与匹配思路参考了 [jayfunc/BetterLyrics](https://github.com/jayfunc/BetterLyrics)，并结合任务栏实时显示场景做了简化与调整。

## 系统要求
- Windows 10/11 x64
- 下载独立版压缩包时，无需额外安装 .NET 8 Runtime
- 从源码运行或自行构建时，需要安装 .NET 8 SDK
- 如设置页或歌词窗口无法显示，请安装 Microsoft Edge WebView2 Runtime

## 安装
### 方式一：Release 下载
在 [Releases](../../releases) 下载最新版本压缩包，完整解压后运行 `TaskbarLyrics.exe`。

请不要只单独复制或运行 exe，发布包中的 `Assets`、`Web` 等目录也是运行所需文件。

### 方式二：源码运行
```bash
dotnet restore
dotnet run --project TaskbarLyrics.App
```

常用开发重启脚本：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/restart-app.ps1 -Build
```

## 运行时目录
- 程序运行目录：发布包解压目录，或源码运行时的 `TaskbarLyrics.App/bin/...` 输出目录。`Assets`、`Web`、`app_debug.log` 等文件位于此处。
- 用户配置：`%APPDATA%\TaskbarLyrics\settings.json`
- 歌词缓存：`%APPDATA%\TaskbarLyrics\cache`
- 歌曲映射数据库：`%APPDATA%\TaskbarLyrics\database\song_maps.db`

### 自行发布
独立发布：

```bash
dotnet publish TaskbarLyrics.App/TaskbarLyrics.App.csproj -c Release -r win-x64 --self-contained true -p:DebugType=None -p:DebugSymbols=false -o publish/win-x64
```

单文件压缩发布：

```bash
dotnet publish TaskbarLyrics.App/TaskbarLyrics.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o publish/single-compressed
```
