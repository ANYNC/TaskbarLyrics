# 歌词 Provider 全链路解耦与 Lyricify 0.2.0 接管规格

## Spec Status

- 状态：Accepted for Planning
- 需求决策日期：2026-08-07
- 当前交付范围：`TaskbarLyrics.Core` 的歌词查询规划、Provider 检索、候选匹配、载荷获取、解密、解析、规范化、可信选择、缓存及 Provider 原生逐词数据保真
- 后续交付范围：逐词扫描消费，以及翻译/音译多轨与外部翻译增强
- 依赖基线：官方 `Lyricify.Lyrics.Helper` 0.2.0
- 在线歌词源：QQ 音乐、网易云音乐、酷狗音乐、LRCLIB
- 本地歌词：保留为独立获取通道，不要求 Lyricify 支持
- 在线源初始可信顺序：`QQMusic > Kugou > Netease > LRCLIB`

## Problem Statement

TaskbarLyrics 当前把检索、候选选择、歌词获取、解密、解析和翻译对齐集中在 Provider 实现中。这导致不同歌词源复用了不同的查询、匹配、解析和缓存逻辑，也使单个 Provider 同时承担多个变化原因。

当前按播放器官方歌词源优先的行为不能稳定提供高质量体验。播放器官方源可以提供稳定 SongId，但该 SongId 只能证明查询亲和性，不能证明歌词时间轴质量。特别是用户上传内容较多的平台，元数据完整和歌曲匹配正确仍不等于时间轴准确。

当前链路还存在以下具体问题：

- QQ 的 QRC 可以解析真实逐词时间，但后续通用补全可能覆盖这些时间。
- 酷狗 KRC 被解析后重建为普通 LRC，真实逐词信息被丢弃。
- 网易云只消费 LRC 和翻译 LRC，未使用 0.2.0 已能提供的 YRC 及相关轨道。
- LRCLIB 拥有与其他 Provider 不同的搜索、缓存和解析实现。
- `LyricDocument.BestScore` 把歌词内容与候选匹配结果混合在同一模型中。
- 播放投影只消费行级进度，已有的 syllable 数据没有驱动逐词扫描。
- 翻译是 `LyricLine` 的单一字段，无法完整表达原文、翻译、音译和多语言轨。

需要一条以节点契约连接的新链路：当前四个在线源尽可能使用 Lyricify 0.2.0 的平台接口、解密器和格式解析器，但每个节点必须仍可独立替换，使未来新歌词源在 Helper 不支持时只需实现缺失节点。

## Solution

在 `TaskbarLyrics.Core` 内建立分层的歌词解析管线。管线分为控制面和内容面：

- 控制面负责原始曲目身份、查询规划、Provider 调度、候选身份匹配、人工可信顺序和最终选择。
- 内容面负责原始载荷、解密、显式格式解析、模型转换、轨道合并、后处理、缓存和播放投影。

```text
TrackIdentity
    → LyricSearchPlan
    → Provider Search
    → SourceTrackCandidate[]
    → Identity Evaluation
    → RawLyricPayload
    → Decode
    → Explicit Format Parse
    → ParsedLyrics
    → Normalize / Validate / Merge Tracks
    → Trust-Ordered Selection
    → Optional Enrichment
    → ResolvedLyrics
    → Playback Projection
```

Lyricify 可以连续接管多个节点，但它的类型不得穿过节点边界。每次调用 Lyricify 后必须立即转换为 TaskbarLyrics 拥有的边界模型。

## Goals

- 四个在线歌词源的检索、获取、解密和解析在 Helper 支持的节点上统一使用官方 Lyricify 0.2.0。
- 保持节点粒度，不形成 `ResolveLyrics(track) -> final document` 式的 Helper 黑盒。
- 歌曲身份匹配只回答“是否是同一首歌”，不决定跨源优先级。
- 跨源选择以人工维护的歌词源可信顺序为第一决策依据。
- 保留 QRC、KRC、YRC 和 TTML 中的 Provider 原生逐词或逐字时间。
- 当前交付先将 Provider 原生逐词数据完整带入 Core 语义模型与缓存，不在同一交付中实现 WebView 逐词扫描。
- 当前交付保持已有内嵌翻译显示不回退；原文/翻译/音译多轨重构与外部翻译生成留到后续翻译阶段。
- 使原始载荷与解析结果可分别缓存与失效；翻译缓存留到后续翻译阶段。
- 保留人工映射、纯音乐标记、本地歌词、播放器与曲目偏移等现有用户数据语义。

