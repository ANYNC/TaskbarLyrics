    const sourceCatalogDefaults = [
      { id: "qqmusic", name: "QQ 音乐", adapter: "QQMusic", settingKey: "enableQQMusic", icon: "../../Assets/PlayerIcons/QQ音乐.png" },
      { id: "netease", name: "网易云音乐", adapter: "Netease", settingKey: "enableNetease", icon: "../../Assets/PlayerIcons/网易云音乐.png" },
      { id: "kugou", name: "酷狗音乐", adapter: "Kugou", settingKey: "enableKugou", icon: "../../Assets/PlayerIcons/酷狗音乐.png" },
      { id: "spotify", name: "Spotify", adapter: "Spotify", settingKey: "enableSpotify", icon: "../../Assets/PlayerIcons/spotify.png" }
    ];

    const selectOptions = {
      spectrumDisplayMode: [{ value: "Disabled", label: "关闭" }, { value: "PureMusicOnly", label: "仅纯音乐时" }, { value: "PureMusicOrNoLyrics", label: "纯音乐或无歌词时" }, { value: "Always", label: "始终显示" }],
      fontFamily: [],
      fontWeight: [{ value: "Light", label: "细体" }, { value: "Normal", label: "常规" }, { value: "Medium", label: "中等" }, { value: "SemiBold", label: "半粗体" }, { value: "Bold", label: "粗体" }],
      foregroundColorMode: [{ value: "System", label: "跟随系统" }, { value: "Dark", label: "深色" }, { value: "Light", label: "浅色" }, { value: "Custom", label: "自定义" }],
      horizontalAnchor: [{ value: "Left", label: "左侧" }, { value: "Center", label: "居中" }, { value: "Right", label: "右侧" }],
      trackOffsetSourceFilter: [{ value: "All", label: "全部歌词源" }],
      trackOffsetSort: [{ value: "updated", label: "最近修改" }, { value: "title", label: "歌曲名称" }, { value: "offset", label: "偏移量" }]
    };
    const presetColors = ["#FFFFFF", "#A1A1AA", "#18181B", "#EF4444", "#F97316", "#EAB308", "#22C55E", "#06B6D4", "#3B82F6", "#A855F7"];

    const pageMeta = {
      sources: ["播放源", "选择需要监听的音乐软件，并调整识别优先级。"],
      shortcuts: ["快捷键", "设置在其他应用前台时控制播放器的全局组合键。"],
      lyrics: ["歌词", "控制歌词显示、翻译和频谱策略。"],
      trackOffsets: ["单曲偏移", "调整当前歌曲同步，并管理按歌词源保存的偏移。"],
      displayArea: ["显示与外观", "调整歌词显示的尺寸、文字、窗口外观与位置，并在歌词窗口中即时检查效果。"],
      general: ["常规", "管理启动行为与界面主题。"],
      advanced: ["高级", "用于诊断播放同步问题和维护缓存数据。"],
      lyricDiagnostics: ["歌词诊断", "针对当前 SMTC 歌曲查看歌词检索候选、匹配分和最终结果。"],
      about: ["关于", "查看版本、许可证与项目技术信息。"]
    };

    let state = null;
    let sourceCatalog = sourceCatalogDefaults.map(item => ({ ...item, enabled: false }));
    let toastTimer;
    let draggedSourceId = null;
    let pageAnimations = [];
    let pageTransitionToken = 0;
    let activeSelectTrigger = null;
    let activeSelectIndex = -1;
    let activeHotkeyRecorder = null;
    let colorDraft = { h: 0, s: 0, v: 1, hex: "#FFFFFF" };
    let colorPointerActive = false;
    let updateState = "idle";
    const reducedMotionQuery = window.matchMedia("(prefers-reduced-motion: reduce)");
    let repositoryUrl = "";
    let updateReleaseUrl = "";
    let activePlayerSourceId = null;
    const TRACK_OFFSET_PAGE_SIZE = 50;
    const TRACK_OFFSET_SEARCH_DEBOUNCE_MS = 200;
    let trackOffsetData = { currentTrack: null, entries: [], page: 1, pageCount: 1, totalCount: 0, unfilteredCount: 0 };
    let visibleTrackOffsetEntries = [];
    let trackOffsetPage = 1;
    let trackOffsetRequestId = 0;
    let trackOffsetSearchTimer;
    let expandedTrackOffsetKey = null;
    let pendingDeleteTrackOffsetKey = null;
    let focusCurrentTrackOnNextRender = false;
    const pendingRangePreviews = new Map();
    let rangePreviewFrame = 0;
    let announceNextLayoutPreview = false;
    let lyricDiagnosticsState = { status: "idle", track: null, report: null, message: "" };
    let pendingSpectrumDisplayMode = null;
    let spectrumCaptureState = { state: "disabled", message: "" };

    const $ = selector => document.querySelector(selector);
    const $$ = selector => Array.from(document.querySelectorAll(selector));
    const escapeHtml = value => String(value).replace(/[&<>"]/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;" }[char]));
    const bridge = { post(message) { window.chrome?.webview?.postMessage(JSON.stringify(message)); } };

    bridge.post = window.taskbarLyricsBridge.post.bind(window.taskbarLyricsBridge);

    function renderSources() {
      const grid = $("#sourceGrid");
      grid.innerHTML = sourceCatalog.map(source => `
        <article class="source-card ${source.enabled ? "enabled" : ""}">
          <span class="source-logo" aria-hidden="true"><img src="${escapeHtml(source.icon)}" alt=""></span>
          <span class="source-info"><strong>${escapeHtml(source.name)}</strong><small>${source.enabled ? "已启用" : "已停用"} · ${formatPlayerOffset(getPlayerOffset(source))}</small></span>
          <button class="source-settings-button" type="button" data-player-settings="${escapeHtml(source.id)}" aria-label="打开 ${escapeHtml(source.name)} 设置"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 15.25A3.25 3.25 0 1 0 12 8.75a3.25 3.25 0 0 0 0 6.5Z" fill="none" stroke="currentColor" stroke-width="1.7"/><path d="M19.1 13.3a7.5 7.5 0 0 0 0-2.6l2-1.55-2-3.46-2.5 1a7.6 7.6 0 0 0-2.25-1.3L14 2.75h-4l-.35 2.64A7.6 7.6 0 0 0 7.4 6.7l-2.5-1-2 3.46 2 1.55a7.5 7.5 0 0 0 0 2.6l-2 1.55 2 3.46 2.5-1a7.6 7.6 0 0 0 2.25 1.3l.35 2.64h4l.35-2.64a7.6 7.6 0 0 0 2.25-1.3l2.5 1 2-3.46-2-1.55Z" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"/></svg></button>
        </article>`).join("");
      const enabled = sourceCatalog.filter(source => source.enabled).length;
      $("#sourceCount").textContent = `${enabled} / ${sourceCatalog.length} 个已启用`;
    }

    function getPlayerOffset(source) {
      const value = Number(state?.playerLyricOffsets?.[source.adapter]);
      return Number.isFinite(value) ? Math.max(-5000, Math.min(5000, Math.round(value))) : source.defaultOffset;
    }

    function formatPlayerOffset(value) {
      if (value > 0) return `提前 ${value} ms`;
      if (value < 0) return `延后 ${Math.abs(value)} ms`;
      return "同步";
    }

    const normalizeTrackOffset = (value, fallback = 0) => window.taskbarLyricsTrackOffsets.normalize(value, fallback);

    function sourceDisplayName(source) {
      const known = sourceCatalogDefaults.find(item => item.adapter.toLowerCase() === String(source ?? "").toLowerCase());
      if (known) return known.name;
      if (String(source).toLowerCase() === "local") return "本地歌词";
      return source || "未知来源";
    }

    const formatTrackDuration = seconds => window.taskbarLyricsTrackOffsets.formatDuration(seconds);

    function formatTrackOffsetDate(value) {
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) return "--";
      return new Intl.DateTimeFormat("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" }).format(date);
    }

    function trackOffsetKeyId(key) {
      return JSON.stringify([
        key?.normalizedTitle ?? "",
        key?.normalizedArtist ?? "",
        key?.normalizedLyricSource ?? "",
        Number(key?.durationBucketSeconds) || 0
      ]);
    }

    function renderCurrentTrackOffset() {
      const container = $("#currentTrackOffset");
      const badge = $("#currentTrackOffsetBadge");
      const current = trackOffsetData.currentTrack;
      if (!current) {
        badge.textContent = "等待播放";
        container.innerHTML = `<div class="track-offset-empty"><div><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 18V5l10-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="16" cy="16" r="3"/></svg><strong>当前没有可调整的歌曲</strong><small>开始播放并成功获取歌词后，可以在这里调整对应歌词源的同步偏移。</small></div></div>`;
        return;
      }

      const ready = Boolean(current.lyricSourceReady);
      badge.textContent = ready ? sourceDisplayName(current.lyricSource) : "正在检索歌词";
      if (!ready) {
        container.innerHTML = `<div class="track-offset-empty"><div><span class="spinner" aria-hidden="true"></span><strong>${escapeHtml(current.title || "当前歌曲")}</strong><small>歌词源确定后即可调整单曲偏移。</small></div></div>`;
        return;
      }

      const playerOffset = Number(current.playerOffsetMilliseconds) || 0;
      const trackOffset = Number(current.trackOffsetMilliseconds) || 0;
      const effectiveOffset = Number(current.effectiveOffsetMilliseconds) || 0;
      container.innerHTML = `
        <div class="current-track-layout">
          <div class="current-track-identity">
            <strong title="${escapeHtml(current.title)}">${escapeHtml(current.title || "未知歌曲")}</strong>
            <small title="${escapeHtml(current.artist)}">${escapeHtml(current.artist || "未知歌手")} · ${formatTrackDuration(current.durationSeconds)}</small>
            <div class="current-track-source"><span class="track-offset-badge">${escapeHtml(sourceDisplayName(current.sourceApp))}</span><span class="track-offset-badge">歌词源 · ${escapeHtml(sourceDisplayName(current.lyricSource))}</span></div>
          </div>
          <div class="current-track-controls">
            <div class="offset-summary">
              <div class="offset-summary-item"><span>播放器偏移</span><strong>${formatPlayerOffset(playerOffset)}</strong></div>
              <div class="offset-summary-item"><span>单曲偏移</span><strong>${formatPlayerOffset(trackOffset)}</strong></div>
              <div class="offset-summary-item"><span>最终效果</span><strong>${formatPlayerOffset(effectiveOffset)}</strong></div>
            </div>
            <div class="current-track-editor">
              <div class="stepper track-offset-stepper">
                <button type="button" data-current-track-offset-delta="-100" aria-label="当前歌曲歌词延后 100 毫秒">−</button>
                <input id="currentTrackOffsetInput" class="control" type="number" min="-5000" max="5000" step="10" inputmode="numeric" value="${trackOffset}" aria-label="当前歌曲单曲偏移毫秒">
                <button type="button" data-current-track-offset-delta="100" aria-label="当前歌曲歌词提前 100 毫秒">+</button>
              </div>
              <button class="btn ghost small" type="button" data-reset-current-track-offset ${trackOffset === 0 ? "disabled" : ""}>恢复为 0</button>
            </div>
          </div>
        </div>`;

      if (focusCurrentTrackOnNextRender) {
        focusCurrentTrackOnNextRender = false;
        requestAnimationFrame(() => $("#currentTrackOffsetInput")?.focus({ preventScroll: true }));
      }
    }

    function renderTrackOffsetList() {
      const container = $("#trackOffsetList");
      visibleTrackOffsetEntries = trackOffsetData.entries ?? [];
      const totalCount = Number(trackOffsetData.totalCount) || 0;
      const unfilteredCount = Number(trackOffsetData.unfilteredCount) || 0;
      const pageCount = Math.max(1, Number(trackOffsetData.pageCount) || 1);
      trackOffsetPage = Math.min(pageCount, Math.max(1, Number(trackOffsetData.page) || 1));
      $("#trackOffsetCount").textContent = totalCount === unfilteredCount
        ? `${unfilteredCount} 首`
        : `${totalCount} / ${unfilteredCount} 首`;
      $("#clearTrackOffsetsButton").disabled = unfilteredCount === 0;
      const pagination = $("#trackOffsetPagination");
      pagination.hidden = false;
      $("#trackOffsetPageStatus").textContent = `${trackOffsetPage} / ${pageCount}`;
      $("#trackOffsetPreviousPage").disabled = trackOffsetPage <= 1;
      $("#trackOffsetNextPage").disabled = trackOffsetPage >= pageCount;
      container.removeAttribute("aria-busy");

      if (!visibleTrackOffsetEntries.length) {
        const hasRecords = unfilteredCount > 0;
        container.innerHTML = `<div class="track-offset-empty"><div><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h10"/></svg><strong>${hasRecords ? "没有找到匹配的歌曲" : "还没有配置过单曲偏移"}</strong><small>${hasRecords ? "请调整搜索内容或歌词源筛选条件。" : "通过上方当前歌曲区域或托盘入口完成第一次调整。"}</small></div></div>`;
        return;
      }

      container.innerHTML = `
        <div class="track-offset-table-head"><span>歌曲</span><span>歌词源</span><span>单曲偏移</span><span>最近修改</span><span></span></div>
        ${visibleTrackOffsetEntries.map((entry, index) => {
          const isExpanded = expandedTrackOffsetKey === trackOffsetKeyId(entry.key);
          const offset = Number(entry.offsetMilliseconds) || 0;
          return `<div class="track-offset-item">
            <div class="track-offset-row">
              <div class="track-offset-song"><strong title="${escapeHtml(entry.title)}">${escapeHtml(entry.title || "未知歌曲")}</strong><small title="${escapeHtml(entry.artist)}">${escapeHtml(entry.artist || "未知歌手")} · ${escapeHtml(sourceDisplayName(entry.sourceApp))} · ${formatTrackDuration(entry.durationSeconds)}</small></div>
              <span class="track-offset-meta">${escapeHtml(sourceDisplayName(entry.lyricSource))}</span>
              <span class="track-offset-value">${formatPlayerOffset(offset)}</span>
              <span class="track-offset-meta">${formatTrackOffsetDate(entry.updatedAtUtc)}</span>
              <div class="track-offset-actions">
                <button class="track-offset-action" type="button" data-edit-track-offset="${index}" aria-label="调整 ${escapeHtml(entry.title)} 的偏移" aria-expanded="${isExpanded}"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h10M4 17h16M18 4v6M14 14v6"/><circle cx="18" cy="13" r="0"/><path d="M18 4v6M15 7h6M14 14v6M11 17h6"/></svg></button>
                <button class="track-offset-action destructive" type="button" data-delete-track-offset="${index}" aria-label="删除 ${escapeHtml(entry.title)} 的偏移"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M7 7l1 13h8l1-13M10 11v5M14 11v5"/></svg></button>
              </div>
            </div>
            ${isExpanded ? `<div class="track-offset-inline-editor"><span>正值让歌词提前，负值让歌词延后。</span><div class="stepper track-offset-stepper"><button type="button" data-stored-track-offset-delta="-100" data-track-offset-index="${index}" aria-label="歌词延后 100 毫秒">−</button><input class="control" type="number" min="-5000" max="5000" step="10" inputmode="numeric" value="${offset}" data-stored-track-offset-input="${index}" aria-label="${escapeHtml(entry.title)} 单曲偏移毫秒"><button type="button" data-stored-track-offset-delta="100" data-track-offset-index="${index}" aria-label="歌词提前 100 毫秒">+</button></div><span class="track-offset-editor-unit">ms</span></div>` : ""}
          </div>`;
        }).join("")}`;
    }

    function renderTrackOffsets() {
      renderCurrentTrackOffset();
      renderTrackOffsetList();
    }

    function diagnosticString(value, fallback = "--") {
      if (value === null || value === undefined) return fallback;
      const text = String(value).trim();
      return text || fallback;
    }

    function diagnosticDurationSeconds(value) {
      if (typeof value === "number" && Number.isFinite(value)) return Math.max(0, value);
      if (typeof value !== "string") return 0;
      const text = value.trim();
      const numeric = Number(text);
      if (Number.isFinite(numeric)) return Math.max(0, numeric);
      const parts = text.split(":").map(Number);
      if (parts.length === 3 && parts.every(Number.isFinite)) return Math.max(0, parts[0] * 3600 + parts[1] * 60 + parts[2]);
      if (parts.length === 2 && parts.every(Number.isFinite)) return Math.max(0, parts[0] * 60 + parts[1]);
      const iso = /^PT(?:(\d+(?:\.\d+)?)H)?(?:(\d+(?:\.\d+)?)M)?(?:(\d+(?:\.\d+)?)S)?$/i.exec(text);
      if (iso) return Math.max(0, Number(iso[1] || 0) * 3600 + Number(iso[2] || 0) * 60 + Number(iso[3] || 0));
      return 0;
    }

    function formatDiagnosticDuration(value) {
      const seconds = diagnosticDurationSeconds(value);
      if (!seconds) return "--";
      const rounded = Math.round(seconds);
      const minutes = Math.floor(rounded / 60);
      const remainder = String(rounded % 60).padStart(2, "0");
      return `${minutes}:${remainder}`;
    }

    function diagnosticArtists(value, fallback = "--") {
      const artists = Array.isArray(value)
        ? value.filter(item => item !== null && item !== undefined).map(item => String(item).trim()).filter(Boolean)
        : [];
      return artists.length ? artists.join(" / ") : fallback;
    }

    function diagnosticTrackArtist(track) {
      return diagnosticArtists(track?.artists, diagnosticString(track?.artist));
    }

    function diagnosticProviderStateLabel(value) {
      return ({
        Succeeded: "成功",
        IdentityRejected: "身份拒绝",
        NoLyrics: "无歌词",
        InvalidContent: "内容无效",
        Failed: "失败",
        TimedOut: "超时",
        Disabled: "未启用",
        Canceled: "已取消"
      })[value] ?? (value ? String(value) : "未完成");
    }

    function diagnosticProviderStateClass(value) {
      if (value === "Succeeded") return "success";
      if (["Failed", "TimedOut", "InvalidContent", "IdentityRejected"].includes(value)) return "error";
      return "unknown";
    }

    function formatDiagnosticTimestamp(value) {
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) return "";
      return new Intl.DateTimeFormat("zh-CN", { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit" }).format(date);
    }

    function renderLyricDiagnosticsTrack(track) {
      const panel = $("#lyricDiagnosticsTrackPanel");
      const container = $("#lyricDiagnosticsTrack");
      if (!track || typeof track !== "object") {
        panel.hidden = true;
        container.replaceChildren();
        return;
      }

      panel.hidden = false;
      const title = diagnosticString(track.title, "未知歌曲");
      const sourceApp = diagnosticString(track.sourceApp);
      const songId = diagnosticString(track.songId);
      container.innerHTML = `
        <div class="diagnostics-track-item"><span>歌曲</span><strong title="${escapeHtml(title)}">${escapeHtml(title)}</strong></div>
        <div class="diagnostics-track-item"><span>歌手</span><strong title="${escapeHtml(diagnosticTrackArtist(track))}">${escapeHtml(diagnosticTrackArtist(track))}</strong></div>
        <div class="diagnostics-track-item"><span>专辑</span><strong title="${escapeHtml(diagnosticString(track.album))}">${escapeHtml(diagnosticString(track.album))}</strong></div>
        <div class="diagnostics-track-item"><span>播放器</span><strong title="${escapeHtml(sourceApp)}">${escapeHtml(sourceApp)}</strong></div>
        <div class="diagnostics-track-item"><span>时长 / 歌曲 ID</span><strong title="${escapeHtml(`${formatDiagnosticDuration(track.durationSeconds ?? track.duration)} · ${songId}`)}">${escapeHtml(formatDiagnosticDuration(track.durationSeconds ?? track.duration))} · ${escapeHtml(songId)}</strong></div>`;
    }

    function renderLyricDiagnosticsProviders(report) {
      const providers = Array.isArray(report?.providers) ? report.providers : [];
      const container = $("#lyricDiagnosticsProviders");
      $("#lyricDiagnosticsProviderCount").textContent = `${providers.length} 个歌词源`;
      const summary = $("#lyricDiagnosticsReportSummary");
      const preferredProvider = diagnosticString(report?.preferredProvider, "");
      const musicKind = report?.isPureMusic === true ? "纯音乐" : report?.isPureMusic === false ? "常规歌曲" : "歌曲类型：未知";
      summary.innerHTML = `<span>${musicKind}</span><span>首选来源：${escapeHtml(preferredProvider || "未指定")}</span>`;
      if (!providers.length) {
        container.innerHTML = `<div class="diagnostics-selection-empty"><strong>没有收到歌词源结果</strong><small>本次运行没有可展示的来源终态。</small></div>`;
        return;
      }

      container.innerHTML = providers.map(provider => {
        const providerId = diagnosticString(provider?.providerId, "未知来源");
        const providerState = provider?.state ?? null;
        const providerStateClass = diagnosticProviderStateClass(providerState);
        const candidates = Array.isArray(provider?.candidates) ? provider.candidates : [];
        const detail = diagnosticString(provider?.detail, "");
        const selectedBadge = provider?.selected ? `<span class="diagnostics-badge" data-state="selected">最终采用</span>` : "";
        const candidateMarkup = candidates.length
          ? candidates.map(candidate => {
            const admitted = candidate?.isAdmitted === true;
            const score = Number.isFinite(Number(candidate?.score)) ? Number(candidate.score) : null;
            const reasons = Array.isArray(candidate?.rejectionReasons) ? candidate.rejectionReasons.filter(Boolean) : [];
            const metadataKeys = Array.isArray(candidate?.fetchMetadataKeys) ? candidate.fetchMetadataKeys.filter(Boolean) : [];
            const title = diagnosticString(candidate?.title, "未知候选");
            const artist = diagnosticArtists(candidate?.artists, "未知歌手");
            return `<article class="diagnostics-candidate">
              <div class="diagnostics-candidate-main">
                <div class="diagnostics-candidate-title"><strong title="${escapeHtml(title)}">${escapeHtml(title)}</strong><small title="${escapeHtml(artist)}">${escapeHtml(artist)}</small></div>
                <div class="diagnostics-candidate-meta"><span>专辑：${escapeHtml(diagnosticString(candidate?.album))}</span><span>时长：${escapeHtml(formatDiagnosticDuration(candidate?.durationSeconds ?? candidate?.duration))}</span><span>查询变体：${escapeHtml(diagnosticString(candidate?.queryVariantId))}</span><span>候选 ID：<code title="${escapeHtml(diagnosticString(candidate?.candidateId))}">${escapeHtml(diagnosticString(candidate?.candidateId))}</code></span>${metadataKeys.length ? `<span>元数据：${escapeHtml(metadataKeys.join("、"))}</span>` : ""}</div>
                ${reasons.length ? `<ul class="diagnostics-reasons" aria-label="拒绝原因">${reasons.map(reason => `<li>${escapeHtml(reason)}</li>`).join("")}</ul>` : ""}
              </div>
              <div class="diagnostics-candidate-side"><span class="diagnostics-badge" data-state="${admitted ? "accepted" : "rejected"}">${admitted ? "已接纳" : "已拒绝"}</span>${admitted && candidate?.isHighConfidence ? '<span class="diagnostics-badge" data-state="high-confidence">高置信</span>' : ""}<strong class="diagnostics-score">${score === null ? "--" : score} 分</strong></div>
            </article>`;
          }).join("")
          : `<div class="diagnostics-candidate-empty">该来源没有返回候选。</div>`;
        return `<details class="diagnostics-provider" open>
          <summary class="diagnostics-provider-head"><span class="diagnostics-provider-heading"><span class="diagnostics-provider-title"><strong>${escapeHtml(providerId)}</strong><span class="diagnostics-badge" data-state="${providerStateClass}">${escapeHtml(diagnosticProviderStateLabel(providerState))}</span>${selectedBadge}</span>${detail ? `<span class="diagnostics-provider-detail" title="${escapeHtml(detail)}">${escapeHtml(detail)}</span>` : ""}</span><span class="diagnostics-provider-toggle-meta"><span>${candidates.length} 个候选</span><svg viewBox="0 0 16 16" aria-hidden="true"><path d="m6 4 4 4-4 4"></path></svg></span></summary>
          <div class="diagnostics-candidates">${candidateMarkup}</div>
        </details>`;
      }).join("");
    }

    function renderLyricDiagnosticsVariants(report) {
      const variants = Array.isArray(report?.searchVariants) ? report.searchVariants : [];
      const panel = $("#lyricDiagnosticsVariantsPanel");
      const container = $("#lyricDiagnosticsVariants");
      panel.hidden = !variants.length;
      if (!variants.length) {
        container.replaceChildren();
        return;
      }

      container.innerHTML = variants.map((variant, index) => {
        const title = diagnosticString(variant?.title, "未知查询");
        const artist = diagnosticArtists(variant?.artists, "未知歌手");
        const reasons = Array.isArray(variant?.relaxationReasons) ? variant.relaxationReasons.filter(Boolean) : [];
        return `<div class="diagnostics-variant"><span class="diagnostics-variant-index">${index + 1}</span><div class="diagnostics-variant-copy"><strong title="${escapeHtml(title)}">${escapeHtml(title)}</strong><small>${escapeHtml(artist)} · ${escapeHtml(diagnosticString(variant?.album))} · ${escapeHtml(formatDiagnosticDuration(variant?.durationSeconds ?? variant?.duration))}</small></div><span class="diagnostics-variant-reasons">${escapeHtml(reasons.length ? reasons.join("、") : "严格匹配")}</span></div>`;
      }).join("");
    }

    function renderLyricDiagnosticsSelection(report) {
      const selection = report?.selection;
      const container = $("#lyricDiagnosticsSelection");
      if (!selection || typeof selection !== "object") {
        const message = diagnosticString(report?.error, "没有候选通过完整校验。");
        container.innerHTML = `<div class="diagnostics-selection-empty"><strong>未找到可用歌词</strong><small>${escapeHtml(message)}</small></div>`;
        return;
      }

      const diagnostics = selection.diagnostics && typeof selection.diagnostics === "object"
        ? Object.entries(selection.diagnostics).filter(([key, value]) => key && value !== null && value !== undefined)
        : [];
      const diagnosticsMarkup = diagnostics.length
        ? `<div class="diagnostics-selection-meta">${diagnostics.map(([key, value]) => `<span>${escapeHtml(key)}：<code>${escapeHtml(value)}</code></span>`).join("")}</div>`
        : "";
      container.innerHTML = `<div class="diagnostics-selection-card is-selected"><div class="diagnostics-selection-title"><strong>${escapeHtml(diagnosticString(selection.providerId, "未知来源"))}</strong><span class="diagnostics-badge" data-state="selected">已采用</span></div><div class="diagnostics-selection-meta"><span>候选 ID：<code>${escapeHtml(diagnosticString(selection.candidateId))}</code></span><span>获取方式：${escapeHtml(diagnosticString(selection.acquisition))}</span><span>格式：${escapeHtml(diagnosticString(selection.format))}</span><span>时序：${escapeHtml(diagnosticString(selection.timingKind))} / ${escapeHtml(diagnosticString(selection.timingProvenance))}</span><span>歌词行数：${escapeHtml(diagnosticString(selection.lineCount, "0"))}</span></div>${diagnosticsMarkup}</div>`;
    }

    function renderLyricDiagnosticsState() {
      const current = lyricDiagnosticsState;
      const status = $("#lyricDiagnosticsStatus");
      const button = $("#runLyricDiagnosticsButton");
      const reportPanel = $("#lyricDiagnosticsReportPanel");
      const selectionPanel = $("#lyricDiagnosticsSelectionPanel");
      const report = current.report;
      const track = current.track ?? report?.effectiveTrack ?? report?.originalTrack;
      const title = diagnosticString(track?.title, "当前歌曲");
      let message = "尚未运行诊断。点击“开始诊断”获取当前歌曲的检索过程。";
      if (current.status === "running") message = `正在诊断“${title}”……`;
      else if (current.status === "success") message = report?.selection ? "诊断完成，已找到可用歌词。" : "诊断完成，但没有候选通过完整校验。";
      else if (current.status === "empty") message = diagnosticString(current.message, "当前没有可诊断的 SMTC 歌曲。");
      else if (current.status === "error") message = diagnosticString(current.message, "歌词诊断失败，请稍后重试。");
      status.dataset.state = current.status;
      status.textContent = message;
      button.disabled = current.status === "running";
      button.setAttribute("aria-busy", String(current.status === "running"));
      button.innerHTML = current.status === "running" ? '<span class="spinner" aria-hidden="true"></span>诊断中……' : current.status === "idle" ? "开始诊断" : "重新诊断";

      renderLyricDiagnosticsTrack(track);
      reportPanel.hidden = !report || current.status !== "success";
      selectionPanel.hidden = !report || current.status !== "success";
      if (report && current.status === "success") {
        const capturedAt = formatDiagnosticTimestamp(report.capturedAtUtc);
        $("#lyricDiagnosticsCapturedAt").textContent = capturedAt ? `捕获于 ${capturedAt}` : "";
        renderLyricDiagnosticsProviders(report);
        renderLyricDiagnosticsVariants(report);
        renderLyricDiagnosticsSelection(report);
      } else {
        $("#lyricDiagnosticsCapturedAt").textContent = "";
        $("#lyricDiagnosticsProviderCount").textContent = "";
        $("#lyricDiagnosticsReportSummary").replaceChildren();
        $("#lyricDiagnosticsProviders").replaceChildren();
        $("#lyricDiagnosticsVariantsPanel").hidden = true;
        $("#lyricDiagnosticsVariants").replaceChildren();
        $("#lyricDiagnosticsSelection").replaceChildren();
      }
    }

    function setLyricDiagnosticsState(payload = {}) {
      const status = ["running", "success", "empty", "error"].includes(payload?.status) ? payload.status : "error";
      const report = status === "success" && payload.report && typeof payload.report === "object" ? payload.report : null;
      lyricDiagnosticsState = {
        status: status === "success" && !report ? "error" : status,
        track: payload.track && typeof payload.track === "object" ? payload.track : null,
        report,
        message: diagnosticString(payload.message, status === "success" && !report ? "诊断结果无效。" : "")
      };
      renderLyricDiagnosticsState();
    }

    function changeTrackOffsetPage(delta) {
      const pageCount = Math.max(1, Number(trackOffsetData.pageCount) || 1);
      const nextPage = Math.min(pageCount, Math.max(1, trackOffsetPage + delta));
      if (nextPage === trackOffsetPage) return;
      expandedTrackOffsetKey = null;
      requestTrackOffsetPage(nextPage);
      $("#trackOffsetList").scrollIntoView({ block: "start", behavior: reducedMotionQuery.matches ? "auto" : "smooth" });
    }

    function requestTrackOffsetPage(page = 1) {
      trackOffsetRequestId += 1;
      $("#trackOffsetList").setAttribute("aria-busy", "true");
      bridge.post({
        type: "queryTrackOffsets",
        value: {
          requestId: trackOffsetRequestId,
          page: Math.max(1, Number(page) || 1),
          pageSize: TRACK_OFFSET_PAGE_SIZE,
          search: $("#trackOffsetSearch")?.value.trim() ?? "",
          lyricSource: state?.trackOffsetSourceFilter ?? "All",
          sort: state?.trackOffsetSort ?? "updated"
        }
      });
    }

    function commitCurrentTrackOffset(value) {
      const current = trackOffsetData.currentTrack;
      if (!current?.lyricSourceReady) return;
      const offset = normalizeTrackOffset(value, Number(current.trackOffsetMilliseconds) || 0);
      current.trackOffsetMilliseconds = offset;
      current.effectiveOffsetMilliseconds = (Number(current.playerOffsetMilliseconds) || 0) + offset;
      renderCurrentTrackOffset();
      bridge.post({ type: "setCurrentTrackOffset", value: offset });
    }

    function commitStoredTrackOffset(entry, value) {
      if (!entry) return;
      const offset = normalizeTrackOffset(value, Number(entry.offsetMilliseconds) || 0);
      entry.offsetMilliseconds = offset;
      renderTrackOffsetList();
      bridge.post({ type: "setStoredTrackOffset", value: { key: entry.key, offsetMilliseconds: offset } });
    }

    function setCurrentTrackOffsetData(currentTrack) {
      trackOffsetData.currentTrack = currentTrack ?? null;
      renderCurrentTrackOffset();
    }

    function setTrackOffsetEntries(payload) {
      if (!payload || Number(payload.requestId) !== trackOffsetRequestId) return;
      trackOffsetData.entries = Array.isArray(payload.entries) ? payload.entries : [];
      trackOffsetData.page = Number(payload.page) || 1;
      trackOffsetData.pageCount = Number(payload.pageCount) || 1;
      trackOffsetData.totalCount = Number(payload.totalCount) || 0;
      trackOffsetData.unfilteredCount = Number(payload.unfilteredCount) || 0;
      const sources = [...new Set((payload.lyricSources ?? []).filter(Boolean))]
        .sort((a, b) => sourceDisplayName(a).localeCompare(sourceDisplayName(b), "zh-CN"));
      selectOptions.trackOffsetSourceFilter = [
        { value: "All", label: "全部歌词源" },
        ...sources.map(source => ({ value: source, label: sourceDisplayName(source) }))
      ];
      if (state && !selectOptions.trackOffsetSourceFilter.some(option => option.value === state.trackOffsetSourceFilter)) {
        state.trackOffsetSourceFilter = "All";
        syncSelectTrigger(document.querySelector('[data-setting="trackOffsetSourceFilter"]'));
        requestTrackOffsetPage(1);
        return;
      }
      syncSelectTrigger(document.querySelector('[data-setting="trackOffsetSourceFilter"]'));
      expandedTrackOffsetKey = null;
      renderTrackOffsetList();
    }

    function setTrackOffsetSaveStatus(status) {
      if (!status?.message) return;
      showToast(status.message);
    }

    function navigateToPage(pageId, focusCurrentTrack = false) {
      if (!pageMeta[pageId]) return;
      const isCurrentPage = state?.page === pageId;
      if (pageId === "trackOffsets") {
        focusCurrentTrackOnNextRender = focusCurrentTrack;
      }
      activatePage(pageId, !focusCurrentTrack);
      if (pageId === "trackOffsets") {
        bridge.post({ type: "trackOffsetsPageActivated" });
        requestTrackOffsetPage(isCurrentPage ? trackOffsetPage : 1);
      }
      renderTrackOffsets();
    }

    function setPageInteractionState(pages, interactivePage) {
      pages.forEach(page => {
        const isInteractive = page === interactivePage;
        page.inert = !isInteractive;
        page.toggleAttribute("inert", !isInteractive);
        page.setAttribute("aria-hidden", String(!isInteractive));
      });
    }

    function renderPlayerSettings() {
      const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
      if (!source) return;
      $("#playerSettingsTitle").textContent = source.name;
      $("#playerSettingsAdapter").textContent = source.adapter;
      $("#playerSettingsLogo").innerHTML = `<img src="${escapeHtml(source.icon)}" alt="">`;
      $("#playerRecognitionToggle").checked = source.enabled;
      const offset = getPlayerOffset(source);
      $("#playerOffsetInput").value = offset;
      $("#playerOffsetStatus").textContent = formatPlayerOffset(offset);
      $("#resetPlayerOffsetButton").disabled = offset === source.defaultOffset;
    }

    function openPlayerSettings(sourceId) {
      const source = sourceCatalog.find(item => item.id === sourceId);
      if (!source) return;
      closeSelect(false);
      closeColorPopover(false);
      activePlayerSourceId = source.id;
      renderPlayerSettings();
      $("#playerSettingsDialog").showModal();
    }

    function commitPlayerOffset(value) {
      const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
      if (!source || !state) return;
      const numeric = Number(value);
      const offset = Number.isFinite(numeric) ? Math.max(-5000, Math.min(5000, Math.round(numeric))) : getPlayerOffset(source);
      state.playerLyricOffsets[source.adapter] = offset;
      bridge.post({ type: "update", key: `playerLyricOffset:${source.adapter}`, value: offset });
      renderSources();
      renderPlayerSettings();
      markSaved();
    }

    function renderPriority() {
      const enabled = sourceCatalog.filter(source => source.enabled);
      $("#priorityList").innerHTML = enabled.length ? enabled.map((source, index) => `
        <div class="priority-item" data-priority-item="${escapeHtml(source.id)}">
          <button class="drag-handle" type="button" draggable="true" data-drag-id="${escapeHtml(source.id)}" aria-label="拖动 ${escapeHtml(source.name)} 调整识别优先级" aria-keyshortcuts="Alt+ArrowUp Alt+ArrowDown"><svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="5" cy="4" r="1.2"/><circle cx="11" cy="4" r="1.2"/><circle cx="5" cy="8" r="1.2"/><circle cx="11" cy="8" r="1.2"/><circle cx="5" cy="12" r="1.2"/><circle cx="11" cy="12" r="1.2"/></svg></button>
          <span class="priority-number">${index + 1}</span>
          <span class="priority-name">${escapeHtml(source.name)}</span>
        </div>`).join("") : `<div class="setting-label"><strong>尚未启用播放源</strong><small>请至少启用一个播放器，以便识别当前播放内容。</small></div>`;
    }

    function applyEnabledOrder(orderedEnabled) {
      const queue = [...orderedEnabled];
      sourceCatalog = sourceCatalog.map(source => source.enabled ? queue.shift() : source);
    }

    function postSourceOrder() {
      bridge.post({ type: "reorderSources", value: sourceCatalog.map(source => source.adapter) });
    }

    function moveEnabledSource(sourceId, targetId, placeAfter = false) {
      const enabled = sourceCatalog.filter(source => source.enabled);
      const moving = enabled.find(source => source.id === sourceId);
      if (!moving || sourceId === targetId) return false;
      const reordered = enabled.filter(source => source.id !== sourceId);
      let targetIndex = reordered.findIndex(source => source.id === targetId);
      if (targetIndex < 0) return false;
      if (placeAfter) targetIndex += 1;
      reordered.splice(targetIndex, 0, moving);
      applyEnabledOrder(reordered);
      return true;
    }

    function activatePage(pageId, moveFocus = true) {
      if (!pageMeta[pageId]) return;
      const previousPageId = state?.page;
      const pages = $$('[data-page]');
      const navigationOrder = $$('[data-nav]').map(button => button.dataset.nav);
      const nextPage = pages.find(page => page.dataset.page === pageId);
      const currentPage = pages.find(page => page.classList.contains("active"));
      const currentIndex = currentPage ? navigationOrder.indexOf(currentPage.dataset.page) : 0;
      const nextIndex = navigationOrder.indexOf(pageId);
      const heading = nextPage.querySelector('h2[tabindex="-1"]');
      const titleBlock = $("#pageTitle").parentElement;
      const updateTitleText = () => {
        $("#pageTitle").textContent = pageMeta[pageId][0];
        $("#pageSubtitle").textContent = pageMeta[pageId][1];
      };

      if (state) state.page = pageId;
      if (previousPageId !== pageId) bridge.post({ type: "settingsPageChanged", value: pageId });
      $$('[data-nav]').forEach(button => {
        const isActive = button.dataset.nav === pageId;
        button.classList.toggle("active", isActive);
        if (isActive) button.setAttribute("aria-current", "page");
        else button.removeAttribute("aria-current");
      });

      pageTransitionToken += 1;
      const token = pageTransitionToken;
      pageAnimations.forEach(animation => animation.cancel());
      pageAnimations = [];
      pages.forEach(page => page.classList.remove("transitioning"));

      if (!currentPage || currentPage === nextPage || reducedMotionQuery.matches || typeof nextPage.animate !== "function") {
        pages.forEach(page => page.classList.toggle("active", page === nextPage));
        setPageInteractionState(pages, nextPage);
        titleBlock.style.transitionDuration = "0ms";
        titleBlock.style.opacity = "1";
        updateTitleText();
        if (moveFocus) heading?.focus({ preventScroll: true });
        return;
      }

      const direction = nextIndex > currentIndex ? 1 : -1;
      nextPage.style.transform = `translateX(${direction * 28}px)`;
      nextPage.classList.add("transitioning");
      setPageInteractionState(pages, nextPage);

      // 标题：先快速淡出，中点换文字再淡入，与正文同步
      titleBlock.style.transitionDuration = "100ms";
      titleBlock.style.opacity = "0";
      setTimeout(() => {
        if (token !== pageTransitionToken) return;
        updateTitleText();
        titleBlock.style.transitionDuration = "160ms";
        titleBlock.style.opacity = "1";
      }, 100);

      const outgoing = currentPage.animate(
        [
          { transform: "translateX(0)" },
          { transform: `translateX(${-direction * 28}px)` }
        ],
        { duration: 180, easing: "cubic-bezier(.4, 0, 1, 1)", fill: "both" }
      );
      const incoming = nextPage.animate(
        [
          { transform: `translateX(${direction * 28}px)` },
          { transform: "translateX(0)" }
        ],
        { duration: 220, easing: "cubic-bezier(.16, 1, .3, 1)", fill: "both" }
      );
      pageAnimations = [outgoing, incoming];

      Promise.allSettled(pageAnimations.map(animation => animation.finished)).then(() => {
        if (token !== pageTransitionToken) return;
        currentPage.classList.remove("active");
        nextPage.classList.remove("transitioning");
        nextPage.classList.add("active");
        currentPage.style.transform = "";
        nextPage.style.transform = "";
        setPageInteractionState(pages, nextPage);
        pageAnimations.forEach(animation => animation.cancel());
        pageAnimations = [];
        if (moveFocus) heading?.focus({ preventScroll: true });
      });
    }

    function markSaved() {
      $("#saveState").dataset.state = "applying";
      $("#saveState").textContent = "正在应用…";
    }

    function setSettingsSaveResult(payload = {}) {
      const success = payload.success === true;
      $("#saveState").dataset.state = success ? "success" : "error";
      $("#saveState").textContent = success
        ? "更改已实时应用"
        : "设置已应用，但保存失败；重启后可能恢复";
    }

    function showToast(message) {
      clearTimeout(toastTimer);
      const toast = $("#toast");
      toast.textContent = message;
      toast.classList.add("show");
      toastTimer = setTimeout(() => toast.classList.remove("show"), 1800);
    }

    function closeDialogWithAnimation(dialog) {
      if (!dialog.open || dialog.classList.contains("closing")) return;
      const finish = () => { dialog.classList.remove("closing"); dialog.removeEventListener("animationend", finish); dialog.close(); };
      dialog.addEventListener("animationend", finish);
      dialog.classList.add("closing");
      setTimeout(() => { if (dialog.classList.contains("closing")) { dialog.removeEventListener("animationend", finish); dialog.classList.remove("closing"); dialog.close(); } }, 400);
    }

    function renderSpectrumAudioAccess() {
      if (!state) return;
      const granted = Boolean(state.spectrumAudioAccessGranted);
      const modeEnabled = state.spectrumDisplayMode !== "Disabled";
      const captureBlocked = spectrumCaptureState.state === "blocked" && granted && modeEnabled;
      const status = $("#spectrumAudioAccessStatus");
      const fallbackMessage = granted
        ? modeEnabled
          ? "已允许；仅在频谱需要显示时读取系统播放声音。"
          : "已允许；频谱关闭时不会读取系统播放声音。"
        : "尚未允许读取系统播放声音。";
      status.textContent = spectrumCaptureState.message || fallbackMessage;
      status.dataset.state = spectrumCaptureState.state || (granted ? "waiting" : "notGranted");
      $("#retrySpectrumAudioAccessButton").hidden = !captureBlocked;
      $("#revokeSpectrumAudioAccessButton").hidden = !granted;
      const tuningAvailable = granted && modeEnabled;
      $("#spectrumTuningButton").disabled = !tuningAvailable;
      $("#spectrumTuningDescription").textContent = tuningAvailable
        ? "打开滑块面板，实时调节频谱参数（自动保存）。"
        : granted
          ? "请先在“歌词”页将“显示频谱”改为非关闭模式。"
          : "请先在“歌词”页启用频谱并允许读取系统播放声音。";
    }

    function requestSpectrumDisplayMode(mode) {
      if (!state || !selectOptions.spectrumDisplayMode.some(option => option.value === mode)) return;
      if (mode === "Disabled") {
        spectrumCaptureState = { state: "disabled", message: "频谱已关闭，不会读取系统播放声音。" };
        commitSetting("spectrumDisplayMode", mode);
        return;
      }
      if (state.spectrumAudioAccessGranted) {
        spectrumCaptureState = { state: "waiting", message: "已允许；仅在频谱需要显示时读取系统播放声音。" };
        commitSetting("spectrumDisplayMode", mode);
        return;
      }

      pendingSpectrumDisplayMode = mode;
      const dialog = $("#spectrumAudioConsentDialog");
      if (!dialog.open) dialog.showModal();
    }

    function setSpectrumCaptureState(payload = {}) {
      const allowedStates = ["notGranted", "disabled", "waiting", "capturing", "blocked"];
      spectrumCaptureState = {
        state: allowedStates.includes(payload.state) ? payload.state : "waiting",
        message: typeof payload.message === "string" ? payload.message : ""
      };
      renderSpectrumAudioAccess();
      if (spectrumCaptureState.state === "blocked" && state?.spectrumAudioAccessGranted && state.spectrumDisplayMode !== "Disabled") {
        $("#spectrumCaptureFailureMessage").textContent = spectrumCaptureState.message || "系统音频采集被系统或安全软件阻止，歌词显示不受影响。";
        const dialog = $("#spectrumCaptureFailureDialog");
        if (!dialog.open) dialog.showModal();
      }
    }

    function setControlValue(control, value) {
      if (control.classList.contains("theme-segmented")) {
        control.value = value;
        control.querySelectorAll("[data-theme-value]").forEach(option => {
          const selected = option.dataset.themeValue === value;
          option.setAttribute("aria-checked", String(selected));
          option.tabIndex = selected ? 0 : -1;
        });
      }
      else if (control.type === "checkbox") control.checked = Boolean(value);
      else if (control.tagName === "TEXTAREA" && Array.isArray(value)) control.value = value.join("\n");
      else if (Number.isFinite(Number(value)) && control.dataset.valueScale) {
        control.value = Math.round(Number(value) * Number(control.dataset.valueScale) * 1e10) / 1e10;
      }
      else control.value = value;
    }

    function readSettingControlValue(control) {
      if (control.type === "checkbox") return control.checked;
      if (control.type !== "number" && control.type !== "range") return control.value;
      const value = Number(control.value);
      const scale = Number(control.dataset.valueScale) || 1;
      return value / scale;
    }

    function syncSliderProgress(control) {
      const min = Number(control.min), max = Number(control.max);
      const progress = max === min ? 0 : ((Number(control.value) - min) / (max - min)) * 100;
      control.style.setProperty("--slider-progress", `${progress}%`);
    }

    function syncSelectTrigger(trigger) {
      const options = selectOptions[trigger.dataset.setting] ?? [];
      const selected = options.find(option => String(option.value) === String(state[trigger.dataset.setting]));
      trigger.querySelector(".select-trigger-value").textContent = selected?.label ?? "请选择";
    }

    function syncControls() {
      if (!state) return;
      $$('[data-setting]').forEach(control => control.classList.contains("select-trigger") ? syncSelectTrigger(control) : setControlValue(control, state[control.dataset.setting]));
      $$('input[type="range"][data-setting]').forEach(syncSliderProgress);
      $$('[data-color-text="foregroundColor"]').forEach(control => {
        if (!control.classList.contains("invalid")) control.value = state.foregroundColor.toUpperCase();
      });
      $$('[data-color-swatch]').forEach(swatch => { swatch.style.backgroundColor = state.foregroundColor; });
    }

    function renderMediaHotkeys() {
      if (!state) return;
      const enabled = Boolean(state.enableGlobalMediaHotkeys);
      $("#mediaHotkeyMasterStatus").textContent = enabled ? "已启用" : "已关闭";
      $("#mediaHotkeyList").innerHTML = (state.mediaHotkeys ?? []).map(definition => `
        <div class="media-hotkey-row"><div class="setting-label"><strong>${escapeHtml(definition.displayName)}</strong></div><div class="media-hotkey-controls"><button class="control media-hotkey-recorder" type="button" data-hotkey-binding="${escapeHtml(definition.settingKey)}" aria-label="录制${escapeHtml(definition.displayName)}快捷键"></button><output class="media-hotkey-status" data-hotkey-status="${escapeHtml(definition.statusKey)}"></output><button class="btn ghost small media-hotkey-reset" type="button" data-hotkey-reset="${escapeHtml(definition.action)}">恢复</button></div></div>`).join("");
      $$('[data-hotkey-binding]').forEach(button => {
        if (button !== activeHotkeyRecorder) button.textContent = state[button.dataset.hotkeyBinding] || "未设置";
      });
      $$('[data-hotkey-status]').forEach(output => {
        const status = state.mediaHotkeyStatuses?.[output.dataset.hotkeyStatus] || "notRegistered";
        output.textContent = window.taskbarLyricsHotkeys.label(status);
        output.dataset.state = window.taskbarLyricsHotkeys.visualState(status);
      });
    }

    function cancelHotkeyRecording() {
      if (!activeHotkeyRecorder) return;
      activeHotkeyRecorder.classList.remove("recording");
      activeHotkeyRecorder = null;
      renderMediaHotkeys();
    }

    function beginHotkeyRecording(button) {
      if (activeHotkeyRecorder === button) return;
      cancelHotkeyRecording();
      activeHotkeyRecorder = button;
      button.classList.add("recording");
      button.textContent = "按下组合键…";
      button.focus({ preventScroll: true });
    }

    function getRecordedHotkey(event) {
      const modifiers = [];
      if (event.ctrlKey) modifiers.push("Ctrl");
      if (event.altKey) modifiers.push("Alt");
      if (event.shiftKey) modifiers.push("Shift");
      if (!modifiers.length) return null;

      const specialKeys = {
        ArrowLeft: "Left", ArrowUp: "Up", ArrowRight: "Right", ArrowDown: "Down",
        Home: "Home", End: "End", PageUp: "PageUp", PageDown: "PageDown",
        Insert: "Insert", Delete: "Delete", " ": "Space"
      };
      const key = specialKeys[event.key] ?? (/^[a-z0-9]$/i.test(event.key) ? event.key.toUpperCase() : /^F(?:[1-9]|1\d|2[0-4])$/i.test(event.key) ? event.key.toUpperCase() : null);
      return key ? [...modifiers, key].join("+") : null;
    }

    function commitHotkeyBinding(key, binding) {
      if (!state) return;
      state[key] = binding;
      bridge.post({ type: "update", key, value: binding });
      markSaved();
      renderMediaHotkeys();
    }

    function setAvailableFonts(fonts) {
      const normalized = fonts.map(font => typeof font === "string"
        ? { value: font, label: font }
        : { value: font.value ?? font.Value, label: font.label ?? font.Label ?? font.value ?? font.Value }
      ).filter(font => font.value);
      selectOptions.fontFamily = normalized;
      if (!normalized.some(font => font.value === state.fontFamily)) state.fontFamily = normalized[0]?.value ?? "Microsoft YaHei UI";
      syncSelectTrigger($("#fontFamilySelect"));
    }

    function fromArgb(color) {
      if (typeof color !== "string") return "#FFFFFF";
      const normalized = color.trim().toUpperCase();
      if (/^#[0-9A-F]{8}$/.test(normalized)) return `#${normalized.slice(3)}`;
      return /^#[0-9A-F]{6}$/.test(normalized) ? normalized : "#FFFFFF";
    }

    function toArgb(color) {
      const normalized = fromArgb(color);
      return `#FF${normalized.slice(1)}`;
    }

    function setState(nextState, fonts = []) {
      const previousPage = state?.page ?? "sources";
      const previousCustom = state?.customForegroundColor;
      const previousTrackOffsetSourceFilter = state?.trackOffsetSourceFilter ?? "All";
      const previousTrackOffsetSort = state?.trackOffsetSort ?? "updated";
      const foregroundColor = fromArgb(nextState.foregroundColor);
      state = window.taskbarLyricsSettingsState.create(nextState, {
        page: previousPage,
        trackOffsetSourceFilter: previousTrackOffsetSourceFilter,
        trackOffsetSort: previousTrackOffsetSort
      }, foregroundColor);
      const incomingOffsets = nextState.playerLyricOffsets ?? {};
      const incomingDefaults = nextState.defaultPlayerLyricOffsets ?? {};
      const defaultOffsetFor = source => {
        const value = Number(incomingDefaults[source.adapter]);
        return Number.isFinite(value) ? Math.max(-5000, Math.min(5000, Math.round(value))) : 0;
      };
      state.playerLyricOffsets = Object.fromEntries(sourceCatalogDefaults.map(source => {
        const value = Number(incomingOffsets[source.adapter]);
        return [source.adapter, Number.isFinite(value) ? Math.max(-5000, Math.min(5000, Math.round(value))) : defaultOffsetFor(source)];
      }));
      state.customForegroundColor = nextState.foregroundColorMode === "Custom"
        ? foregroundColor
        : previousCustom ?? foregroundColor;
      repositoryUrl = nextState.repositoryUrl ?? "";
      sourceCatalog = sourceCatalogDefaults.map(source => ({ ...source, defaultOffset: defaultOffsetFor(source), enabled: Boolean(nextState[source.settingKey]) }));
      const order = Array.isArray(nextState.sourceRecognitionOrder) ? nextState.sourceRecognitionOrder : [];
      sourceCatalog.sort((a, b) => {
        const aIndex = order.indexOf(a.adapter);
        const bIndex = order.indexOf(b.adapter);
        return (aIndex < 0 ? 99 : aIndex) - (bIndex < 0 ? 99 : bIndex);
      });
      setAvailableFonts(fonts);
      const version = nextState.appVersion || "--";
      $(".version-badge").textContent = `Version ${version}`;
      if (updateState === "idle") $("#updateStatusDetail").textContent = `当前版本 ${version}`;
      refresh();
    }

    function positionPopover(popover, trigger, preferredWidth) {
      const rect = trigger.getBoundingClientRect();
      const margin = 8;
      const width = Math.min(window.innerWidth - margin * 2, Math.max(preferredWidth, rect.width));
      popover.style.width = `${width}px`;
      const height = popover.offsetHeight;
      const below = window.innerHeight - rect.bottom - margin;
      const top = below >= height || below >= rect.top ? rect.bottom + 5 : rect.top - height - 5;
      popover.style.left = `${Math.min(window.innerWidth - width - margin, Math.max(margin, rect.left))}px`;
      popover.style.top = `${Math.max(margin, Math.min(window.innerHeight - height - margin, top))}px`;
    }

    function renderSelectOptions() {
      if (!activeSelectTrigger) return;
      const key = activeSelectTrigger.dataset.setting;
      const options = selectOptions[key] ?? [];
      $("#selectListbox").innerHTML = options.map((option, index) => {
        const selected = String(option.value) === String(state[key]);
        return `<div id="selectOption-${index}" class="select-option${index === activeSelectIndex ? " is-active" : ""}" role="option" aria-selected="${selected}" data-option-index="${index}"><svg class="select-option-check" viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round"/></svg><span>${escapeHtml(option.label)}</span></div>`;
      }).join("");
      $("#selectListbox").setAttribute("aria-activedescendant", `selectOption-${activeSelectIndex}`);
      $("#selectListbox").querySelector(".is-active")?.scrollIntoView({ block: "nearest" });
    }

    function openSelect(trigger, direction = 0) {
      if (!state) return;
      closeColorPopover(false);
      if (activeSelectTrigger && activeSelectTrigger !== trigger) closeSelect(false);
      activeSelectTrigger = trigger;
      const options = selectOptions[trigger.dataset.setting] ?? [];
      const selectedIndex = options.findIndex(option => String(option.value) === String(state[trigger.dataset.setting]));
      activeSelectIndex = direction < 0 ? options.length - 1 : direction > 0 ? Math.max(0, selectedIndex) : Math.max(0, selectedIndex);
      trigger.setAttribute("aria-expanded", "true");
      trigger.setAttribute("aria-controls", "selectListbox");
      $("#selectPopover").setAttribute("data-state", "open");
      renderSelectOptions();
      positionPopover($("#selectPopover"), trigger, 210);
      $("#selectListbox").focus({ preventScroll: true });
    }

    function closeSelect(returnFocus = true) {
      if (!activeSelectTrigger) return;
      const trigger = activeSelectTrigger;
      trigger.setAttribute("aria-expanded", "false");
      trigger.removeAttribute("aria-controls");
      $("#selectPopover").removeAttribute("data-state");
      $("#selectListbox").removeAttribute("aria-activedescendant");
      activeSelectTrigger = null;
      activeSelectIndex = -1;
      if (returnFocus) trigger.focus({ preventScroll: true });
    }

    function applySettingLocally(key, value) {
      const previousCornerRadius = state.coverCornerRadius;
      state[key] = value;
      if (key === "foregroundColor") {
        state.foregroundColor = fromArgb(value);
        state.customForegroundColor = state.foregroundColor;
        state.foregroundColorMode = "Custom";
      }
      if (key === "coverCornerRadius") state.coverCornerRadius = Math.min(state.coverCornerRadius, state.coverSize / 2);
      syncColorMode(); applyDependencies(); syncLayoutBounds(); syncWindowBounds(); updateOutputs(); syncControls(); renderSpectrumAudioAccess();
      return previousCornerRadius;
    }

    function commitSetting(key, value) {
      if (!state) return;
      if (key === "trackOffsetSourceFilter" || key === "trackOffsetSort") {
        state[key] = value;
        expandedTrackOffsetKey = null;
        syncControls();
        requestTrackOffsetPage(1);
        return;
      }
      pendingRangePreviews.delete(key);
      const previousCornerRadius = applySettingLocally(key, value);
      if (key === "lyricsLayoutScalePercent") announceNextLayoutPreview = true;
      const payload = key === "foregroundColor" ? toArgb(state.foregroundColor) : state[key];
      bridge.post({ type: "update", key, value: payload });
      if (key !== "coverCornerRadius" && previousCornerRadius !== state.coverCornerRadius) {
        bridge.post({ type: "update", key: "coverCornerRadius", value: state.coverCornerRadius });
      }
      markSaved();
    }

    function scheduleSettingPreview(key, value) {
      if (!state) return;
      applySettingLocally(key, value);
      pendingRangePreviews.set(key, state[key]);
      if (rangePreviewFrame) return;
      rangePreviewFrame = requestAnimationFrame(() => {
        rangePreviewFrame = 0;
        pendingRangePreviews.forEach((previewValue, previewKey) => {
          bridge.post({ type: "previewUpdate", key: previewKey, value: previewValue });
        });
        pendingRangePreviews.clear();
      });
    }

    function chooseSelectOption(index) {
      if (!activeSelectTrigger) return;
      const key = activeSelectTrigger.dataset.setting;
      const option = (selectOptions[key] ?? [])[index];
      if (!option) return;
      if (key === "spectrumDisplayMode") {
        closeSelect(true);
        requestSpectrumDisplayMode(option.value);
        return;
      }
      commitSetting(key, option.value);
      closeSelect(true);
    }

    function hexToRgb(hex) {
      const value = hex.replace("#", "");
      return { r: parseInt(value.slice(0, 2), 16), g: parseInt(value.slice(2, 4), 16), b: parseInt(value.slice(4, 6), 16) };
    }

    function rgbToHex({ r, g, b }) {
      return `#${[r, g, b].map(value => Math.round(value).toString(16).padStart(2, "0")).join("")}`.toUpperCase();
    }

    function rgbToHsv({ r, g, b }) {
      r /= 255; g /= 255; b /= 255;
      const max = Math.max(r, g, b), min = Math.min(r, g, b), delta = max - min;
      let h = 0;
      if (delta) h = max === r ? 60 * (((g - b) / delta) % 6) : max === g ? 60 * ((b - r) / delta + 2) : 60 * ((r - g) / delta + 4);
      return { h: (h + 360) % 360, s: max ? delta / max : 0, v: max };
    }

    function hsvToRgb({ h, s, v }) {
      const c = v * s, x = c * (1 - Math.abs((h / 60) % 2 - 1)), m = v - c;
      const [r, g, b] = h < 60 ? [c, x, 0] : h < 120 ? [x, c, 0] : h < 180 ? [0, c, x] : h < 240 ? [0, x, c] : h < 300 ? [x, 0, c] : [c, 0, x];
      return { r: (r + m) * 255, g: (g + m) * 255, b: (b + m) * 255 };
    }

    function updateColorDraft(options = {}) {
      colorDraft.hex = rgbToHex(hsvToRgb(colorDraft));
      $("#colorArea").style.setProperty("--picker-hue", colorDraft.h);
      $("#hueSlider").style.setProperty("--picker-hue", colorDraft.h);
      $("#colorSaturationSlider").style.setProperty("--picker-hue", colorDraft.h);
      $("#colorBrightnessSlider").style.setProperty("--picker-hue", colorDraft.h);
      $("#colorSaturationSlider").value = Math.round(colorDraft.s * 100);
      $("#colorBrightnessSlider").value = Math.round(colorDraft.v * 100);
      $("#hueSlider").value = Math.round(colorDraft.h);
      $("#hueNumberInput").value = Math.round(colorDraft.h);
      $("#colorCursor").style.left = `${colorDraft.s * 100}%`;
      $("#colorCursor").style.top = `${(1 - colorDraft.v) * 100}%`;
      $("#colorDraftPreview").style.backgroundColor = colorDraft.hex;
      if (!options.keepInput) {
        $("#colorDraftInput").value = colorDraft.hex;
        setColorInputValidity($("#colorDraftInput"), $("#colorDraftError"), true);
      }
    }

    function setColorInputValidity(input, error, valid) {
      input.classList.toggle("invalid", !valid);
      input.setAttribute("aria-invalid", String(!valid));
      error.hidden = valid;
    }

    function setColorDraftFromHex(hex) {
      if (!/^#[0-9a-f]{6}$/i.test(hex)) return false;
      const hsv = rgbToHsv(hexToRgb(hex));
      colorDraft = { ...hsv, hex: hex.toUpperCase() };
      updateColorDraft();
      return true;
    }

    function openColorPopover() {
      if (!state) return;
      closeSelect(false);
      setColorDraftFromHex(state.customForegroundColor);
      $("#colorPopover").setAttribute("data-state", "open");
      $("#colorPickerButton").setAttribute("aria-expanded", "true");
      positionPopover($("#colorPopover"), $("#colorPickerButton"), 264);
      $("#colorSaturationSlider").focus({ preventScroll: true });
    }

    function closeColorPopover(returnFocus = true) {
      if ($("#colorPopover").getAttribute("data-state") !== "open") return;
      $("#colorPopover").removeAttribute("data-state");
      $("#colorPickerButton").setAttribute("aria-expanded", "false");
      colorPointerActive = false;
      if (returnFocus) $("#colorPickerButton").focus({ preventScroll: true });
    }

    function updateColorFromPointer(event) {
      const rect = $("#colorArea").getBoundingClientRect();
      colorDraft.s = Math.max(0, Math.min(1, (event.clientX - rect.left) / rect.width));
      colorDraft.v = 1 - Math.max(0, Math.min(1, (event.clientY - rect.top) / rect.height));
      updateColorDraft();
    }

    function updateColorFromRange(event) {
      const value = Math.max(0, Math.min(100, Number(event.target.value) || 0)) / 100;
      if (event.target === $("#colorSaturationSlider")) colorDraft.s = value;
      else colorDraft.v = value;
      updateColorDraft();
    }

    function syncColorMode() {
      const custom = state.foregroundColorMode === "Custom";
      if (state.foregroundColorMode === "Dark") state.foregroundColor = "#111827";
      else if (state.foregroundColorMode === "Light") state.foregroundColor = "#FFFFFF";
      else if (custom) state.foregroundColor = state.customForegroundColor;
      $("[data-custom-color]").hidden = !custom;
      $("[data-preset-color]").hidden = custom;
      $("[data-custom-color]").closest(".color-mode-control").classList.toggle("is-custom", custom);
      $("[data-mode-swatch]").style.backgroundColor = state.foregroundColor;
      $("[data-mode-value]").textContent = state.foregroundColor.toUpperCase();
    }

    function setUpdateStatus(payload = {}) {
      const status = payload.state ?? "idle";
      updateState = status;
      const title = $("#updateStatusTitle");
      const detail = $("#updateStatusDetail");
      const checkButton = $("#checkUpdateButton");
      const releaseButton = $("#openReleaseButton");
      const version = state?.appVersion ?? "--";
      title.textContent = status === "checking" ? "正在检查更新…"
        : status === "available" ? `发现新版本 ${payload.version ?? ""}`
        : status === "latest" ? "当前已是最新版本"
        : status === "error" ? "检查更新失败"
        : "尚未检查更新";
      detail.textContent = payload.message || `当前版本 ${version}`;
      checkButton.disabled = status === "checking";
      checkButton.innerHTML = status === "checking" ? '<span class="spinner" aria-hidden="true"></span>检查中' : status === "available" ? "重新检查" : "检查更新";
      releaseButton.hidden = status !== "available";
      updateReleaseUrl = payload.url ?? "";
    }

    function setWindowState(nextState) {
      const maximized = nextState === "maximized";
      document.documentElement.classList.toggle("window-maximized", maximized);
      $$(".caption-glyph-max").forEach(el => el.hidden = maximized);
      $$(".caption-glyph-restore").forEach(el => el.hidden = !maximized);
    }

    function syncLayoutBounds() {
      state.lyricsLayoutScalePercent = Math.min(300, Math.max(25, Number(state.lyricsLayoutScalePercent) || 100));
      state.fontSize = Math.min(96, Math.max(6, Number(state.fontSize) || 14));
      state.coverSize = Math.min(200, Math.max(12, Number(state.coverSize) || 34));
      state.coverGap = Math.min(240, Math.max(0, Number(state.coverGap) || 0));
      state.coverCornerRadius = Math.min(
        Math.max(0, Number(state.coverCornerRadius) || 0),
        state.coverSize / 2);
      $$('[data-setting="coverCornerRadius"]').forEach(input => { input.max = state.coverSize / 2; });
    }

    function syncWindowBounds() {
      state.xOffset = Math.min(2000, Math.max(-2000, Number(state.xOffset) || 0));
      state.yOffset = Math.min(2000, Math.max(-2000, Number(state.yOffset) || 0));
      state.windowWidth = Math.min(1400, Math.max(320, Number(state.windowWidth) || 420));
    }

    function applyDependencies() {
      $$('[data-depends]').forEach(row => {
        const enabled = Boolean(state[row.dataset.depends]);
        row.classList.toggle("is-disabled", !enabled);
        row.querySelectorAll("input, select, textarea, button").forEach(control => { control.disabled = !enabled; });
      });
    }

    function updateOutputs() {
      if ($("[data-effective-font-size]")) {
        const showCover = state.showCover !== false;
        $("[data-effective-font-size]").textContent = `${formatLayoutMetric(state.effectiveFontSize)} px`;
        $("[data-effective-cover-size]").textContent = showCover
          ? `${formatLayoutMetric(state.effectiveCoverSize)} px`
          : "已隐藏";
        $("[data-effective-cover-gap]").textContent = `${formatLayoutMetric(state.effectiveCoverGap)} px`;
        $("[data-effective-cover-gap-item]").hidden = !showCover;
        $("[data-effective-window-width]").textContent = `${formatLayoutMetric(state.effectiveWindowWidth)} px`;
      }
    }

    function formatLayoutMetric(value) {
      const numeric = Number(value);
      return Number.isFinite(numeric)
        ? numeric.toLocaleString("zh-CN", { maximumFractionDigits: 2 })
        : "--";
    }

    function updateLayoutPreview(payload = {}) {
      if (!state) return;
      ["scalePercent", "fontSize", "coverSize", "coverGap", "coverCornerRadius", "effectiveFontSize", "effectiveCoverSize", "effectiveCoverGap", "effectiveCoverCornerRadius", "effectiveWindowWidth"].forEach(key => {
        const value = Number(payload[key]);
        if (!Number.isFinite(value)) return;
        const stateKey = key === "scalePercent" ? "lyricsLayoutScalePercent" : key;
        state[stateKey] = value;
      });
      syncLayoutBounds();
      syncWindowBounds();
      syncControls();
      updateOutputs();
      if (announceNextLayoutPreview) {
        const coverAnnouncement = state.showCover === false
          ? "封面已隐藏。"
          : `封面 ${formatLayoutMetric(state.effectiveCoverSize)} 像素，间距 ${formatLayoutMetric(state.effectiveCoverGap)} 像素。`;
        $("#layoutScaleAnnouncement").textContent = `歌词区域缩放已设为 ${formatLayoutMetric(state.lyricsLayoutScalePercent)}%，实际字号 ${formatLayoutMetric(state.effectiveFontSize)} 像素，${coverAnnouncement}`;
        announceNextLayoutPreview = false;
      }
    }

    function refresh() {
      renderSources();
      renderPriority();
      renderTrackOffsets();
      if ($("#playerSettingsDialog").open) renderPlayerSettings();
      syncLayoutBounds();
      syncWindowBounds();
      syncColorMode();
      syncControls();
      renderMediaHotkeys();
      applyDependencies();
      renderSpectrumAudioAccess();
      updateOutputs();
      activatePage(state.page, false);
    }

    function resetState() {
      bridge.post({ type: "resetDefaults" });
      markSaved();
    }

    document.addEventListener("click", event => {
      const hotkeyBinding = event.target.closest("[data-hotkey-binding]");
      if (hotkeyBinding) { beginHotkeyRecording(hotkeyBinding); return; }

      const hotkeyReset = event.target.closest("[data-hotkey-reset]");
      if (hotkeyReset) {
        bridge.post({ type: "resetMediaHotkey", value: hotkeyReset.dataset.hotkeyReset });
        markSaved();
        return;
      }

      const themeOption = event.target.closest("[data-theme-value]");
      if (themeOption) { commitSetting("toolWindowTheme", themeOption.dataset.themeValue); return; }

      const nav = event.target.closest("[data-nav]");
      if (nav) { navigateToPage(nav.dataset.nav); return; }

      const currentTrackDelta = event.target.closest("[data-current-track-offset-delta]");
      if (currentTrackDelta) {
        commitCurrentTrackOffset((Number(trackOffsetData.currentTrack?.trackOffsetMilliseconds) || 0) + Number(currentTrackDelta.dataset.currentTrackOffsetDelta));
        return;
      }

      if (event.target.closest("[data-reset-current-track-offset]")) {
        commitCurrentTrackOffset(0);
        return;
      }

      const editTrackOffset = event.target.closest("[data-edit-track-offset]");
      if (editTrackOffset) {
        const entry = visibleTrackOffsetEntries[Number(editTrackOffset.dataset.editTrackOffset)];
        if (entry) {
          const key = trackOffsetKeyId(entry.key);
          expandedTrackOffsetKey = expandedTrackOffsetKey === key ? null : key;
          renderTrackOffsetList();
        }
        return;
      }

      const storedTrackDelta = event.target.closest("[data-stored-track-offset-delta]");
      if (storedTrackDelta) {
        const entry = visibleTrackOffsetEntries[Number(storedTrackDelta.dataset.trackOffsetIndex)];
        if (entry) commitStoredTrackOffset(entry, (Number(entry.offsetMilliseconds) || 0) + Number(storedTrackDelta.dataset.storedTrackOffsetDelta));
        return;
      }

      const deleteTrackOffset = event.target.closest("[data-delete-track-offset]");
      if (deleteTrackOffset) {
        const entry = visibleTrackOffsetEntries[Number(deleteTrackOffset.dataset.deleteTrackOffset)];
        if (entry) {
          pendingDeleteTrackOffsetKey = entry.key;
          $("#deleteTrackOffsetDialog").showModal();
        }
        return;
      }

      const playerSettings = event.target.closest("[data-player-settings]");
      if (playerSettings) { openPlayerSettings(playerSettings.dataset.playerSettings); return; }

      const offsetStep = event.target.closest("[data-player-offset-delta]");
      if (offsetStep) {
        const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
        if (source) commitPlayerOffset(getPlayerOffset(source) + Number(offsetStep.dataset.playerOffsetDelta));
        return;
      }

      const step = event.target.closest("[data-step-target]");
      if (step) {
        if (!state) return;
        const key = step.dataset.stepTarget;
        const input = document.querySelector(`[data-setting="${key}"]`);
        const min = Number(input.min);
        const max = Number(input.max);
        const value = Math.min(max, Math.max(min, Number(state[key]) + Number(step.dataset.delta)));
        commitSetting(key, value); return;
      }

      if (event.target.closest("[data-reset-layout-scale]")) {
        commitSetting("lyricsLayoutScalePercent", 100);
        return;
      }

      if (event.target.closest("[data-reset-layout-base]")) {
        bridge.post({ type: "resetLyricsLayoutBase" });
        markSaved();
        return;
      }

      const cancel = event.target.closest("[data-dialog-cancel]");
      if (cancel) { closeDialogWithAnimation(document.getElementById(cancel.dataset.dialogCancel)); }
    });

    document.addEventListener("dragstart", event => {
      const handle = event.target.closest("[data-drag-id]");
      if (!handle) return;
      draggedSourceId = handle.dataset.dragId;
      event.dataTransfer.effectAllowed = "move";
      event.dataTransfer.setData("text/plain", draggedSourceId);
      handle.closest("[data-priority-item]").classList.add("dragging");
    });

    document.addEventListener("dragover", event => {
      const item = event.target.closest("[data-priority-item]");
      if (!item || !draggedSourceId || item.dataset.priorityItem === draggedSourceId) return;
      event.preventDefault();
      event.dataTransfer.dropEffect = "move";
      $$('[data-priority-item]').forEach(node => node.classList.toggle("drag-over", node === item));
    });

    document.addEventListener("drop", event => {
      const item = event.target.closest("[data-priority-item]");
      if (!item || !draggedSourceId) return;
      event.preventDefault();
      const rect = item.getBoundingClientRect();
      const placeAfter = event.clientY > rect.top + rect.height / 2;
      const moved = moveEnabledSource(draggedSourceId, item.dataset.priorityItem, placeAfter);
      const movedSource = sourceCatalog.find(source => source.id === draggedSourceId);
      draggedSourceId = null;
      renderPriority();
      if (moved) {
        postSourceOrder();
        markSaved();
        const position = sourceCatalog.filter(source => source.enabled).findIndex(source => source.id === movedSource.id) + 1;
        showToast(`${movedSource.name} 已移动到第 ${position} 位`);
      }
    });

    document.addEventListener("dragend", () => {
      draggedSourceId = null;
      $$('[data-priority-item]').forEach(node => node.classList.remove("dragging", "drag-over"));
    });

    document.addEventListener("keydown", event => {
      if (activeHotkeyRecorder) {
        event.preventDefault();
        event.stopPropagation();
        if (event.key === "Escape") { cancelHotkeyRecording(); return; }
        const binding = getRecordedHotkey(event);
        if (!binding) {
          if (!["Control", "Alt", "Shift", "Meta"].includes(event.key)) activeHotkeyRecorder.textContent = "请使用 Ctrl、Alt 或 Shift";
          return;
        }
        const button = activeHotkeyRecorder;
        button.classList.remove("recording");
        activeHotkeyRecorder = null;
        commitHotkeyBinding(button.dataset.hotkeyBinding, binding);
        return;
      }

      const handle = event.target.closest("[data-drag-id]");
      if (!handle || !event.altKey || !["ArrowUp", "ArrowDown"].includes(event.key)) return;
      event.preventDefault();
      const enabled = sourceCatalog.filter(source => source.enabled);
      const current = enabled.findIndex(source => source.id === handle.dataset.dragId);
      const target = current + (event.key === "ArrowUp" ? -1 : 1);
      if (target < 0 || target >= enabled.length) return;
      [enabled[current], enabled[target]] = [enabled[target], enabled[current]];
      applyEnabledOrder(enabled);
      postSourceOrder();
      const sourceId = handle.dataset.dragId;
      const sourceName = enabled[target].name;
      renderPriority();
      markSaved();
      showToast(`${sourceName} 已移动到第 ${target + 1} 位`);
      requestAnimationFrame(() => document.querySelector(`[data-drag-id="${sourceId}"]`)?.focus());
    });

    document.addEventListener("click", event => {
      const trigger = event.target.closest(".select-trigger");
      if (trigger) {
        if (activeSelectTrigger === trigger) closeSelect(true); else openSelect(trigger);
        return;
      }
      const option = event.target.closest("[data-option-index]");
      if (option) chooseSelectOption(Number(option.dataset.optionIndex));
    });

    document.addEventListener("keydown", event => {
      const themeOption = event.target.closest("[data-theme-value]");
      if (themeOption && ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"].includes(event.key)) {
        event.preventDefault();
        const options = Array.from(themeOption.parentElement.querySelectorAll("[data-theme-value]"));
        const current = options.indexOf(themeOption);
        const next = event.key === "Home"
          ? 0
          : event.key === "End"
            ? options.length - 1
            : (current + (["ArrowRight", "ArrowDown"].includes(event.key) ? 1 : -1) + options.length) % options.length;
        options[next].focus({ preventScroll: true });
        commitSetting("toolWindowTheme", options[next].dataset.themeValue);
        return;
      }

      const trigger = event.target.closest(".select-trigger");
      if (trigger && ["ArrowDown", "ArrowUp", "Home", "End", "Enter", " "].includes(event.key)) {
        event.preventDefault();
        openSelect(trigger, event.key === "ArrowUp" || event.key === "End" ? -1 : 1);
        return;
      }
      if (event.target === $("#selectListbox") && activeSelectTrigger) {
        const options = selectOptions[activeSelectTrigger.dataset.setting] ?? [];
        if (["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
          event.preventDefault();
          if (event.key === "Home") activeSelectIndex = 0;
          else if (event.key === "End") activeSelectIndex = options.length - 1;
          else activeSelectIndex = (activeSelectIndex + (event.key === "ArrowDown" ? 1 : -1) + options.length) % options.length;
          renderSelectOptions();
        } else if (["Enter", " "].includes(event.key)) { event.preventDefault(); chooseSelectOption(activeSelectIndex); }
        else if (event.key === "Escape") { event.preventDefault(); closeSelect(true); }
        else if (event.key === "Tab") closeSelect(false);
      } else if (event.key === "Escape" && $("#colorPopover").getAttribute("data-state") === "open") {
        event.preventDefault(); closeColorPopover(true);
      }
    });

    document.addEventListener("pointerdown", event => {
      if (activeSelectTrigger && !$("#selectPopover").contains(event.target) && !activeSelectTrigger.contains(event.target)) closeSelect(false);
      if ($("#colorPopover").getAttribute("data-state") === "open" && !$("#colorPopover").contains(event.target) && !$("#colorPickerButton").contains(event.target)) closeColorPopover(false);
    });

    window.addEventListener("resize", () => { closeSelect(false); closeColorPopover(false); });
    window.addEventListener("blur", cancelHotkeyRecording);
    $$(".page").forEach(page => page.addEventListener("scroll", () => { closeSelect(false); closeColorPopover(false); }, { passive: true }));

    $("#colorPickerButton").addEventListener("click", () => $("#colorPopover").getAttribute("data-state") !== "open" ? openColorPopover() : closeColorPopover(true));
    $("#colorArea").addEventListener("pointerdown", event => {
      colorPointerActive = true;
      $("#colorArea").setPointerCapture(event.pointerId);
      updateColorFromPointer(event);
    });
    $("#colorArea").addEventListener("pointermove", event => { if (colorPointerActive) updateColorFromPointer(event); });
    $("#colorArea").addEventListener("pointerup", event => { colorPointerActive = false; $("#colorArea").releasePointerCapture(event.pointerId); });
    $("#colorSaturationSlider").addEventListener("input", updateColorFromRange);
    $("#colorBrightnessSlider").addEventListener("input", updateColorFromRange);
    $("#hueSlider").addEventListener("input", event => { colorDraft.h = Number(event.target.value); updateColorDraft(); });
    $("#hueNumberInput").addEventListener("input", event => {
      if (event.target.value === "" || !event.target.validity.valid) return;
      colorDraft.h = Number(event.target.value);
      updateColorDraft();
    });
    $("#hueNumberInput").addEventListener("change", event => {
      colorDraft.h = Math.min(360, Math.max(0, Number(event.target.value) || 0));
      updateColorDraft();
    });
    $("#colorDraftInput").addEventListener("input", event => {
      const valid = /^#[0-9a-f]{6}$/i.test(event.target.value);
      setColorInputValidity(event.target, $("#colorDraftError"), valid);
      if (valid) setColorDraftFromHex(event.target.value);
    });
    $("#colorPresets").addEventListener("click", event => {
      const preset = event.target.closest("[data-preset-color-value]");
      if (preset) setColorDraftFromHex(preset.dataset.presetColorValue);
    });
    $("#colorCancelButton").addEventListener("click", () => closeColorPopover(true));
    $("#colorApplyButton").addEventListener("click", () => {
      if (!/^#[0-9a-f]{6}$/i.test($("#colorDraftInput").value)) {
        setColorInputValidity($("#colorDraftInput"), $("#colorDraftError"), false);
        $("#colorDraftInput").focus({ preventScroll: true });
        return;
      }
      state.customForegroundColor = colorDraft.hex;
      state.foregroundColorMode = "Custom";
      commitSetting("foregroundColor", colorDraft.hex);
      closeColorPopover(true);
    });

    document.addEventListener("change", event => {
      if (event.target === $("#currentTrackOffsetInput")) {
        commitCurrentTrackOffset(event.target.value);
        return;
      }

      const storedTrackOffsetInput = event.target.closest("[data-stored-track-offset-input]");
      if (storedTrackOffsetInput) {
        const entry = visibleTrackOffsetEntries[Number(storedTrackOffsetInput.dataset.storedTrackOffsetInput)];
        if (entry) commitStoredTrackOffset(entry, storedTrackOffsetInput.value);
        return;
      }

      if (event.target === $("#playerRecognitionToggle")) {
        const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
        if (source) {
          source.enabled = event.target.checked;
          state[source.settingKey] = source.enabled;
          bridge.post({ type: "update", key: source.settingKey, value: source.enabled });
        }
        renderSources(); renderPriority(); renderPlayerSettings(); markSaved(); return;
      }

      if (event.target === $("#playerOffsetInput")) { commitPlayerOffset(event.target.value); return; }

      const control = event.target.closest("[data-setting]");
      if (!control) return;
      const key = control.dataset.setting;
      let value = readSettingControlValue(control);
      if (key === "localMusicFolders") value = control.value.split(/\r?\n/).map(folder => folder.trim()).filter(Boolean);
      commitSetting(key, value);
    });

    document.addEventListener("input", event => {
      if (event.target === $("#trackOffsetSearch")) {
        clearTimeout(trackOffsetSearchTimer);
        trackOffsetSearchTimer = setTimeout(() => {
          expandedTrackOffsetKey = null;
          requestTrackOffsetPage(1);
        }, TRACK_OFFSET_SEARCH_DEBOUNCE_MS);
        return;
      }
      const control = event.target.closest('input[type="range"][data-setting]');
      if (control) {
        scheduleSettingPreview(control.dataset.setting, readSettingControlValue(control));
        return;
      }
      const numberControl = event.target.closest('.slider-number-control input[type="number"][data-setting]');
      if (!numberControl || numberControl.value === "" || !numberControl.validity.valid) return;
      scheduleSettingPreview(numberControl.dataset.setting, readSettingControlValue(numberControl));
    });

    $$('[data-color-text="foregroundColor"]').forEach(input => input.addEventListener("change", () => {
      const valid = /^#[0-9a-f]{6}$/i.test(input.value);
      setColorInputValidity(input, $("#foregroundColorError"), valid);
      if (valid) commitSetting("foregroundColor", input.value.toUpperCase());
    }));

    $("#sidebarToggle").addEventListener("click", () => {
      const collapsed = $("#appShell").classList.toggle("sidebar-collapsed");
      $("#sidebarToggle").setAttribute("aria-label", collapsed ? "展开侧栏" : "折叠侧栏");
    });
    $$("dialog").forEach(d => d.addEventListener("cancel", event => { event.preventDefault(); closeDialogWithAnimation(d); }));
    $("#restoreButton").addEventListener("click", () => $("#restoreDialog").showModal());
    $("#clearCacheButton").addEventListener("click", () => $("#clearDialog").showModal());
    $("#confirmRestore").addEventListener("click", () => { closeDialogWithAnimation($("#restoreDialog")); resetState(); });
    $("#confirmClear").addEventListener("click", () => { closeDialogWithAnimation($("#clearDialog")); bridge.post({ type: "clearCache" }); showToast("歌词与封面缓存已清理"); });
    $("#confirmSpectrumAudioAccess").addEventListener("click", () => {
      const mode = pendingSpectrumDisplayMode;
      if (!mode) return;
      pendingSpectrumDisplayMode = null;
      closeDialogWithAnimation($("#spectrumAudioConsentDialog"));
      bridge.post({ type: "confirmSpectrumAudioAccess", value: mode });
      markSaved();
    });
    $("#spectrumAudioConsentDialog").addEventListener("close", () => { pendingSpectrumDisplayMode = null; });
    $("#revokeSpectrumAudioAccessButton").addEventListener("click", () => {
      bridge.post({ type: "revokeSpectrumAudioAccess" });
      markSaved();
    });
    $$('[data-retry-spectrum-capture]').forEach(button => button.addEventListener("click", () => {
      spectrumCaptureState = { state: "waiting", message: "正在重试系统音频采集…" };
      renderSpectrumAudioAccess();
      closeDialogWithAnimation($("#spectrumCaptureFailureDialog"));
      bridge.post({ type: "retrySpectrumCapture" });
    }));
    $("#disableSpectrumButton").addEventListener("click", () => {
      closeDialogWithAnimation($("#spectrumCaptureFailureDialog"));
      bridge.post({ type: "disableSpectrum" });
      markSaved();
    });
    $("#trackOffsetPreviousPage").addEventListener("click", () => changeTrackOffsetPage(-1));
    $("#trackOffsetNextPage").addEventListener("click", () => changeTrackOffsetPage(1));
    $("#clearTrackOffsetsButton").addEventListener("click", () => $("#clearTrackOffsetsDialog").showModal());
    $("#confirmDeleteTrackOffset").addEventListener("click", () => {
      closeDialogWithAnimation($("#deleteTrackOffsetDialog"));
      if (pendingDeleteTrackOffsetKey) {
        bridge.post({ type: "deleteTrackOffset", value: pendingDeleteTrackOffsetKey });
        pendingDeleteTrackOffsetKey = null;
      }
    });
    $("#confirmClearTrackOffsets").addEventListener("click", () => {
      closeDialogWithAnimation($("#clearTrackOffsetsDialog"));
      bridge.post({ type: "clearTrackOffsets" });
    });
    $("#resetPlayerOffsetButton").addEventListener("click", () => {
      const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
      if (source) commitPlayerOffset(source.defaultOffset);
    });
    $("#playerSettingsDialog").addEventListener("click", event => {
      if (event.target === $("#playerSettingsDialog")) closeDialogWithAnimation($("#playerSettingsDialog"));
    });
    $("#playerSettingsDialog").addEventListener("close", () => {
      const sourceId = activePlayerSourceId;
      activePlayerSourceId = null;
      document.querySelector(`[data-player-settings="${sourceId}"]`)?.focus({ preventScroll: true });
    });
    $("#browseButton").addEventListener("click", () => bridge.post({ type: "pickLocalFolder" }));
    $$('[data-show-lyrics-window]').forEach(button => button.addEventListener("click", () => bridge.post({ type: "showLyricsWindow" })));
    $("#smtcMonitorButton").addEventListener("click", () => bridge.post({ type: "openSmtcMonitor" }));
    $("#spectrumTuningButton").addEventListener("click", () => bridge.post({ type: "openSpectrumTuning" }));
    $("#runLyricDiagnosticsButton").addEventListener("click", () => {
      if (lyricDiagnosticsState.status === "running") return;
      setLyricDiagnosticsState({ status: "running" });
      bridge.post({ type: "runLyricDiagnostics" });
    });
    const openRepository = () => { if (repositoryUrl) bridge.post({ type: "openExternalLink", value: repositoryUrl }); };
    $("#repositoryButton").addEventListener("click", openRepository);
    $$('[data-repository-link]').forEach(button => button.addEventListener("click", openRepository));
    $("#checkUpdateButton").addEventListener("click", () => {
      setUpdateStatus({ state: "checking", message: "正在连接 GitHub Releases" });
      bridge.post({ type: "checkForUpdates" });
    });
    $("#openReleaseButton").addEventListener("click", () => { if (updateReleaseUrl) bridge.post({ type: "openExternalLink", value: updateReleaseUrl }); });

    document.addEventListener("pointerdown", event => {
      if (event.button !== 0) return;
      const resizeHandle = event.target.closest?.("[data-window-resize]");
      if (!resizeHandle) return;
      event.preventDefault();
      event.stopImmediatePropagation();
      bridge.post({ type: "windowResizeStart", value: resizeHandle.dataset.windowResize });
    }, true);

    document.addEventListener("pointerdown", event => {
      if (event.button !== 0) return;
      const dragArea = event.target.closest("[data-caption-drag]");
      if (!dragArea || event.target.closest("button, input, select, textarea")) return;
      bridge.post({ type: "windowDrag" });
    });

    document.addEventListener("click", event => {
      const action = event.target.closest("[data-window-action]");
      if (!action) return;
      const actionType = action.dataset.windowAction;
      if (actionType === "minimize") bridge.post({ type: "windowMinimize" });
      else if (actionType === "maximize") bridge.post({ type: "windowMaximize" });
      else if (actionType === "close") bridge.post({ type: "windowClose" });
    });

    $("#colorPresets").innerHTML = presetColors.map(color => `<button class="color-preset" type="button" style="--preset:${color}" data-preset-color-value="${color}" aria-label="选择 ${color}"></button>`).join("");
    function receive(message) {
      if (message?.version !== 1 || !message.type) return;
      switch (message.type) {
        case "settingsState":
          setState(message.payload?.settings ?? {}, message.payload?.fonts ?? []);
          break;
        case "lyricsLayoutPreview":
          updateLayoutPreview(message.payload);
          break;
        case "settingsSaveResult":
          setSettingsSaveResult(message.payload);
          break;
        case "updateStatus":
          setUpdateStatus(message.payload);
          break;
        case "currentTrackOffset":
          setCurrentTrackOffsetData(message.payload);
          break;
        case "trackOffsetEntries":
          setTrackOffsetEntries(message.payload);
          break;
        case "trackOffsetSaveStatus":
          setTrackOffsetSaveStatus(message.payload);
          break;
        case "lyricDiagnosticsState":
          setLyricDiagnosticsState(message.payload);
          break;
        case "requestSpectrumDisplayMode":
          requestSpectrumDisplayMode(message.payload?.mode);
          break;
        case "spectrumCaptureState":
          setSpectrumCaptureState(message.payload);
          break;
        case "navigate":
          navigateToPage(message.payload?.page, Boolean(message.payload?.focusCurrentTrack));
          break;
        case "windowState":
          setWindowState(message.payload);
          break;
      }
    }

    window.settingsApp = { receive };
    setPageInteractionState($$("[data-page]"), document.querySelector("[data-page].active"));
    bridge.post({ type: "ready" });
