# Main 与 Light 功能资产对标

本文用于把 main 当前文档和实现，与 TaskbarLyrics Light README 中描述的能力进行对标。对标重点只覆盖三块：歌词检索、歌词显示、设置页。

资料来源：

- main：当前仓库 `README.md`、`doc/` 文档、`TaskbarLyrics.App` 和 `TaskbarLyrics.Core` 实现。
- Light：`https://github.com/sorawithcat/TaskbarLyrics/blob/main/TaskbarLyrics.Light/README.md`，以及 PR #19 工作副本中的 Light 实现。

本文不把 Light 的原生 WPF 技术路线作为 main 必须追赶的目标。main 仍以 WPF + WebView2 的视觉表现方向为主，只评估可复用的产品能力、交互能力和设置项。

## 1. 文档目的与对标口径

对标目标：

- 梳理 main 已具备但 README / 文档未充分体现的能力。
- 找出 Light 已有而 main 缺失的通用功能点。
- 给出 main 可采用的实现方案，便于后续评估需求取舍和追赶进度。

差距类型：

| 类型 | 含义 |
| --- | --- |
| 已实现待补文档 | main 代码已有能力，但 README 或文档没有充分说明。 |
| 缺功能 | Light 已有，main 当前没有完整实现。 |
| 方向不一致暂不追 | 属于 Light 原生 WPF 路线本身，main 不应为了对齐而追赶。 |

优先级：

| 优先级 | 含义 |
| --- | --- |
| P0 | 产品一致性或用户高频痛点，建议优先评估。 |
| P1 | 价值明确，但可排在主线稳定后实现。 |
| P2 | 增强体验或高级配置，可按反馈推进。 |
| 暂缓 | 风险较高、收益不确定，或与 main 方向不一致。 |

## 2. 总览矩阵

| 模块 | main 当前状态 | Light 当前状态 | main 主要差距 | 建议 |
| --- | --- | --- | --- | --- |
| 歌词检索 | Core 能力较完整，已支持多源、翻译、本地歌词、缓存、播放器排序。 | 共享 Core，并额外暴露本地策略、offset、诊断、重新匹配等入口。 | main 缺少一部分策略开关和诊断入口，文档也未完整展示已有能力。 | 先补文档，再实现 offset / rematch / 诊断入口。 |
| 歌词显示 | WebView2 显示能力较强，已有双行歌词、动画、翻译、频谱、封面、外观设置。 | 原生 WPF 显示，功能项更细，封面布局、频谱样式、自动尺寸更多。 | main 缺少部分显示策略和布局配置，但不需要追原生 WPF 实现。 | 在 WebView2 前端扩展显示模式和测量回传。 |
| 设置页 | WebView2 设置页已覆盖主线基础配置。 | 原生 WPF 设置页按功能域重排，数值输入和高级设置更完整。 | main 缺少 Light 的信息架构、部分策略控件和自动安装更新。 | 保留 WebView2，重排信息架构并补关键设置项。 |

## 3. 歌词检索