## Non-Goals

- 不通过结构规则声称能自动判断歌词时间轴是否与音频真实对齐。
- 不在本 Spec 中引入基于音频特征、人声识别或机器学习的时间轴校验。
- 不直接复制 BetterLyrics 的服务定位器、可变全局解析状态或先选源后解析的模式。
- 不在本 Spec 中建立对外插件 SDK。
- 不要求开发者为每一个管线箭头创建一个接口或类。
- 不在本 Spec 中定义歌词源优先级的设置页 UI。
- 不在本 Spec 中要求新增 Apple Music、AMLL TTML DB 或其他在线源。
- 当前 Core 交付不修改 WebView 逐词扫描、动画或交互；该能力只能在 Core 输出验收通过后实现。
- 当前 Core 交付不实现新的翻译多轨、音译生成或外部翻译引擎；但不得破坏已有内嵌翻译显示。

## User Stories

1. As a user, I want a lyric source proven reliable through real-world use to take precedence over the current player's official source, so that official but poorly aligned lyrics do not win automatically.
2. As a user, I want TaskbarLyrics to reject a high-priority result that is not the same recording or version, so that trust priority never bypasses song identity safety.
3. As a user, I want manually mapped lyrics and local lyrics to retain their intentional override behavior, so that my explicit corrections remain effective.
4. As a user, I want QQ QRC, Kugou KRC, and Netease YRC word timings to be preserved in Core first, so that a later word-by-word scanning stage can consume trustworthy provider-supplied timing.
5. As a user, I want the currently supported embedded translation display to remain available during the Core migration, so that architecture work does not remove existing behavior.
6. As a user, I want line-synced lyrics to remain usable when word timing is unavailable, so that format capability does not become a hard availability requirement.
7. As a user, I want a lower-priority source to be used when every higher-priority source fails, times out, does not match the song, or returns invalid lyrics, so that trust ordering does not reduce availability.
8. As a user, I want a player-provided SongId to speed up the matching provider's lookup without forcing that provider to win, so that query affinity and content trust remain separate.
9. As a user, I want cached lyrics to be invalidated when parser or normalization semantics change, so that stale data cannot hide an improved result.
10. As a developer, I want a new lyric source unsupported by Lyricify to implement only its missing search, fetch, decode, or parse capabilities, so that adding a source does not require duplicating the full pipeline.
11. As a developer, I want Lyricify types to remain inside integration adapters, so that a package upgrade does not reshape TaskbarLyrics domain and cache contracts.
12. As a diagnostic user, I want logs to identify query variant, provider, candidate identity, match decision, format, timing capability, acquisition type, and terminal failure category, so that lyric selection can be explained without exposing raw lyrics.

## Normative Pipeline Behavior

### 1. Original Track Identity

- The system MUST preserve an immutable original track identity containing title, artists, album, duration, source player, player track ID, and any known version markers.
- Query normalization MUST NOT mutate or replace the original identity.
- A provider-specific SongId MUST only be used with the provider whose identity namespace owns that ID.
- Version markers such as Live, Remix, Acoustic, Demo, Instrumental, Radio Edit, and Remaster MUST remain available to final identity validation even when a relaxed query omits them.

### 2. Query Planning

- Search queries MUST be produced centrally as an ordered `LyricSearchPlan`, not independently assembled inside each Provider.
- The plan SHOULD support exact, normalized punctuation, all-artists, primary-artist, simplified/traditional, diacritic-normalized, and relaxed variants.
- A relaxed query MAY remove featuring text or bracket suffixes for recall, but final identity evaluation MUST compare the candidate with the original identity.
- The query string MUST NOT be used as a durable cache identity.

### 3. Provider Search

