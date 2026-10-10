# QQ 音乐歌词通道：新旧实现对比与旧版失效分析

- 对比对象：TaskbarLyrics `main`（改动前，基于 Lyricify 0.2.0 封装） vs 本次改动（Core 内自持传输层）
- 测试时间：2026-10-10，同一台机器、同一出口 IP、同一时段
- 相关改动：`TaskbarLyrics.Core/Services.QqMusicLyrics.cs`、`TaskbarLyrics.Core/Services.QqMusicApiClient.cs`（新增），`Services.LyricifySources.cs`（移除旧 QQ 实现），`Services.LyricifyPayloadDecoder.cs`（新增 QRC 解包）

---

## 1. 一句话结论

旧版把 QQ 音乐的**唯一**搜索路径放在 `u.y.qq.com/cgi-bin/musicu.fcg` 上，而该接口在连续/密集请求下会返回 `req_1.code=2001` + 空歌曲列表；旧代码对这种"HTTP 200 + 空列表"既不重试也不告警，直接当作"本来源无歌词"，于是 QQ 音乐在当前歌曲的竞争中静默消失。新实现改用 `c.y.qq.com/soso/fcgi-bin/search_for_qq_cp`，在完全相同的压力下稳定返回候选；同时修正了"把字母 songmid 当数字 songid 用"的直连语义。

---

## 2. 旧实现（改动前）的连接方式

| 环节 | 旧实现 |
| --- | --- |
| 搜索 | `ProviderHelper.QQMusicApi.Search(...)` → POST `https://u.y.qq.com/cgi-bin/musicu.fcg`，body 只有 `req_1`（`DoSearchForQQMusicDesktop` / `music.search.SearchCgiService`）；请求头为 Chrome 63 的 UA 与 `Referer: https://c.y.qq.com/`，无 cookie、无签名、无 `comm` 块；结果取 `Req_1.Data.Body.Song.List` |
| 候选 | `CandidateId = song.Id`（数字 songid），`songmid` 存入 `FetchMetadata["mid"]` |
| 歌词 | `ProviderHelper.QQMusicApi.GetLyricsAsync(id)` → POST `lyric_download.fcg`（`version=15&miniversion=82&lrctype=4&musicid=<id>`）；Lyricify 内部完成：剥 `<!--`/`-->` → XmlDocument → 找 `content`/`contentts`/`contentroma` → 对 hex 用自实现 3DES + inflate 解密 → 解压结果是 `QrcInfos` XML → 取 `Lyric_1` 的 `LyricContent` → 返回明文 QRC 文本；因此载荷被标记为**未加密** |
| 兜底 | `ProviderHelper.QQMusicApi.GetLyric(mid)` → POST `lyric/fcgi-bin/fcg_query_lyric_new.fcg`，Lyricify 解析 JSONP + base64 → 普通 LRC |
| 直连 | `ProviderSongIdPolicy.CanUseDirectSongId`（只要求来源是 QQMusic 且有 `SongId`）→ 直接把 SMTC 的 `QQ-` 值当 `musicid` |

旧版搜索的核心代码（改动前）：

```csharp
var response = await LyricifyTask.WaitWithProxyRecoveryAsync(
    () => ProviderHelper.QQMusicApi.Search(
        BuildQuery(variant),
        Lyricify.Lyrics.Providers.Web.QQMusic.Api.SearchTypeEnum.SONG_ID),
    token);
var songs = response?.Req_1?.Data?.Body?.Song?.List ?? [];   // 空列表不会抛异常
return songs.Select(...).Where(...).Cast<SourceTrackCandidate>().ToArray();
```

---

## 3. 新实现的连接方式

