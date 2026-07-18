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
      foregroundColorMode: [{ value: "Dark", label: "深色" }, { value: "Light", label: "浅色" }, { value: "Custom", label: "自定义" }],
      horizontalAnchor: [{ value: "Left", label: "左侧" }, { value: "Center", label: "居中" }, { value: "Right", label: "右侧" }],
      trackOffsetSourceFilter: [{ value: "All", label: "全部歌词源" }],
      trackOffsetSort: [{ value: "updated", label: "最近修改" }, { value: "title", label: "歌曲名称" }, { value: "offset", label: "偏移量" }]
    };
    const presetColors = ["#FFFFFF", "#A1A1AA", "#18181B", "#EF4444", "#F97316", "#EAB308", "#22C55E", "#06B6D4", "#3B82F6", "#A855F7"];

    const pageMeta = {
      sources: ["播放源", "选择需要监听的音乐软件，并调整识别优先级。"],
      lyrics: ["歌词", "控制歌词显示、翻译和频谱策略。"],
      trackOffsets: ["单曲偏移", "调整当前歌曲同步，并管理按歌词源保存的偏移。"],
      appearance: ["外观", "调整文字与封面，并在任务栏预览中即时检查效果。"],
      window: ["窗口", "设置歌词窗口背景、宽度、位置与置顶行为。"],
      general: ["常规", "管理启动、后台运行和更新行为。"],
      advanced: ["高级", "用于诊断播放同步问题和维护缓存数据。"],
      about: ["关于", "查看版本、许可证与项目技术信息。"]
    };

    const sizeRanges = {
      fontSize: { safeKey: "useSafeFontSizeRange", safe: { min: 10, max: 24 }, extended: { min: 6, max: 96 } },
      coverSize: { safeKey: "useSafeCoverSizeRange", safe: { min: 20, max: 40 }, extended: { min: 12, max: 200 } }
    };

    let state = null;
    let sourceCatalog = sourceCatalogDefaults.map(item => ({ ...item, enabled: false }));
    let saveTimer;
    let toastTimer;
    let draggedSourceId = null;
    let pageAnimations = [];
    let pageTransitionToken = 0;
    let activeSelectTrigger = null;
    let activeSelectIndex = -1;
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

    const $ = selector => document.querySelector(selector);
    const $$ = selector => Array.from(document.querySelectorAll(selector));
    const escapeHtml = value => String(value).replace(/[&<>"]/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;" }[char]));
    const bridge = { post(message) { window.chrome?.webview?.postMessage(JSON.stringify(message)); } };

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

    function normalizeTrackOffset(value, fallback = 0) {
      const numeric = Number(value);
      if (!Number.isFinite(numeric)) return fallback;
      return Math.max(-5000, Math.min(5000, Math.round(numeric / 10) * 10));
    }

    function sourceDisplayName(source) {
      const known = sourceCatalogDefaults.find(item => item.adapter.toLowerCase() === String(source ?? "").toLowerCase());
      if (known) return known.name;
      if (String(source).toLowerCase() === "local") return "本地歌词";
      return source || "未知来源";
    }

    function formatTrackDuration(seconds) {
      const value = Math.max(0, Number(seconds) || 0);
      if (!value) return "时长未知";
      const minutes = Math.floor(value / 60);
      return `${String(minutes).padStart(2, "0")}:${String(Math.round(value % 60)).padStart(2, "0")}`;
    }

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
      const nextPage = pages.find(page => page.dataset.page === pageId);
      const currentPage = pages.find(page => page.classList.contains("active"));
      const currentIndex = currentPage ? pages.indexOf(currentPage) : 0;
      const nextIndex = pages.indexOf(nextPage);
      const heading = nextPage.querySelector('h2[tabindex="-1"]');
      const titleBlock = $("#pageTitle").parentElement;
      const updateTitleText = () => {
        $("#pageTitle").textContent = pageMeta[pageId][0];
        $("#pageSubtitle").textContent = pageMeta[pageId][1];
      };

      if (state) state.page = pageId;
      if (previousPageId !== pageId) bridge.post({ type: "settingsPageChanged", value: pageId });
      $$('[data-nav]').forEach(button => button.classList.toggle("active", button.dataset.nav === pageId));

      pageTransitionToken += 1;
      const token = pageTransitionToken;
      pageAnimations.forEach(animation => animation.cancel());
      pageAnimations = [];
      pages.forEach(page => page.classList.remove("transitioning"));

      if (!currentPage || currentPage === nextPage || reducedMotionQuery.matches || typeof nextPage.animate !== "function") {
        pages.forEach(page => page.classList.toggle("active", page === nextPage));
        titleBlock.style.transitionDuration = "0ms";
        titleBlock.style.opacity = "1";
        updateTitleText();
        if (moveFocus) heading?.focus({ preventScroll: true });
        return;
      }

      const direction = nextIndex > currentIndex ? 1 : -1;
      nextPage.style.transform = `translateX(${direction * 28}px)`;
      nextPage.classList.add("transitioning");

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
        pageAnimations.forEach(animation => animation.cancel());
        pageAnimations = [];
        if (moveFocus) heading?.focus({ preventScroll: true });
      });
    }

    function markSaved() {
      clearTimeout(saveTimer);
      $("#saveState").textContent = "正在应用…";
      saveTimer = setTimeout(() => { $("#saveState").textContent = "更改已实时应用"; }, 360);
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
      else control.value = value;
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
      $$('[data-color-text="foregroundColor"]').forEach(control => { control.value = state.foregroundColor.toUpperCase(); control.classList.remove("invalid"); });
      $$('[data-color-swatch]').forEach(swatch => { swatch.style.backgroundColor = state.foregroundColor; });
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
      state = {
        ...nextState,
        page: previousPage,
        foregroundColor,
        trackOffsetSourceFilter: previousTrackOffsetSourceFilter,
        trackOffsetSort: previousTrackOffsetSort
      };
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

    function commitSetting(key, value) {
      if (!state) return;
      if (key === "trackOffsetSourceFilter" || key === "trackOffsetSort") {
        state[key] = value;
        expandedTrackOffsetKey = null;
        syncControls();
        requestTrackOffsetPage(1);
        return;
      }
      const previousCornerRadius = state.coverCornerRadius;
      state[key] = value;
      if (key === "foregroundColor") {
        state.foregroundColor = fromArgb(value);
        state.customForegroundColor = state.foregroundColor;
        state.foregroundColorMode = "Custom";
      }
      if (key === "coverCornerRadius") state.coverCornerRadius = Math.min(state.coverCornerRadius, state.coverSize / 2);
      syncColorMode(); applyDependencies(); syncSizeBounds(); updateOutputs(); syncControls(); markSaved();
      const payload = key === "foregroundColor" ? toArgb(state.foregroundColor) : state[key];
      bridge.post({ type: "update", key, value: payload });
      if (key === "useSafeFontSizeRange") bridge.post({ type: "update", key: "fontSize", value: state.fontSize });
      if (key === "useSafeCoverSizeRange") bridge.post({ type: "update", key: "coverSize", value: state.coverSize });
      if (key !== "coverCornerRadius" && previousCornerRadius !== state.coverCornerRadius) {
        bridge.post({ type: "update", key: "coverCornerRadius", value: state.coverCornerRadius });
      }
    }

    function chooseSelectOption(index) {
      if (!activeSelectTrigger) return;
      const key = activeSelectTrigger.dataset.setting;
      const option = (selectOptions[key] ?? [])[index];
      if (!option) return;
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
      $("#hueSlider").value = Math.round(colorDraft.h);
      $("#colorCursor").style.left = `${colorDraft.s * 100}%`;
      $("#colorCursor").style.top = `${(1 - colorDraft.v) * 100}%`;
      $("#colorDraftPreview").style.backgroundColor = colorDraft.hex;
      if (!options.keepInput) { $("#colorDraftInput").value = colorDraft.hex; $("#colorDraftInput").classList.remove("invalid"); }
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
      $("#colorArea").focus({ preventScroll: true });
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

    function syncColorMode() {
      const custom = state.foregroundColorMode === "Custom";
      if (state.foregroundColorMode === "Dark") state.foregroundColor = "#111827";
      else if (state.foregroundColorMode === "Light") state.foregroundColor = "#FFFFFF";
      else state.foregroundColor = state.customForegroundColor;
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

    function syncSizeBounds() {
      Object.entries(sizeRanges).forEach(([valueKey, config]) => {
        const useSafeRange = Boolean(state[config.safeKey]);
        const range = useSafeRange ? config.safe : config.extended;
        state[valueKey] = Math.min(range.max, Math.max(range.min, Number(state[valueKey])));
        const input = document.querySelector(`[data-setting="${valueKey}"]`);
        input.min = range.min;
        input.max = range.max;
        const copy = document.querySelector(`[data-safe-copy="${valueKey}"]`);
        copy.textContent = `${useSafeRange ? "安全" : "扩展"}范围 ${range.min}–${range.max} px`;
      });
      state.coverCornerRadius = Math.min(state.coverCornerRadius, state.coverSize / 2);
    }

    function applyDependencies() {
      $$('[data-depends]').forEach(row => {
        const enabled = Boolean(state[row.dataset.depends]);
        row.classList.toggle("is-disabled", !enabled);
        row.querySelectorAll("input, select, textarea, button").forEach(control => { control.disabled = !enabled; });
      });
    }

    function updateOutputs() {
      $$('[data-output]').forEach(node => {
        const key = node.dataset.output;
        node.textContent = key === "backgroundOpacity" ? state[key].toFixed(2) : `${state[key]} px`;
      });
    }

    function refresh() {
      renderSources();
      renderPriority();
      renderTrackOffsets();
      if ($("#playerSettingsDialog").open) renderPlayerSettings();
      syncSizeBounds();
      syncColorMode();
      syncControls();
      applyDependencies();
      updateOutputs();
      activatePage(state.page, false);
    }

    function resetState() {
      bridge.post({ type: "resetDefaults" });
      markSaved();
    }

    document.addEventListener("click", event => {
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
    $$(".page").forEach(page => page.addEventListener("scroll", () => { closeSelect(false); closeColorPopover(false); }, { passive: true }));

    $("#colorPickerButton").addEventListener("click", () => $("#colorPopover").getAttribute("data-state") !== "open" ? openColorPopover() : closeColorPopover(true));
    $("#colorArea").addEventListener("pointerdown", event => {
      colorPointerActive = true;
      $("#colorArea").setPointerCapture(event.pointerId);
      updateColorFromPointer(event);
    });
    $("#colorArea").addEventListener("pointermove", event => { if (colorPointerActive) updateColorFromPointer(event); });
    $("#colorArea").addEventListener("pointerup", event => { colorPointerActive = false; $("#colorArea").releasePointerCapture(event.pointerId); });
    $("#colorArea").addEventListener("keydown", event => {
      const step = event.shiftKey ? .1 : .02;
      if (!["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown"].includes(event.key)) return;
      event.preventDefault();
      if (event.key === "ArrowLeft") colorDraft.s = Math.max(0, colorDraft.s - step);
      if (event.key === "ArrowRight") colorDraft.s = Math.min(1, colorDraft.s + step);
      if (event.key === "ArrowUp") colorDraft.v = Math.min(1, colorDraft.v + step);
      if (event.key === "ArrowDown") colorDraft.v = Math.max(0, colorDraft.v - step);
      updateColorDraft();
    });
    $("#hueSlider").addEventListener("input", event => { colorDraft.h = Number(event.target.value); updateColorDraft(); });
    $("#colorDraftInput").addEventListener("input", event => {
      const valid = /^#[0-9a-f]{6}$/i.test(event.target.value);
      event.target.classList.toggle("invalid", !valid);
      if (valid) setColorDraftFromHex(event.target.value);
    });
    $("#colorPresets").addEventListener("click", event => {
      const preset = event.target.closest("[data-preset-color-value]");
      if (preset) setColorDraftFromHex(preset.dataset.presetColorValue);
    });
    $("#colorCancelButton").addEventListener("click", () => closeColorPopover(true));
    $("#colorApplyButton").addEventListener("click", () => {
      if (!/^#[0-9a-f]{6}$/i.test($("#colorDraftInput").value)) { $("#colorDraftInput").classList.add("invalid"); showToast("请输入 #RRGGBB 格式的颜色值"); return; }
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
      let value = control.type === "checkbox" ? control.checked : control.type === "number" || control.type === "range" ? Number(control.value) : control.value;
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
      if (!control) return;
      commitSetting(control.dataset.setting, Number(control.value));
    });

    $$('[data-color-text="foregroundColor"]').forEach(input => input.addEventListener("change", () => {
      const valid = /^#[0-9a-f]{6}$/i.test(input.value);
      input.classList.toggle("invalid", !valid);
      if (valid) { commitSetting("foregroundColor", input.value.toUpperCase()); }
      else { showToast("请输入 #RRGGBB 格式的颜色值"); }
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
    window.settingsApp = { setState, setUpdateStatus, setCurrentTrackOffsetData, setTrackOffsetEntries, setTrackOffsetSaveStatus, navigateToPage };
    window.settingsApp.setWindowState = setWindowState;
    bridge.post({ type: "ready" });