- QQ Music, Netease, Kugou, and LRCLIB searches MAY run concurrently.
- Each source MUST return its candidate collection to TaskbarLyrics before final candidate selection.
- The integration MUST prefer Lyricify's lower-level provider/searcher APIs over `SearchHelper.Search`, because the latter may preselect a single candidate and hide alternatives.
- Provider-specific query differences MUST be isolated inside that source adapter and MUST NOT redefine global matching policy.
- Network responses MUST be treated as untrusted boundary data and validated before conversion.

### 4. Candidate Identity Evaluation

- Candidate evaluation MUST be owned by TaskbarLyrics.
- Identity evaluation MUST use title, artist set, duration, album when useful, and semantic recording/version conflicts.
- Match score and rejection reasons MUST be stored outside parsed lyric content.
- Provider trust, lyric format, translation availability, and word timing availability MUST NOT increase a candidate's song-identity score.
- A candidate below the identity admission threshold MUST be rejected even when its source has the highest trust priority.

### 5. Payload Acquisition

- Fetch MUST operate on a provider candidate with a stable provider candidate ID whenever available.
- The current Core raw payload MUST preserve provider ID, candidate ID, declared or inferred payload format, raw original lyrics, current embedded translation data needed for compatibility, pure-music signal, and acquisition diagnostics.
- The raw payload contract MUST remain extensible for deferred translation and transliteration tracks, but the current Core delivery does not require extracting newly supported transliteration payloads.
- Provider responses containing several lyric variants MUST preserve those variants until format choice is made.
- Empty, malformed, access-denied, or explicitly no-lyric responses MUST become typed terminal outcomes rather than indistinguishable `null` values where practical.

### 6. Decode and Parse

- Decode and parse MUST remain separate conceptual nodes even if one adapter implements both.
- QRC and KRC decryption SHOULD use Lyricify 0.2.0.
- Parsing MUST call an explicit format parser such as `QrcParser`, `KrcParser`, `YrcParser`, `TtmlParser`, or `LrcParser`.
- Production code MUST NOT depend on Lyricify automatic format detection in 0.2.0.
- Production code MUST NOT depend on `ParseHelper.ParseLyrics(string)` in 0.2.0 because the released implementation does not return a parsed result reliably.
- A Provider that already knows its format MUST carry that declared format to the parser rather than re-detect it from content.
- Lyricify parsing results MUST be converted immediately to TaskbarLyrics-owned models.

### 7. Timing Semantics

- The domain model MUST distinguish synchronization granularity from timing provenance.
- Synchronization granularity MUST be able to express at least: Unsynced, LineTimed, WordTimed, CharacterTimed, and Mixed.
- Timing provenance MUST be able to express at least: ProviderSupplied, Synthetic, and Unknown.
- `ProviderSupplied` means that timestamps came from the source payload; it MUST NOT be described as objectively audio-verified.
- Provider-supplied syllables MUST NOT be replaced by equal-duration synthetic characters.
- Synthetic word or character timing MAY be generated only as an explicit fallback and MUST remain distinguishable from provider-supplied timing.
- Normalization MUST validate non-negative time, non-empty text, monotonic line ordering, and usable syllable ranges without silently inventing source accuracy.

### 8. Track Semantics

- The current Core delivery MUST preserve the existing embedded translation behavior through a compatibility representation.
- The current Core delivery MUST NOT introduce an external translation engine, generated transliteration, or a new user-facing translation contract.
- Full Original, Translation, and Transliteration track semantics are deferred to the translation stage following the Core and word-scanning work.
- In the deferred translation stage, a track SHOULD retain language, source, format, and alignment provenance when available.
- In the deferred translation stage, external translation generation MUST occur only after final cross-source selection.
- Translation or transliteration failure MUST NOT invalidate otherwise usable original lyrics in either the compatibility path or the deferred track model.
- When the deferred track model is implemented, line association MUST use format-native relationships when available; timestamp tolerance matching is a fallback, not the primary universal rule.

### 9. Validation and Content Capability

