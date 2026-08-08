# 歌词 Provider 全链路解耦与 Lyricify 0.2.0 接管实施计划

## Plan Status

- 状态：Ready for Implementation
- 规范基线：`docs/歌词Provider全链路解耦与Lyricify0.2.0接管-spec.md`
- Spec 状态：Accepted for Planning
- 当前主交付：Phase A — Core 歌词链路、四源接管、可信选择、Provider 原生逐词数据保真
- 后续交付：Phase B — 逐词扫描；Phase C — 翻译/音译多轨与翻译增强
- 初始在线源可信顺序：`QQMusic > Kugou > Netease > LRCLIB`

## Outcome

Phase A 完成后，TaskbarLyrics 应当拥有一条由 Core 掌握节点契约的歌词链路。QQ 音乐、酷狗音乐、网易云音乐和 LRCLIB 使用官方 Lyricify 0.2.0 已支持的平台 API、解密器和显式格式解析器，但 Lyricify 类型不进入匹配、选择、缓存、播放或 App 边界。

选源由 Core 单点负责：先保护人工映射和本地歌词，再对在线候选执行严格歌曲身份准入，最后按固定可信顺序选择。播放器官方源和 SongId 只优化查询，不改变跨源顺序。

Phase A 只确保 QRC、KRC 和 YRC 的 Provider 原生逐词时间完整进入 Core 模型和缓存。它不实现 WebView 逐词扫描，不生成合成逐词时间，也不开始新的翻译多轨或外部翻译开发。

## Fixed Product Decisions

1. 在线可信顺序固定为 `QQMusic > Kugou > Netease > LRCLIB`。
2. 可信顺序由可测试注入的 Core 策略统一维护，Phase A 不增加用户排序设置。
3. 有效 Local 歌词直接结束在线查询。
4. 人工 `PreferredProvider` 是硬绑定；该源失败时不回退到其他在线源。
5. Phase A 完成 Core 逐词数据能力，Phase B 才实现播放与 WebView 逐词扫描。
6. Phase A 和初始 Phase B 都不生成或渲染合成逐字时间。
7. Phase A 保持已有内嵌翻译显示；新的多轨、音译、外部翻译和翻译缓存属于 Phase C。
8. 只有解析器或 Provider 给出明确信息行标记时才在正常播放中隐藏；语义内容和诊断仍保留该行。

## Development Rules

- 每次开始新阶段前必须重读根 `AGENTS.md`、Clean Code skill、Accepted Spec 和本 Plan。
- 存在 `.codegraph/` 时，理解代码和定位调用链必须先使用 CodeGraph。
- 每个阶段开始前运行 `git status --short`，保留用户未跟踪的差异报告、Spec 和其他无关更改。
- 同一时间只迁移一个主要行为边界。不将大规模文件移动、格式化与行为改造混在同一阶段。
- 新旧路径并存时，必须明确唯一生产选择权；不允许 Registry 和 `LyricSyncService` 同时再次按分数选择。
- 不为所有管线箭头创建空转接口。只在多实现、测试隔离、第三方边界或真实复用需要时建立 seam。
- 不将 Lyricify 可变对象存入 TaskbarLyrics 缓存，也不把这些对象传递到 App 或 WebView。
- 不删除旧缓存文件或用户数据。新语义通过新版本和新 key 自然隔离。
- 每个完整、可独立验证的工程阶段在验证后更新一次 `docs/工程变更记录.md`，不记录尚未完成的中间过程。
- 任何影响可运行应用的阶段在完整验证后都必须运行 `scripts/restart-app.ps1`，使应用处于可立即手工验证状态。

## Current Code Map

