# TaskbarLyrics vs BetterLyrics 歌词 Provider 全链路差异报告

## 全链路节点总览

```
[1.数据源注册] → [2.搜索调度] → [3.匹配评分] → [4.缓存] → [5.格式解析] → [6.后处理] → [7.渲染消费]
```

两项目都覆盖这 7 个节点，但在每个节点的实现深度和架构选择上差异显著。

---

## 节点 1：数据源注册与覆盖

### TaskbarLyrics.Core

**抽象方式**：`ILyricProvider` 接口 + DI 多实例注入

| Provider 类 | SourceApp | 底层依赖 | 逐词能力 |
|------------|-----------|---------|---------|
| `LyricifyLyricProvider` (QQMusic) | "QQMusic" | `Lyricify.Lyrics` | QRC syllable |
| `LyricifyLyricProvider` (Netease) | "Netease" | `Lyricify.Lyrics` | 只取 Lrc.Lyric |
| `LyricifyLyricProvider` (Kugou) | "Kugou" | `Lyricify.Lyrics` | KRC 解密后降级为纯 LRC |
| `LrcLibSmtcLyricProviderBase` 派生 | "LRCLIB" | HTTP 直连 lrclib.net | 只解析标准 LRC |
| `LocalLyricProvider` | "Local" | 本地文件扫描 | 无 |
| `GenericSmtcLyricProvider` | 各 SMTC 来源 | SMTC 原生 | 无 |

**缺失的源**：AMLL TTML DB、Apple Music、插件扩展

### BetterLyrics

**抽象方式**：`LyricsProvider` 枚举 + `SearchSingleAsync` switch 分发 + `ILyricsSource` 插件 SDK

| 枚举值 | 底层依赖 | 逐词能力 |
|--------|---------|---------|
| `QQ` | `Lyricify.Lyrics` | QRC |
| `Kugou` | `Lyricify.Lyrics` | KRC（委托库解析） |
| `Netease` | `Lyricify.Lyrics` | YRC（委托库解析） |
| `LrcLib` | HTTP 直连 | ESLRC（若返回增强 LRC） |
| `AmllTtmlDb` | GitHub raw 直连 + 本地索引 | TTML 逐字 |
| `LocalMusicFile` | 本地音乐内嵌 | 取决于内嵌格式 |
| `LocalLrcFile` | 本地 .lrc | 无 |
| `LocalEslrcFile` | 本地 .eslrc | 有 |
| `LocalTtmlFile` | 本地 .ttml | 有 |
| `AppleMusic` | Apple Music API（需 token） | TTML |
| `BetterLyrics` | 内部（IsInternal 过滤） | — |
| `LibreTranslate` | 内部翻译生成 | — |
| 插件 | `ILyricsSource` SDK | 取决于插件 |

### 差异要点

| 维度 | TaskbarLyrics | BetterLyrics |
|------|--------------|--------------|
| **源数量** | 6 个 | 10 个 + 插件 |
| **AMLL TTML DB** | 无 | 本地索引 + 增量更新 |
| **Apple Music** | 无 | 有 |
| **网易云逐词 (YRC)** | 丢弃 | 委托库解析 |
| **酷狗逐词 (KRC)** | 降级为 LRC | 委托库解析 |
| **ESLRC** | 无 | 有 |
| **插件体系** | 无 | `ILyricsSource` SDK |
| **本地格式细分** | 统一 Local | 分 Lrc/Eslrc/Ttml 三类 |

---

## 节点 2：搜索调度

### TaskbarLyrics — 策略驱动的"官方源独占 + 回退批次"

`LyricProviderRegistry.ResolveLyricsAsync`（`TaskbarLyrics.Core/Services.LyricProviderRegistry.cs:22`）：

```
1. 查 SQLite 映射 (ResolveMapping)
   ├─ 纯音乐标记 → 直接返回
   └─ PreferredProvider → 独占超时检索，失败返回空

2. Local provider 优先 (LocalProviderTimeout=2s)
   └─ 命中 → 直接返回

3. 官方源独占 (TryGetOfficialProvider 按 SourceApp 推断)
   ├─ QQMusic → "QQMusic" provider
   ├─ Netease/cloudmusic/163music/wyy → "Netease" provider
   └─ Kugou → "Kugou" provider
   独占超时 OfficialSourceTimeout=10s
   ├─ 高分 (≥90) → 直接采纳
   ├─ 中分 → 进入回退竞争
   └─ 未返回 → 进入回退

4. 回退批次并行 (BuildFallbackBatches)
   ├─ 已知源: [其余 AdaptedProviders + LRCLIB] 一批并行
   └─ 未知源: [QQMusic, Netease, LRCLIB] → [Kugou] 两批串行
   每批 FallbackProviderTimeout=5s
   ├─ FallbackImmediateExitScore=95 → 立即采纳
   └─ FallbackSoftWaitScore=85 → 800ms 弱等待窗口
```