- Structural validation MAY reject empty, non-timed when timed content is required, nonsensical duration, invalid syllable ranges, or completely unparsable payloads.
- Structural validation MUST NOT claim to determine whether a plausible timeline is perceptually aligned with the audio.
- The current Core delivery MUST record timing granularity and timing provenance as capabilities and diagnostics.
- Translation, transliteration, language, background-vocal, and duet capability expansion belongs to the deferred translation delivery except where required to preserve current visible behavior.
- Content capabilities MUST NOT override source trust priority by default.

### 10. Trust-Ordered Selection

- Manual song mapping and pure-music mapping MUST remain the highest explicit user intent.
- Local lyrics MUST retain their defined local override behavior.
- Online source selection MUST use one centrally defined ordered list of stable Provider IDs.
- The ordered list MUST be based on manually reviewed real-world source experience, not metadata completeness or current-player ownership.
- A lower-priority source MUST NOT override a valid higher-priority source because it has a larger match score; match score is an admission gate, not a cross-source quality ranking.
- A lower-priority result MAY be selected only after every higher-priority source reaches a terminal state: rejected identity, no lyrics, invalid content, failed, timed out, disabled, or canceled by the overall request.
- Sources MAY execute concurrently, but completion order MUST NOT change the deterministic trust result.
- The current player's official source and SongId MAY accelerate lookup but MUST NOT change trust order.
- The initial online trust order MUST be `QQMusic > Kugou > Netease > LRCLIB`.
- The trust order MUST be maintained in one Core policy using stable Provider IDs and MUST be injectable in tests.
- The first implementation MUST NOT add a user-facing setting for this order.

### 11. Enrichment

- New translation, transliteration generation, and translation caching are deferred to the translation stage.
- The current Core delivery MAY apply existing behavior-preserving text normalization, but MUST NOT add a new external enrichment dependency.
- Information lines MUST be hidden from normal playback only when the parser or Provider supplies an explicit information-line marker.
- Unmarked lines MUST NOT be hidden solely by a new heuristic in the current Core delivery.
- Hidden information lines MUST remain available in semantic content and diagnostics; parser code MUST NOT irreversibly delete them.
- In the deferred translation stage, enrichment MUST consume the selected semantic document rather than Provider response objects, and enrichment failure MUST preserve the selected original lyrics.

### 12. Caching

- Raw payload, parsed semantic content, and generated enrichment MUST have separate cache identities or separately versioned sections.
- Raw payload cache identity MUST include stable Provider ID and stable provider candidate ID.
- Parsed cache identity MUST include raw payload hash, parser identity/version, and TaskbarLyrics normalization version.
- When the deferred translation stage is implemented, translation cache identity MUST include source-text hash, source language, target language, engine identity, and engine version when known.
- Fuzzy metadata matches without a stable media identity MUST NOT become durable cache entries that suppress future searches.
- Cache formats MUST be versioned whenever parsing, identity, selection, timing, or track semantics change.
- Invalid cached entries MUST be rejected and removed through the cache-store abstraction.
- Existing settings and track/player offset persistence MUST remain backward compatible.

### 13. Playback Projection

- The current Core delivery MUST expose provider-supplied fine-grained timing without requiring WebView or playback-frame consumption in the same delivery.
- Existing line-level playback projection and translation visibility MUST remain behaviorally compatible during the Core delivery.
- Word-by-word playback projection MUST be implemented only after the Core timing model, parsing, normalization, and cache behavior have passed their acceptance criteria.
- The later word-scanning stage MUST consume TaskbarLyrics semantic timing data and MUST NOT depend on Lyricify models.
- The later playback frame MUST be able to identify the current line and current word/character timing segment when provider-supplied timing exists.
- Synthetic word or character timing MUST NOT be generated or rendered in the current Core or initial word-scanning delivery.
- Existing per-player and per-track lyric offsets MUST continue to affect current line playback; the later word-scanning stage MUST apply the same effective offset consistently to line and syllable timing.

### 14. Cancellation and Concurrency