| 环节 | 新实现 |
| --- | --- |
| 搜索 | GET `https://c.y.qq.com/soso/fcgi-bin/search_for_qq_cp?w=<歌名 歌手>&format=json&n=10&p=1&t=0&remoteplace=txt.yqq.song&platform=yqq.json`，取 `data.song.list[]`（`songid`/`songmid`/`songname`/`singer[].name`/`albumname`/`interval`） |
| 候选 | `CandidateId = songid`（数字，纯 0 与字母 id 被过滤），`songmid` 存入 `FetchMetadata["mid"]` |
| 歌词 | POST `lyric_download.fcg`（`version=15&miniversion=100&lrctype=4&musicid=<数字 songid>`）→ 正则取 `content`/`contentts`/`contentroma` 的 CDATA；`content` 是纯 hex 时标为 `Qrc` + `IsEncrypted=true`，否则按明文 LRC |
| 解码 | 仍由既有的 `LyricifyPayloadDecoder` 完成：QRC 用同一套 QQ 专用 3DES + inflate 解密（与旧版算法完全一致），解密后再从 `QrcInfos` 里取出 `Lyric_1` 的 `LyricContent` |
| 兜底 | 空内容时按 `songmid` 调用 `fgc_query_lyric_new.fcg`，本仓库实现 JSONP + base64 |
| 直连 | 仅当 `QQ-` 值为纯数字（且非 0）才直连；字母 songmid 回退关键词搜索 |

---

## 4. 实测：旧接口到底怎么"不行"的

### 4.1 复现证据（2026-10-10，Node 与 .NET 两种客户端，结果一致）

**A. 密集请求下旧接口被拒，新接口正常（同一分钟）**

```
旧接口 musicu.fcg：12 次无间隔并发 → 0/2001 | 0/2001 | ... ×12（全部 0 首歌曲）
                   立即再试 3 次    → 0/2001 | 0/2001 | 0/2001
新接口 soso      ：12 次无间隔并发 → 10/0 | 10/0 | ... ×12（全部 10 首候选）
```

**B. 冷却后恢复一次，稍密集再次被拒**

```
等待 30 秒后第 1 次：15/0   ← 恢复
第 2、3 次（间隔 2 秒）：0/2001 | 0/2001
```

**C. 低频时旧接口其实能成功**（说明不是"永久不可用"，而是"频率稍高就被拒"）

```
旧接口 6 次、间隔 300ms：15/0 ×6
旧接口 3 次、间隔 1.5s ：15/0 ×3
```

**D. .NET 客户端同样复现**：改动前的 C# 对比探针连续 3 次搜索（嘘月/小城夏天/Flower Dance）全部 `Req_1.Code=2001`、`Song.List` 为空。

> 说明：旧版请求头（UA、Referer）在新旧测试中都做过组合变化，2001 与请求头无关，主要跟随"请求密度 + 距上次密集请求的时间"。

### 4.2 为什么这一次拒绝会让 QQ 音乐整首歌都失效

`LyricResolutionCoordinator` 对"搜索返回 0 个候选"的处理（现网代码）：

```csharp
var candidates = await source.SearchAsync(searchPlan, token);
if (candidates.Count == 0)
{
    return new SourceOutcome(source.ProviderId, LyricSourceTerminalState.NoLyrics, null, "no-candidates");
}
```

- 终态是 `NoLyrics`，`IsTransientFailure = false` → **不会**进入 2026-10-10 新增的"瞬时失败最多重试两次"机制（该机制只对异常类瞬时失败生效）。
- 只有异常才会 `Log.Warn`，所以"被拒"这件事在日志里完全不可见。
- 旧实现只有一个搜索接口：没有第二个搜索通道，也没有"空结果换个接口再试"。

**用户可见后果**：QQ 音乐正在播放的歌，歌词改由网易云/酷狗/LRCLIB 提供（标题歌手匹配可能更松、没有 QQ 的翻译、没有逐字时间轴），或者直接显示"未找到歌词"。

**为什么感觉"以前就是坏的"**：`LyricSearchStageExecutor` 会按变体顺序补发请求（exact → normalized → primary-artist → relaxed-title → version-relaxed-title），切歌、手动选源、诊断都会各发一次；短时间内的连续调用正好落在这个接口的敏感区间，一旦进入 2001 窗口，之后数十秒内都是空结果。

### 4.3 次因：直连 ID 把 songmid 当 songid

旧版直连路径不区分数字/字母，直接把 SMTC 的 `QQ-` 值当 `musicid`。实测把 songmid 传给歌词接口：