### BetterLyrics — 用户配置驱动的 Sequential / BestMatch

`LyricsSearchService.SearchSmartlyAsync`（`LyricsSearchService.cs:68`）：

```
1. 查 SongSearchMapService 映射
   ├─ 纯音乐 → 直接返回
   └─ 指定 LyricsSearchProvider → 单源检索

2. 查当前播放器配置 (MediaSourceProvidersInfo[PlayerId])
   取 LyricsSearchProvidersInfo.Where(IsEnabled)
   默认列表 = LyricsProvider 枚举顺序:
   QQ > Kugou > Netease > LrcLib > AmllTtmlDb > Local* > AppleMusic

3. 按搜索模式执行:
   ├─ Sequential (默认): 逐个尝试，MatchPercentage >= threshold 命中即停
   └─ BestMatch: 全量并行，取 MatchPercentage 最高

4. SearchSingleAsync 内部:
   先查缓存 (provider.IsCacheable())
   → switch 分发到具体 Search*Async
   → 结果写回缓存
```

### 差异要点

| 维度 | TaskbarLyrics | BetterLyrics |
|------|--------------|--------------|
| **优先级来源** | 代码硬编码 `LyricSourceRoutingPolicy` | 用户配置（每播放器独立有序列表） |
| **官方源优先** | 自动按 SourceApp 绑定 | 无自动推断，靠用户排序 |
| **搜索模式** | 单一（官方独占+回退批次） | Sequential / BestMatch 二选一 |
| **超时控制** | 策略常量（10s/5s/2s） | 无显式超时，靠 CancellationToken |
| **弱等待** | 85分→800ms窗口 | 无 |
| **批次并行** | 分批 + 即时退出 + 弱等待 | BestMatch 全量并行无批次 |
| **返回类型** | `List<LyricResolveResult>`（全量结果） | 单个 `LyricsCacheItem?` |
| **搜索与解析** | 耦合（provider 返回已解析 LyricDocument） | 分离（provider 返回原始 Raw 文本） |

---

## 节点 3：匹配评分

### TaskbarLyrics — `LyricMatcher.Score`

`TaskbarLyrics.Core/Utilities/LyricMatcher.cs:18`，JaroWinkler 算法：

- **预处理**：简繁统一 → 去变音符号 → 去括号后缀 → 去 feat/ft 后缀 → 去噪声标签
- **版本冲突检测**：live/remix/acoustic/demo/instrumental 等关键词单边出现 → 直接 0 分
- **相似度**：标题 JaroWinkler + 艺人 JaroWinkler（取 token overlap 最大值）+ 时长线性（1s 内满分，10s 外 0 分）
- **加权**：标题 0.50 + 艺人 0.30 + 时长 0.20（三者齐全时）
- **门槛**：标题相似度 < 0.72 且无包含关系 → 0 分；艺人 < 0.45 且无 overlap → 0 分；时长差 ≥ 20s → 0 分（QQ 豁免）
- **准入线**：`MinimumAcceptedMatchScore = 70`（全局统一）
- **质量权重**（`SourceQualityWeights`）：Local +10, QQMusic +6, Netease +3, Kugou +2, LRCLIB +1

### BetterLyrics — `MetadataComparer.CalculateScore`

- 标题/艺人/专辑/时长的综合匹配分
- **准入线**：默认 `MatchingThreshold = 60`，**每个 provider 可独立覆盖**
- **无质量权重**：纯靠 MatchPercentage

### 差异要点

| 维度 | TaskbarLyrics | BetterLyrics |
|------|--------------|--------------|
| **算法** | JaroWinkler + 版本冲突检测 + token overlap | 元数据综合比对 |
| **预处理** | 简繁/变音/括号/feat/噪声标签 | 基础规范化 |
| **版本冲突** | live/remix/acoustic 等检测 | 无 |
| **准入线** | 全局 70 | 默认 60，每 provider 可覆盖 |
| **质量加权** | SourceQualityWeights | 无，纯匹配分 |
| **QQ 时长豁免** | ≤61s 不参与时长比对 | 无 |

---

## 节点 4：缓存

### TaskbarLyrics

- `JsonLyricCacheStore<CachedLyrics>` 按 provider 分文件
- 缓存内容：**已解析文本** `{ SyncedLyrics, PlainLyrics }`
- 缓存粒度：provider 级，无独立跳过缓存配置