- Cancellation MUST propagate through all TaskbarLyrics-owned async boundaries.
- Provider timeouts MUST remain owned by the orchestration layer.
- Lyricify 0.2.0 calls that do not accept `CancellationToken` MAY be wrapped so TaskbarLyrics stops waiting, but the implementation MUST NOT claim that the underlying HTTP request was aborted.
- Late results from canceled or superseded tracks MUST be ignored and MUST NOT update current playback state or durable selection state.
- Per-provider concurrency gates MUST prevent overlapping calls from corrupting shared Helper HTTP headers or exceeding provider limits.
- Cancellation MUST be distinguished from provider failure in diagnostics.

### 15. Observability

- One request correlation identity SHOULD connect query planning, provider work, selection, caching, and playback handoff.
- Logs SHOULD include Provider ID, query variant ID, candidate ID, identity score/rejection reason, payload format, timing kind, available tracks, acquisition kind, elapsed time, and terminal result.
- Logs MUST NOT include full raw lyrics, authentication tokens, cookies, or other secrets.
- Exceptions SHOULD be logged once at the boundary that can add provider and operation context.

## Required Domain Boundaries

The following names are descriptive rather than mandatory file names, but their responsibilities are normative.

### `TrackIdentity`

Immutable original media identity. It owns the facts obtained from SMTC or manual mapping and is never rewritten by relaxed search normalization.

### `LyricSearchPlan` and `SearchQueryVariant`

Derived query variants with stable variant IDs and relaxation reasons. They do not own final match decisions.

### `SourceTrackCandidate`

TaskbarLyrics-owned candidate containing stable Provider ID, provider candidate ID, title, artists, album, duration, provider-specific direct-fetch identity, and the query variant that produced it.

### `RawLyricPayload`

Provider response normalized into original raw lyrics, current embedded translation compatibility data, format, encryption/encoding state, pure-music signal, stable source identity, and acquisition diagnostics. The contract may later add first-class translation and transliteration tracks without changing source search or selection contracts.

### `ParsedLyrics`

Provider-neutral semantic lyrics containing lines, provider-supplied syllables, explicitly marked information lines, timing kind, timing provenance, and the minimum compatibility data required by existing display behavior. First-class track, language, background-vocal, and alignment semantics are added only in the deferred translation delivery.

### `LyricCandidateEvaluation`

Identity admission result and rejection reasons. It is not stored inside `ParsedLyrics` or the final semantic lyric document.

### `ResolvedLyrics`

Selected semantic lyrics plus source provenance, acquisition kind, selected candidate identity, and diagnostics needed by cache and playback. It does not expose Lyricify types.

### Replaceable capabilities

The architecture MUST expose replaceable seams for source search/fetch, payload decode, and payload parse. One concrete adapter MAY implement multiple seams; interface-per-node class proliferation is not required.

## Lyricify 0.2.0 Integration Boundary

Use the official package:

```xml
<PackageReference Include="Lyricify.Lyrics.Helper" Version="0.2.0" />
```

Do not substitute `Lyricify.Lyrics.Helper.Jayfunc`; it is a different package identity and version stream.

Lyricify 0.2.0 MAY own:

- Provider-specific HTTP API calls and response DTOs at the adapter edge.
- QQ and KRC lyric decryption.
- LRC, QRC, KRC, YRC, and TTML format parsing.
- Format-specific extraction such as provider-supplied syllables, TTML translation metadata, background vocals, duet alignment, writers, and information-line metadata.
- Explicitly selected format-specific optimizations covered by TaskbarLyrics regression fixtures.

Lyricify 0.2.0 MUST NOT own:

- Original track identity.
- Query-variant policy.
- Cross-provider candidate matching.
- Provider trust order.
- Final source selection.
- TaskbarLyrics cache keys or cache payloads.
- Translation-engine orchestration.
- Playback projection or WebView contracts.
- Durable TaskbarLyrics domain models.

Known 0.2.0 integration constraints that MUST be accommodated:

- `TypeHelper.GetLyricsTypes` does not provide a usable general detector in the released source.
- `ParseHelper.ParseLyrics(string)` does not provide a usable generic return path in the released source.
- Some raw enum variants such as full provider JSON require provider-specific extraction before format parsing.
- Provider API calls do not consistently accept `CancellationToken`.
- Helper uses shared static HTTP state in its provider layer, so concurrency must be controlled externally.
- Helper models are mutable and do not express the TaskbarLyrics distinction between provider-supplied and synthetic fine-grained timing.