| 功能点 | Light 状态 | main 实现状态 | main 文档状态 | 差距类型 | main 实现方案 | 建议优先级 |
| --- | --- | --- | --- | --- | --- | --- |
| SMTC 播放源识别 | 支持 QQ 音乐、网易云、酷狗、Spotify 和通用 SMTC。 | 已通过 `SmtcMusicSessionProvider` 和 Core provider 支持。 | README 有说明，但没有展开识别顺序和兜底策略。 | 已实现待补文档 | README 增加“播放源识别与优先级”小节，说明播放器开关、排序和通用 SMTC 兜底。 | P0 |
| 播放源启用/关闭 | 设置页支持各播放器开关。 | 已有 `EnableQQMusic`、`EnableNetease`、`EnableKugou`、`EnableSpotify`。 | README 只列支持播放器。 | 已实现待补文档 | 在 README 和资产文档中补充每个播放器可单独启用/禁用。 | P1 |
| 播放源识别优先级 | 支持拖拽排序。 | 已有 `SourceRecognitionOrder`，设置页有排序 UI。 | README 未说明。 | 已实现待补文档 | 补充“多播放器同时运行时可调整识别顺序”。 | P1 |
| 在线歌词源 | 共享 Core，覆盖 QQ / 网易云 / 酷狗 / Spotify / LRCLIB。 | Core 已有 Lyricify 与 LRCLIB 相关 provider。 | README 有部分说明。 | 已实现待补文档 | README 中把“播放源”和“歌词源”分开描述，避免用户以为只支持播放器对应源。 | P1 |
| 本地歌词 `.lrc/.qrc/.krc` | 支持。 | 已有 `LocalLyricProvider`，支持本地歌词和内嵌歌词读取。 | README 已说明本地目录，但可以更明确格式。 | 已实现待补文档 | 补充支持格式、匹配方式和缓存目录。 | P1 |
| 本地歌词策略 | 支持“本地优先 / 在线失败后使用”。 | main 目前是启用本地 provider 后参与 provider 组合，缺少明确策略枚举。 | README 未说明策略。 | 缺功能 | 新增 `LocalLyricsSearchMode` 到 `AppSettings`；provider 组合层按模式决定本地 provider 是前置还是在线失败后兜底；设置页增加策略下拉。 | P1 |
| 歌词翻译解析 | 歌词源提供翻译时可显示。 | Core `LyricLine` 有 Translation，QQ / 网易云 / 酷狗解析已写入翻译。 | README 功能列表未突出。 | 已实现待补文档 | README 增加“歌词翻译显示”说明，并注明需要歌词源返回译文。 | P0 |
| 歌词翻译显示开关 | 支持。 | main 已有 `ShowLyricTranslation` 和设置页开关。 | README 未体现。 | 已实现待补文档 | 补文档即可。 | P0 |
| 全局歌词偏移 | 支持。 | main 当前未见全局 offset 设置。 | README 未体现。 | 缺功能 | 新增 `LyricOffsetMs`；在播放快照进入显示层前调整 frame position 或 line timestamp；设置页加入 stepper，范围建议 `-5000~5000ms`，步进 `50ms`。 | P0 |
| 分播放器歌词偏移 | 支持 QQ / 网易云 / 酷狗 / Spotify 独立 offset。 | main 当前未见分播放器 offset 设置。 | README 未体现。 | 缺功能 | 新增 `QqMusicLyricOffsetMs`、`NeteaseLyricOffsetMs`、`KugouLyricOffsetMs`、`SpotifyLyricOffsetMs`；按 `TrackInfo.SourceApp` 叠加全局 offset。 | P1 |
| 歌词缓存 | 支持缓存和清理。 | Core 有 provider 缓存，设置页有清除缓存按钮。 | README 提到缓存，但清理入口不突出。 | 已实现待补文档 | README 补充设置页可清理缓存，说明首次重新检索会稍慢。 | P1 |
| 重新匹配 | 托盘/设置可重新匹配当前歌词。 | main 当前未见明确 rematch 入口。 | README 未体现。 | 缺功能 | 在 `LyricsWindowHost` / `MainWindow` 增加 `RematchCurrentLyrics`；清除当前 track 的 provider 缓存或重置当前匹配状态后触发重新 resolve；托盘和设置页增加入口。 | P1 |
| 歌词命中诊断 | 提供诊断快照和入口。 | main 有 SMTC 时间轴监视器和日志，但缺少面向歌词命中的诊断视图。 | README 未体现。 | 缺功能 | 新增 `LyricResolveDiagnosticsSnapshot`，记录当前 track、命中 provider、候选、相似度、是否缓存、offset；设置页或诊断窗口展示。 | P2 |
| SMTC 时间轴稳定器 | README 标注减少时间轴跳变。 | main 已有 timeline strategy / diagnostics 相关类，具备时间轴处理能力。 | README 未展开。 | 已实现待补文档 | 文档中补充时间轴策略和 SMTC 监视器用途。 | P1 |

