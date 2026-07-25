# TaskbarLyrics Clean Code 代码审查与优化清单

> 文档状态：基于当前 `main` 分支代码审查整理  
> 审查范围：`TaskbarLyrics.App`、`TaskbarLyrics.Core`、Web UI 及现有工程化配置  
> 文档用途：作为后续渐进式重构、任务拆分和验收的依据  
> 本文保留审查基线，并在第 16、19 节同步记录已完成的优化与验证结果。

## 1. 总体结论

从 Clean Code 各维度审视，项目目前属于“功能完整、实现有效，但功能持续堆叠到宿主类”的阶段。主要技术债不是算法本身，而是职责边界、生命周期、状态传播和测试保护不足。

当前没有发现必须立即停用程序的 P0 问题，但有四项建议优先处理的 P1 问题：

1. 设置即时应用会反复重建歌词服务和本地目录索引。
2. 歌词 STA 线程初始化失败可能让应用启动永久等待。
3. 设置文件不是原子写入，读取失败会静默恢复默认值。
4. 后台歌词检索采用未观察任务，异常与失败状态不完整。

## 2. P1：优先处理的问题

### 2.1 设置即时应用会反复重建歌词服务和本地目录索引

设置页的滑块在每次 `input` 时发送消息：

- `TaskbarLyrics.App/Web/Settings/settings.js:631`

消息经过以下调用链：

1. `SettingsWindow` 接收更新并保存设置：`SettingsWindow.xaml.cs:151`。
2. `SettingsStore` 同步覆写 `settings.json`：`SettingsStore.cs:35`。
3. `App` 将整份设置重新发送给歌词线程：`App.xaml.cs:101`。
4. `MainWindow.ApplySettings` 重建 `LocalMediaCoverProvider`、`LyricSyncService` 和全部歌词提供者：`MainWindow.xaml.cs:110`。
5. 本地歌词和封面提供者分别启动不可取消的后台目录扫描：
   - `TaskbarLyrics.Core/Services.LocalLyricProvider.cs:62`
   - `TaskbarLyrics.App/LocalMediaCoverProvider.cs:45`

因此，拖动背景透明度、封面间距等纯视觉滑块，也可能重复创建歌词服务、HTTP 提供者并扫描整个本地媒体目录。旧索引任务没有 `Dispose` 或取消机制，会继续运行到结束。

启动时还存在重复应用：`MainWindow` 构造时应用一次全局设置，`LyricsWindowHost.Run` 随后又应用一次。启用本地目录时可能立即产生重复索引。

建议：

- 引入 `SettingsChangeSet`，将变化分为：
  - 纯视觉设置
  - 窗口布局设置
  - 播放器识别设置
  - 歌词提供者拓扑设置
  - 本地目录设置
- 视觉设置只调用 `LyricsView.ApplyStyle`。
- 只有本地目录或本地歌词开关变化时才重建本地索引。
- 歌词提供者保持长生命周期，不随每次设置更新重建。
- 前端预览可以即时发送，磁盘持久化应做 200～500 ms 防抖。
- 本地索引服务实现 `IAsyncDisposable`，支持取消旧索引。

这是当前投资回报最高的重构项。

### 2.2 歌词 STA 线程初始化失败可能让启动永久等待

`LyricsWindowHost` 启动线程后调用无超时的 `_ready.Wait()`：

- `TaskbarLyrics.App/LyricsWindowHost.cs:17`

但 `_ready.Set()` 只有在 `MainWindow` 创建和设置应用全部成功后才执行：

- `TaskbarLyrics.App/LyricsWindowHost.cs:118`

如果窗口构造、程序集加载或初始设置应用抛出异常，后台线程会退出，而主线程将永久等待。

建议：

- 使用 `TaskCompletionSource<Dispatcher>` 表达初始化结果。
- 在线程入口使用 `try/catch`，把异常传回主线程。
- 设置合理的初始化超时。
- 构造函数不要执行阻塞等待，改成 `LyricsWindowHost.CreateAsync(...)`。
- `InvokeAsync` 返回 `Task`，不丢弃跨线程执行异常。

### 2.3 设置持久化不是原子写入，读取失败会静默恢复默认值

`SettingsStore.Load` 捕获所有读取和反序列化异常，然后直接返回默认设置：

- `TaskbarLyrics.App/SettingsStore.cs:15`

保存时通过 `File.WriteAllText` 覆写原文件：

- `TaskbarLyrics.App/SettingsStore.cs:35`

如果写入期间崩溃、磁盘空间不足或文件暂时损坏，下次启动会悄悄恢复默认设置，用户无法得知原设置发生了什么。

建议：

- 写入临时文件，再通过 `File.Replace` 或同卷原子移动替换。
- 保留一份 `settings.json.bak`。
- 区分文件不存在、JSON 损坏、权限不足等错误。
- 加载失败时记录明确日志，并保留损坏文件。
- 返回 `SettingsLoadResult`，让 UI 有机会提示“设置文件损坏，已使用默认值”。
- `Save` 不应调用会修改传入对象的规范化方法。

### 2.4 后台歌词检索采用未观察任务，失败状态不完整