### BetterLyrics

- `ILyricsCacheService` + `provider.IsCacheable()` 判断
- 缓存内容：**原始 `LyricsCacheItem`**（Raw + 元数据 + 匹配分）
- 缓存粒度：**每 provider 可独立配置 `IgnoreCacheWhenSearching`**
- AMLL 索引本地缓存：`amll-ttml-db-index.jsonl` + 24h 过期 + `DownloadAmllTtmlDbIndexAsync` 增量更新

### 差异要点

| 维度 | TaskbarLyrics | BetterLyrics |
|------|--------------|--------------|
| **缓存对象** | 解析后文本 | 原始 Raw（可重新解析） |
| **格式切换** | 不支持（缓存的是解析结果） | 支持（Raw 可重新走格式路由） |
| **缓存跳过** | 无独立配置 | 每 provider 可配 `IgnoreCacheWhenSearching` |
| **AMLL 索引** | 无 | 本地 jsonl + 24h 过期 |

---

## 节点 5：格式检测与解析

### TaskbarLyrics — 解析耦合在 provider 内

| 格式 | 解析位置 | 逐词 | 说明 |
|------|---------|------|------|
| **LRC** | `LrcLibSmtcLyricProviderBase.ParseLrc:643` | 无 | 只取最后时间戳后的文本 |
| **QRC** | `LyricifyLyricProvider.ParseQrc:319` | 有 | 委托 `QrcParser.Parse`，提取 syllable → `LyricSyllable(RelativeOffset, Duration, Text)` |
| **KRC** | `LyricifyLyricProvider:219-239` | 无 | 解密后重建 `[mm:ss.ff]text` 纯 LRC，**丢弃逐词时间** |
| **ESLRC** | 无 | 无 | 不支持，LrcLib 返回的增强 LRC 被降级 |
| **YRC** | 无 | 无 | 只取 `Lrc.Lyric` 字段 |
| **TTML** | 无 | 无 | 无此能力 |

### BetterLyrics — 解析与搜索严格分离

`LyricsContentParser.PreParseAsync`（`LyricsContentParser.cs:38`）统一路由：

| 格式 | 检测 | 解析器 | 逐词 |
|------|------|--------|------|
| **LRC** | `DetectFormat()` | `ParseLrc`（`LyricsContentParser.Lrc.cs:15`） | 无，行级 |
| **ESLRC** | `DetectFormat()` | `ParseLrc` 同一正则 `SyllableRegex` | 有，`<mm:ss.xx>字` 标签 |
| **QRC** | `DetectFormat()` | `ParseQrcKrc`（`LyricsContentParser.QrcKrc.cs:10`）委托 `QrcParser.Parse` | 有 |
| **KRC** | `DetectFormat()` | `ParseQrcKrc` 委托 `KrcParser.Parse` | 有 |
| **TTML** | `DetectFormat()` | `ParseTtml`（`LyricsContentParser.Ttml.cs`）XML 解析 | 有，span 级 |
| **元数据** | — | `LyricsMetadataParser.Parse` | — 从 `[ti]/[ar]/[al]` 或 `ttm:title/agent/amll:meta` 提取 |

### 差异要点

| 维度 | TaskbarLyrics | BetterLyrics |
|------|--------------|--------------|
| **架构** | 解析耦合在 provider 内 | 解析与搜索分离（Raw → 统一路由） |
| **格式检测** | 无（provider 固定走一种） | `Raw.DetectFormat()` 自动路由 |
| **ESLRC** | 无 | 有 |
| **KRC 逐词** | 降级 | 有 |
| **YRC** | 无 | 有 |
| **TTML** | 无 | 有，AMLL 规范 |
| **元数据提取** | 无 | 有，`LyricsMetadata` |
| **新增格式成本** | 改 provider 类 | 只加解析器，搜索层不动 |

---

## 节点 6：后处理

### TaskbarLyrics — 几乎无后处理

- 翻译：`LyricifyLyricProvider:272-286` 在 provider 内用时间戳对齐（±60ms epsilon）合并到 `LyricLine.Translation` 单字段
- 简繁转换：`NormalizeLyricText` 调 `ChineseScriptConverter.ToSimplified`（开关控制）
- 音节兜底：无
- 行 EndMs 补全：无（模型用 `RelativeOffset+Duration`，不依赖 EndMs）
- 多轨分离：无
- 音译/罗马音：无
- 信息行过滤：`LyricDocument.IsInformationalLine`（纯音乐检测用，不剔除）

### BetterLyrics — 完整后处理流水线