## Four-Source Capability Contract

| Source | Search integration | Payload acquisition | Decode | Explicit parse | Required retained tracks/capabilities |
| --- | --- | --- | --- | --- | --- |
| QQ Music | Lower-level QQ search API returning all candidates | `GetLyricsAsync` with provider ID, `GetLyric` fallback where required | Helper QRC support | QRC preferred, LRC fallback | Original, existing embedded translation compatibility, provider-supplied syllables |
| Netease | `SearchNew` as the primary supported search path, with a tested fallback only if needed | Lyric endpoint exposing YRC/LRC and related tracks | None beyond provider response handling | YRC preferred, LRC fallback | Original, existing Tlyric/Ytlrc translation compatibility, provider-supplied syllables; Romalrc/Yromalrc processing is deferred |
| Kugou | Song search followed by lyric-candidate search | Candidate ID and access key | Helper KRC download/decryption | KRC | Original, embedded translation, provider-supplied syllables; MUST NOT rebuild as plain LRC |
| LRCLIB | Structured lower-level API search using planned fields | Search payload or stable-ID fetch | None | Synced LRC when present; plain lyrics as unsynced fallback | Original, instrumental signal, line timing when present |

The generic Lyricify `SearchHelper.Search` and the current 0.2.0 `LRCLIBSearcher` query-string splitting MUST NOT define TaskbarLyrics search or candidate policy.

## Compatibility Requirements

- `TaskbarLyrics.Core` remains platform-neutral and MUST NOT gain WPF, WebView2, WinForms, or native-window dependencies.
- Cross-domain construction remains in `AppCompositionRoot`.
- Existing `settings.json` fields and read-time migration behavior MUST remain valid.
- The existing WebView V1 envelope `{ version: 1, type, payload }` MUST remain valid; any playback payload extension must reject incompatible versions safely.
- Existing manual song mappings, preferred-provider mappings, pure-music mappings, local media indexing, per-player offsets, and per-track offsets MUST remain usable.
- Existing caches MUST either be read through an explicit compatible migration or ignored through a cache-version change; they MUST NOT be silently interpreted under new timing or track semantics.
- Local lyrics and local cover lookup MUST continue sharing `ILocalMediaIndex`.
- User-visible fallback to line-synced lyrics MUST remain available when fine-grained timing is absent.

## Acceptance Criteria

### Current Core Delivery

1. All four current online sources can execute through the same orchestration pipeline while using Lyricify 0.2.0 for every supported integration node.
2. No Lyricify search result, provider response, `LyricsData`, line, or syllable type crosses into selection, cache, playback, App, or WebView contracts.
3. Search completion order and current-player source do not change the fixed initial trust result `QQMusic > Kugou > Netease > LRCLIB`.
4. A higher-trust source with a rejected song identity cannot win; a lower-trust valid source can be selected after the higher source becomes terminal.
5. A lower-trust source with a higher identity score cannot override an already admitted higher-trust source.
6. QQ QRC, Kugou KRC, and Netease YRC fixtures retain provider-supplied syllable start/end timing after normalization and caching.
7. Provider-supplied syllables are never replaced by synthetic equal-duration characters.
8. LRCLIB synced lyrics retain line timing and plain lyrics remain usable as an unsynced fallback.
9. Existing embedded translation display remains available, but no new translation, transliteration, or external translation feature is required.
10. Explicitly marked information lines are hidden from normal playback while remaining available in semantic content; unmarked lines are not newly hidden by heuristics.
11. Local lyrics short-circuit online lookup when valid, and an explicitly mapped `PreferredProvider` remains a hard binding whose failure does not fall back to another online source.
12. Cache keys do not persist relaxed query strings as stable media identity and cache versions change with new parsing semantics.
13. Cancellation or track replacement prevents late Provider results from changing the current lyric selection.
14. A test-only fifth source unsupported by Lyricify can supply a custom search/fetch or parser implementation without copying the complete four-source flow.
15. Existing settings, song mappings, local lyrics, offsets, line-level playback, embedded translation visibility, and WebView V1 behavior remain compatible.
16. The solution builds with zero warnings and the repository verification suite passes after implementation.