`LyricSyncService.GetDisplayFrameAsync` 主要执行同步计算，却在发现新歌时通过以下方式启动后台检索：

```csharp
_ = UpdateLyricsAsync(snapshot.Track, trackId);
```

相关位置：

- `TaskbarLyrics.Core/Services.LyricSyncService.cs:47`
- `TaskbarLyrics.Core/Services.LyricSyncService.cs:74`
- `TaskbarLyrics.Core/Services.LyricSyncService.cs:172`

`UpdateLyricsAsync` 只处理主动取消，没有处理其他异常。虽然单个提供者的大部分异常会在 Registry 内转换成未找到，但 Registry 自身或未来新增逻辑抛出的异常仍可能成为未观察任务。

这个方法名也具有误导性：调用者 `await GetDisplayFrameAsync` 并不代表已经等待歌词检索完成。

建议：

- 将同步部分改名为 `GetDisplayFrame`。
- 把曲目变化显式建模为 `SetTrackAsync` 或后台工作循环。
- 保存并观察 `_searchTask`。
- 捕获非取消异常，设置明确的 `Failed` 状态并记录完整异常。
- 用状态机表达 `Idle → Searching → Ready/NotFound/Failed`。
- 为切歌、取消、旧结果回写等竞态编写单元测试。

## 3. Clean Code 维度总览

| 维度 | 当前情况 | 主要优化方向 |
| --- | --- | --- |
| 有意义的命名 | 存在语义复用和名称与行为不一致 | 区分播放器与歌词源；修正异步和窗口命名 |
| 函数 | 部分函数具有隐藏副作用，宿主方法过多 | 单一职责、命令查询分离、显式状态变化 |
| 注释 | 有历史性、实现复述和过期注释 | 只保留“为什么”和平台约束 |
| 格式 | 部分 Core 文件格式明显不统一 | `.editorconfig`、格式化与分析器 |
| 对象与数据结构 | `AppSettings` 是大型可变属性包 | 按领域拆分不可变设置快照 |
| 错误处理 | 空 `catch` 和错误吞没较多 | 结果类型、边界捕获、用户提示与日志分离 |
| 边界 | WebView、文件系统、数据库和第三方库直接渗入宿主 | 建立适配器和类型化契约 |
| 类 | 多个 700～1300 行多职责类 | 按业务能力拆分服务和控制器 |
| 系统设计 | 对象主要在窗口内直接 `new` | 建立 Composition Root 和依赖注入 |
| 并发 | 独立 STA 方向正确，但任务生命周期不完整 | 可取消、可等待、可观察、可关闭 |
| 单元测试 | 没有 .NET 测试项目 | 优先保护 Core 纯逻辑和并发状态 |
| 消除重复 | 本地扫描、缓存和解析存在重复实现 | 抽取媒体索引、缓存仓储和解析器 |
| 前端代码 | 单文件、全局可变状态、字符串消息 | 模块化 store、组件控制器和版本化消息 |

## 4. 有意义的命名

### 4.1 `SourceApp` 同时表示播放器和歌词来源

`TrackInfo.SourceApp` 表示播放器，而 `ILyricProvider.SourceApp` 表示歌词提供者：

- `TaskbarLyrics.Core/Models.TrackInfo.cs:3`
- `TaskbarLyrics.Core/Abstractions.ILyricProvider.cs:5`

这导致大量代码必须依靠上下文判断 `SourceApp` 究竟代表 QQ 播放器还是 QQ 歌词源。

建议统一为：

- `PlayerId` / `PlayerKind`
- `LyricProviderId`
- `CurrentLyricProviderId`
- 数据库中的歌词来源字段改为 `LyricProviderId`

### 4.2 其他命名问题

- `MainWindow` 实际是歌词悬浮窗，建议改为 `LyricsOverlayWindow`。
- `GenericSmtcLyricProvider` 实际使用 LRCLIB，建议改为 `LrcLibLyricProvider`。
- `GetDisplayFrameAsync` 应改为同步名称，或真正等待异步工作。
- `LeadTime` 与用户设置中的 `Offset` 混用，建议统一定义正负方向。

## 5. 函数

### 5.1 隐藏副作用过多

典型例子：

- `AppSettings.Clone()` 会先修改原对象：`AppSettings.cs:166`。
- `SaveSettings()` 不仅保存，还切换主题、替换全局状态并重新配置歌词线程。
- `GetDisplayFrameAsync()` 会隐式启动网络检索。
- `ApplySettings()` 会创建服务、扫描文件、重建同步状态、定位窗口和更新 UI。
- `UpdateLyricLines` 的 `lineProgress` 参数完全未使用：`MainWindow.xaml.cs:586`。

建议遵循命令查询分离：

- `Clone` 必须无副作用。
- `Normalize` 返回新对象，或由专门迁移器执行。
- `SaveSettingsAsync` 只负责持久化。
- 拆分 `ApplyVisualSettings`、`ApplyPlayerSettings`、`ReconfigureLocalLibraryAsync`。
- 删除未使用参数和死代码。

### 5.2 异步语义需要明确