| Responsibility | Current owner | Migration concern |
| --- | --- | --- |
| Provider contract | `TaskbarLyrics.Core/Abstractions.ILyricProvider.cs` | 直接返回最终 `LyricDocument`，节点不可见 |
| Shared Provider cache/parse | `Services.LyricProviderBase.cs` | 缓存已解析文档；`ProcessDocument` 会改写 syllable |
| QQ/Netease/Kugou | `Services.LyricifyLyricProvider.cs` | 搜索、匹配、获取、解密、解析、翻译对齐集中在单类 |
| LRCLIB | `Services.LrcLibSmtcLyricProviderBase.cs`, `Services.GenericSmtcLyricProvider.cs` | 自成搜索、解析和缓存体系 |
| Matching | `Utilities/LyricMatcher.cs` | 身份分数与跨源质量权重混用 |
| Routing/selection | `Services.LyricProviderRegistry.cs`, `LyricSourceRoutingPolicy.cs`, `LyricMatchingPolicy.cs` | 官方源独占、回退权重、批次选择 |
| Content model | `Models.LyricDocument.cs`, `Models.LyricLine.cs`, `Models.LyricSyllable.cs` | `BestScore` 与内容耦合，timing provenance 缺失 |
| Final second selection | `Services.LyricSyncService.cs` | Registry 返回多结果后再按 `BestScore` 选一次 |
| Playback frame | `Models.LyricDisplayFrame.cs`, `LyricSyncService.GetDisplayFrameAsync` | Phase A 保持行级兼容，Phase B 才扩展逐词 |
| Composition | `TaskbarLyrics.App/AppCompositionRoot.cs` | 最终只在这里组装 Core 管线和四源适配器 |
| Existing tests | `TaskbarLyrics.Core.Tests/*Lyric*.cs` | 先建特征测试，再替换行为 |

## Target Boundary Set

文件名可按仓库惯例调整，但责任不可重新合并。

| Boundary | Required responsibility |
| --- | --- |
| `TrackIdentity` | 不可变原始曲目身份，保留 SourceApp、SongId 和版本标记 |
| `LyricSearchPlan` / `SearchQueryVariant` | 统一生成有序查询变体，不替换原始身份 |
| `SourceTrackCandidate` | 隔离 Provider 搜索 DTO，保留稳定候选 ID 和查询变体来源 |
| `LyricCandidateEvaluation` | 单独表达身份准入分数和拒绝原因 |
| `RawLyricPayload` | 原文载荷、已有内嵌翻译兼容数据、格式、加密状态、纯音乐和诊断 |
| `ParsedLyrics` | 行、Provider 原生 syllable、明确信息行标记、timing kind/provenance 与翻译兼容数据 |
| `ResolvedLyrics` | 单一已选内容、Provider/候选来源、获取方式和诊断 |
| Source seam | `SearchAsync(plan/query)` 和 `FetchAsync(candidate)`，不返回最终歌词 |
| Decode seam | 只处理加密/编码边界 |
| Parse seam | 接收明确格式，输出 Core 模型 |
| Trust policy | 统一稳定 Provider ID 和可信顺序 |
| Resolution coordinator | 映射、Local、并发调度、超时、终态、可信选择的唯一所有者 |

## Phase Overview

| Phase | Delivery | Status | Can start when |
| --- | --- | --- | --- |
| A0 | 基线、特征测试与 fixture | Completed (2026-08-08: baseline verify passed; Core 29/29) | Spec Accepted |
| A1 | Core 边界模型与可替换 seam | Completed (2026-08-08: Core 36/36; zero-warning Core build) | A0 passed |
| A2 | Lyricify 0.2.0 升级、显式解密/解析适配 | Completed (2026-08-08: explicit parser fixtures; 0.2.0 build) | A1 passed |
| A3 | 查询规划与身份准入解耦 | Completed (2026-08-08: identity/query tests; Core 56/56) | A1 passed |
| A4 | 四个在线源节点化迁移 | Completed | Four adapter boundary tests passed; no live network dependency |
| A5 | 原始载荷与解析结果分层缓存 | Completed | Layered cache tests passed; stable identity and version gates enforced |
| A6 | 可信顺序协调器和单一选择权 | Completed | Deterministic trust-order, cancellation, mapping and lifecycle tests passed |
| A7 | 信息行、兼容投影和旧路径收敛 | Completed | New pipeline is the only production path; legacy providers and selection weights removed |
| A8 | Phase A 全量验证和手工验收 | In Progress | Automated verification passed; real-player manual matrix pending |
| B | 逐词播放投影与 WebView 扫描 | Deferred | A8 accepted |
| C | 翻译/音译多轨与翻译增强 | Deferred | B accepted and translation addendum accepted |

## Phase A0 — Baseline and Characterization

### Objective

在任何生产改造前固定当前可见行为、数据兼容红线和测试样本，使后续每个迁移阶段可归因。

### Work