```
musicid=003WkhSf2FOUwq
→ <check musicid="0"><downloadtag>0</downloadtag></check>
  <content><![CDATA[]]></content><contentts><![CDATA[]]></contentts><contentroma><![CDATA[]]></contentroma>
```

服务端直接把 `musicid` 归零，三个字段全空 → QRC 拿不到，旧代码只能退到 songmid 兜底拿逐行 LRC（实测 51 行）。而 QQ 侧其实有能用的 mid→id 转换接口（改动前未使用）：

```
GetSong("003WkhSf2FOUwq") → code=0, 1 条结果, id=295618479
```

新实现的做法是不做 mid→id 转换，而是判定"非纯数字 → 走关键词搜索"，实测对同一首歌取回完整 QRC。

---

## 5. 逐项差异对照

| 维度 | 旧实现 | 新实现 | 影响 |
| --- | --- | --- | --- |
| 搜索接口 | `u.y.qq.com/cgi-bin/musicu.fcg`（无 `comm`/cookie/签名） | `c.y.qq.com/soso/fcgi-bin/search_for_qq_cp` | 密集请求下旧接口 12/12 被拒、新接口 12/12 正常 |
| 空结果语义 | HTTP 200 + 空列表 → `NoLyrics/no-candidates`，不重试、不告警 | 同样不会因空列表重试，但搜索本身不再被拒 | 旧版 QQ 源会间歇性"整首消失" |
| 直连 ID | `QQ-` 值直接当 `musicid` | 纯数字校验，非数字退回搜索 | 字母 songmid 场景从"只有逐行 LRC"变为"完整 QRC" |
| 兜底 LRC | Lyricify `GetLyric(mid)` | 本仓库等价实现（同接口同参数） | 行为不变（实测 51 行逐行歌词） |
| 解码分层 | 库内部解密，载荷 `IsEncrypted=false` | 载荷 `IsEncrypted=true`，管道 decoder 解密并解包 `LyricContent` | 分层清晰；两版解出的 QRC 文本实测一致（同一首歌 8231 字符、内容相同） |
| 解密算法 | QQ 专用 3DES + inflate | 同一个解密实现（复用 Lyricify 的 QRC 解密器） | 无算法差异；标准 .NET TripleDES 解密结果是错的（已实测字节对比） |
| 压缩容器 | 库内部 SharpZipLib 自动识别 zlib 头 | 同上（doc 说的 "raw deflate" 不准确，实际是 zlib 包装） | 无行为差异 |
| 候选数量 | musicu 每页 20 条 | soso `n=10` | 候选集合略有差异，评分链路不变 |
| 响应容错 | 依赖库反序列化，异常向上抛 → 整源失败 | 手写 `JsonDocument` + 空值判断，畸形/空响应返回空列表 | 不再因解析异常丢整源 |
| 依赖 | Lyricify 的 QQ 搜索 + 歌词 API | 只复用其 QRC 解密器（可选） | QQ 通道与第三方库解耦 |
| 请求超时 | 库默认 HttpClient（100 s），源超时 5 s | 自持 HttpClient（同默认），源超时仍 5 s | 超时分类不变 |
| 失败重试 | 库调用层"网络异常重试一次" | 同样"网络异常重试一次" | 不变 |

---

## 6. 结论与边界

1. **主因是搜索接口的静默拒绝**：旧接口在连续请求下返回 `2001` + 空列表，旧实现既没有备用接口也没有对空结果的重试和告警，所以表现为"QQ 音乐有时候完全没有歌词/歌词来自别的源"。
2. **新接口在相同压力下稳定**：12 次并发、6 次连发、多次随机抽样都正常返回 10 条候选，且响应结构更简单（`data.song.list`）。
3. **次因是直连 ID 语义**：字母 songmid 会被服务端归零，导致拿不到 QRC；新实现改为回退搜索。
4. **结论边界**：以上是本次环境（同一出口 IP、同一时段）的可复现结果，不排除与出口 IP/时段相关。旧接口失败时表现为 HTTP 200，需要在用户机器上用第 7 节的方法复核。
5. **期望管理**：当前显示链路只用行级时间与行内进度，`Segments`（逐字时间）没有参与渲染（`TaskbarLyrics.App/Web/Lyrics` 不消费音节数据）。因此 QRC 与 LRC 的差别目前体现在歌词数据完整度、缓存内容和未来逐字渲染的能力上，而不是画面上的卡拉 OK 高亮。
6. **新接口也不是"绝对可靠"**：`soso` 是 QQ 的老接口；歌词下载接口在高频请求下仍偶发 502/空响应（本次实测遇到一次），这类瞬时失败仍走既有重试策略，429 不在自动重试范围。