- 除事件处理器外避免 `async void`。
- `SettingsWindow.ApplyExternalSettings` 应返回 `Task`。
- 所有 fire-and-forget 操作应通过统一的任务观察器记录异常。
- 跨线程 `InvokeAsync` 应将成功、失败和取消传给调用者。

## 6. 类与职责

当前复杂度集中在少数大型类和脚本中：

| 文件 | 规模 |
| --- | ---: |
| `MainWindow.xaml.cs` | 1317 行，约 61 个方法 |
| `SettingsWindow.xaml.cs` | 1297 行，约 56 个方法 |
| `Web/Settings/settings.js` | 1158 行，约 61 个函数、26 个顶层可变变量 |
| `Web/Lyrics/app.js` | 909 行，约 45 个函数、38 个顶层可变变量 |
| `SmtcMusicSessionProvider.cs` | 849 行 |
| `TrayMenuWindow.xaml.cs` | 710 行，约 66 个方法 |
| `Services.LrcLibSmtcLyricProviderBase.cs` | 862 行 |
| `Services.LocalLyricProvider.cs` | 718 行 |

### 6.1 拆分 `MainWindow`

当前同时负责播放轮询、歌词服务构建、封面检索、频谱采集、WebView 生命周期、任务栏定位、Win32 样式和诊断日志。

建议结构：

```text
LyricsOverlayWindow
├─ PlaybackCoordinator
├─ LyricsPresentationController
├─ CoverController
├─ SpectrumController
├─ LyricsWebViewAdapter
└─ TaskbarPlacementService
```

### 6.2 拆分 `SettingsWindow`

建议拆出：

- `SettingsMessageRouter`
- `SettingsMutationService`
- `TrackOffsetController`
- `UpdateController`
- `FontCatalogService`
- `NativeWindowController`

### 6.3 拆分托盘原生交互

`TrayMenuWindow` 中的 Win32 定位、鼠标钩子和托盘溢出窗口处理应移动到：

- `TrayPopupPlacementService`
- `TrayOverflowInterop`
- `NativeMouseHook`

## 7. 对象与数据结构

### 7.1 `AppSettings` 过大且重复表达

`AppSettings` 同时包含播放器、歌词、频谱、外观、窗口、启动和更新状态。Web 侧又有 `WebSettingsPayload`，恢复默认时还有手写 `CopySettings`：

- `TaskbarLyrics.App/SettingsWindow.xaml.cs:1108`

每增加一个设置，通常需要修改：

- `AppSettings`
- Web payload
- `CopySettings`
- C# 设置 switch
- JS state
- HTML marker
- 契约测试数组

建议拆成不可变子配置：

```csharp
AppSettings
{
    PlayerSettings Players;
    LyricSettings Lyrics;
    SpectrumSettings Spectrum;
    AppearanceSettings Appearance;
    WindowSettings Window;
    StartupSettings Startup;
    UpdateSettings Update;
}
```

再由统一的设置映射器处理迁移、验证和默认值。

### 7.2 清理遗留状态

`EnablePureMusicSpectrum` 和 `ShowSpectrumWhenLyricsNotFound` 只存在于序列化和复制链路，实际显示逻辑已经使用 `SpectrumDisplayMode`：

- `TaskbarLyrics.App/AppSettings.cs:101`

建议通过设置版本迁移后删除，避免存在多个事实来源。

### 7.3 减少 Primitive Obsession

当前大量领域概念使用裸字符串和整数表达：

- 播放器 ID
- 歌词提供者 ID
- Web 消息类型
- 设置 key
- 页面 ID
- 十六进制颜色
- 毫秒偏移

建议在 C# 边界使用枚举、记录类型或值对象，并在 JS 侧集中定义常量。

## 8. 错误处理

### 8.1 空捕获和错误吞没

设置读取、数据库初始化、封面索引、缓存写入和 WebView 初始化等位置存在空 `catch` 或只记录 `ex.Message` 的情况。

建议：

- 记录完整异常，而不只记录 `ex.Message`。
- 预期的 I/O 失败转换为可识别结果。
- 仅在真正可以安全忽略时使用空捕获，并注明业务原因。
- 用户界面显示友好错误信息，详细堆栈只进入日志。
- WebView 消息反序列化边界增加统一 `try/catch`。

### 8.2 Error 日志没有写入独立错误文件

`Log.Write` 无论 Warn 还是 Error 都写入 `GetDebugLogPath()`：

- `TaskbarLyrics.Core/Utilities/Log.cs:24`

`GetErrorLogPath()` 当前没有调用者。因此“错误日志独立文件”的设计实际上没有生效。

建议：

- Error 写入 error log，必要时同时写 debug log。
- 为日志事件增加类别、异常和上下文字段。
- 避免 `MainWindow` 再实现一套 `LogToFile`。

### 8.3 数据库初始化错误被完全忽略

`SongSearchMapDbContext.InitializeDatabase()` 捕获所有异常后不报告：

- `TaskbarLyrics.Core/Database/SongSearchMapDbContext.cs:39`

建议至少写入错误日志，并让调用者知道歌曲映射功能是否可用。

## 9. 边界与第三方代码

### 9.1 WebView2 反射失去了类型安全