### Deferred Word-Scanning Delivery

1. Playback consumes the Core-owned provider-supplied syllable model without referencing Lyricify types.
2. QRC, KRC, and YRC fixtures drive the expected active syllable and fine-grained progress.
3. Line-timed lyrics retain line-level fallback and do not receive synthetic per-character timing.
4. Player and track offsets affect line and syllable selection consistently.

### Deferred Translation Delivery

1. Original, Translation, and Transliteration tracks can be represented independently and associated through format-native relationships where available.
2. External translation, if later selected, runs only after final source selection and cannot invalidate usable original lyrics.
3. Translation cache identity includes source text, languages, engine, and engine version where known.

## Testing Decisions

- Tests MUST assert observable domain behavior rather than the internal shape of Lyricify DTOs.
- Network behavior MUST be tested through replaceable source seams or recorded synthetic response fixtures; the standard verification suite MUST NOT depend on live provider availability.
- Add synthetic, copyright-safe fixtures for LRC, QRC, KRC, YRC, and TTML.
- Current Core parser fixtures MUST cover empty input, malformed time tags, offset handling, multiple line timestamps, non-monotonic input, zero-duration syllables, mixed line/word timing, explicit information-line markers, and pure music.
- Translation, transliteration, background-vocal, and cross-track alignment fixture expansion belongs to the deferred translation delivery except where needed to preserve current embedded translation behavior.
- Provider-adapter tests MUST cover multiple search candidates, missing lyrics on the best candidate, fallback candidate fetch, malformed provider response, direct SongId lookup, timeout, cancellation, and late completion.
- Query-plan tests MUST cover punctuation, featuring artists, multiple artists, simplified/traditional variants, diacritics, bracket suffixes, and semantic version tags.
- Identity tests MUST preserve existing version-conflict, duration, artist-token, and title-admission protections.
- Selection tests MUST inject several trust orders and prove deterministic selection independent of completion order and match-score differences above the admission threshold.
- Cache tests MUST cover stable Provider IDs, raw hashes, parser/normalization versions, invalid entry removal, old-version rejection, and non-persistence of fuzzy matches.
- Current Core tests MUST prove provider-supplied timing survives parsing, normalization, and cache round trips without being synthesized or overwritten.
- Fine-grained playback and consistent line/syllable offset tests belong to the deferred word-scanning delivery.
- Existing line-level playback and embedded translation visibility MUST remain covered during the Core delivery.
- Cancellation tests MUST distinguish user/track cancellation, provider timeout, provider failure, and ignored late results.
- Implementation verification MUST run `scripts/verify.ps1`, `dotnet build TaskbarLyrics.sln`, and `git diff --check`; runnable behavior changes require restarting the local app for manual validation.

## Manual Validation Decisions

- Validate at least one representative track from each of QQ Music, Netease, Kugou, and LRCLIB.
- Include a track where the current player's official source is not the highest-trust valid source.
- Include QRC, KRC, and YRC provider-supplied word timing and verify through Core diagnostics or tests that segment timing is retained; visual scanning is validated only in the deferred word-scanning delivery.
- Include a line-synced-only result and verify graceful fallback.
- Confirm existing embedded translation display does not regress; new transliteration behavior is deferred.
- Include explicitly marked and unmarked information lines and verify that only explicitly marked lines are hidden from normal playback.
- Change tracks during an in-flight search and confirm the old result never replaces the new track.
- Validate manual mapping, pure-music mapping, local lyrics, per-player offset, and per-track offset.
- Restart the application and confirm compatible stable-ID cache reuse without stale fuzzy selection.

## Out of Scope

- Audio-aware or human-feedback-based automatic timeline correctness scoring.
- Treating metadata completeness, translation presence, or word timing presence as a universal cross-source quality score.
- A user-facing Provider-order settings page in the first implementation governed by this Spec.
- New online sources beyond QQ Music, Netease, Kugou, and LRCLIB.
- A public third-party Provider plugin SDK.
- Replacing the WebView UI framework or changing the WebView V1 envelope version solely for this work.
- Persisting fuzzy candidate selection without a stable media or Provider identity.
- Guaranteeing physical cancellation of Helper HTTP calls that expose no cancellation token.
- WebView word-by-word scanning in the current Core delivery.
- Synthetic word or character timing in the current Core and initial word-scanning deliveries.
- New translation-track architecture, transliteration generation, external translation engines, or translation-cache implementation in the current Core delivery.