- 运行并记录基线 `scripts/verify.ps1`、`dotnet build TaskbarLyrics.sln` 和 `git diff --check`。
- 为人工映射、纯音乐、Local 短路、`PreferredProvider` 硬绑定、无稳定 ID 不持久化和旧缓存无效删除增加特征测试。
- 为当前内嵌翻译显示与行级播放建立回归测试，防止 Phase A 意外删除现有功能。
- 在 `TaskbarLyrics.Core.Tests/TestData/Lyrics/` 建立合成、无版权风险的 LRC/QRC/KRC/YRC/TTML fixture。
- fixture 覆盖 offset、多时间标签、非单调输入、空行、零时长 syllable、明确信息行标记和已有翻译兼容样本。
- 为 QQ、酷狗、网易云、LRCLIB 建立合成搜索/载荷响应样本，标明稳定 ID 和格式。

### Verification

- 现有测试和新特征测试通过。
- 测试不访问真实 Provider 网络。
- 未改变生产选源行为或缓存版本。

### Rollback Boundary

只新增测试和 fixture。如果特征测试暴露当前缺陷，先将缺陷记录为旧行为，不在 A0 中顺带修复。

## Phase A1 — Core Contracts and Invariants

### Objective

建立新链路的 Core 自有边界和不变式，但不切换生产 Provider 路由。

### Work

- 引入稳定 Provider ID 的单一定义，至少包含 `Local`、`QQMusic`、`Kugou`、`Netease`、`LRCLIB`。
- 引入 `TrackIdentity`、`LyricSearchPlan`、`SearchQueryVariant`、`SourceTrackCandidate`、`RawLyricPayload`、`ParsedLyrics`、`LyricCandidateEvaluation`和 `ResolvedLyrics`。
- 引入 timing kind 与 timing provenance；当前至少支持 `Unsynced`、`LineTimed`、`WordTimed`、`CharacterTimed`、`Mixed` 以及 `ProviderSupplied`、`Synthetic`、`Unknown`。
- 将非负时间、有效文本、行排序、syllable 边界和 Provider ID 约束放在 Core 类型或纯规范化服务中。
- 建立 source search/fetch、decode 和 parse 的最小可替换 seam。
- 建立从新 `ResolvedLyrics` 到旧 `LyricDocument` 的临时兼容投影，使 `LyricSyncService` 在 Phase A 后期前无需同步大改。
- 不在 A1 实现翻译多轨或 WebView 逐词协议。

### Tests

- 无效时间、空文本、重复时间、混合粒度和 provenance 保真。
- `LyricCandidateEvaluation` 不进入歌词内容模型。
- 一个测试用的第五源可以实现自定义 source 或 parser，不引用 Lyricify。
- 兼容投影保持当前行文本、syllable、翻译字段和纯音乐语义。

### Exit Gate

- Core 项目不引入任何 App/WebView 依赖。
- 新边界可在不运行真实 HTTP 的情况下完整测试。
- 生产仍走旧 Provider 路径，可一键回滚 A1 新类型而不触及用户数据。

## Phase A2 — Lyricify 0.2.0 Decode and Parse Boundary

### Objective

将官方依赖升级到 0.2.0，并用明确格式调用建立受控的解密/解析适配器。

### Work

- 将 `TaskbarLyrics.Core.csproj` 的 `Lyricify.Lyrics.Helper` 从 0.1.4 升级到官方 0.2.0。
- 编译并盘点 API 变化，不通过动态调用或分析器抑制隐藏不兼容。
- 建立 `LyricifyPayloadDecoder` 和 `LyricifyPayloadParser` 或等价边界。
- parser 必须显式调用 `LrcParser`、`QrcParser`、`KrcParser`、`YrcParser`、`TtmlParser`；不使用 `TypeHelper.GetLyricsTypes` 或 `ParseHelper.ParseLyrics(string)` 作为生产入口。
- 将 Lyricify `LyricsData`/line/syllable 立即转换为 Core 模型，并记录 `ProviderSupplied` provenance。
- 删除或绕开会覆盖原生 syllable 的通用 `EnsureSyllables` 路径；Phase A 不生成 synthetic timing。
- 只保留现有内嵌翻译所需的兼容转换，不引入新音译或多轨模型。

### Tests