项目已经直接引用 WebView2，但歌词窗口仍将 `WebView2` 保存为 `object`，通过反射调用初始化、导航、事件和脚本：

- `TaskbarLyrics.App/MainWindow.xaml.cs:953`

这会导致：

- API 拼写错误只能在运行时发现。
- 出现不必要的事件反射和 Delegate 绑定。
- 增加测试难度和状态字段。
- 增加窗口类代码量。

如果没有明确的多版本兼容需求，应直接使用 `WebView2` 类型。如果确有兼容需求，应把反射全部隔离在 `ILyricsWebViewAdapter` 的单个实现中。

### 9.2 静态文件系统、数据库和全局应用访问

多处直接使用：

- `Environment.GetFolderPath`
- `File` / `Directory`
- `Application.Current is App`
- 静态缓存
- 静态 `HttpClient`

建议逐步引入：

- `IAppPaths`
- `ISettingsStore`
- `ILyricCacheStore`
- `IClock`
- `IUpdateService`
- `IApplicationCommands`

不要求一次性引入完整 DI 容器，但应建立明确的 Composition Root，由 `App` 负责组装对象，窗口只接收依赖。

### 9.3 缓存清理耦合具体类型

`SettingsWindow.ClearLyricCache` 直接调用两个具体类型的静态方法：

```csharp
LyricProviderBase.ClearCache();
GenericSmtcLyricProvider.ClearCache();
```

新增歌词源或缓存实现时容易遗漏。建议改为 `ILyricCacheService.ClearAsync()`。

## 10. 消除重复

### 10.1 本地媒体索引重复

`LocalLyricProvider` 与 `LocalMediaCoverProvider` 重复实现：

- 音频扩展名
- 文件名清理正则
- 歌手/标题拆分
- 递归安全枚举
- 后台分批索引
- 歌曲匹配和评分

相关位置：

- `TaskbarLyrics.Core/Services.LocalLyricProvider.cs:126`
- `TaskbarLyrics.App/LocalMediaCoverProvider.cs:95`

建议建立一次扫描、多消费者共享的 `LocalMediaIndex`：

```text
LocalMediaIndex
└─ LocalMediaEntry
   ├─ 音频路径
   ├─ 标题/歌手/专辑
   ├─ 歌词候选
   └─ 封面候选
```

### 10.2 缓存实现重复

`LyricProviderBase` 和 `LrcLibSmtcLyricProviderBase` 分别实现了内存缓存、磁盘缓存、锁、JSON 读写和清理。

建议抽取：

```csharp
ILyricCacheStore<TPayload>
```

统一处理：

- 缓存 key
- 内存缓存
- 原子磁盘写入
- 缓存格式版本
- 损坏恢复
- 日志
- 清理

### 10.3 日志实现重复

`MainWindow.LogToFile` 绕过 `Log` 的级别和配置，应删除或统一到一个日志抽象。

## 11. 注释

`Services.LyricProviderBase.cs` 存在类似以下注释：

```csharp
// ✅ 匹配与得分逻辑 (此前被误删)
```

这类注释描述的是提交历史，不是当前代码意图。代码中还存在“用于 syllable animation”的注释，但当前主链路只发送行级进度。

建议：

- 删除版本历史式注释。
- 删除复述代码的编号注释。
- 保留协议限制、播放器兼容原因、时间轴经验值来源等“为什么”。
- 对 `300 ms`、`800 ms`、评分阈值等策略值写明来源和适用范围。
- 统一中英文注释语言。

## 12. 格式与工程规范

`LyricProviderBase` 中存在大量单行 `if`、单行 `try/catch` 和不一致的大括号风格。Core 根目录还采用 `Services.X.cs`、`Models.X.cs` 文件命名，而 Database 使用真实目录，结构不一致。

项目当前缺少：

- `.editorconfig`
- Roslyn 分析规则
- `Directory.Build.props`
- 格式检查
- 警告升级策略

建议：

- 按命名空间建立 `Abstractions/`、`Models/`、`Services/`、`Utilities/` 目录。
- 启用 `AnalysisLevel=latest-recommended`。
- 在清理现有警告后逐步启用 `TreatWarningsAsErrors`。
- 增加 `dotnet format --verify-no-changes`。
- 先统一空格、大括号、命名和空捕获规则，避免一次性引入过度严格的风格规则。

## 13. 前端代码

### 13.1 全局可变状态过多

`settings.js` 和 `app.js` 都是大型脚本，状态、渲染、事件处理、动画和原生桥接互相调用。

建议至少按职责拆分：

```text
Settings
├─ state.js
├─ bridge.js
├─ sources-page.js
├─ track-offsets-page.js
├─ appearance-page.js
├─ color-picker.js
└─ dialogs.js

Lyrics
├─ state.js
├─ lyrics-transition.js
├─ cover-controller.js
├─ spectrum-renderer.js
└─ native-api.js
```

### 13.2 WebView 消息是字符串协议

当前消息包括：

- `"update"`
- `"clearCache"`
- `"queryTrackOffsets"`
- `"playerLyricOffset:..."`

现有 PowerShell 测试只检查源码是否包含字符串，不能验证 JSON 结构和行为。

建议：

