# TaskbarLyrics
使用codex开发
一个 Windows 任务栏歌词工具，支持基于 SMTC 的播放状态识别与多歌词源检索。
![效果图](doc/images/preview.gif)

## 功能
- 识别 SMTC 信息
- 自动匹配在线歌词库
- 任务栏歌词显示

## 已支持播放器
- QQ音乐
- 网易云音乐（需安装[inflink-rs](https://github.com/apoint123/inflink-rs)插件）
- Spotify


## 系统要求
- Windows 10/11
- .NET 8 Runtime（小体积版本需要）
- x64

## 安装
### 方式一：Release 下载
在 [Releases](../../releases) 下载最新版本并运行。

### 方式二：源码运行
```bash
dotnet run --project TaskbarLyrics.App