- QRC/KRC/YRC 的 syllable 文本、绝对起止时间、行起止时间和 provenance 精确断言。
- LRC offset、多时间标签、纯文本回退和非法输入。
- TTML 作为解析边界回归 fixture，但不在 Phase A 注册新在线源。
- 解析结果不暴露任何 Lyricify 类型。
- 旧内嵌翻译兼容样本仍能投影到当前 `LyricLine.Translation`。

### Exit Gate

- 解析测试只依赖 fixture，无真实网络。
- 0.2.0 升级后方案构建零警告。
- 生产源尚未切换时，可单独回滚 package 和适配层。

## Phase A3 — Query Planning and Identity Admission

### Objective

将查询变体和歌曲身份准入从 Provider 中提取为纯 Core 策略。

### Work

- 从原始 `TrackIdentity` 生成 exact、标点规范化、多艺人、主艺人、简繁/变音符号和 relaxed 查询变体。
- 每个变体保留稳定 ID 和放宽原因，不将 relaxed 字符串用作持久化 key。
- 复用并改造 `LyricMatcher` 为候选身份准入服务，保留现有版本冲突、时长、标题和艺人防护。
- 将 score、是否准入和拒绝原因放入 `LyricCandidateEvaluation`，不放入 `ParsedLyrics`。
- 定义 SongId 命名空间：只有 SourceApp 对应的 QQ/Netease/Kugou 适配器可直达，不得跨源复用。

### Tests

- feat、多艺人、括号后缀、简繁、变音符号、Live/Remix/Acoustic/Instrumental 冲突。
- relaxed 变体能提高召回，但最终仍对原始身份执行版本冲突。
- 匹配分高于准入线时只表示通过，不导出跨源排名。
- 错误 Provider 不使用其他平台 SongId。

### Exit Gate

查询和匹配策略无 HTTP、无 Lyricify 及无 UI 依赖，可用纯单元测试完整证明。

## Phase A4 — Four-Source Migration

每个子阶段单独验证，不在一次改动中同时迁移四源。在 A6 切换最终协调器前，可使用临时兼容投影让新节点结果返回现有 Registry。

### A4.1 QQ Music

- 调用 QQ 底层搜索 API，返回完整候选集，不使用 `SearchHelper.Search` 的单候选预选。
- 对 QQ SourceApp + SongId 保留直达查询。
- 使用 `GetLyricsAsync`，必要时使用 `GetLyric` 回退。
- 显式优先 QRC，LRC 仅作回退。
- 保留 Provider 原生 syllable 和已有内嵌翻译显示。
- 验证多候选、ID/Mid 回退、空歌词、QRC 失败后 LRC 和取消后迟到结果。

### A4.2 Kugou

- 使用歌曲搜索和歌词候选搜索两个独立节点。
- 使用 Helper KRC 获取/解密和 `KrcParser`。
- 禁止将 KRC 重建为普通 LRC；原生 syllable 直接转换为 Core 模型。
- 保留已有内嵌翻译显示，不扩展新翻译轨。
- 验证空歌词候选重试、第一候选无歌词时继续、解密失败、syllable 保真和并发门。

### A4.3 Netease

- 使用 `SearchNew` 作为主搜索路径，只保留有 fixture 证明必要的备用接口。
- 对 Netease SourceApp + SongId 保留直达查询。
- 获取 YRC/LRC 及现有翻译兼容数据，显式优先 YRC，LRC 作回退。
- 使用 `YrcParser` 保留 Provider 原生 syllable。
- Romalrc/Yromalrc 音译处理不在 A4.3 实现。
- 验证 YRC 有效、YRC 缺失回退 LRC、SongId 命名空间、翻译兼容和 API 失败。

### A4.4 LRCLIB

- 使用底层结构化 `LRCLIBApi.Search`/`GetById`，不使用当前 `LRCLIBSearcher` 的字符串对半拆分。
- 使用中央 `LyricSearchPlan` 的 title/artist/album/duration 字段。
- synced lyrics 显式使用 LRC parser；plain lyrics 作 unsynced 回退；instrumental 转为纯音乐结果。
- 将 `LrcLibSmtcLyricProviderBase` 中可复用的身份逻辑迁到中央策略，不复制其自成体系的解析和缓存。
- 验证结构化查询、多候选、stable ID 获取、synced/plain 回退和 instrumental。