- 消息统一为 `{ version, type, requestId, payload }`。
- C# 使用枚举或类型化 DTO。
- JS 集中定义消息常量。
- 对请求/响应加入关联 ID 和明确错误响应。
- 把七个位置参数的 `setLyrics(...)` 改为单个对象参数。
- 保留当前契约测试，同时增加真实消息反序列化测试和 DOM 行为测试。

### 13.3 前端状态管理建议

当前项目规模不一定需要引入大型前端框架，可以采用轻量方案：

- 单一 store 保存设置状态。
- reducer 或显式 mutation 函数修改状态。
- 页面控制器只订阅相关状态。
- 原生桥接集中在一个模块。
- DOM 查询结果在组件内部管理，减少跨模块直接操作。

## 14. 并发

独立 STA 歌词线程的方向是合理的，但任务生命周期还不完整。

主要问题：

- `_ready.Wait()` 无超时、无异常传播。
- `InvokeAsync` 不返回任务，调用方无法得知失败。
- 本地歌词和封面索引使用 `CancellationToken.None`。
- 设置重建后旧索引任务继续运行。
- 歌词检索任务没有被保存和观察。
- 部分 `ExecuteScriptAsync` 调用采用 fire-and-forget，失败无法关联到具体操作。
- `async void` 非事件方法无法让调用者观察异常。

建议建立并发约束：

- 每个长生命周期后台任务都有 owner。
- owner 负责取消、等待和释放。
- fire-and-forget 必须进入统一任务观察器。
- 跨线程命令返回 `Task`。
- UI 关闭时等待关键任务有界退出。
- 为切歌、关闭、快速修改设置等场景编写竞态测试。

### 14.1 本地索引就绪语义不明确

本地歌词提供者在索引为空时立即返回未找到。当前歌曲身份不变时，歌词同步服务不会因为索引稍后完成而自动重新发起检索。

建议选择一种明确策略：

- 第一次查询在合理超时内等待索引首次可用；或
- 索引完成后发出事件，使当前歌曲重新检索；或
- 提供增量查询，在后台索引期间直接检查当前歌曲对应路径。

## 15. 单元测试

初始审查时没有 .NET 测试项目；目前已新增 `TaskbarLyrics.App.Tests`，但仍需按下列清单补齐 Core 纯逻辑与并发测试。

建议优先补充：

1. `LyricMatcher` 的标题、歌手、时长和版本冲突匹配。
2. LRC/QRC/KRC 解析、offset、重复时间戳和翻译对齐。
3. `LyricSourceRoutingPolicy` 的官方源和回退批次。
4. `LyricSyncService` 的切歌、取消、旧结果回写、暂停和偏移。
5. `TrackLyricOffsetStore` 的身份、时长容差和 SQLite 持久化。
6. 设置迁移、损坏文件恢复和原子保存。
7. 本地索引未完成时的查询与索引完成后的重试。
8. Web 消息 DTO 的正反序列化与未知消息处理。

测试应优先于大规模拆类，否则重构歌词匹配和同步逻辑的风险较高。

## 16. 建议实施顺序

### 第一阶段：稳定性和资源生命周期

- [x] 修复设置热应用导致的全量重建。
- [x] 快捷键仅在总开关或绑定实际变化时重新注册，普通外观设置不得触发全量注销与注册。
- [x] 为快捷键命令建立串行执行、取消、异常观察和退出等待策略。
- [x] 给本地索引增加取消和释放。
- [x] 修复 STA 初始化永久等待。
- [x] 观察所有后台任务异常。
- [x] 设置改为原子保存。
- [x] 修复 error log 路由。

### 第二阶段：建立测试保护网

- [x] 新建 `TaskbarLyrics.Core.Tests`。
- [x] 覆盖匹配、解析、路由、同步和偏移。
- [x] 覆盖快捷键解析、规范化、重复检测、注册失败、快速连按和关闭竞态。
- [x] 验证快捷键始终控制歌词识别策略选中的同一 SMTC 会话。
- [x] 把设置契约测试纳入统一验证脚本。
- [x] 增加设置存储和并发生命周期测试。

### 第三阶段：拆分核心类

- [x] 拆分 `MainWindow`：播放会话、歌词同步创建、WebView 脚本、任务栏定位和原生窗口调用已移至专用服务；窗口保留 WPF 生命周期与事件转发。
- [x] 拆分 `SettingsWindow`：WebView 消息解析与字体目录查询已移至专用服务，设置页既有消息字段和交互保持不变。
- [x] 提取 `LocalMediaIndex`：本地歌词与封面共用可取消、引用计数管理的媒体目录索引。
- [x] 提取统一缓存仓储：两个歌词提供者共用 `ILyricCacheStore`，统一内存/磁盘缓存、原子写入、读取失败回退和清理。
- [x] 拆分快捷键定义、绑定解析、Windows 注册器和命令协调器：默认键位、设置键、状态键和注册 ID 均从 `MediaHotkeyCatalog` 派生。
- [x] 通过 `IMediaPlaybackController`、`IPlayerRecognitionController` 等能力接口替代对 `SmtcMusicSessionProvider` 的具体类型判断。
- [x] 把对象创建移动到应用 Composition Root。

