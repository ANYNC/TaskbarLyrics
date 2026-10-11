<p align="center">
  <img src="docs/images/promo/taskbarlyrics-ui-showcase.svg" alt="TaskbarLyrics — 播放控制与任务栏歌词效果展示" width="100%">
</p>

<p align="center">
  <a href="https://github.com/ANYNC/TaskbarLyrics/releases/latest"><img src="https://img.shields.io/github/v/release/ANYNC/TaskbarLyrics?style=flat&amp;label=release&amp;labelColor=202124&amp;color=58a6ff" alt="最新版本"></a>
  <a href="https://github.com/ANYNC/TaskbarLyrics/releases"><img src="https://img.shields.io/github/downloads/ANYNC/TaskbarLyrics/total?style=flat&amp;label=downloads&amp;labelColor=202124&amp;color=3fb950" alt="总下载量"></a>
  <a href="https://github.com/ANYNC/TaskbarLyrics"><img src="https://img.shields.io/github/languages/top/ANYNC/TaskbarLyrics?style=flat&amp;labelColor=202124&amp;color=a371f7" alt="主要开发语言：C#"></a>
  <a href="LICENSE"><img src="https://img.shields.io/github/license/ANYNC/TaskbarLyrics?style=flat&amp;labelColor=202124&amp;color=d29922" alt="MIT 许可证"></a>
</p>

<p align="center">
  将同步歌词融入 Windows 任务栏，让音乐与工作自然相伴。
</p>

## 效果演示
逐字扫描·歌词翻译·主题切换·音频频谱

<p align="center">
  <img src="docs/images/preview/demo.gif" alt="TaskbarLyrics 任务栏歌词、翻译与频谱动态效果" height="400">
</p>

<p align="center">
  演示使用内置的 Source Han Sans SC 字体，无需单独安装。
</p>

## 下载与快速开始

### 下载运行