### A4 Exit Gate

- 四源都输出相同 Core 候选和载荷契约。
- 每源有独立适配测试，标准测试不依赖真实网络。
- 临时兼容投影不修改当前 WebView 输入。
- 回滚可按单一源恢复旧适配器，不要求回滚其他已迁移源。

## Phase A5 — Layered Caching

### Objective

将原始 Provider 载荷与 Core 解析结果的生命周期分开，防止 parser 升级被旧已解析缓存屏蔽。

### Work

- 定义 raw cache envelope：Provider ID、stable candidate ID、载荷格式、载荷哈希、获取时间和必要兼容字段。
- 定义 parsed cache envelope：raw hash、parser ID/version、normalization version 和 Core semantic payload。
- 只对拥有稳定 Provider candidate ID 或已有可信稳定媒体 ID 的结果持久化。
- 继续允许无稳定 ID 结果在当前请求/内存中使用，但不得阻止未来远程检索。
- 新 cache version 忽略旧解析语义，不删除旧文件，不把旧 `BestScore` 解释成新选择依据。
- 无效缓存经 `ILyricCacheStore` 删除，保持原子写入和读失败安全回退。
- 不实现 translation cache。

### Tests

- raw/parsed 跨实例往返、版本不兼容、损坏载荷删除和原子更新。
- parser/normalization version 变化导致 parsed cache miss，但可复用 raw cache 重新解析。
- relaxed query 文本不出现在持久化身份中。
- QRC/KRC/YRC syllable 在 parsed cache 往返后时间与 provenance 不变。

### Exit Gate

删除新 parsed cache 可从 raw cache 重建；忽略新 raw cache 可重新网络获取；两者都不依赖查询字符串作稳定身份。

## Phase A6 — Trust-Ordered Resolution Coordinator

### Objective

替换官方源独占和分数竞争，让 Core 协调器成为唯一最终选择所有者。

### Required Execution Order

1. 解析人工映射；纯音乐映射直接返回。
2. 有 `PreferredProvider` 时只执行该源；失败返回无歌词。
3. 有效 Local 歌词直接返回。
4. 启动在线源工作，可并发搜索/获取，每源有明确超时和终态。
5. 每源只将通过身份准入并产生有效内容的结果交给 selector。
6. selector 按 `QQMusic > Kugou > Netease > LRCLIB` 检查结果。
7. 低优先级结果先完成时只暂存；只有它之前的所有源都终态后才可选中。
8. 返回单一 `ResolvedLyrics`；后续服务不得再按分数重选。

### Work

- 实现稳定 Provider ID 可信策略，构造时验证重复、遗漏和未知 ID。
- 为每源定义 `Succeeded`、`IdentityRejected`、`NoLyrics`、`InvalidContent`、`Failed`、`TimedOut`、`Disabled`、`Canceled` 等终态。
- 保留 Provider gate，防止 Lyricify 共享 HTTP 头状态被并发改写。
- 外层取消停止等待不支持 token 的 Helper 调用，但日志不声称底层 HTTP 已中止。
- 删除 `OfficialImmediateAcceptScore`、`FallbackOverrideMargin`、`SourceQualityWeights` 对最终选择的影响。
- `LyricSyncService` 改为接收唯一已解析结果，删除其 `OrderByDescending(BestScore)` 二次选择。
- 保持当前行级投影、翻译开关、偏移和新旧轨道切换取消语义。

### Tests

- 对每种可信顺序注入结果，证明完成先后不改变选择。
- QQ 有效时不被任何低源覆盖；QQ 身份拒绝时酷狗可选；QQ/酷狗终态时网易云可选；前三源终态时 LRCLIB 可选。
- 低源更高匹配分、更完整元数据或逐词能力都不能覆盖已准入高源。
- 官方 SourceApp/SongId 使查询直达，但不改变可信顺序。
- `PreferredProvider` 失败无回退，Local 有效无在线请求。
- 超时、主动取消、换歌、迟到结果和 Dispose 不导致状态泄漏。

### Exit Gate

- 生产只有一处最终选择逻辑。
- `LyricDocument.BestScore` 不再影响新路径的跨源选择。
- 已有所有 Registry/Sync 取消与 Dispose 测试通过。
- 可通过恢复旧 Registry 组装回滚选择策略，不需要回滚已迁移的 parser 和 source 适配器。