本阶段完成窗口宿主的首轮收敛；Settings/Lyrics 的前端脚本拆分、WebView 消息版本化及 C#/JS 对称 DTO 仍归入第四阶段。

### 第四阶段：前端模块化和强类型契约

- [x] 拆分 `settings.js` 和 `app.js`：桥接、快捷键状态、页面状态、单曲偏移以及歌词桥接/状态独立为可加载模块；入口脚本继续保持既有页面初始化职责。
- [x] 版本化 WebView 消息：设置页和歌词页双向使用 V1 `{ version, type, payload }` 信封，不再接受旧格式。
- [x] 建立 C#/JS 对称 DTO：C# 路由器验证版本、类型和 payload，C# 到 JS 的脚本调用统一进入 `receive(envelope)`。
- [x] 快捷键状态使用稳定状态码传输，在前端完成中文展示，不以中文文案作为协议字段。
- [x] 增加 DOM 和消息行为测试：Vitest + jsdom 覆盖桥接、状态映射与设置页导航。

### 第五阶段：清理与规范化

- [ ] 删除遗留设置、死方法和无效参数。
- [ ] 用单一快捷键定义表消除默认键位、动作键、状态键和设置字段的重复映射。
- [ ] 统一命名、目录与格式。
- [ ] 清理历史式和误导性注释。
- [ ] 启用分析器和格式验证。

## 17. 建议的验收原则

每次重构应满足：

- 用户可见行为不发生未经说明的变化。
- 先增加相关测试，再移动或重写核心逻辑。
- 不把 `main` 的 WebView2 UI 逻辑与 Light 分支的 WPF UI 混合。
- 设置页变更必须继续通过 `settings-contract.tests.ps1`。
- 跨线程调用继续通过歌词线程 Dispatcher，不直接访问内部窗口。
- 新增后台任务必须明确 owner、取消、异常观察和退出行为。
- 新增设置必须明确默认值、验证、迁移、持久化和前端契约。
- 新增歌词源应通过提供者注册和统一缓存接口接入，避免继续扩展窗口类。

## 18. 新增快捷键专项复审（2026-07-26）

### 18.1 总体结论

新增快捷键功能已经形成完整的用户链路：配置持久化、按键录制、重复与占用提示、Windows 全局注册、歌词线程调度、SMTC 控制和退出注销均已接通。以下设计值得保留：

- 使用独立隐藏消息窗口承接 `WM_HOTKEY`，资源 owner 明确。
- 注册时启用 `MOD_NOREPEAT`，避免按住按键持续触发。
- 退出时先释放快捷键，再关闭歌词线程。
- 播放控制通过 `LyricsWindowHost` 的 Dispatcher 边界进入歌词线程，没有从主线程直接访问 `_window`。
- 快捷键设置更新后重新推送注册状态，用户能够看到无效、重复或被占用的结果。

没有发现需要立即停用功能的 P0 问题，但有三项建议进入 P1：

1. `App.SaveSettings()` 对任何设置保存都会调用 `GlobalMediaHotkeyService.Apply()`。调整字体、颜色等无关设置也会先注销全部系统快捷键再重新注册，产生不必要的 Windows 资源抖动和短暂失效窗口。
2. `WndProc` 以 fire-and-forget 启动命令，服务和 SMTC 提供者又分别使用空 `catch`。快速连按可能并发执行多个播放命令，退出时也没有取消或等待在途命令，真实失败与程序缺陷均不可观察。
3. 规格要求快捷键解析和会话选择测试，但当前仍没有 .NET 测试项目。字符串扫描式设置契约只能确认标记存在，不能证明解析、注册和并发行为正确。

### 18.2 Clean Code 多维度检查