## 4. 歌词显示

| 功能点 | Light 状态 | main 实现状态 | main 文档状态 | 差距类型 | main 实现方案 | 建议优先级 |
| --- | --- | --- | --- | --- | --- | --- |
| 双行歌词显示 | 支持。 | main WebView 歌词端已支持双行歌词。 | README 已说明。 | 已实现待补文档 | README 可补充双行切换动画与翻译显示的关系。 | P2 |
| 歌词切换动画 | 支持上滑、淡入淡出、紧凑滑动、无动画。 | main Web 前端已有平滑切换动画，但没有用户可选样式。 | README 仅写平滑动画。 | 缺功能 | 新增 `LyricTransitionStyle` 设置；通过 bridge 下发到 `Web/Lyrics/app.js`；CSS/JS 分别实现 slide、fade、compactSlide、none。 | P2 |
| 歌词翻译显示 | 支持。 | main 已支持。 | README 未突出。 | 已实现待补文档 | 在功能列表增加“歌词翻译显示”。 | P0 |
| 纯音乐频谱 | 支持。 | main 已有 `EnablePureMusicSpectrum` 与频谱渲染。 | README 已说明。 | 已实现待补文档 | 保持。 | P2 |
| 未检索到歌词时显示频谱 | 支持，默认开启。 | main 已有 `ShowSpectrumWhenLyricsNotFound`，默认当前为关闭。 | README 未说明设置项。 | 已实现待补文档 | README 补充该开关；是否改默认值另行评估。 | P1 |
| 有歌词时也显示频谱 | 支持。 | main 当前未见该设置。 | README 未体现。 | 缺功能 | 新增 `ShowSpectrumWhenLyricsAvailable`；在显示决策中允许歌词可用时进入频谱模式，或提供“歌词+频谱叠加/替代”二选一。建议先实现“替代显示”，减少布局复杂度。 | P2 |
| 频谱多样式 | 支持中心、底部、镜像、细线、点阵、呼吸条。 | main 当前频谱样式较单一。 | README 未体现。 | 缺功能 | 新增 `SpectrumDisplayStyle`；Web 前端根据 style 切换 bar transform、height origin、dot rendering、pulse opacity；托盘可加“切换频谱样式”。 | P2 |
| 频谱调参 | 支持。 | main 已有 `SpectrumTuningWindow` 和设置页入口。 | README 未充分体现。 | 已实现待补文档 | README 补充“频谱调参窗口”。 | P1 |
| SMTC 封面显示 | 支持异步读取和淡入。 | main Web 歌词端已有封面处理、本地封面 provider 和交叉淡入逻辑。 | README 未突出封面能力。 | 已实现待补文档 | README 增加“封面显示、本地封面回退”。 | P0 |
| 本地封面 | 支持同目录图片和内嵌封面。 | main 已有 `LocalMediaCoverProvider`。 | README 未体现。 | 已实现待补文档 | 补充本地封面匹配来源。 | P0 |
| 本地/在线封面策略 | 支持在线优先、本地优先、仅在线、仅本地。 | main 当前缺少用户可选策略。 | README 未体现。 | 缺功能 | 新增 `LocalCoverSearchMode`；在封面决策中先按策略查 SMTC 或本地 provider；设置页增加下拉。 | P1 |
| 封面显示开关 | 支持。 | main 当前未见独立开关。 | README 未体现。 | 缺功能 | 新增 `ShowCoverImage`；Web 前端隐藏 cover 容器并重新测量歌词区域。 | P1 |
| 封面样式 | 支持方形、圆角、圆形、隐藏。 | main 当前样式固定。 | README 未体现。 | 缺功能 | 新增 `CoverDisplayStyle`；通过 CSS class 控制 border-radius、尺寸和隐藏状态。 | P2 |
| 封面布局 | 支持横向和上下布局，含歌名/歌手信息。 | main 当前以 Web 端固定布局为主。 | README 未体现。 | 缺功能 | 新增 `CoverLayoutMode`；Web 前端增加 inline / stacked 布局 class；stacked 模式展示 cover、track info、歌词/频谱，并把测量结果回传 host。 | P2 |
| 封面主色取色 | 支持给歌词和频谱取色。 | main 当前未见封面主色驱动前景色。 | README 未体现。 | 缺功能 | 在 host 侧读取封面 bytes 后提取 accent color，或在 Web 端用 canvas 采样；新增 `UseCoverAccentColor`，生成前景色和频谱色变量。 | P2 |
| 背景显示/不透明度 | 支持。 | main 已有 `ShowBackground` 和 `BackgroundOpacity`。 | README 未展开。 | 已实现待补文档 | 补充外观设置能力。 | P2 |
| 背景材质 | 支持 Dim / CoverTint / Solid。 | main 当前未见材质枚举。 | README 未体现。 | 缺功能 | 新增 `LyricsBackgroundMaterial`；Web 端根据材质切换透明黑、封面主色 tint、自定义纯色。 | P2 |
| 边框/文字阴影 | 支持。 | main 已有开关。 | README 未展开。 | 已实现待补文档 | 补文档即可。 | P2 |
| 字体/字号/字重/颜色 | 支持。 | main 已有设置项和字体列表。 | README 未展开。 | 已实现待补文档 | README 增加“字体和外观可配置”。 | P1 |
| 窗口宽度、锚点、X/Y 偏移 | 支持。 | main 已有。 | README 未展开。 | 已实现待补文档 | README 增加“窗口布局可配置”。 | P1 |
| 自动窗口宽度 | 支持，纳入歌词和封面布局计算。 | main 当前以手动宽度为主，缺少自动宽度设置。 | README 未体现。 | 缺功能 | Web 歌词端测量当前歌词、翻译、封面宽度，通过 host object 回传 desired width；host 侧加 `AutoAdjustWindowWidth` 和 `WindowWidthOffset`。 | P1 |
| 自动/手动窗口高度 | 支持。 | main 当前未见窗口高度设置。 | README 未体现。 | 缺功能 | 新增 `WindowHeight`、`AutoAdjustWindowHeight`、`WindowHeightOffset`；Web 端回传内容高度，host 侧保持底部锚点不漂移。 | P1 |
| 原生 WPF 歌词控件 | Light 核心实现。 | main 采用 WebView2。 | main 文档说明依赖 WebView2。 | 方向不一致暂不追 | 不追 `LyricsDisplayControl`；只把可复用能力迁移到 Web 前端和 host 设置层。 | 暂缓 |