## Phase A7 — Compatibility, Information Lines, and Legacy Convergence

### Objective

完成 Core 新路径收敛，保留用户可见兼容，删除已无调用者的并行事实源。

### Work

- 将有明确 parser/Provider 标记的信息行从正常播放投影排除，但保留在 `ParsedLyrics` 和诊断中。
- 不对未标记行新增基于文本的启发式隐藏。
- 确认现有内嵌翻译在 QQ、酷狗和网易云兼容投影中不回退。
- 将 `LyricDocument.BestScore` 从最终语义内容移出。若为短期序列化兼容保留字段，必须明确标记已弃用且新路径不读取。
- 更新 `AppCompositionRoot` 只组装新协调器、四源适配器、parser/decoder 和 cache。
- 在确认无调用者、无缓存迁移依赖和无测试依赖后，删除旧 `LyricifyLyricProvider`、`LrcLibSmtcLyricProviderBase` 和已迁移分支。
- 删除只服务旧官方独占/质量权重策略的死常量与日志。
- 为请求、query variant、Provider、candidate、identity result、format、timing provenance、cache acquisition、terminal state 和选择提供相关性日志，不记录全量歌词。

### Tests

- 明确信息行隐藏但可诊断；未标记作词文本仍显示。
- 旧 `settings.json`、人工映射、Local、player/track offset 和缓存异常回退测试。
- `AppCompositionRoot` 不注册旧 Provider。
- CodeGraph/编译确认旧类无生产调用者后才删除。

### Exit Gate

- 新链路为唯一生产实现。
- 无旧选择权重或 Provider 内置匹配分支影响最终结果。
- 当前 WebView V1 包络、行级显示和翻译开关不变。

## Phase A8 — Core Final Verification

### Automated Verification

1. 运行全部 Core 定向测试。
2. 运行 `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1`。
3. 运行 `dotnet build TaskbarLyrics.sln`，要求零警告、零错误。
4. 运行 `git diff --check`。
5. 对最终 diff 执行 Clean Code rubric 全维度复核。
6. 更新一次 `docs/工程变更记录.md`，记录 Phase A 整体行为、兼容、验证和剩余 Phase B/C。
7. 运行 `scripts/restart-app.ps1` 并保持应用运行。

### Manual Matrix

- QQ 播放器：验证 QQ 直达 SongId、QRC 保真、行级显示和已有翻译。
- 酷狗播放器：验证 KRC 不再降级为 LRC，Core 诊断可见 syllable。
- 网易云播放器：验证即使有官方 SongId，仍按 QQ > 酷狗 > 网易云 > LRCLIB 选择，并验证 YRC/LRC 回退。
- 通用 SMTC 播放器：验证无官方源亲和时仍按可信顺序选择。
- LRCLIB：验证 synced/plain/instrumental 三种情况。
- 人工映射：验证纯音乐、PreferredProvider 硬绑定失败无回退。
- Local：验证有效时无在线请求，并继续共享 `ILocalMediaIndex`。
- 换歌：在请求进行中换歌，旧结果不替换新歌词。
- 偏移：验证 player offset 与 track offset 的现有叠加和优先级。
- 信息行：明确标记行隐藏，未标记相似文本仍显示。
- 重启：稳定 ID 缓存可复用，无稳定 ID 结果不抑制新检索。

### Phase A Done

Phase A 只在自动验证、手工矩阵、工程记录和应用重启全部完成后结束。不得因为 Core 已保留 syllable 就声称“逐词扫描已完成”。

## Phase B — Deferred Word Scanning

### Entry Gate

- Phase A 已完成且手工验收。
- 开始前必须完整阅读 `docs/WebView界面视觉与交互规范.md`。
- 不增加 synthetic timing，只消费 `ProviderSupplied` 细粒度时间。

### Work

- 在 Core 播放投影中计算当前行、当前 syllable、syllable 进度和 timing provenance。
- 保留行级 `LineProgress` 回退，无 Provider 逐词时不伪造卡拉 OK 进度。
- 将 player/track 偏移在同一命名边界应用到行和 syllable 选择。
- 在保持 WebView V1 envelope 的前提下扩展歌词 payload；对旧/无逐词 payload 安全回退。
- 用原生 HTML/CSS/JavaScript 实现逐词扫描，不引入新前端框架。
- 更新 Web 测试、App 协议测试和 Core 播放测试。