| 维度 | 当前问题 | 优化方向 | 优先级 |
| --- | --- | --- | --- |
| 命名与函数语义 | `TryControlAsync()` 不返回成功状态；不支持的动作和播放器拒绝命令时也表现为正常完成 | 返回 `MediaCommandResult`，或改名为 `ExecuteAsync` 并在边界明确区分成功、无会话、不支持和失败 | P2 |
| 函数与副作用 | `SaveSettings()` 同时持久化、重配歌词服务并重注册快捷键；快捷键配置未变化也会触发 Windows 调用 | 比较不可变快照，只在 `GlobalMediaHotkeys` 实际变化时调用 `Apply`；继续拆分设置保存与运行时应用 | P1 |
| 类与单一职责 | `MediaHotkeys.cs` 同时包含动作枚举、持久化模型、默认值、解析器、Win32 P/Invoke、注册状态和命令调度 | 拆分为 `MediaHotkeyDefinition`、`HotkeyBinding`、`HotkeyBindingParser`、`IGlobalHotkeyRegistrar` 和协调器 | P2 |
| 消除重复 | 六个动作及其默认值、C# 属性名、Web 设置键和状态键散落在多个 switch、数组、DTO、HTML 与 JS 中 | 建立单一动作定义表；由定义表生成默认值、注册 ID、状态键和 UI 行，或至少集中映射 | P2 |
| 抽象与依赖倒置 | `MainWindow.ExecuteMediaHotkeyAsync()` 通过 `is SmtcMusicSessionProvider` 下转型获得控制能力 | 定义独立 `IMediaPlaybackController`，让窗口依赖能力接口；Win32 注册也通过接口隔离 | P2 |
| 数据与类型设计 | 绑定以任意字符串持久化；解析器接受重复修饰键和空分段，没有规范化后的值对象 | 使用可比较的 `HotkeyBinding` 值对象，集中 Parse/Normalize/Format，保存规范字符串或结构化 DTO | P2 |
| 状态一致性 | 重复键位只把后出现的动作标为重复，先出现的动作仍注册；托盘菜单显示“已配置”键位，即使实际无效或被占用 | 对同一组合的全部动作给出一致冲突状态；托盘只展示已成功注册的有效绑定，或明确标注不可用 | P2 |
| 错误处理与可观察性 | `ExecuteActionSilentlyAsync()` 和 `TryControlAsync()` 均捕获所有异常且不分类；`UnregisterHotKey` 返回值被忽略 | 只静默用户界面，不静默诊断；捕获预期的 WinRT/会话异常，意外异常进入限频日志；记录 Win32 错误码 | P1 |
| 并发与生命周期 | 每次按键创建未保存任务；快速连按可并发读取状态并发出相同命令；`Dispose()` 不等待在途命令 | 使用单消费者队列或 `SemaphoreSlim` 串行化命令；owner 负责停止接收、取消、等待后再释放 HWND | P1 |
| 边界与线程约束 | HwndSource、设置应用和状态读取依赖主 UI 线程，但契约只存在于调用习惯中 | 在服务入口断言 Dispatcher 访问，或由显式 UI-thread owner 封装所有注册与状态操作 | P2 |
| Web 契约 | C# 发送“已注册”“已关闭”等中文字符串，JS 再用中文文本判断 `ready/off/warning` | 传输 `registered/disabled/invalid/duplicate/occupied` 等稳定状态码，中文只属于展示层 | P2 |
| 前端可访问性 | 录制状态主要依赖按钮文字和 CSS；状态变化没有明确的 live-region 语义 | 增加 `aria-pressed`/录制说明，并让注册结果通过合适的 `aria-live` 区域播报 | P3 |
| 可测试性 | 私有静态解析器与 P/Invoke、HwndSource 紧耦合，无法在无桌面资源的单元测试中验证核心规则 | 把纯解析和冲突分析移到无 UI 类型；用 registrar/controller fake 测试协调流程 | P1 |

### 18.3 建议目标结构

```text
GlobalMediaHotkeyCoordinator
├─ MediaHotkeyCatalog             单一动作定义、默认值和稳定键
├─ HotkeyBindingParser            解析、规范化和格式化
├─ IGlobalHotkeyRegistrar         Windows 注册、注销与错误码
├─ IMediaPlaybackController       播放、切歌和跳转能力
└─ MediaHotkeyStatusSnapshot      强类型状态，供设置页和托盘读取
```

设置页只提交动作键与规范绑定；协调器比较新旧快照并执行增量注册；Windows 适配器只处理 HWND 和 P/Invoke；播放器控制器负责选择与歌词一致的会话。这样可以分别测试规则、系统资源和播放器行为，避免继续扩大 `App`、`MainWindow` 与 `SettingsWindow` 的职责。

### 18.4 快捷键专项验收清单

- [ ] 修改非快捷键设置不会产生任何 `RegisterHotKey`/`UnregisterHotKey` 调用。
- [x] 同一组合分配给两个动作时，两个动作都显示冲突且均不注册。
- [ ] 无效、重复、系统占用和关闭状态使用稳定状态码，C#/JS 不依赖中文文本判断逻辑。
- [x] 快速连续触发播放/暂停、切歌和跳转时，命令顺序确定且没有未观察异常。
- [x] 程序退出或歌词线程关闭时，停止接收新命令并有界等待在途命令。
- [ ] 托盘展示的快捷键与实际注册状态一致。
- [x] 解析器覆盖字母、数字、方向键、功能键、重复修饰键、空分段、超长输入和不支持键。
- [ ] 播放源启用状态或识别顺序变化后，快捷键控制的会话与歌词会话一致。
- [x] 设置契约测试继续通过，并增加不依赖 WebView2/Win32 的纯逻辑单元测试。

## 19. 本次审查验证结果