## 5. 设置页

| 功能点 | Light 状态 | main 实现状态 | main 文档状态 | 差距类型 | main 实现方案 | 建议优先级 |
| --- | --- | --- | --- | --- | --- | --- |
| 设置页技术路线 | 原生 WPF。 | WebView2 HTML/CSS/JS。 | README 有 WebView2 Runtime 要求。 | 方向不一致暂不追 | main 保持 WebView2 设置页，继续服务视觉方向。 | 暂缓 |
| 播放器开关 | 支持。 | main 已有。 | README 未展开。 | 已实现待补文档 | 补充设置页支持播放器启用/禁用。 | P1 |
| 播放器排序 | 支持拖拽。 | main 已有 `SourceRecognitionOrder` 和排序 UI。 | README 未展开。 | 已实现待补文档 | 补充设置页支持识别优先级调整。 | P1 |
| 本地目录配置 | 支持。 | main 已有。 | README 已简要说明。 | 已实现待补文档 | 补充格式、每行一个目录、支持同名歌词。 | P1 |
| 启动显示 | 支持。 | main 已有 `ShowLyricsOnStartup`。 | README 未展开。 | 已实现待补文档 | 补充设置项。 | P2 |
| 开机自启动 | 支持，Light 独立注册表项。 | main 已有 `StartWithWindows` 和 startup service。 | README 未展开。 | 已实现待补文档 | 补充开机自启动说明。 | P1 |
| 播放器打开/关闭联动 | 支持自动显示/隐藏。 | main 当前未见 `PlayerPresenceMonitor` 等联动设置。 | README 未体现。 | 缺功能 | 新增 `AutoShowLyricsWhenPlayerOpens`、`AutoHideLyricsWhenPlayerCloses`；以 SMTC 活跃会话为信号实现 player presence monitor；启动阶段若开启自动隐藏则不闪出歌词窗。 | P1 |
| 显示歌词翻译 | 支持。 | main 已有。 | README 未体现。 | 已实现待补文档 | 补文档。 | P0 |
| 数值输入统一 stepper | 支持直接输入和 `+/-` 步进。 | main 设置页已有部分 stepper，但 Light 更统一。 | 文档未体现。 | 缺功能 | 统一 Web 设置页 stepper 组件，覆盖 offset、窗口尺寸、透明度、字体大小等数值项。 | P2 |
| 设置页信息架构 | 按播放源、歌词与本地库、显示与外观、窗口布局、维护调试、关于更新分区。 | main 当前按 Web 设置页布局组织，信息密度和分区可继续优化。 | 视觉规范已有方向，但不含功能域对标。 | 缺功能 | 保留 WebView2，按 Light 的功能域重排 main 设置页；新增左侧导航或分组锚点，减少高级项混杂。 | P1 |
| SMTC 时间轴监视器 | 支持。 | main 已有设置项和窗口。 | README 未展开。 | 已实现待补文档 | 补充用于排查同步问题。 | P1 |
| 频谱调参 | 支持。 | main 已有设置页入口。 | README 未展开。 | 已实现待补文档 | 补充用于实时调整频谱响应。 | P1 |
| 歌词缓存清理 | 支持。 | main 设置页已有清除缓存按钮。 | README 未展开。 | 已实现待补文档 | 补充缓存清理说明。 | P1 |
| 重新匹配入口 | 托盘/设置提供。 | main 未见完整入口。 | README 未体现。 | 缺功能 | 在设置页维护调试区和托盘菜单增加“重新匹配当前歌曲”；调用 rematch 流程。 | P1 |
| 更新检查 | 支持 GitHub API 和 fallback。 | main 已有检查更新与通知。 | README 未展开。 | 已实现待补文档 | README 补充自动检查更新每天一次、手动检查入口。 | P1 |
| 更新下载并覆盖安装 | 支持下载 zip、解压、覆盖、重启。 | main 当前未见安装器。 | README 未体现。 | 缺功能 | 高风险功能。先只做 Release 跳转或下载提示；若实现自动安装，必须限制发布源、资产命名、包内结构，并考虑 checksum / 签名。 | 暂缓 |
| 关于页仓库入口 | 支持。 | main 设置页有关于/更新相关能力。 | README 未展开。 | 已实现待补文档 | 补充关于页能力。 | P2 |