## Resolved Product Decisions

The following decisions are normative and must be carried into the implementation Plan:

1. **Initial online source trust order**: `QQMusic > Kugou > Netease > LRCLIB`.
2. **Trust-order ownership**: maintain one injectable Core policy using stable Provider IDs; do not add a user-facing order setting in the first implementation.
3. **Local precedence**: valid local lyrics short-circuit online sources.
4. **Manual preferred Provider**: an explicit `PreferredProvider` mapping is a hard binding; failure does not trigger another online source.
5. **Word-scanning sequence**: first complete and verify Core timing acquisition, parsing, normalization, storage, and output; implement playback/WebView word scanning afterward.
6. **Synthetic timing**: do not generate or render synthetic per-character timing in the current Core or initial word-scanning delivery.
7. **Translation sequence**: preserve existing embedded translation behavior during Core migration; defer multi-track translation, transliteration generation, external translation, and translation cache work to a later translation delivery.
8. **Information lines**: hide a line from normal playback only when it has an explicit parser/Provider information-line marker; retain it in semantic content and diagnostics. Do not add heuristic-only hiding in the current Core delivery.

## Context Snapshot for Future Development Environments

This section is informative and exists so a new development environment can reconstruct why this Spec exists. Normative behavior remains defined by the sections above.

- The current package reference is `Lyricify.Lyrics.Helper` 0.1.4 in `TaskbarLyrics.Core/TaskbarLyrics.Core.csproj`.
- Current QQ, Netease, and Kugou behavior is combined in `TaskbarLyrics.Core/Services.LyricifyLyricProvider.cs`.
- Current LRCLIB behavior is implemented separately in `TaskbarLyrics.Core/Services.LrcLibSmtcLyricProviderBase.cs` and `TaskbarLyrics.Core/Services.GenericSmtcLyricProvider.cs`.
- Current orchestration and official-source preference live in `TaskbarLyrics.Core/Services.LyricProviderRegistry.cs`, `LyricSourceRoutingPolicy.cs`, and `LyricMatchingPolicy.cs`.
- Current matching is implemented in `TaskbarLyrics.Core/Utilities/LyricMatcher.cs`.
- Current semantic content and score are coupled in `Models.LyricDocument.cs`; syllables are represented by `Models.LyricSyllable.cs`.
- Current playback projection is implemented in `TaskbarLyrics.Core/Services.LyricSyncService.cs` and does not consume syllables for word-level output.
- Provider construction occurs in `TaskbarLyrics.App/AppCompositionRoot.cs`.
- Existing regression coverage includes `LyricProviderBaseTests`, `LyricProviderRegistryTests`, `LyricMatcherTests`, `LyricSourceRoutingPolicyTests`, `LyricCacheStoreTests`, and `LyricSyncServiceTests`.
- `docs/TaskbarLyrics-vs-BetterLyrics-歌词Provider全链路差异报告.md` is background material, not a normative source. Some statements in that report are stale or inaccurate; current source code, this Spec, repository contracts, and official Lyricify 0.2.0 source take precedence.
- Official dependency references: <https://github.com/WXRIW/Lyricify-Lyrics-Helper/releases/tag/v0.2.0> and <https://www.nuget.org/packages/Lyricify.Lyrics.Helper/0.2.0>.

## Further Notes

- “官方源”是查询亲和性，不是质量担保。
- “Provider-supplied timing”表示时间来自歌词载荷，不表示 TaskbarLyrics 已与音频进行客观校验。
- 人工可信顺序是唯一的跨源质量排序依据；身份分数只是准入门。
- 节点可替换性比“是否由 Helper 接管”更重要。当 Helper 同时支持多个节点时可以全部使用，但 TaskbarLyrics 仍拥有节点契约和组合方式。