- 2026-07-26（第四阶段）：WebView 协议直接切换为 V1，设置页和歌词页的入站/出站消息统一为 `{ version: 1, type, payload }`；错误版本、空消息和损坏 JSON 会被 C# 路由器安全忽略。
- 新增 `package.json`/锁文件与 Vitest + jsdom 测试。前端测试不属于应用输出目录或发布 ZIP；`scripts/verify.ps1` 会先运行 `npm run test:web`，在本机未安装依赖时执行 `npm ci --ignore-scripts`。
- 当前测试为 App 36 项、Core 19 项、前端 4 项，合计 59 项；设置页 DOM 导航、快捷键稳定状态码和歌词页诊断信封均有自动验证。
- 2026-07-26（第三阶段）：新增 `LocalMediaIndex`、`ILyricCacheStore`、`MediaHotkeyCatalog`、`IMediaPlaybackController`、`AppCompositionRoot`、任务栏定位服务、歌词 WebView 脚本工厂、设置消息路由和字体目录服务。`MainWindow` 不再直接创建歌词/封面提供者或依赖 SMTC 具体类型；`SettingsWindow` 不再自行反序列化 WebView 消息或扫描系统字体。
- 本地歌词与本地封面通过同一份可取消的媒体文件索引消费音频/歌词文件；两个歌词缓存实现收敛为统一的内存与磁盘缓存仓储，写入先落临时文件再替换正式文件，读写失败只降级缓存并记录日志，不影响歌词检索主链路。
- 第三阶段新增应用层测试 8 项（快捷键定义表、重复注册协调、Composition Root、设置消息路由、歌词 WebView 脚本），新增核心层测试 2 项（本地媒体索引、歌词缓存仓储）。当前 `TaskbarLyrics.App.Tests` 为 35 项、`TaskbarLyrics.Core.Tests` 为 19 项，合计 54 项。
- 已通过 `scripts/verify.ps1`、`dotnet test TaskbarLyrics.App.Tests/TaskbarLyrics.App.Tests.csproj --no-restore` 与 `dotnet test TaskbarLyrics.Core.Tests/TaskbarLyrics.Core.Tests.csproj --no-restore`；提交前还会执行全解决方案构建与 `git diff --check`。
- 2026-07-26：新增 `TaskbarLyrics.App.Tests` 与 `TaskbarLyrics.Core.Tests`：前者包含设置变化分类、快捷键命令队列/绑定解析/注册协调、SMTC 会话复用与设置存储共 27 项测试；后者包含歌词 Registry 生命周期、歌词同步取消/旧结果隔离、匹配规则、来源路由、LRC 时间戳解析和显示偏移共 17 项测试。
- `dotnet test TaskbarLyrics.App.Tests/TaskbarLyrics.App.Tests.csproj --no-restore -p:BaseOutputPath=build_verify_app_tests/` 与 `dotnet test TaskbarLyrics.Core.Tests/TaskbarLyrics.Core.Tests.csproj --no-restore -p:BaseOutputPath=build_verify_core_tests/` 均通过：合计 44/44。
- 设置变化按歌词服务、本地媒体、播放器识别、频谱、样式、窗口布局和快捷键分流；普通外观或更新设置不再触发系统快捷键重新注册。
- 快捷键命令改为单消费者队列：按序执行，退出时停止接收、取消在途命令并最多等待 1 秒；意外异常保留在诊断日志中。新增队列顺序与关闭行为单元测试。
- 快捷键绑定解析已从 Win32 注册服务中分离为可独立验证的纯逻辑，覆盖字母、数字、方向键、功能键、空输入、重复修饰键、缺少修饰键、多按键和不支持按键。
- 快捷键注册协调已从窗口句柄与 P/Invoke 中分离：协调器负责状态、重复检测、重注册与注销，Windows 适配器只负责调用系统 API；可在不注册真实系统快捷键的前提下验证系统占用等失败路径。
- SMTC 会话选择会缓存歌词刷新时实际选中的会话实例；快捷键优先控制该实例，只有会话已从系统列表移除时才重新选择，避免多个播放器同时存在时歌词与控制目标不一致。
- 歌词同步测试覆盖播放器 offset 与单曲 offset 的叠加，确认偏移在歌词选行及行内进度计算前生效。
- 歌词同步测试覆盖切歌竞态：上一首搜索晚到返回时，其结果不会回写到已经切换的当前歌曲。
- 新增 `scripts/verify.ps1`，一次执行 App/Core 单元测试与设置页源码契约测试；测试输出固定写入被忽略的 `build_verify_tests` 目录。
- 本地歌词与封面索引改由各自 provider 持有取消令牌；目录或本地歌词设置变化、窗口关闭时会取消旧索引、清空索引与封面缓存，并在索引任务结束后释放令牌资源。
- 歌词 STA 线程启动改为 10 秒有界等待；线程初始化异常会写入日志并反馈给启动方，超时后的迟到线程会自行关闭。
- 歌词 Registry 现在拥有 provider 与并发 gate 的生命周期：销毁时会停止新检索，并等待在途 provider 调用结束后再释放 provider 与并发 gate；同时清理了一段不可达的官方歌词返回分支。
- 设置先写入同目录临时文件并强制刷盘，再以同卷替换更新正式文件；异常保留旧设置并记录错误。`Error` 级别日志已独立写入 `app_error-日期.log`，其余级别继续写入调试日志。
- 引入统一 `TaskObserver`：应用启动、更新检查、激活监听、设置导航及三个 WebView 窗口的异步初始化和脚本调用均会记录意外失败；原先的窗口内 `LogToFile` 已并入统一日志设施。
- 早期审查中，`dotnet build TaskbarLyrics.App/TaskbarLyrics.App.csproj --no-restore -o build_verify_hotkey` 成功：0 警告、0 错误；`settings-contract.tests.ps1` 通过。
- 常规解决方案构建会受正在运行的 `TaskbarLyrics.App` 锁定默认输出目录影响；验证使用独立输出目录完成。