## 6. main 缺失功能追赶清单

| 功能点 | 模块 | main 实现方向 | 风险 | 建议优先级 |
| --- | --- | --- | --- | --- |
| 全局歌词偏移 | 歌词检索 | 新增 `LyricOffsetMs`，显示前统一叠加 offset。 | 低，需验证正负偏移边界。 | P0 |
| 分播放器歌词偏移 | 歌词检索 | 按 `TrackInfo.SourceApp` 叠加播放器 offset。 | 中，需统一播放器 source key。 | P1 |
| 本地歌词策略选择 | 歌词检索 | 新增 `LocalLyricsSearchMode`，控制本地 provider 前置或兜底。 | 中，影响 provider 顺序和超时策略。 | P1 |
| 重新匹配当前歌词 | 歌词检索 / 设置页 | 清理当前匹配状态并触发重新 resolve。 | 中，需避免全量清缓存造成误伤。 | P1 |
| 歌词命中诊断 | 歌词检索 / 设置页 | 新增诊断快照和诊断窗口/设置页入口。 | 中，需避免泄露过多内部日志给普通用户。 | P2 |
| 播放器打开/关闭联动 | 设置页 / App 生命周期 | 监听 SMTC 活跃会话，自动 show/hide。 | 中，需处理用户手动隐藏后的优先级。 | P1 |
| 有歌词时显示频谱 | 歌词显示 | 新增设置并在 Web 前端允许歌词可用时进入频谱显示。 | 低到中，主要是交互语义。 | P2 |
| 频谱多样式 | 歌词显示 | Web 前端基于 style 切换 DOM/CSS 渲染。 | 中，需保证性能和视觉质量。 | P2 |
| 封面显示开关和样式 | 歌词显示 | 新增 cover style 设置，CSS 控制圆角、圆形、隐藏。 | 低。 | P1 |
| 本地/在线封面策略 | 歌词显示 | 新增 `LocalCoverSearchMode`，封面读取按策略选择。 | 中，需处理 SMTC 封面延迟和本地回退。 | P1 |
| 封面布局和歌名歌手信息 | 歌词显示 | Web 前端增加 inline / stacked 布局，host 做尺寸调整。 | 中到高，涉及自动尺寸和动画裁剪。 | P2 |
| 封面主色取色 | 歌词显示 | 从封面 bytes 或 Web canvas 提取 accent，驱动 CSS 变量。 | 中，需控制颜色可读性。 | P2 |
| 自动窗口宽度 | 歌词显示 | Web 测量内容宽度并回传 host。 | 中，需防抖和宽度上下限。 | P1 |
| 自动/手动窗口高度 | 歌词显示 | Web 测量高度，host 保持底部锚点。 | 中到高，需避免布局跳动。 | P1 |
| 歌词切换动画样式 | 歌词显示 | 新增 transition style，Web 前端切换动画实现。 | 低到中。 | P2 |
| 背景材质选择 | 歌词显示 | 新增 material，Web CSS 切换 dim / cover tint / solid。 | 低到中。 | P2 |
| 设置页信息架构重排 | 设置页 | 保留 WebView2，按功能域重排。 | 中，需避免隐藏现有设置。 | P1 |
| 数值输入统一 stepper | 设置页 | 抽象 Web stepper，统一数值项输入体验。 | 低。 | P2 |
| 自动下载并覆盖安装 | 设置页 / 发布 | 实现 installer 前先限定发布源、资产命名、包结构、校验。 | 高，发布链路敏感。 | 暂缓 |

## 7. 建议优先级

P0 建议先做：

- 补充 README / 文档中 main 已实现但未说明的能力：歌词翻译、本地封面、本地歌词、播放器排序、频谱调参、缓存清理、更新检查。
- 实现全局歌词偏移。该能力用户价值明确，改动范围相对可控。

P1 建议作为下一阶段：

- 分播放器歌词偏移。
- 本地歌词策略选择。
- 重新匹配当前歌词。
- 播放器打开/关闭联动。
- 本地/在线封面策略。
- 自动窗口宽度和高度。
- 设置页按功能域重排。

P2 可按用户反馈推进：

- 歌词命中诊断。
- 有歌词时显示频谱。
- 频谱多样式。
- 封面样式、封面布局、封面主色取色。
- 动画样式选择。
- 背景材质选择。
- 数值输入统一 stepper。

暂缓：

- 把 main 歌词窗或设置页改成原生 WPF。
- 自动下载并覆盖安装更新包。该能力涉及发布链路和本地执行信任边界，除非先明确发布源、资产命名、包结构、checksum / 签名和回滚策略，否则不建议优先追。