**[下载最新版本 →](https://github.com/ANYNC/TaskbarLyrics/releases/latest)** · [查看所有版本](https://github.com/ANYNC/TaskbarLyrics/releases)

1. **解压**：下载版本压缩包，并完整解压到一个普通文件夹。
2. **启动**：运行 `TaskbarLyrics.exe`，程序会常驻系统托盘。
3. **播放**：打开受支持的播放器播放音乐，歌词会自动显示在任务栏。

请保留发布包中的 `Assets`、`Web` 等目录，不要只复制或单独运行 EXE。

也可以在解压目录中打开 PowerShell，通过命令启动：

```powershell
.\TaskbarLyrics.exe
```

### 从源码启动

准备好 Git、.NET 8 SDK 和 Windows 11 SDK 10.0.22621，在 Windows x64 环境中打开 PowerShell，执行：

```powershell
git clone https://github.com/ANYNC/TaskbarLyrics.git
cd TaskbarLyrics
dotnet restore
dotnet run --project TaskbarLyrics.App
```

已经下载源码时，在项目根目录执行最后两条命令即可。启动后，打开受支持的播放器播放音乐。

### 系统要求

- Windows 10 / 11 x64。
- 独立版压缩包无需额外安装 .NET 8 Runtime。
- 主版本使用 Microsoft Edge WebView2 Runtime；如果设置页或歌词窗口为空白，请安装该运行时。

### 调整到你喜欢的样子

- **外观与位置**：在“设置 → 显示与外观”调整字体、颜色、封面和窗口位置，主要外观设置即时生效。
- **多显示器**：在“显示与外观 → 窗口位置 → 显示器”选择全部或指定显示器。
- **播放源**：启用正在使用的播放器，并拖动调整识别优先级。
- **歌词校准**：从托盘菜单打开“调整当前歌曲偏移”，修正当前歌曲的提前或延后。

## 播放器支持

| 播放器 | 支持情况 | 使用提示 |
| --- | --- | --- |
| QQ 音乐 | 支持 | 通常可获得较完整的歌曲信息和播放进度 |
| 网易云音乐 | 支持 | 歌曲信息或进度不完整时，可安装 [inflink-rs](https://github.com/apoint123/inflink-rs) 改善识别与同步 |
| 酷狗音乐 | 有限支持 | 部分版本无法提供可靠进度，歌词可能无法滚动；也可尝试 [MoeKoeMusic](https://github.com/MoeKoeMusic/MoeKoeMusic) |
| Spotify | 支持 | 歌词由可用的在线来源自动匹配 |
| 其他播放器 | 视播放器而定 | 需要向 Windows 提供可用的歌曲信息和播放进度 |

歌词与翻译的可用性取决于具体歌曲和歌词来源。

## 常见问题

**歌词没有显示？** 从托盘菜单选择“显示/隐藏歌词”，并确认“设置 → 播放源”中已启用当前播放器。如果歌词出现但不滚动，请检查上方播放器兼容提示。

**匹配到了错误歌词？** 打开“设置 → 歌词匹配”查找候选，选择“本次使用”立即修正，或选择“使用并记住”保存结果。

**歌词提前或延后？** 多首歌曲都有同样偏差时，先调整播放器偏移；只有某一首不同步时，再调整单曲偏移。

更多问题请查看 **[常见问题与故障排查](docs/常见问题与故障排查.md)**。

<details>
<summary><strong>本地歌词、频谱与数据位置</strong></summary>

### 本地歌词

在“设置 → 播放源”开启“优先使用本地歌词”，并添加本地音乐目录。支持 `.lrc`、`.qrc`、`.krc`，以及部分音频文件中的内嵌歌词。建议歌词文件与歌曲文件使用相同或接近的文件名。

内嵌歌词需要包含 LRC 时间标签，不同文件格式与标签的兼容情况见[本地歌词排查说明](docs/常见问题与故障排查.md#4-本地歌词没有被识别)。

### 实时频谱

在“设置 → 歌词”或托盘菜单中选择关闭、仅纯音乐时显示、纯音乐或无歌词时显示，或始终显示。频谱没有反应时，请确认音频正在播放，并检查当前音频输出设备。

### 数据与卸载

设置、歌词缓存、歌曲映射和单曲偏移保存在 `%APPDATA%\TaskbarLyrics`，日志保存在程序目录的 `Logs` 文件夹。

清理歌词缓存会同时删除“使用并记住”的歌词匹配记录，但不会删除应用设置或单曲偏移。卸载步骤见[如何彻底卸载](docs/常见问题与故障排查.md#7-如何彻底卸载)。

在线匹配会将当前歌曲的标题、歌手、专辑或时长发送给相应歌词服务；自动检查更新时会访问 GitHub Releases。

</details>

## 相关文档

- [常见问题与故障排查](docs/常见问题与故障排查.md)：播放器兼容、本地歌词、卸载与问题反馈。
- [功能与技术说明](docs/功能与技术说明.md)：完整功能、系统结构与实现说明。
- [歌曲歌词匹配与录用评分标准](docs/歌曲歌词匹配与录用评分标准.md)：歌词候选的匹配与选择规则。
- [歌词呈现引擎](docs/歌词呈现引擎.md) · [频谱调节参数说明](docs/频谱调节参数说明.md)。
- [WebView 界面视觉与交互规范](docs/WebView界面视觉与交互规范.md) · [工程变更记录](docs/工程变更记录.md)。

<details>
<summary><strong>构建与发布</strong></summary>

完成上方的源码准备后，在项目根目录构建解决方案：

```powershell
dotnet build TaskbarLyrics.sln
```

生成独立发布包：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish-release.ps1
```

</details>

## 致谢

- [BetterLyrics](https://github.com/jayfunc/BetterLyrics)：歌词检索与匹配思路参考。
- [Lyricify Lyrics Helper](https://github.com/WXRIW/Lyricify-Lyrics-Helper)：在线歌词搜索、解密与解析能力。

## 支持项目

喜欢 TaskbarLyrics，欢迎 [Star 项目](https://github.com/ANYNC/TaskbarLyrics)、[提交问题或建议](https://github.com/ANYNC/TaskbarLyrics/issues)，或赞助支持持续开发。

<details>
<summary><strong>微信赞赏</strong></summary>

<p align="center">
  <img src="docs/images/weixin-payment-code.png" alt="微信赞赏码" width="200">
</p>

感谢你的支持！

</details>

## 许可证

本项目使用 [MIT License](LICENSE)。