### Exit Gate

- QRC/KRC/YRC fixture 的当前 syllable 和进度精确。
- 行级歌词、无歌词、纯音乐和旧 payload 安全回退。
- 无 synthetic per-character timing。
- WebView 行为测试、App/Core 测试、full verify、零警告 build、`git diff --check` 与应用重启完成。
- 在 `docs/工程变更记录.md` 记录 Phase B。

## Phase C — Deferred Translation and Transliteration

### Entry Gate

Phase C 不能直接按本 Plan 开始外部翻译。必须先为以下内容通过一份翻译增补 Spec：

- 是否启用外部翻译生成。
- 翻译引擎、网络与隐私边界。
- 目标语言和设置语义。
- 失败、重试、超时和费用边界。
- 翻译缓存期限、引擎版本与失效。

### Planned Boundary

- 将 Original、Translation、Transliteration 建模为独立 track。
- 保留语言、Provider/引擎、格式、对齐和来源信息。
- 嵌入翻译在最终选源前作为候选内容解析；外部翻译只在选源后执行。
- 格式原生关联优先，时间容差只作回退对齐。
- 翻译或音译失败不使原文无效。
- 翻译缓存 key 包含原文哈希、源/目标语言、引擎和引擎版本。

## Spec Traceability

| Spec acceptance | Plan coverage |
| --- | --- |
| 四源统一节点链路 | A1、A2、A4 |
| Lyricify 类型不泄漏 | A1、A2，A4 exit gate |
| 固定可信顺序与完成顺序无关 | A6 |
| 身份准入与跨源质量分离 | A3、A6 |
| QRC/KRC/YRC 原生 syllable 保真 | A2、A4.1、A4.2、A4.3、A5 |
| LRCLIB synced/plain/instrumental | A4.4 |
| 已有内嵌翻译不回退 | A0、A2、A4、A7 |
| 明确信息行才隐藏 | A0、A7 |
| Local 短路和 PreferredProvider 硬绑定 | A0、A6 |
| 分层缓存和无稳定 ID 不持久化 | A5 |
| 取消、换歌和迟到结果 | A4、A6、A8 |
| Helper 不支持的第五源 | A1 |
| 设置、映射、偏移、WebView V1 兼容 | A0、A7、A8 |
| 逐词扫描 | B，不算入 Phase A Done |
| 多轨和外部翻译 | C，需翻译增补 Spec |

## Handoff Checklist

任何在缺少对话上下文的环境中接手时，依次执行：

1. 阅读 `AGENTS.md`、Clean Code skill、Accepted Spec 和本 Plan。
2. 查看 `git status --short`，不覆盖未跟踪的差异报告和 Spec/Plan。
3. 在 `Phase Overview` 中确认第一个未完成且前置已通过的阶段；不跳过 exit gate。
4. 用 CodeGraph 重新查询该阶段的 owner、caller、tests 和 blast radius。
5. 运行该阶段的基线测试，再修改代码。
6. 严格限定在当前阶段；发现后续需求时记录，不顺带实现。
7. 逐项完成 tests、exit gate、full verification、Clean Code 复核、工程记录和必要的 app restart。
8. 仅在阶段完整验证后将其状态从 `Pending` 改为 `Completed`，并在 Plan 中记录验证日期与证据。

## Global Definition of Done

- 当前 Phase 的 Spec 验收项和本 Plan exit gate 全部满足。
- 新增或改变的行为有会在改动前失败的回归测试。
- 无 Lyricify 类型泄漏到选择、缓存、播放、App 或 WebView。
- 无新的持久化模糊匹配，无旧缓存语义误读。
- 取消、超时、Dispose 和迟到结果均有明确测试。
- `scripts/verify.ps1` 通过。
- `dotnet build TaskbarLyrics.sln` 零警告、零错误。
- `git diff --check` 通过。
- Clean Code rubric 复核无未处理的重大问题。
- 完整阶段已更新 `docs/工程变更记录.md`。
- 可运行应用变更已重启应用，并留在可手工验证状态。
- 未将 Phase B 或 C 的延后能力误报为 Phase A 已完成。