---

## 7. 自己动手复核

**旧接口（会被限流的那条）**

```bash
curl -s 'https://u.y.qq.com/cgi-bin/musicu.fcg' \
  -H 'User-Agent: Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36' \
  -H 'Referer: https://c.y.qq.com/' -H 'Content-Type: application/json' \
  --data '{"req_1":{"method":"DoSearchForQQMusicDesktop","module":"music.search.SearchCgiService","param":{"num_per_page":"20","page_num":"1","query":"嘘月 ヨルシカ","search_type":0}}}'
```

- 反复快速执行几次，观察 `req_1.code` 从 `0` 变成 `2001`、`req_1.data.body.song.list` 变空。

**新接口（本次替代的那条）**

```bash
curl -s 'https://c.y.qq.com/soso/fcgi-bin/search_for_qq_cp?w=%E5%98%98%E6%9C%88%20%E3%83%A8%E3%83%AB%E3%82%B7%E3%82%AB&format=json&n=10&p=1&t=0&remoteplace=txt.yqq.song&platform=yqq.json' \
  -H 'User-Agent: Mozilla/5.0' -H 'Referer: https://y.qq.com/'
```

- 同样快速反复执行，观察 `code=0` 且 `data.song.list` 稳定返回 10 条。

**歌词接口需要数字 songid**

```bash
# 数字 songid：正常返回 hex 的 content
curl -s 'https://c.y.qq.com/qqmusic/fcgi-bin/lyric_download.fcg' \
  -H 'User-Agent: Mozilla/5.0' -H 'Referer: https://y.qq.com/' \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data 'version=15&miniversion=100&lrctype=4&musicid=295618479'

# 字母 songmid：musicid 被归零，三个 CDATA 全空
curl -s 'https://c.y.qq.com/qqmusic/fcgi-bin/lyric_download.fcg' \
  -H 'User-Agent: Mozilla/5.0' -H 'Referer: https://y.qq.com/' \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data 'version=15&miniversion=100&lrctype=4&musicid=003WkhSf2FOUwq'
```

**应用内确认直连值形态**：播放 QQ 音乐时看日志中的

```
SMTC QQMusic metadata: Album='...', SongId='...', Genres='...'
```

`SongId` 是纯数字 → 直连命中原生 QRC；是字母 → 新实现会转走关键词搜索（旧实现会退到逐行 LRC）。

---

## 8. 附：本次改动落地后的验证结果

| 检查项 | 结果 |
| --- | --- |
| `dotnet build TaskbarLyrics.sln` | 0 警告 / 0 错误 |
| `scripts/verify.ps1`（Full） | Web 106、App 438、Core 257 全通过，生产资源边界、代码格式、验证输出与重启脚本回归检查通过 |
| 新增回归用例 | `QqMusicLyricSourceTests`：真实抓包的搜索/歌词/兜底夹具、CDATA/hex/空载荷分类、JSONP+base64、直连 ID 判定、"加密 QRC → 解密解包 → 逐字解析 + 译文对齐"链路 |
| 真实网络端到端 | 搜索 10 候选（250~600 ms）；歌词下载+解码 123~178 ms；46 行、444~453 个逐字片段、43 行译文；数字 songid 直连与 songmid 回退拿到同一份 QRC；兜底 LRC 46 行逐行歌词 |
| 仍需人工复核 | 真机 QQ 音乐播放的滚动与翻译、`QQ-` 实际取值、老接口长期可用性 |