`LyricsContentParser.ParseAsync`（`LyricsContentParser.cs:96`）：

1. **多轨分离**：同时间戳多行按语言分轨 → `LyricsDataArr`，`TrackType` 标记 Original/Translation/Transliteration
2. **翻译加载**：`LoadTranslation` 独立解析翻译原文 → `GenerateTranslationLyricsDataAsync`（无翻译轨时调外部 API 生成）
3. **音译加载**：`LoadTransliteration` → `GenerateTransliterationLyricsDataAsync`（无音译轨时生成拼音/粤拼/罗马音）
4. **音节兜底**（`EnsureSyllables:391`）：
   - 有真 syllable → 补全缺失 EndMs（取下一 syllable 起始）
   - 无 syllable（普通 LRC）→ 按字符均分整句时长，合成伪音节
5. **行 EndMs 补全**（`EnsureEndMs`）：缺失则取下一行起始时间
6. **信息行过滤**：`InfoLines.IsInfoLine` 剔除作词/作曲等
7. **翻译叠加**：`SetTranslatedText` 把翻译轨合到原文行 `SecondaryText`
8. **音译叠加**：`SetPhoneticText` 合到 phonetic 字段
9. **简繁转换**：对原文/翻译按设置做 S→T 或 T→S
10. **语言检测**：`LanguageHelper.DetectLanguageTag` 自动识别

### 差异要点

| 维度 | TaskbarLyrics | BetterLyrics |
|------|--------------|--------------|
| **音节兜底** | 无 | 真伪 syllable 统一补全 |
| **行 EndMs** | 无（模型不依赖） | 有 |
| **多轨分离** | 无 | 有 |
| **翻译** | 单字段，时间戳对齐 | 独立轨 + 外部 API 生成 |
| **音译** | 无 | 拼音/粤拼/罗马音 |
| **语言检测** | 无 | 有 |
| **信息行过滤** | 检测但不剔除 | 剔除 |
| **真伪音节标志** | 无 | `IsPrimaryHasRealSyllableInfo` |

---

## 节点 7：渲染消费

### TaskbarLyrics

`LyricSyncService.GetDisplayFrameAsync`（`TaskbarLyrics.Core/Services.LyricSyncService.cs:48`）：
- 只算 **行级进度** `LineProgress`（`elapsed / duration`，`:152-161`）
- **从不读取 `LyricLine.Syllables`**——syllable 数据虽存在于模型但完全未被消费
- `LyricDisplayFrame` 只传 `CurrentLine/NextLine/Title/LineProgress/CurrentLineIndex/IsPureMusic`
- WebView 脚本 `SetLyrics` 只传文本

### BetterLyrics

- 三级渲染模型：Line → Syllable → Char（`RenderLyricsLine → RenderLyricsSyllable → RenderLyricsChar`）
- `LyricsAnimator` 独立驱动 syllable/char 级卡拉OK动画
- `LyricsData.IsWordByWord` 标志驱动渲染模式切换

### 差异要点

| 维度 | TaskbarLyrics | BetterLyrics |
|------|--------------|--------------|
| **进度粒度** | 行级 | 字符级 |
| **Syllable 消费** | 不读取 | 驱动动画 |
| **渲染模型** | 扁平（Line only） | 三级（Line→Syllable→Char） |
| **逐词扫描** | 无 | 有 |

---

## 全链路差异总结

| 节点 | TaskbarLyrics 成熟度 | BetterLyrics 成熟度 | 核心差距 |
|------|---------------------|---------------------|---------|
| **1.数据源** | 6源，无AMLL/Apple | 10源+插件 | AMLL TTML DB、Apple Music、插件 |
| **2.调度** | 策略驱动，官方独占 | 配置驱动，Sequential/BestMatch | 灵活性 vs 开箱即用 |
| **3.评分** | JaroWinkler+冲突检测+质量权重 | 基础元数据比对 | TaskbarLyrics 更严谨 |
| **4.缓存** | 解析后文本 | 原始Raw+独立跳过+AMLL索引 | BetterLyrics 更灵活 |
| **5.解析** | 耦合provider，5种格式 | 独立路由，6种格式+元数据 | 架构分离 + ESLRC/KRC/YRC/TTML |
| **6.后处理** | 几乎无 | 完整10步流水线 | 音节兜底/多轨/音译/语言检测 |
| **7.渲染** | 行级，不消费syllable | 字符级卡拉OK | syllable 数据未接入渲染 |

**TaskbarLyrics 唯一明显领先的点**：匹配评分更严谨（版本冲突检测、质量权重、QQ时长豁免），调度有弱等待窗口优化。这两个点在完善 Core 时应保留。
