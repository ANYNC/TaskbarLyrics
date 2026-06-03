# TaskbarLyrics
一个轻量的 Windows 任务栏歌词工具。它通过 SMTC 识别当前播放歌曲，并从多个在线歌词源并发检索高置信结果。

![效果图](doc/images/preview.gif)

## 功能
- 双行任务栏歌词与平滑切换动画
- QQ音乐、网易云音乐、酷狗音乐、Spotify 播放状态识别
- 播放器对应歌词源优先，失败后按质量权重跨源检索
- 多歌词源检索、相似度校验与缓存
- 封面显示、字体样式、位置、背景和边框配置
- 多播放器同时运行时的识别顺序与启用开关

## 已支持播放器
- QQ音乐
- 网易云音乐（建议安装 [inflink-rs](https://github.com/apoint123/inflink-rs) 插件，以获得更完整的 SMTC 元数据）
- 酷狗音乐
- Spotify

## 系统要求
- Windows 10/11
- .NET 8 Runtime
- x64

## 安装
### 方式一：Release 下载
在 [Releases](../../releases) 下载最新版本并运行。

### 方式二：源码运行
```bash
dotnet run --project TaskbarLyrics.App
```
