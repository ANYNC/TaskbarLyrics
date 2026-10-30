    const sourceCatalogDefaults = [
      { id: "qqmusic", name: window.taskbarLyricsI18n.t("UI_QQMusic"), adapter: "QQMusic", settingKey: "enableQQMusic", icon: "../../Assets/PlayerIcons/QQ音乐.png" },
      { id: "netease", name: window.taskbarLyricsI18n.t("UI_NetEaseCloudMusic"), adapter: "Netease", settingKey: "enableNetease", icon: "../../Assets/PlayerIcons/网易云音乐.png" },
      { id: "kugou", name: window.taskbarLyricsI18n.t("UI_KugouMusic"), adapter: "Kugou", settingKey: "enableKugou", icon: "../../Assets/PlayerIcons/酷狗音乐.png" },
      { id: "spotify", name: "Spotify", adapter: "Spotify", settingKey: "enableSpotify", icon: "../../Assets/PlayerIcons/spotify.png" },
      { id: "applemusic", name: "Apple Music", adapter: "AppleMusic", settingKey: "enableAppleMusic", icon: "../../Assets/PlayerIcons/Apple Music.png.png" },
      { id: "sodamusic", name: window.taskbarLyricsI18n.t("UI_SodaMusic"), adapter: "SodaMusic", settingKey: "enableSodaMusic", icon: "../../Assets/PlayerIcons/Soda Music.png" },
      { id: "foobar2000", name: "foobar2000", adapter: "Foobar2000", settingKey: "enableFoobar2000", icon: "../../Assets/PlayerIcons/Foobar2000.png" },
      { id: "browser", name: window.taskbarLyricsI18n.t("UI_Browser"), adapter: "Browser", settingKey: "enableBrowser", icon: "../../Assets/PlayerIcons/Browser.png" }
    ];
    const lyricProviderDefaults = [
      { providerId: "QQMusic", name: window.taskbarLyricsI18n.t("UI_QQMusic") },
      { providerId: "Kugou", name: window.taskbarLyricsI18n.t("UI_KugouMusic") },
      { providerId: "Netease", name: window.taskbarLyricsI18n.t("UI_NetEaseCloudMusic") },
      { providerId: "LRCLIB", name: "LRCLIB" }
    ];
    const gettingStartedPlayers = window.taskbarLyricsPlayerCompatibility.getPlayers();
    let gettingStartedPlayer = gettingStartedPlayers[0].id;

    const selectOptions = {
      spectrumDisplayMode: [{ value: "Disabled", label: window.taskbarLyricsI18n.t("UI_Close") }, { value: "PureMusicOnly", label: window.taskbarLyricsI18n.t("UI_InstrumentalOnly") }, { value: "PureMusicOrNoLyrics", label: window.taskbarLyricsI18n.t("UI_InstrumentalOrNoLyrics") }, { value: "Always", label: window.taskbarLyricsI18n.t("UI_Always") }],
      fontFamily: [],
      fontWeight: [{ value: "Light", label: window.taskbarLyricsI18n.t("UI_Light_5") }, { value: "Normal", label: window.taskbarLyricsI18n.t("UI_General") }, { value: "Medium", label: window.taskbarLyricsI18n.t("UI_Medium") }, { value: "SemiBold", label: window.taskbarLyricsI18n.t("UI_Semibold") }, { value: "Bold", label: window.taskbarLyricsI18n.t("UI_Bold") }],
      foregroundColorMode: [{ value: "System", label: window.taskbarLyricsI18n.t("UI_FollowSystem") }, { value: "Dark", label: window.taskbarLyricsI18n.t("UI_Dark") }, { value: "Light", label: window.taskbarLyricsI18n.t("UI_Light") }, { value: "Custom", label: window.taskbarLyricsI18n.t("UI_Custom") }],
      trackOffsetSourceFilter: [{ value: "All", label: window.taskbarLyricsI18n.t("UI_AllLyricSources") }],
      trackOffsetSort: [{ value: "updated", label: window.taskbarLyricsI18n.t("UI_RecentlyModified") }, { value: "title", label: window.taskbarLyricsI18n.t("UI_TrackTitle") }, { value: "offset", label: window.taskbarLyricsI18n.t("UI_Offset") }],
      uiLanguage: []
    };
    const presetColors = ["#FFFFFF", "#A1A1AA", "#18181B", "#EF4444", "#F97316", "#EAB308", "#22C55E", "#06B6D4", "#3B82F6", "#A855F7"];

    const pageMeta = {
      gettingStarted: [window.taskbarLyricsI18n.t("UI_GetStarted"), window.taskbarLyricsI18n.t("UI_CheckPlayerRequirementsBeforeDisplayingLyrics")],
      sources: [window.taskbarLyricsI18n.t("UI_Sources"), window.taskbarLyricsI18n.t("UI_ChooseMusicPlayersToMonitorAndConfigureTheirLyric")],
      shortcuts: [window.taskbarLyricsI18n.t("UI_Shortcuts"), window.taskbarLyricsI18n.t("UI_SetGlobalShortcutsForPlaybackControlsAndLyricDisplay")],
      lyrics: [window.taskbarLyricsI18n.t("UI_Lyrics"), window.taskbarLyricsI18n.t("UI_ManageLyricDisplayTranslationAndSpectrumBehavior")],
      trackOffsets: [window.taskbarLyricsI18n.t("UI_TrackOffsets"), window.taskbarLyricsI18n.t("UI_AdjustCurrentTrackSynchronizationAndManageOffsetsSavedPer")],
      displayArea: [window.taskbarLyricsI18n.t("UI_DisplayAppearance"), window.taskbarLyricsI18n.t("UI_AdjustLyricSizeTextAppearanceAndPositionWithLive")],
      general: [window.taskbarLyricsI18n.t("UI_General"), window.taskbarLyricsI18n.t("UI_ManageStartupBehaviorAndInterfaceTheme")],
      advanced: [window.taskbarLyricsI18n.t("UI_Advanced"), window.taskbarLyricsI18n.t("UI_ManageLyricHintsDiagnosticToolsAndCachedData")],
      lyricDiagnostics: [window.taskbarLyricsI18n.t("UI_LyricMatching"), window.taskbarLyricsI18n.t("UI_FindLyricCandidatesForTheCurrentSMTCTrackCompare")],
      about: [window.taskbarLyricsI18n.t("UI_About"), window.taskbarLyricsI18n.t("UI_ViewVersionLicenseAndProjectTechnologyInformation")]
    };

    let state = null;
    let initialPageId = "";
    let sourceCatalog = sourceCatalogDefaults.map(item => ({ ...item, enabled: false }));
    let toastTimer;
    let draggedPlayerSourceId = null;
    let draggedLyricProviderId = null;
    let availablePlayerSessions = [];
    let selectedPlayerSessionId = null;
    let playerDrawerMode = "edit";
    let pendingPlayerSourceAction = null;
    let playerInfoBusy = false;
    let selectedPlayerIconDataUrl = "";
    let selectedSessionAutoIconDataUrl = "";
    let selectedPlayerIconMode = "preset";
    let selectedPlayerPresetIconId = "note";
    let selectedPlayerPresetColor = "#EAB308";
    let newPlayerEnabled = true;
    let newPlayerLyricProviders = lyricProviderDefaults.map(item => ({ providerId: item.providerId, enabled: true }));
    let playerIconReading = false;
    let playerIconReadToken = 0;
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
    const liveRangeValues = new Map();
    let rangePreviewFrame = 0;
    let announceNextLayoutPreview = false;
    let lyricDiagnosticsState = { status: "idle", track: null, report: null, message: "", apply: null };
    let diagnosticApplyFocus = null;
    document.addEventListener("focusin", event => {
      if (diagnosticApplyFocus && event.target !== diagnosticApplyFocus) diagnosticApplyFocus = null;
    });
    let pendingSpectrumDisplayMode = null;
    let pendingFallbackSettings = null;
    let openFallbackState = null;
    let spectrumCaptureState = { state: "disabled", message: "" };

    const $ = selector => document.querySelector(selector);
    const $$ = selector => Array.from(document.querySelectorAll(selector));
    const escapeHtml = value => String(value).replace(/[&<>"]/g, char => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", "\"": "&quot;" }[char]));
    const bridge = { post(message) { window.chrome?.webview?.postMessage(JSON.stringify(message)); } };

    bridge.post = window.taskbarLyricsBridge.post.bind(window.taskbarLyricsBridge);

    const presetPlayerIconIds = ["note", "disc"];
    const validPresetPlayerColor = color => /^#[0-9a-f]{6}$/i.test(color);
    const presetPlayerPaths = {
      note: "M16 3.3 8 5v10.1a4.7 4.7 0 0 0-2-.4c-2.5 0-4.5 1.5-4.5 3.4s2 3.4 4.5 3.4 4.5-1.5 4.5-3.4V8.2l9-1.9v6.8a4.7 4.7 0 0 0-2-.4c-2.5 0-4.5 1.5-4.5 3.4s2 3.4 4.5 3.4 4.5-1.5 4.5-3.4V2.1L16 3.3Z",
      disc: "M12 2a10 10 0 1 0 0 20 10 10 0 0 0 0-20Zm0 7a3 3 0 1 0 0 6 3 3 0 0 0 0-6Z"
    };
    const presetPlayerIcon = (id, color) => `<svg class="preset-player-icon preset-icon-${id}" viewBox="0 0 24 24" style="color:${color}" aria-hidden="true"><path fill="currentColor" fill-rule="evenodd" d="${presetPlayerPaths[id]}"></path></svg>`;
    const defaultPlayerIcon = () => '<img class="default-player-icon" src="../../Assets/PlayerIcons/Presets/music-player.svg" alt="" draggable="false">';
    const safeSessionIcon = value => typeof value === "string" && value.length <= 180000 &&
      /^data:image\/(?:png|jpeg|webp);base64,[A-Za-z0-9+/=]+$/.test(value) ? value : "";
    const playerIcon = source => source.icon
      ? `<img src="${escapeHtml(source.icon)}" alt="" draggable="false">`
      : presetPlayerIconIds.includes(source.presetIconId) && validPresetPlayerColor(source.presetIconColor)
        ? presetPlayerIcon(source.presetIconId, source.presetIconColor)
        : defaultPlayerIcon();

    function updateAddPlayerButton() {
      const invalid = !selectedPlayerSessionId || !$("#customPlayerName").value.trim() || playerInfoBusy ||
        playerIconReading ||
        (selectedPlayerIconMode === "custom" && !selectedPlayerIconDataUrl) ||
        (selectedPlayerIconMode === "preset" && selectedPlayerPresetIconId && !validPresetPlayerColor(selectedPlayerPresetColor));
      $("#confirmAddPlayerSource").disabled = Boolean(invalid);
      $("#savePlayerInfoButton").disabled = Boolean(invalid);
    }

    function renderSelectedPlayerIcon() {
      $("#customPlayerIconPreview").innerHTML = selectedPlayerIconMode === "custom" && selectedPlayerIconDataUrl
        ? `<img src="${escapeHtml(selectedPlayerIconDataUrl)}" alt="">`
        : selectedPlayerPresetIconId
          ? presetPlayerIcon(selectedPlayerPresetIconId, selectedPlayerPresetColor)
          : defaultPlayerIcon();
      $("#clearCustomPlayerIconButton").hidden =
        (selectedPlayerIconMode !== "custom" && !selectedPlayerPresetIconId) ||
        (playerDrawerMode === "add" && selectedPlayerIconDataUrl === selectedSessionAutoIconDataUrl);
      const awaitingSession = playerDrawerMode === "add" && !selectedPlayerSessionId;
      $("#chooseCustomPlayerIconButton").disabled = awaitingSession;
      $("#customPlayerName").disabled = awaitingSession;
      $("#customPlayerName").placeholder = awaitingSession ? window.taskbarLyricsI18n.t("UI_SelectAMediaSessionFirst") : window.taskbarLyricsI18n.t("UI_EnterADisplayName");
      updateAddPlayerButton();
    }

    function clearCustomPlayerIcon() {
      playerIconReadToken++;
      playerIconReading = false;
      selectedPlayerIconDataUrl = playerDrawerMode === "add" ? selectedSessionAutoIconDataUrl : "";
      selectedPlayerIconMode = selectedPlayerIconDataUrl ? "custom" : "preset";
      selectedPlayerPresetIconId = "";
      selectedPlayerPresetColor = "";
      $("#customPlayerIcon").value = "";
      $("#customPlayerIconStatus").textContent = selectedPlayerIconDataUrl
        ? window.taskbarLyricsI18n.t("UI_UsingTheSystemDetectedIconClickToUploadAnother")
        : window.taskbarLyricsI18n.t("UI_ClickToUploadAPNGJPEGOrWebPImage");
      renderSelectedPlayerIcon();
    }

    function resetSelectedPlayerIcon() {
      clearCustomPlayerIcon();
    }

    function loadPlayerInfoForm(source) {
      selectedPlayerSessionId = source.adapter;
      resetSelectedPlayerIcon();
      $("#customPlayerName").value = source.name;
      if (source.icon) {
        selectedPlayerIconMode = "custom";
        selectedPlayerIconDataUrl = source.icon;
        $("#customPlayerIconStatus").textContent = window.taskbarLyricsI18n.t("UI_CurrentCustomImage");
      } else if (presetPlayerIconIds.includes(source.presetIconId) && validPresetPlayerColor(source.presetIconColor)) {
        selectedPlayerPresetIconId = source.presetIconId;
        selectedPlayerPresetColor = source.presetIconColor.toUpperCase();
      } else {
        selectedPlayerPresetIconId = "";
      }
      renderSelectedPlayerIcon();
    }

    function playerInfoPayload() {
      const preset = selectedPlayerIconMode === "preset" && Boolean(selectedPlayerPresetIconId);
      return {
        sourceAppUserModelId: selectedPlayerSessionId,
        displayName: $("#customPlayerName").value.trim(),
        iconDataUrl: selectedPlayerIconMode === "custom" ? selectedPlayerIconDataUrl : "",
        presetIconId: preset ? selectedPlayerPresetIconId : "",
        presetIconColor: preset ? selectedPlayerPresetColor : "",
        ...(playerDrawerMode === "add" ? { enabled: newPlayerEnabled, lyricProviders: newPlayerLyricProviders } : {})
      };
    }

    function renderPlayerCompatibility(container, playerId, onboarding = false) {
      window.taskbarLyricsPlayerCompatibility.render(container, playerId,
        url => bridge.post({ type: "openExternalLink", value: url }), onboarding);
    }

    function gettingStartedNeedsPlayer() {
      const profile = window.taskbarLyricsPlayerCompatibility.getProfile(gettingStartedPlayer);
      return profile.requiresAdding && (gettingStartedPlayer === "other" || !sourceCatalog.some(source =>
        source.custom && window.taskbarLyricsPlayerCompatibility.resolveSourceProfile(source.adapter) === gettingStartedPlayer));
    }

    function renderGettingStarted() {
      const profile = window.taskbarLyricsPlayerCompatibility.getProfile(gettingStartedPlayer);
      const needsPlayer = gettingStartedNeedsPlayer();
      renderPlayerCompatibility($("#gettingStartedGuidance"), gettingStartedPlayer, true);
      $("[data-getting-started-action-label]").textContent = profile.level === "setup" ? window.taskbarLyricsI18n.t("UI_ViewSetupGuide") : needsPlayer ? window.taskbarLyricsI18n.t("UI_AddPlayer") : window.taskbarLyricsI18n.t("UI_ShowLyrics");
      $("[data-getting-started-external]").hidden = profile.level !== "setup";
      $("#gettingStartedAddNote").hidden = !needsPlayer;
    }

    function syncGettingStartedTabStops() {
      const radios = $$('.getting-started-option:not([hidden]) input');
      const tabbable = radios.find(radio => radio.checked) ?? radios[0];
      $$('input[name="gettingStartedPlayer"]').forEach(radio => { radio.tabIndex = radio === tabbable ? 0 : -1; });
    }

    function filterGettingStartedPlayers() {
      const query = $("#gettingStartedSearch").value.trim().toLowerCase().replace(/\s+/g, "");
      let count = 0;
      $$("#gettingStartedPlayerList .getting-started-option").forEach(option => {
        option.hidden = !option.dataset.search.toLowerCase().replace(/\s+/g, "").includes(query);
        if (!option.hidden) count++;
      });
      $("#gettingStartedNoResults").hidden = count !== 0;
      $("#gettingStartedSearchStatus").textContent = query ? window.taskbarLyricsI18n.format("UI_FoundPlayersOtherPlayerRemainsAvailable", count) : "";
      syncGettingStartedTabStops();
    }

    function initializeGettingStarted() {
      const option = player => `<label class="getting-started-option" data-search="${escapeHtml(`${player.name} ${player.search ?? ""}`)}"><input type="radio" name="gettingStartedPlayer" value="${player.id}"${player.id === gettingStartedPlayer ? " checked" : ""}><img class="${player.id === "other" ? "default-player-icon" : ""}" src="../../Assets/PlayerIcons/${escapeHtml(player.icon)}" alt="" draggable="false"><span>${escapeHtml(player.name)}</span><svg class="getting-started-check" viewBox="0 0 24 24" aria-hidden="true"><path d="m5 12 4 4L19 6"/></svg></label>`;
      $("#gettingStartedPlayerList").innerHTML = gettingStartedPlayers.filter(player => player.id !== "other").map(option).join("");
      $("#gettingStartedOther").innerHTML = gettingStartedPlayers.filter(player => player.id === "other").map(option).join("");
      $("#gettingStartedCount").textContent = gettingStartedPlayers.length;
      $("#gettingStartedSearch").addEventListener("input", filterGettingStartedPlayers);
      $("#gettingStartedSearch").addEventListener("keydown", event => {
        if (event.key !== "Escape") return;
        event.preventDefault();
        $("#gettingStartedSearch").value = "";
        filterGettingStartedPlayers();
      });
      $(".getting-started-players").addEventListener("keydown", event => {
        if (!event.target.matches('input[name="gettingStartedPlayer"]') ||
          !["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight", "Home", "End"].includes(event.key)) return;
        event.preventDefault();
        const radios = $$('.getting-started-option:not([hidden]) input');
        const current = radios.indexOf(event.target);
        const next = event.key === "Home" ? 0 : event.key === "End" ? radios.length - 1
          : (current + (["ArrowDown", "ArrowRight"].includes(event.key) ? 1 : -1) + radios.length) % radios.length;
        radios[next].click();
        radios[next].focus({ preventScroll: true });
        radios[next].closest("label").scrollIntoView?.({ block: "nearest" });
      });
      syncGettingStartedTabStops();
      renderGettingStarted();
    }

    function startGettingStartedPlayback() {
      if (gettingStartedNeedsPlayer()) {
        navigateToPage("sources");
        openAddPlayerDrawer();
      } else {
        bridge.post({ type: "showLyricsWindow" });
        navigateToPage("sources");
      }
    }

    function renderSources() {
      const grid = $("#sourceGrid");
      grid.innerHTML = sourceCatalog.map(source => `
        <article class="source-card ${source.enabled ? "enabled" : ""}" data-player-card="${escapeHtml(source.id)}">
          <button class="source-drag-handle" type="button" draggable="true" data-player-drag-id="${escapeHtml(source.id)}" aria-label="${window.taskbarLyricsI18n.t("UI_Drag")} ${escapeHtml(source.name)} ${window.taskbarLyricsI18n.t("UI_ToChangePriorityDuringSimultaneousPlayback")}" aria-keyshortcuts="Alt+ArrowUp Alt+ArrowDown Alt+ArrowLeft Alt+ArrowRight" title="${window.taskbarLyricsI18n.t("UI_DragToChangePlayerPriorityOrPressAltArrow")}">
            <span class="source-logo ${source.custom ? "custom-player-icon" : ""}" aria-hidden="true">${playerIcon(source)}</span>
            <span class="source-info"><strong>${escapeHtml(source.name)}</strong><small>${source.enabled ? window.taskbarLyricsI18n.t("UI_Enabled") : window.taskbarLyricsI18n.t("UI_Disabled")} · ${formatPlayerOffset(getPlayerOffset(source))}</small></span>
          </button>
          <button class="source-settings-button" type="button" data-player-settings="${escapeHtml(source.id)}" aria-label="${window.taskbarLyricsI18n.t("UI_Open")} ${escapeHtml(source.name)} ${window.taskbarLyricsI18n.t("UI_Settings")}"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M12 15.25A3.25 3.25 0 1 0 12 8.75a3.25 3.25 0 0 0 0 6.5Z" fill="none" stroke="currentColor" stroke-width="1.7"/><path d="M19.1 13.3a7.5 7.5 0 0 0 0-2.6l2-1.55-2-3.46-2.5 1a7.6 7.6 0 0 0-2.25-1.3L14 2.75h-4l-.35 2.64A7.6 7.6 0 0 0 7.4 6.7l-2.5-1-2 3.46 2 1.55a7.5 7.5 0 0 0 0 2.6l-2 1.55 2 3.46 2.5-1a7.6 7.6 0 0 0 2.25 1.3l.35 2.64h4l.35-2.64a7.6 7.6 0 0 0 2.25-1.3l2.5 1 2-3.46-2-1.55Z" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linejoin="round"/></svg></button>
        </article>`).join("");
      const enabled = sourceCatalog.filter(source => source.enabled).length;
      $("#sourceCount").textContent = window.taskbarLyricsI18n.format("UI_Enabled_6", enabled, sourceCatalog.length);
    }

    function commitPlayerSourceOrder(order) {
      sourceCatalog = order.map(id => sourceCatalog.find(source => source.id === id));
      state.sourceRecognitionOrder = sourceCatalog.map(source => source.adapter);
      bridge.post({ type: "reorderSources", value: state.sourceRecognitionOrder });
      renderSources();
      markSaved();
    }

    function announcePlayerSourcePosition(sourceId) {
      const index = sourceCatalog.findIndex(source => source.id === sourceId);
      if (index < 0) return;
      $("#sourceOrderAnnouncement").textContent = window.taskbarLyricsI18n.format("UI_MovedToPositionOf", sourceCatalog[index].name, index + 1, sourceCatalog.length);
    }

    function renderAvailablePlayerSessions(payload) {
      const status = $("#playerDiscoveryStatus");
      const list = $("#availablePlayerSessions");
      $("#refreshPlayerSessionsButton").disabled = false;
      list.removeAttribute("aria-busy");
      if (payload?.status === "error") {
        status.className = "player-discovery-status error";
        status.textContent = window.taskbarLyricsI18n.t("UI_CannotReadMediaSessionsPleaseRetryLater");
        return;
      } else {
        availablePlayerSessions = (Array.isArray(payload?.sessions) ? payload.sessions : [])
          .filter(session => !session.isBuiltIn &&
            !sourceCatalog.some(source => source.adapter.toLowerCase() === String(session.sourceAppUserModelId).toLowerCase()));
        status.className = availablePlayerSessions.length ? "player-discovery-status" : "player-discovery-status empty";
        status.innerHTML = availablePlayerSessions.length ? "" :
          `<span class="player-discovery-empty-icon" aria-hidden="true">${defaultPlayerIcon()}</span><strong>${window.taskbarLyricsI18n.t("UI_NoPlayersAvailableToAdd")}</strong><small>${window.taskbarLyricsI18n.t("UI_OpenAPlayerAndPlayASongThenRefresh")}</small>`;
      }
      if (playerDrawerMode === "add" && !availablePlayerSessions.some(session => session.sourceAppUserModelId === selectedPlayerSessionId)) {
        selectedPlayerSessionId = null;
        selectedSessionAutoIconDataUrl = "";
        $("#customPlayerName").value = "";
        resetSelectedPlayerIcon();
      }
      list.innerHTML = availablePlayerSessions.map((session, index) => `
        <button class="available-player-session" type="button" data-player-session-index="${index}" aria-pressed="${session.sourceAppUserModelId === selectedPlayerSessionId}">
          <span class="available-player-session-icon" aria-hidden="true">${safeSessionIcon(session.iconDataUrl) ? `<img src="${escapeHtml(session.iconDataUrl)}" alt="">` : defaultPlayerIcon()}</span>
          <span class="available-player-session-info"><strong>${escapeHtml(session.displayName)}</strong>
            <small>${session.isPlaying ? window.taskbarLyricsI18n.t("UI_Playing") : window.taskbarLyricsI18n.t("UI_Paused")}${session.trackTitle ? ` · ${escapeHtml(session.trackTitle)}` : ""}</small>
            ${session.displayName === session.sourceAppUserModelId ? "" : `<small>${escapeHtml(session.sourceAppUserModelId)}</small>`}
          </span>
        </button>`).join("");
    }

    function requestPlayerSessions(preserveResults = true) {
      const list = $("#availablePlayerSessions");
      const status = $("#playerDiscoveryStatus");
      if (!preserveResults) {
        availablePlayerSessions = [];
        list.innerHTML = "";
        status.textContent = "";
      }
      if (!list.childElementCount && !status.textContent.trim()) {
        status.className = "player-discovery-status loading";
        status.textContent = window.taskbarLyricsI18n.t("UI_DetectingMediaSessions");
      }
      list.setAttribute("aria-busy", "true");
      $("#refreshPlayerSessionsButton").disabled = true;
      bridge.post({ type: "discoverPlayerSessions" });
    }

    function getPlayerOffset(source) {
      const value = Number(state?.playerLyricOffsets?.[source.adapter]);
      return Number.isFinite(value) ? Math.max(-5000, Math.min(5000, Math.round(value))) : source.defaultOffset;
    }

    function formatPlayerOffset(value) {
      if (value > 0) return window.taskbarLyricsI18n.format("UI_AdvanceMs", value);
      if (value < 0) return window.taskbarLyricsI18n.format("UI_DelayMs", Math.abs(value));
      return window.taskbarLyricsI18n.t("UI_InSync");
    }

    const normalizeTrackOffset = (value, fallback = 0) => window.taskbarLyricsTrackOffsets.normalize(value, fallback);

    function sourceDisplayName(source) {
      const known = sourceCatalogDefaults.find(item => item.adapter.toLowerCase() === String(source ?? "").toLowerCase());
      if (known) return known.name;
      if (String(source).toLowerCase() === "local") return window.taskbarLyricsI18n.t("UI_LocalLyrics");
      return source || window.taskbarLyricsI18n.t("UI_UnknownSource");
    }

    const formatTrackDuration = seconds => window.taskbarLyricsTrackOffsets.formatDuration(seconds);

    function formatTrackOffsetDate(value) {
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) return "--";
      return new Intl.DateTimeFormat(window.taskbarLyricsI18n.locale, { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit" }).format(date);
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
        badge.textContent = window.taskbarLyricsI18n.t("UI_WaitingForPlayback");
        container.innerHTML = `<div class="track-offset-empty"><div><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M9 18V5l10-2v13"/><circle cx="6" cy="18" r="3"/><circle cx="16" cy="16" r="3"/></svg><strong>${window.taskbarLyricsI18n.t("UI_NoTrackAvailableToAdjust")}</strong><small>${window.taskbarLyricsI18n.t("UI_PlayATrackAndLoadLyricsToAdjustIts")}</small></div></div>`;
        return;
      }

      const ready = Boolean(current.lyricSourceReady);
      badge.textContent = ready ? sourceDisplayName(current.lyricSource) : window.taskbarLyricsI18n.t("UI_SearchingForLyrics_7");
      if (!ready) {
        container.innerHTML = `<div class="track-offset-empty"><div><span class="spinner" aria-hidden="true"></span><strong>${escapeHtml(current.title || window.taskbarLyricsI18n.t("UI_CurrentTrack"))}</strong><small>${window.taskbarLyricsI18n.t("UI_TrackOffsetsBecomeAvailableOnceALyricSourceIs")}</small></div></div>`;
        return;
      }

      const playerOffset = Number(current.playerOffsetMilliseconds) || 0;
      const trackOffset = Number(current.trackOffsetMilliseconds) || 0;
      const effectiveOffset = Number(current.effectiveOffsetMilliseconds) || 0;
      container.innerHTML = `
        <div class="current-track-layout">
          <div class="current-track-identity">
            <strong title="${escapeHtml(current.title)}">${escapeHtml(current.title || window.taskbarLyricsI18n.t("UI_UnknownSong"))}</strong>
            <small title="${escapeHtml(current.artist)}">${escapeHtml(current.artist || window.taskbarLyricsI18n.t("UI_UnknownArtist"))} · ${formatTrackDuration(current.durationSeconds)}</small>
            <div class="current-track-source"><span class="track-offset-badge">${escapeHtml(sourceDisplayName(current.sourceApp))}</span><span class="track-offset-badge">${window.taskbarLyricsI18n.t("UI_LyricSource")} ${escapeHtml(sourceDisplayName(current.lyricSource))}</span></div>
          </div>
          <div class="current-track-controls">
            <div class="offset-summary">
              <div class="offset-summary-item"><span>${window.taskbarLyricsI18n.t("UI_PlayerOffset")}</span><strong>${formatPlayerOffset(playerOffset)}</strong></div>
              <div class="offset-summary-item"><span>${window.taskbarLyricsI18n.t("UI_TrackOffsets")}</span><strong>${formatPlayerOffset(trackOffset)}</strong></div>
              <div class="offset-summary-item"><span>${window.taskbarLyricsI18n.t("UI_EffectiveOffset")}</span><strong>${formatPlayerOffset(effectiveOffset)}</strong></div>
            </div>
            <div class="current-track-editor">
              <div class="stepper track-offset-stepper">
                <button type="button" data-current-track-offset-delta="-100" aria-label="${window.taskbarLyricsI18n.t("UI_DelayCurrentTrackLyricsBy100Milliseconds")}">−</button>
                <input id="currentTrackOffsetInput" class="control" type="number" min="-5000" max="5000" step="10" inputmode="numeric" value="${trackOffset}" aria-label="${window.taskbarLyricsI18n.t("UI_CurrentTrackOffsetInMilliseconds")}">
                <button type="button" data-current-track-offset-delta="100" aria-label="${window.taskbarLyricsI18n.t("UI_AdvanceCurrentTrackLyricsBy100Milliseconds")}">+</button>
              </div>
              <button class="btn ghost small" type="button" data-reset-current-track-offset ${trackOffset === 0 ? "disabled" : ""}>${window.taskbarLyricsI18n.t("UI_ResetTo0")}</button>
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
        ? window.taskbarLyricsI18n.format("UI_Tracks", unfilteredCount)
        : window.taskbarLyricsI18n.format("UI_Tracks_8", totalCount, unfilteredCount);
      $("#clearTrackOffsetsButton").disabled = unfilteredCount === 0;
      const pagination = $("#trackOffsetPagination");
      pagination.hidden = false;
      $("#trackOffsetPageStatus").textContent = `${trackOffsetPage} / ${pageCount}`;
      $("#trackOffsetPreviousPage").disabled = trackOffsetPage <= 1;
      $("#trackOffsetNextPage").disabled = trackOffsetPage >= pageCount;
      container.removeAttribute("aria-busy");

      if (!visibleTrackOffsetEntries.length) {
        const hasRecords = unfilteredCount > 0;
        container.innerHTML = `<div class="track-offset-empty"><div><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M4 12h16M4 17h10"/></svg><strong>${hasRecords ? window.taskbarLyricsI18n.t("UI_NoMatchingTracks") : window.taskbarLyricsI18n.t("UI_NoTrackOffsetsConfiguredYet")}</strong><small>${hasRecords ? window.taskbarLyricsI18n.t("UI_TryAnotherSearchOrLyricSourceFilter") : window.taskbarLyricsI18n.t("UI_MakeYourFirstAdjustmentAboveOrFromTheTray")}</small></div></div>`;
        return;
      }

      container.innerHTML = `
        <div class="track-offset-table-head"><span>${window.taskbarLyricsI18n.t("UI_Track")}</span><span>${window.taskbarLyricsI18n.t("UI_LyricSource_9")}</span><span>${window.taskbarLyricsI18n.t("UI_TrackOffsets")}</span><span>${window.taskbarLyricsI18n.t("UI_RecentlyModified")}</span><span></span></div>
        ${visibleTrackOffsetEntries.map((entry, index) => {
          const isExpanded = expandedTrackOffsetKey === trackOffsetKeyId(entry.key);
          const offset = Number(entry.offsetMilliseconds) || 0;
          return `<div class="track-offset-item">
            <div class="track-offset-row">
              <div class="track-offset-song"><strong title="${escapeHtml(entry.title)}">${escapeHtml(entry.title || window.taskbarLyricsI18n.t("UI_UnknownSong"))}</strong><small title="${escapeHtml(entry.artist)}">${escapeHtml(entry.artist || window.taskbarLyricsI18n.t("UI_UnknownArtist"))} · ${escapeHtml(sourceDisplayName(entry.sourceApp))} · ${formatTrackDuration(entry.durationSeconds)}</small></div>
              <span class="track-offset-meta">${escapeHtml(sourceDisplayName(entry.lyricSource))}</span>
              <span class="track-offset-value">${formatPlayerOffset(offset)}</span>
              <span class="track-offset-meta">${formatTrackOffsetDate(entry.updatedAtUtc)}</span>
              <div class="track-offset-actions">
                <button class="track-offset-action" type="button" data-edit-track-offset="${index}" aria-label="${window.taskbarLyricsI18n.t("UI_Adjust")} ${escapeHtml(entry.title)} ${window.taskbarLyricsI18n.t("UI_Offset_10")}" aria-expanded="${isExpanded}"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h10M4 17h16M18 4v6M14 14v6"/><circle cx="18" cy="13" r="0"/><path d="M18 4v6M15 7h6M14 14v6M11 17h6"/></svg></button>
                <button class="track-offset-action destructive" type="button" data-delete-track-offset="${index}" aria-label="${window.taskbarLyricsI18n.t("UI_Delete")} ${escapeHtml(entry.title)} ${window.taskbarLyricsI18n.t("UI_Offset_10")}"><svg viewBox="0 0 24 24" aria-hidden="true"><path d="M4 7h16M9 7V4h6v3M7 7l1 13h8l1-13M10 11v5M14 11v5"/></svg></button>
              </div>
            </div>
            ${isExpanded ? `<div class="track-offset-inline-editor"><span>${window.taskbarLyricsI18n.t("UI_PositiveValuesAdvanceLyricsNegativeValuesDelayThem")}</span><div class="stepper track-offset-stepper"><button type="button" data-stored-track-offset-delta="-100" data-track-offset-index="${index}" aria-label="${window.taskbarLyricsI18n.t("UI_DelayLyricsBy100Milliseconds")}">−</button><input class="control" type="number" min="-5000" max="5000" step="10" inputmode="numeric" value="${offset}" data-stored-track-offset-input="${index}" aria-label="${escapeHtml(entry.title)} ${window.taskbarLyricsI18n.t("UI_TrackOffsetInMilliseconds")}"><button type="button" data-stored-track-offset-delta="100" data-track-offset-index="${index}" aria-label="${window.taskbarLyricsI18n.t("UI_AdvanceLyricsBy100Milliseconds")}">+</button></div><span class="track-offset-editor-unit">ms</span></div>` : ""}
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
        Succeeded: window.taskbarLyricsI18n.t("UI_Success"),
        IdentityRejected: window.taskbarLyricsI18n.t("UI_IdentityRejected"),
        NoLyrics: window.taskbarLyricsI18n.t("UI_NoLyrics"),
        InvalidContent: window.taskbarLyricsI18n.t("UI_InvalidContent"),
        Failed: window.taskbarLyricsI18n.t("UI_Failed"),
        TimedOut: window.taskbarLyricsI18n.t("UI_TimedOut"),
        Disabled: window.taskbarLyricsI18n.t("UI_Disabled_11"),
        Canceled: window.taskbarLyricsI18n.t("UI_Canceled")
      })[value] ?? (value ? String(value) : window.taskbarLyricsI18n.t("UI_Incomplete"));
    }

    function diagnosticProviderStateClass(value) {
      if (value === "Succeeded") return "success";
      if (["Failed", "TimedOut", "InvalidContent", "IdentityRejected"].includes(value)) return "error";
      return "unknown";
    }

    function diagnosticRawIdentifier(value) {
      return value === null || value === undefined ? "" : String(value);
    }

    function diagnosticCandidateMatchesSelection(selection, providerId, candidateId) {
      if (!selection || typeof selection !== "object") return false;
      const selectedProviderId = diagnosticRawIdentifier(selection.providerId);
      const selectedCandidateId = diagnosticRawIdentifier(selection.candidateId);
      return Boolean(selectedProviderId && selectedCandidateId && providerId && candidateId) &&
        selectedProviderId.toLowerCase() === providerId.toLowerCase() &&
        selectedCandidateId === candidateId;
    }

    function formatDiagnosticTimestamp(value) {
      const date = new Date(value);
      if (Number.isNaN(date.getTime())) return "";
      return new Intl.DateTimeFormat(window.taskbarLyricsI18n.locale, { month: "2-digit", day: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit" }).format(date);
    }

    function diagnosticCandidateKey(providerId, candidateId, mode = "current") {
      return `${providerId.toLowerCase()}\u0000${candidateId}\u0000${mode}`;
    }

    function diagnosticApplyButtonState(providerId, candidateId, mode) {
      const apply = lyricDiagnosticsState.apply;
      const matches = apply?.key === diagnosticCandidateKey(providerId, candidateId, mode);
      const running = matches && apply.status === "running";
      const busy = apply?.status === "running" &&
        ["current", "remember"].some(value => apply.key === diagnosticCandidateKey(providerId, candidateId, value));
      const label = mode === "remember"
        ? running ? window.taskbarLyricsI18n.t("UI_Remembering") : matches && apply.status === "success" ? window.taskbarLyricsI18n.t("UI_AppliedAndRemembered") : window.taskbarLyricsI18n.t("UI_UseAndRemember")
        : running ? window.taskbarLyricsI18n.t("UI_Applying") : matches && apply.status === "success" ? window.taskbarLyricsI18n.t("UI_Applied") : window.taskbarLyricsI18n.t("UI_UseThisTime");
      return { running, busy, label };
    }

    function updateLyricDiagnosticsApplyState() {
      const page = $('[data-page="lyricDiagnostics"]');
      const scrollTop = page.scrollTop;
      const apply = lyricDiagnosticsState.apply;
      $$("[data-lyric-diagnostics-apply]").forEach(button => {
        const { running, busy, label } = diagnosticApplyButtonState(button.dataset.providerId, button.dataset.candidateId, button.dataset.applyMode);
        button.disabled = busy;
        button.toggleAttribute("aria-busy", running);
        if (running) button.setAttribute("aria-busy", "true");
        const signature = `${running}:${label}`;
        if (button.dataset.applyState !== signature) {
          button.dataset.applyState = signature;
          button.innerHTML = `${running ? '<span class="spinner" aria-hidden="true"></span>' : ""}${label}`;
        }
      });
      if (apply?.status !== "running" && diagnosticApplyFocus) {
        if (diagnosticApplyFocus.isConnected && !diagnosticApplyFocus.disabled && document.activeElement === document.body)
          diagnosticApplyFocus.focus({ preventScroll: true });
        diagnosticApplyFocus = null;
      }
      page.scrollTop = scrollTop;
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
      const title = diagnosticString(track.title, window.taskbarLyricsI18n.t("UI_UnknownSong"));
      const sourceApp = diagnosticString(track.sourceApp);
      const songId = diagnosticString(track.songId);
      container.innerHTML = `
        <div class="diagnostics-track-item"><span>${window.taskbarLyricsI18n.t("UI_Track")}</span><strong title="${escapeHtml(title)}">${escapeHtml(title)}</strong></div>
        <div class="diagnostics-track-item"><span>${window.taskbarLyricsI18n.t("UI_Artist")}</span><strong title="${escapeHtml(diagnosticTrackArtist(track))}">${escapeHtml(diagnosticTrackArtist(track))}</strong></div>
        <div class="diagnostics-track-item"><span>${window.taskbarLyricsI18n.t("UI_Album")}</span><strong title="${escapeHtml(diagnosticString(track.album))}">${escapeHtml(diagnosticString(track.album))}</strong></div>
        <div class="diagnostics-track-item"><span>${window.taskbarLyricsI18n.t("UI_Players")}</span><strong title="${escapeHtml(sourceApp)}">${escapeHtml(sourceApp)}</strong></div>
        <div class="diagnostics-track-item"><span>${window.taskbarLyricsI18n.t("UI_DurationTrackID")}</span><strong title="${escapeHtml(`${formatDiagnosticDuration(track.durationSeconds ?? track.duration)} · ${songId}`)}">${escapeHtml(formatDiagnosticDuration(track.durationSeconds ?? track.duration))} · ${escapeHtml(songId)}</strong></div>`;
    }

    function renderLyricDiagnosticsProviders(report) {
      const providers = Array.isArray(report?.providers) ? report.providers : [];
      const container = $("#lyricDiagnosticsProviders");
      $("#lyricDiagnosticsProviderCount").textContent = window.taskbarLyricsI18n.format("UI_LyricSources", providers.length);
      const summary = $("#lyricDiagnosticsReportSummary");
      const preferredProvider = diagnosticString(report?.preferredProvider, "");
      const musicKind = report?.isPureMusic === true ? window.taskbarLyricsI18n.t("UI_Instrumental") : report?.isPureMusic === false ? window.taskbarLyricsI18n.t("UI_RegularTrack") : window.taskbarLyricsI18n.t("UI_TrackTypeUnknown");
      summary.innerHTML = `<span>${musicKind}</span><span>${window.taskbarLyricsI18n.t("UI_PreferredSource")}${escapeHtml(preferredProvider || window.taskbarLyricsI18n.t("UI_NotSpecified"))}</span>`;
      if (!providers.length) {
        container.innerHTML = `<div class="diagnostics-selection-empty"><strong>${window.taskbarLyricsI18n.t("UI_NoLyricSourceResultsReceived")}</strong><small>${window.taskbarLyricsI18n.t("UI_NoTerminalSourceStatusIsAvailableForThisRun")}</small></div>`;
        return;
      }

      container.innerHTML = providers.map(provider => {
        const providerIdentifier = diagnosticRawIdentifier(provider?.providerId);
        const providerId = diagnosticString(providerIdentifier, window.taskbarLyricsI18n.t("UI_UnknownSource"));
        const providerState = provider?.state ?? null;
        const providerStateClass = diagnosticProviderStateClass(providerState);
        const candidates = Array.isArray(provider?.candidates) ? provider.candidates : [];
        const detail = diagnosticString(provider?.detail, "");
        const selectedBadge = provider?.selected ? `<span class="diagnostics-badge" data-state="selected">${window.taskbarLyricsI18n.t("UI_FinalSelection")}</span>` : "";
        const candidateMarkup = candidates.length
          ? candidates.map(candidate => {
            const admitted = candidate?.isAdmitted === true;
            const score = Number.isFinite(Number(candidate?.score)) ? Number(candidate.score) : null;
            const reasons = Array.isArray(candidate?.rejectionReasons) ? candidate.rejectionReasons.filter(Boolean) : [];
            const metadataKeys = Array.isArray(candidate?.fetchMetadataKeys) ? candidate.fetchMetadataKeys.filter(Boolean) : [];
            const title = diagnosticString(candidate?.title, window.taskbarLyricsI18n.t("UI_UnknownCandidate"));
            const artist = diagnosticArtists(candidate?.artists, window.taskbarLyricsI18n.t("UI_UnknownArtist"));
            const candidateIdentifier = diagnosticRawIdentifier(candidate?.candidateId);
            const candidateId = diagnosticString(candidateIdentifier);
            const candidateIsSelected = diagnosticCandidateMatchesSelection(report?.selection, providerIdentifier, candidateIdentifier);
            const current = diagnosticApplyButtonState(providerIdentifier, candidateIdentifier, "current");
            const remember = diagnosticApplyButtonState(providerIdentifier, candidateIdentifier, "remember");
            const applyActions = providerIdentifier && candidateIdentifier
              ? `<div class="diagnostics-candidate-actions"><button class="btn secondary small diagnostics-apply-button" type="button" data-lyric-diagnostics-apply data-apply-mode="current" data-provider-id="${escapeHtml(providerIdentifier)}" data-candidate-id="${escapeHtml(candidateIdentifier)}"${current.busy ? " disabled" : ""}${current.running ? " aria-busy=\"true\"" : ""}>${current.running ? '<span class="spinner" aria-hidden="true"></span>' : ""}${current.label}</button><button class="btn ghost small diagnostics-apply-button" type="button" data-lyric-diagnostics-apply data-apply-mode="remember" data-provider-id="${escapeHtml(providerIdentifier)}" data-candidate-id="${escapeHtml(candidateIdentifier)}"${remember.busy ? " disabled" : ""}${remember.running ? " aria-busy=\"true\"" : ""}>${remember.running ? '<span class="spinner" aria-hidden="true"></span>' : ""}${remember.label}</button></div>`
              : "";
            const candidateState = candidateIsSelected ? "selected" : admitted ? "accepted" : "rejected";
            const candidateStateLabel = candidateIsSelected ? window.taskbarLyricsI18n.t("UI_CurrentSelection") : admitted ? window.taskbarLyricsI18n.t("UI_Accepted") : window.taskbarLyricsI18n.t("UI_Rejected");
            return `<article class="diagnostics-candidate${candidateIsSelected ? " is-selected" : ""}">
              <div class="diagnostics-candidate-main">
                <div class="diagnostics-candidate-title"><strong title="${escapeHtml(title)}">${escapeHtml(title)}</strong><small title="${escapeHtml(artist)}">${escapeHtml(artist)}</small></div>
                <div class="diagnostics-candidate-meta"><span>${window.taskbarLyricsI18n.t("UI_Album_12")}${escapeHtml(diagnosticString(candidate?.album))}</span><span>${window.taskbarLyricsI18n.t("UI_Duration")}${escapeHtml(formatDiagnosticDuration(candidate?.durationSeconds ?? candidate?.duration))}</span><span>${window.taskbarLyricsI18n.t("UI_QueryVariant")}${escapeHtml(diagnosticString(candidate?.queryVariantId))}</span><span>${window.taskbarLyricsI18n.t("UI_CandidateID")}<code title="${escapeHtml(candidateId)}">${escapeHtml(candidateId)}</code></span>${metadataKeys.length ? `<span>${window.taskbarLyricsI18n.t("UI_Metadata")}${escapeHtml(metadataKeys.join("、"))}</span>` : ""}</div>
                ${reasons.length ? `<ul class="diagnostics-reasons" aria-label="${window.taskbarLyricsI18n.t("UI_RejectionReason")}">${reasons.map(reason => `<li>${escapeHtml(reason)}</li>`).join("")}</ul>` : ""}
              </div>
              <div class="diagnostics-candidate-controls"><div class="diagnostics-candidate-summary"><div class="diagnostics-candidate-badges"><span class="diagnostics-badge" data-state="${candidateState}">${candidateStateLabel}</span>${admitted && candidate?.isHighConfidence ? `<span class="diagnostics-badge" data-state="high-confidence">${window.taskbarLyricsI18n.t("UI_HighConfidence")}</span>` : ""}</div><strong class="diagnostics-score">${score === null ? "--" : score} ${window.taskbarLyricsI18n.t("UI_Points")}</strong></div>${applyActions}</div>
            </article>`;
          }).join("")
          : `<div class="diagnostics-candidate-empty">${window.taskbarLyricsI18n.t("UI_ThisSourceReturnedNoCandidates")}</div>`;
        return `<details class="diagnostics-provider" open>
          <summary class="diagnostics-provider-head"><span class="diagnostics-provider-heading"><span class="diagnostics-provider-title"><strong>${escapeHtml(providerId)}</strong><span class="diagnostics-badge" data-state="${providerStateClass}">${escapeHtml(diagnosticProviderStateLabel(providerState))}</span>${selectedBadge}</span>${detail ? `<span class="diagnostics-provider-detail" title="${escapeHtml(detail)}">${escapeHtml(detail)}</span>` : ""}</span><span class="diagnostics-provider-toggle-meta"><span>${candidates.length} ${window.taskbarLyricsI18n.t("UI_Candidates")}</span><svg viewBox="0 0 16 16" aria-hidden="true"><path d="m6 4 4 4-4 4"></path></svg></span></summary>
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
        const title = diagnosticString(variant?.title, window.taskbarLyricsI18n.t("UI_UnknownQuery"));
        const artist = diagnosticArtists(variant?.artists, window.taskbarLyricsI18n.t("UI_UnknownArtist"));
        const reasons = Array.isArray(variant?.relaxationReasons) ? variant.relaxationReasons.filter(Boolean) : [];
        return `<div class="diagnostics-variant"><span class="diagnostics-variant-index">${index + 1}</span><div class="diagnostics-variant-copy"><strong title="${escapeHtml(title)}">${escapeHtml(title)}</strong><small>${escapeHtml(artist)} · ${escapeHtml(diagnosticString(variant?.album))} · ${escapeHtml(formatDiagnosticDuration(variant?.durationSeconds ?? variant?.duration))}</small></div><span class="diagnostics-variant-reasons">${escapeHtml(reasons.length ? reasons.join("、") : window.taskbarLyricsI18n.t("UI_StrictMatch"))}</span></div>`;
      }).join("");
    }

    function renderLyricDiagnosticsSelection(report) {
      const selection = report?.selection;
      const container = $("#lyricDiagnosticsSelection");
      if (!selection || typeof selection !== "object") {
        const message = diagnosticString(report?.error, window.taskbarLyricsI18n.t("UI_NoCandidatePassedAllChecks"));
        container.innerHTML = `<div class="diagnostics-selection-empty"><strong>${window.taskbarLyricsI18n.t("UI_NoUsableLyricsFound")}</strong><small>${escapeHtml(message)}</small></div>`;
        return;
      }

      const diagnostics = selection.diagnostics && typeof selection.diagnostics === "object"
        ? Object.entries(selection.diagnostics).filter(([key, value]) => key && value !== null && value !== undefined)
        : [];
      const diagnosticsMarkup = diagnostics.length
        ? `<div class="diagnostics-selection-meta">${diagnostics.map(([key, value]) => `<span>${escapeHtml(key)}：<code>${escapeHtml(value)}</code></span>`).join("")}</div>`
        : "";
      container.innerHTML = `<div class="diagnostics-selection-card is-selected"><div class="diagnostics-selection-title"><strong>${escapeHtml(diagnosticString(selection.providerId, window.taskbarLyricsI18n.t("UI_UnknownSource")))}</strong><span class="diagnostics-badge" data-state="selected">${window.taskbarLyricsI18n.t("UI_Selected")}</span></div><div class="diagnostics-selection-meta"><span>${window.taskbarLyricsI18n.t("UI_CandidateID")}<code>${escapeHtml(diagnosticString(selection.candidateId))}</code></span><span>${window.taskbarLyricsI18n.t("UI_Acquisition")}${escapeHtml(diagnosticString(selection.acquisition))}</span><span>${window.taskbarLyricsI18n.t("UI_Format")}${escapeHtml(diagnosticString(selection.format))}</span><span>${window.taskbarLyricsI18n.t("UI_Timing")}${escapeHtml(diagnosticString(selection.timingKind))} / ${escapeHtml(diagnosticString(selection.timingProvenance))}</span><span>${window.taskbarLyricsI18n.t("UI_LyricLines_13")}${escapeHtml(diagnosticString(selection.lineCount, "0"))}</span></div>${diagnosticsMarkup}</div>`;
    }

    function renderLyricDiagnosticsState(applyOnly = false) {
      const current = lyricDiagnosticsState;
      const status = $("#lyricDiagnosticsStatus");
      const button = $("#runLyricDiagnosticsButton");
      const reportPanel = $("#lyricDiagnosticsReportPanel");
      const selectionPanel = $("#lyricDiagnosticsSelectionPanel");
      const report = current.report;
      const track = current.track ?? report?.effectiveTrack ?? report?.originalTrack;
      const title = diagnosticString(track?.title, window.taskbarLyricsI18n.t("UI_CurrentTrack"));
      const apply = current.apply;
      let message = window.taskbarLyricsI18n.t("UI_NoSearchYetClickFindLyricsToSeeMatches");
      if (current.status === "running") message = window.taskbarLyricsI18n.format("UI_SearchingForLyricsFor", title);
      else if (current.status === "success") message = report?.selection ? window.taskbarLyricsI18n.t("UI_MatchingCompleteUsableLyricsFound") : window.taskbarLyricsI18n.t("UI_MatchingCompleteNoCandidatePassedAllChecks");
      else if (current.status === "empty") message = diagnosticString(current.message, window.taskbarLyricsI18n.t("UI_NoSMTCTrackAvailableForMatching"));
      else if (current.status === "error") message = diagnosticString(current.message, window.taskbarLyricsI18n.t("UI_LyricMatchingFailedPleaseRetryLater"));
      if (apply?.status === "running") message = diagnosticString(apply.message, window.taskbarLyricsI18n.t("UI_ApplyingCurrentLyrics"));
      else if (apply?.status === "success") message = diagnosticString(apply.message, window.taskbarLyricsI18n.t("UI_CurrentLyricsApplied"));
      else if (apply?.status === "error") message = diagnosticString(apply.message, window.taskbarLyricsI18n.t("UI_FailedToApplyCurrentLyrics"));
      status.dataset.state = apply?.status === "running" ? "running" : apply?.status === "error" ? "error" : apply?.status === "success" ? "success" : current.status;
      status.textContent = message;
      const applyBusy = current.apply?.status === "running";
      button.disabled = current.status === "running" || applyBusy;
      button.setAttribute("aria-busy", String(current.status === "running" || applyBusy));
      button.innerHTML = current.status === "running" ? `<span class="spinner" aria-hidden="true"></span>${window.taskbarLyricsI18n.t("UI_Searching")}` : current.status === "idle" ? window.taskbarLyricsI18n.t("UI_FindLyrics") : window.taskbarLyricsI18n.t("UI_SearchAgain");

      if (applyOnly) { updateLyricDiagnosticsApplyState(); return; }
      diagnosticApplyFocus = null;

      renderLyricDiagnosticsTrack(track);
      reportPanel.hidden = !report || current.status !== "success";
      selectionPanel.hidden = !report || current.status !== "success";
      if (report && current.status === "success") {
        const capturedAt = formatDiagnosticTimestamp(report.capturedAtUtc);
        $("#lyricDiagnosticsCapturedAt").textContent = capturedAt ? window.taskbarLyricsI18n.format("UI_CapturedAt", capturedAt) : "";
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
      const previousReport = lyricDiagnosticsState.report;
      const status = ["running", "success", "empty", "error"].includes(payload?.status) ? payload.status : "error";
      const hasApply = payload.apply && typeof payload.apply === "object";
      const report = status === "success" && payload.report && typeof payload.report === "object"
        ? payload.report
        : hasApply
          ? lyricDiagnosticsState.report
          : null;
      const track = payload.track && typeof payload.track === "object"
        ? payload.track
        : hasApply
          ? lyricDiagnosticsState.track
          : null;
      const apply = hasApply
        ? {
          status: ["running", "success", "error"].includes(payload.apply.status) ? payload.apply.status : "error",
          key: diagnosticCandidateKey(
            diagnosticRawIdentifier(payload.apply.providerId),
            diagnosticRawIdentifier(payload.apply.candidateId),
            payload.apply.mode === "remember" ? "remember" : "current"),
          message: diagnosticString(payload.apply.message)
        }
        : null;
      lyricDiagnosticsState = {
        status: status === "success" && !report ? "error" : status,
        track,
        report,
        message: diagnosticString(payload.message, status === "success" && !report ? window.taskbarLyricsI18n.t("UI_InvalidMatchingResult") : ""),
        apply
      };
      renderLyricDiagnosticsState(hasApply && report === previousReport && lyricDiagnosticsState.status === "success");
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
        .sort((a, b) => sourceDisplayName(a).localeCompare(sourceDisplayName(b), window.taskbarLyricsI18n.locale));
      selectOptions.trackOffsetSourceFilter = [
        { value: "All", label: window.taskbarLyricsI18n.t("UI_AllLyricSources") },
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
      const adding = playerDrawerMode === "add";
      const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
      $("#playerCompatibilitySection").hidden = adding || !source;
      if (adding || !source) $("#playerCompatibilitySection").replaceChildren();
      $("#playerDiscoverySection").hidden = !adding;
      $("#playerRecognitionSection").hidden = false;
      $("#playerCardInfoSection").hidden = !adding && !sourceCatalog.find(item => item.id === activePlayerSourceId)?.custom;
      $("#playerAdvancedSettings").hidden = adding;
      $("#playerRemoveActions").hidden = adding || !sourceCatalog.find(item => item.id === activePlayerSourceId)?.custom;
      $("#confirmAddPlayerSource").hidden = !adding;
      $("#savePlayerInfoButton").hidden = adding;
      if (adding) {
        $("#playerSettingsTitle").textContent = window.taskbarLyricsI18n.t("UI_AddPlayer");
        $("#playerSettingsAdapter").textContent = window.taskbarLyricsI18n.t("UI_ChooseAPlayerFromMediaSessions");
        $("#playerSettingsLogo").innerHTML = defaultPlayerIcon();
        $("#playerSettingsLogo").classList.remove("custom-player-icon");
        $("#playerRecognitionToggle").checked = newPlayerEnabled;
        renderLyricProviders();
        return;
      }
      if (!source) return;
      const compatibilityProfile = source.custom
        ? window.taskbarLyricsPlayerCompatibility.resolveSourceProfile(source.adapter) : source.id;
      renderPlayerCompatibility($("#playerCompatibilitySection"), compatibilityProfile);
      $("#playerSettingsTitle").textContent = source.name;
      $("#playerSettingsAdapter").textContent = source.adapter;
      $("#playerSettingsLogo").innerHTML = playerIcon(source);
      $("#playerSettingsLogo").classList.toggle("custom-player-icon", Boolean(source.custom));
      $("#playerRecognitionToggle").checked = source.enabled;
      $("#removePlayerSourceButton").hidden = !source.custom;
      const offset = getPlayerOffset(source);
      $("#playerOffsetInput").value = offset;
      $("#playerOffsetStatus").textContent = formatPlayerOffset(offset);
      $("#resetPlayerOffsetButton").disabled = offset === source.defaultOffset;
      renderLyricProviders(source.adapter);
    }

    function normalizeLyricProviders(items, sourceApp) {
      if (!Array.isArray(items) || items.length !== lyricProviderDefaults.length ||
          new Set(items.map(item => item?.providerId)).size !== lyricProviderDefaults.length ||
          items.some(item => !lyricProviderDefaults.some(known => known.providerId === item?.providerId))) {
        const officialProvider = ["QQMusic", "Netease", "Kugou"].includes(sourceApp) ? sourceApp : undefined;
        return [...lyricProviderDefaults]
          .sort((a, b) => Number(b.providerId === officialProvider) - Number(a.providerId === officialProvider))
          .map(item => ({ providerId: item.providerId, enabled: true }));
      }
      return items.map(item => ({ providerId: item.providerId, enabled: item.enabled === true }));
    }

    function currentLyricProviders() {
      if (playerDrawerMode === "add") return newPlayerLyricProviders;
      const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
      return state?.playerLyricProviders?.[source?.adapter] ?? normalizeLyricProviders(undefined, source?.adapter);
    }

    function renderLyricProviders() {
      const providers = currentLyricProviders();
      const list = $("#lyricProviderList");
      const focusedControl = list.contains(document.activeElement) ? document.activeElement : null;
      const focusedProviderId = focusedControl?.dataset.lyricProviderToggle ?? focusedControl?.dataset.lyricProviderDrag;
      const focusedAttribute = focusedControl?.dataset.lyricProviderToggle ? "data-lyric-provider-toggle" : "data-lyric-provider-drag";
      list.innerHTML = providers.map((item, index) => {
        const name = lyricProviderDefaults.find(known => known.providerId === item.providerId).name;
        return `<div class="priority-item lyric-provider-item ${item.enabled ? "" : "disabled"}" data-lyric-provider-item="${item.providerId}">
          <button class="drag-handle" type="button" draggable="true" data-lyric-provider-drag="${item.providerId}" aria-label="${window.taskbarLyricsI18n.t("UI_Drag")} ${name} ${window.taskbarLyricsI18n.t("UI_AdjustTrustOrder")}" aria-keyshortcuts="Alt+ArrowUp Alt+ArrowDown"><svg viewBox="0 0 16 16" aria-hidden="true"><circle cx="5" cy="4" r="1.2"/><circle cx="11" cy="4" r="1.2"/><circle cx="5" cy="8" r="1.2"/><circle cx="11" cy="8" r="1.2"/><circle cx="5" cy="12" r="1.2"/><circle cx="11" cy="12" r="1.2"/></svg></button>
          <span class="priority-number">${index + 1}</span><span class="priority-name">${name}</span>
          <label class="switch"><input type="checkbox" data-lyric-provider-toggle="${item.providerId}" aria-label="${window.taskbarLyricsI18n.t("UI_Enable")} ${name} ${window.taskbarLyricsI18n.t("UI_OnlineLyricSource")}" ${item.enabled ? "checked" : ""}><span class="switch-track"></span></label>
        </div>`;
      }).join("");
      $("#lyricProviderEmptyHint").hidden = providers.some(item => item.enabled);
      if (focusedProviderId) {
        list.querySelector(`[${focusedAttribute}="${focusedProviderId}"]`)?.focus({ preventScroll: true });
      }
    }

    function announceLyricProviderPosition(providerId) {
      const providers = currentLyricProviders();
      const index = providers.findIndex(item => item.providerId === providerId);
      if (index < 0) return;
      const name = lyricProviderDefaults.find(item => item.providerId === providerId).name;
      $("#lyricProviderOrderAnnouncement").textContent = window.taskbarLyricsI18n.format("UI_MovedToPositionOf", name, index + 1, providers.length);
    }

    function commitLyricProviders() {
      if (playerDrawerMode === "add") {
        renderLyricProviders();
        return;
      }
      const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
      if (!source || !state) return;
      bridge.post({ type: "update", key: `playerLyricProviders:${source.adapter}`, value: state.playerLyricProviders[source.adapter] });
      renderLyricProviders(source.adapter);
      markSaved();
    }

    function openPlayerSettings(sourceId) {
      const source = sourceCatalog.find(item => item.id === sourceId);
      if (!source) return;
      closeSelect(false);
      closeColorPopover(false);
      pendingPlayerSourceAction = null;
      playerInfoBusy = false;
      playerDrawerMode = "edit";
      activePlayerSourceId = source.id;
      if (source.custom) loadPlayerInfoForm(source);
      renderPlayerSettings();
      $("#playerSettingsDialog").showModal();
    }

    function openAddPlayerDrawer() {
      closeSelect(false);
      closeColorPopover(false);
      pendingPlayerSourceAction = null;
      playerDrawerMode = "add";
      activePlayerSourceId = null;
      selectedPlayerSessionId = null;
      selectedSessionAutoIconDataUrl = "";
      playerInfoBusy = false;
      newPlayerEnabled = true;
      newPlayerLyricProviders = normalizeLyricProviders();
      $("#customPlayerName").value = "";
      resetSelectedPlayerIcon();
      renderPlayerSettings();
      $("#playerSettingsDialog").showModal();
      requestPlayerSessions(false);
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
      window.taskbarLyricsDisplayPresentation?.sync(state);
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
      $("#saveState").textContent = window.taskbarLyricsI18n.t("UI_Applying");
    }

    function setSettingsSaveResult(payload = {}) {
      const success = payload.success === true;
      $("#saveState").dataset.state = success ? "success" : "error";
      $("#saveState").textContent = success
        ? window.taskbarLyricsI18n.t("UI_ChangesAppliedImmediately")
        : window.taskbarLyricsI18n.t("UI_SettingsAppliedButCouldNotBeSavedTheyMay");
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
          ? window.taskbarLyricsI18n.t("UI_AllowedSystemAudioIsCapturedOnlyWhileTheSpectrum")
          : window.taskbarLyricsI18n.t("UI_AllowedSystemAudioIsNotCapturedWhileTheSpectrum")
        : window.taskbarLyricsI18n.t("UI_SystemAudioCaptureHasNotBeenAllowed");
      status.textContent = spectrumCaptureState.message || fallbackMessage;
      status.dataset.state = spectrumCaptureState.state || (granted ? "waiting" : "notGranted");
      $("#retrySpectrumAudioAccessButton").hidden = !captureBlocked;
      $("#revokeSpectrumAudioAccessButton").hidden = !granted;
      const tuningAvailable = granted && modeEnabled;
      $("#spectrumTuningButton").disabled = !tuningAvailable;
      $("#spectrumTuningDescription").textContent = tuningAvailable
        ? window.taskbarLyricsI18n.t("UI_OpenSlidersToAdjustSpectrumParametersLiveChangesSave")
        : granted
          ? window.taskbarLyricsI18n.t("UI_ChooseASpectrumModeOtherThanOffOnThe")
          : window.taskbarLyricsI18n.t("UI_EnableTheSpectrumAndAllowSystemAudioCaptureOn");
    }

    function fallbackLabels() {
      return {
        searching: window.taskbarLyricsI18n.t("UI_Searching_18"),
        instrumental: window.taskbarLyricsI18n.t("UI_Instrumental"),
        notFound: window.taskbarLyricsI18n.t("UI_NoLyrics")
      };
    }

    function fallbackMessage(key) {
      return window.taskbarLyricsI18n.t(key === "searching" ? "UI_SearchingForLyrics" : key === "instrumental" ? "UI_Instrumental" : "Lyrics_NotFound");
    }

    function getFallbackRules() {
      if (state?.lyricsFallback) return state.lyricsFallback;
      const line = (kind, artist = false) => ({ kind, showTitle: true, showArtist: artist, customText: null });
      const mode = state?.spectrumDisplayMode ?? "Disabled";
      return {
        version: 1,
        searching: { useSpectrum: false, firstLine: line("Metadata", true), secondLine: line(state?.showLyricSearchStatus === false ? "None" : "Text") },
        instrumental: { useSpectrum: mode !== "Disabled", firstLine: line("Text"), secondLine: line("None") },
        notFound: { useSpectrum: ["Always", "PureMusicOrNoLyrics"].includes(mode), firstLine: line("Text"), secondLine: line("None") },
        spectrumWithLyrics: mode === "Always"
      };
    }

    function fallbackSpectrumMode(rules) {
      const pure = rules.instrumental.useSpectrum, missing = rules.notFound.useSpectrum, lyrics = rules.spectrumWithLyrics;
      return lyrics && pure && missing ? "Always" : !lyrics && pure && missing ? "PureMusicOrNoLyrics"
        : !lyrics && pure && !missing ? "PureMusicOnly" : !lyrics && !pure && !missing ? "Disabled" : "Custom";
    }

    function commitFallbackChange(mutate) {
      if (!state) return;
      const rules = JSON.parse(JSON.stringify(getFallbackRules()));
      mutate(rules);
      rules.searching.useSpectrum = false;
      const mode = fallbackSpectrumMode(rules);
      if (mode !== "Disabled" && !state.spectrumAudioAccessGranted) {
        pendingFallbackSettings = rules;
        pendingSpectrumDisplayMode = mode;
        renderFallbackRules();
        $("#spectrumAudioConsentDialog").showModal();
        return;
      }
      commitSetting("lyricsFallback", rules);
    }

    function fallbackLineText(line, key) {
      if (line.kind === "None") return "";
      if (line.kind === "Text") return line.customText ?? fallbackMessage(key);
      return [line.showTitle ? window.taskbarLyricsI18n.t("UI_FallbackExampleTitle") : "",
        line.showArtist ? window.taskbarLyricsI18n.t("UI_FallbackExampleArtist") : ""].filter(Boolean).join(" - ");
    }

    function initializeFallbackRules() {
      const root = $("#lyricsFallbackRules");
      const text = key => window.taskbarLyricsI18n.t(key);
      for (const [key, name] of Object.entries(fallbackLabels())) {
        const item = document.createElement("section");
        item.className = "fallback-rule";
        item.dataset.fallbackState = key;
        const row = document.createElement("div");
        row.className = "fallback-summary";
        const title = document.createElement("strong");
        title.textContent = name;
        const preview = document.createElement("div");
        preview.className = "fallback-preview";
        const cover = document.createElement("span");
        cover.className = "fallback-cover";
        cover.textContent = "TL";
        cover.setAttribute("aria-hidden", "true");
        const lines = document.createElement("div");
        lines.className = "fallback-lines";
        preview.append(cover, lines);
        const change = document.createElement("button");
        change.type = "button";
        change.className = "btn secondary small";
        change.dataset.fallbackChange = key;
        change.setAttribute("aria-controls", `fallback-editor-${key}`);
        change.setAttribute("aria-label", `${text("UI_FallbackChange")} ${name}`);
        row.append(title, preview);
        const editor = document.createElement("div");
        editor.className = "fallback-editor";
        editor.id = `fallback-editor-${key}`;
        editor.hidden = true;
        if (key !== "searching") {
          const modes = document.createElement("div");
          modes.className = "fallback-modes";
          modes.setAttribute("role", "group");
          modes.setAttribute("aria-label", `${name} ${text("UI_DisplayMode")}`);
          for (const [mode, label] of [["spectrum", "UI_Spectrum"], ["text", "UI_FallbackText"]]) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "btn secondary small";
            button.dataset.fallbackMode = mode;
            button.textContent = text(label);
            modes.append(button);
          }
          row.append(modes);
        }
        row.append(change);
        const fields = document.createElement("div");
        fields.className = "fallback-fields";
        for (const [lineKey, lineLabel] of [["firstLine", "UI_FallbackFirstLine"], ["secondLine", "UI_FallbackSecondLine"]]) {
          const field = document.createElement("fieldset");
          field.className = "fallback-field";
          field.dataset.fallbackLine = lineKey;
          const legend = document.createElement("legend");
          legend.textContent = text(lineLabel);
          field.append(legend);
          const radio = (kind, label) => {
            const input = document.createElement("input");
            input.type = "radio";
            input.name = `fallback-${key}-${lineKey}`;
            input.value = kind;
            input.dataset.fallbackKind = kind;
            input.setAttribute("aria-label", window.taskbarLyricsI18n.format("UI_FallbackControlLabel", name, text(lineLabel), text(label)));
            return input;
          };
          const metadata = document.createElement("div");
          metadata.className = "fallback-choice";
          const parts = document.createElement("div");
          parts.className = "fallback-metadata";
          parts.setAttribute("role", "group");
          parts.setAttribute("aria-label", `${text(lineLabel)} ${text("UI_FallbackMetadata")}`);
          for (const [part, label] of [["showTitle", "UI_FallbackTitle"], ["showArtist", "UI_Artist"]]) {
            const button = document.createElement("button");
            button.type = "button";
            button.className = "btn secondary small";
            button.dataset.fallbackPart = part;
            button.textContent = text(label);
            parts.append(button);
          }
          metadata.append(radio("Metadata", "UI_FallbackMetadata"), parts);
          const custom = document.createElement("div");
          custom.className = "fallback-choice";
          const input = document.createElement("input");
          input.type = "text";
          input.className = "fallback-text";
          input.dataset.fallbackText = "";
          input.maxLength = 120;
          input.placeholder = text("UI_FallbackPlaceholder");
          input.setAttribute("aria-label", window.taskbarLyricsI18n.format("UI_FallbackControlLabel", name, text(lineLabel), text("UI_FallbackEditable")));
          custom.append(radio("Text", "UI_FallbackEditable"), input);
          const none = document.createElement("label");
          none.className = "fallback-choice";
          const caption = document.createElement("span");
          caption.textContent = text("UI_FallbackNone");
          none.append(radio("None", "UI_FallbackNone"), caption);
          field.append(metadata, custom, none);
          fields.append(field);
        }
        const note = document.createElement("p");
        note.className = "fallback-note";
        note.textContent = text("UI_FallbackSpectrumNote");
        editor.append(fields, note);
        item.append(row, editor);
        root.append(item);
      }
      const context = target => ({
        key: target.closest("[data-fallback-state]").dataset.fallbackState,
        line: target.closest("[data-fallback-line]")?.dataset.fallbackLine
      });
      root.addEventListener("click", event => {
        const toggle = event.target.closest("[data-fallback-change]");
        if (toggle) {
          openFallbackState = openFallbackState === toggle.dataset.fallbackChange ? null : toggle.dataset.fallbackChange;
          renderFallbackRules();
          return;
        }
        const mode = event.target.closest("[data-fallback-mode]");
        const part = event.target.closest("[data-fallback-part]");
        if (!mode && !part) return;
        const { key, line } = context(mode || part);
        if (part) delete part.closest("[data-fallback-line]").querySelector("[data-fallback-text]").dataset.fallbackDraft;
        commitFallbackChange(rules => {
          if (mode) rules[key].useSpectrum = mode.dataset.fallbackMode === "spectrum";
          else {
            const value = rules[key][line], name = part.dataset.fallbackPart;
            value.kind = "Metadata";
            value[name] = !value[name];
            if (!value.showTitle && !value.showArtist) value[name] = true;
          }
        });
      });
      root.addEventListener("change", event => {
        const radio = event.target.closest("[data-fallback-kind]");
        if (!radio) return;
        const { key, line } = context(radio);
        delete radio.closest("[data-fallback-line]").querySelector("[data-fallback-text]").dataset.fallbackDraft;
        commitFallbackChange(rules => { rules[key][line].kind = radio.value; });
        if (radio.value === "Text") radio.closest("[data-fallback-line]").querySelector("[data-fallback-text]").focus();
      });
      const updateText = event => {
        const input = event.target.closest("[data-fallback-text]");
        if (!input || !state) return;
        if (event.type === "input") input.dataset.fallbackDraft = input.value;
        const { key, line } = context(input);
        if (event.type === "focusin" && getFallbackRules()[key][line].kind === "Text") return;
        commitFallbackChange(rules => {
          rules[key][line].kind = "Text";
          if (event.type === "input") rules[key][line].customText = input.value;
        });
      };
      root.addEventListener("focusin", updateText);
      root.addEventListener("input", updateText);
      root.addEventListener("focusout", event => {
        if (event.target.matches("[data-fallback-text]")) delete event.target.dataset.fallbackDraft;
      });
      root.addEventListener("keydown", event => {
        if (event.key !== "Escape" || !openFallbackState) return;
        const key = openFallbackState;
        openFallbackState = null;
        renderFallbackRules();
        root.querySelector(`[data-fallback-change="${key}"]`).focus();
      });
      $("#fallbackSpectrumWithLyrics").addEventListener("change", event => {
        commitFallbackChange(rules => { rules.spectrumWithLyrics = event.target.checked; });
      });
    }

    function renderFallbackRules() {
      if (!state) return;
      const rules = getFallbackRules();
      for (const item of $$("#lyricsFallbackRules [data-fallback-state]")) {
        const key = item.dataset.fallbackState, rule = rules[key];
        const opened = openFallbackState === key;
        const button = item.querySelector("[data-fallback-change]");
        button.textContent = window.taskbarLyricsI18n.t(opened ? "UI_FallbackCollapse" : "UI_FallbackChange");
        button.setAttribute("aria-expanded", String(opened));
        item.querySelector(".fallback-editor").hidden = !opened;
        item.querySelector(".fallback-fields").hidden = rule.useSpectrum;
        item.querySelector(".fallback-note").hidden = !rule.useSpectrum && !state.singleLineLyrics;
        item.querySelector(".fallback-note").textContent = window.taskbarLyricsI18n.t(rule.useSpectrum ? "UI_FallbackSpectrumNote" : "UI_FallbackSingleLineNote");
        for (const mode of item.querySelectorAll("[data-fallback-mode]")) mode.setAttribute("aria-pressed", String((mode.dataset.fallbackMode === "spectrum") === rule.useSpectrum));
        for (const field of item.querySelectorAll("[data-fallback-line]")) {
          const line = rule[field.dataset.fallbackLine];
          const input = field.querySelector("[data-fallback-text]");
          if (document.activeElement === input && input.dataset.fallbackDraft !== undefined) {
            line.kind = "Text";
            line.customText = input.dataset.fallbackDraft;
          }
          for (const radio of field.querySelectorAll("[data-fallback-kind]")) radio.checked = radio.value === line.kind;
          for (const part of field.querySelectorAll("[data-fallback-part]")) {
            part.setAttribute("aria-pressed", String(line.kind === "Metadata" && line[part.dataset.fallbackPart]));
            part.disabled = line.kind === "Metadata" && line[part.dataset.fallbackPart] && Number(line.showTitle) + Number(line.showArtist) === 1;
          }
          const text = line.customText ?? fallbackMessage(key);
          if (document.activeElement !== input && input.value !== text) input.value = text;
        }
        const preview = item.querySelector(".fallback-lines");
        preview.replaceChildren();
        preview.classList.toggle("spectrum", rule.useSpectrum);
        preview.classList.toggle("single", !rule.useSpectrum && state.singleLineLyrics === true);
        if (rule.useSpectrum) {
          const bars = document.createElement("div");
          bars.className = "fallback-bars";
          bars.setAttribute("aria-hidden", "true");
          for (const height of [18, 32, 58, 79, 94, 62, 40, 73, 85, 54, 36, 65, 78, 44, 26]) {
            const bar = document.createElement("i");
            bar.style.height = `${height}%`;
            bars.append(bar);
          }
          preview.append(bars);
        } else {
          for (const lineKey of ["firstLine", "secondLine"]) {
            const span = document.createElement("span");
            const input = item.querySelector(`[data-fallback-line="${lineKey}"] [data-fallback-text]`);
            span.textContent = document.activeElement === input && rule[lineKey].kind === "Text" ? input.value : fallbackLineText(rule[lineKey], key);
            preview.append(span);
          }
        }
      }
      $("#fallbackSpectrumWithLyrics").checked = rules.spectrumWithLyrics;
    }

    function requestSpectrumDisplayMode(mode) {
      pendingFallbackSettings = null;
      if (!state || !selectOptions.spectrumDisplayMode.some(option => option.value === mode)) return;
      if (mode === "Disabled") {
        spectrumCaptureState = { state: "disabled", message: window.taskbarLyricsI18n.t("UI_SpectrumIsOffSystemAudioIsNotBeingCaptured") };
        commitSetting("spectrumDisplayMode", mode);
        return;
      }
      if (state.spectrumAudioAccessGranted) {
        spectrumCaptureState = { state: "waiting", message: window.taskbarLyricsI18n.t("UI_AllowedSystemAudioIsCapturedOnlyWhileTheSpectrum") };
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
        $("#spectrumCaptureFailureMessage").textContent = spectrumCaptureState.message || window.taskbarLyricsI18n.t("UI_AudioCaptureIsBlockedByWindowsOrSecuritySoftware");
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
      else if (control.type === "radio") control.checked = control.value === String(value);
      else if (control.type === "checkbox") control.checked = Boolean(value);
      else if (control.tagName === "TEXTAREA" && Array.isArray(value)) control.value = value.join("\n");
      else if (Number.isFinite(Number(value)) && control.dataset.valueScale) {
        control.value = Math.round(Number(value) * Number(control.dataset.valueScale) * 1e10) / 1e10;
      }
      else control.value = value;
    }

    function readSettingControlValue(control) {
      if (control.type === "checkbox") return control.checked;
      if (control.dataset.valueType === "boolean") return control.value === "true";
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
      trigger.querySelector(".select-trigger-value").textContent = selected?.label ?? window.taskbarLyricsI18n.t("UI_Select");
    }

    function syncWindowModeSelection() {
      const selectedMode = state.useFloatingWindow === true ? "floating" : "embedded";
      $$('[data-window-mode]').forEach(option => {
        const selected = option.dataset.windowMode === selectedMode;
        option.checked = selected;
        option.tabIndex = selected ? 0 : -1;
      });
    }

    function syncControls() {
      if (!state) return;
      $$('[data-setting]').forEach(control => control.classList.contains("select-trigger") ? syncSelectTrigger(control) : setControlValue(control, state[control.dataset.setting]));
      syncWindowModeSelection();
      const coverDisplay = state.showCover === false ? "Hidden" : state.coverPosition;
      $$('[data-cover-display]').forEach(option => {
        option.checked = option.value === coverDisplay;
        option.tabIndex = option.checked ? 0 : -1;
      });
      $$('input[type="range"][data-setting]').forEach(syncSliderProgress);
      $$('[data-color-text="foregroundColor"]').forEach(control => {
        if (!control.classList.contains("invalid")) control.value = state.foregroundColor.toUpperCase();
      });
      $$('[data-color-swatch]').forEach(swatch => { swatch.style.backgroundColor = state.foregroundColor; });
    }

    function renderMediaHotkeys() {
      if (!state) return;
      const focusAttributes = ["data-hotkey-binding", "data-hotkey-enabled", "data-hotkey-clear", "data-hotkey-reset"];
      const focusAttribute = focusAttributes.find(attribute => document.activeElement?.hasAttribute(attribute));
      const focusKey = focusAttribute ? document.activeElement.getAttribute(focusAttribute) : null;
      const recordingKey = activeHotkeyRecorder?.dataset.hotkeyBinding;
      const enabled = Boolean(state.enableGlobalMediaHotkeys);
      $("#mediaHotkeyMasterStatus").textContent = enabled ? window.taskbarLyricsI18n.t("UI_Enabled") : window.taskbarLyricsI18n.t("UI_Off");
      $("#mediaHotkeyList").innerHTML = (state.mediaHotkeys ?? []).map(definition => `
        <div class="media-hotkey-row"><div class="setting-label"><strong>${escapeHtml(definition.displayName)}</strong></div><div class="media-hotkey-controls"><button class="control media-hotkey-recorder" type="button" data-hotkey-binding="${escapeHtml(definition.settingKey)}" aria-label="${window.taskbarLyricsI18n.t("UI_Record")}${escapeHtml(definition.displayName)}${window.taskbarLyricsI18n.t("UI_Shortcuts")}"></button><output class="media-hotkey-status" data-hotkey-status="${escapeHtml(definition.statusKey)}" aria-live="polite"></output><div class="media-hotkey-actions"><label class="switch"><input type="checkbox" data-hotkey-enabled="${escapeHtml(definition.settingKey)}" aria-label="${window.taskbarLyricsI18n.t("UI_Enable")}${escapeHtml(definition.displayName)}${window.taskbarLyricsI18n.t("UI_Shortcuts")}" ${definition.isEnabled !== false ? "checked" : ""}><span class="switch-track"></span></label><button class="btn ghost small" type="button" data-hotkey-clear="${escapeHtml(definition.settingKey)}" aria-label="${window.taskbarLyricsI18n.t("UI_Clear")}${escapeHtml(definition.displayName)}${window.taskbarLyricsI18n.t("UI_Shortcuts")}">${window.taskbarLyricsI18n.t("UI_Clear")}</button><button class="btn ghost small media-hotkey-reset" type="button" data-hotkey-reset="${escapeHtml(definition.action)}" aria-label="${window.taskbarLyricsI18n.t("UI_Restore")}${escapeHtml(definition.displayName)}${window.taskbarLyricsI18n.t("UI_DefaultShortcut")}">${window.taskbarLyricsI18n.t("UI_Restore")}</button></div></div></div>`).join("");
      activeHotkeyRecorder = recordingKey
        ? [...$$('[data-hotkey-binding]')].find(button => button.dataset.hotkeyBinding === recordingKey) ?? null : null;
      if (activeHotkeyRecorder) {
        activeHotkeyRecorder.classList.add("recording");
        activeHotkeyRecorder.textContent = window.taskbarLyricsI18n.t("UI_PressAKeyCombination");
      }
      $$('[data-hotkey-binding]').forEach(button => {
        if (button !== activeHotkeyRecorder) button.textContent = state[button.dataset.hotkeyBinding] || window.taskbarLyricsI18n.t("UI_NotSet");
      });
      $$('[data-hotkey-status]').forEach(output => {
        const definition = state.mediaHotkeys.find(item => item.statusKey === output.dataset.hotkeyStatus);
        const status = !enabled || definition.isEnabled === false ? "disabled"
          : !state[definition.settingKey] ? "unassigned"
            : state.mediaHotkeyStatuses?.[output.dataset.hotkeyStatus] || "notRegistered";
        output.textContent = window.taskbarLyricsHotkeys.label(status);
        output.dataset.state = window.taskbarLyricsHotkeys.visualState(status);
      });
      if (focusAttribute) [...document.querySelectorAll(`[${focusAttribute}]`)]
        .find(control => control.getAttribute(focusAttribute) === focusKey)?.focus({ preventScroll: true });
    }

    function renderDisplayMonitorOption(display, mode, selected) {
      const displayId = escapeHtml(display.id);
      const displayName = escapeHtml(display.name);
      const primaryBadge = display.isPrimary ? `<span class="monitor-badge">${window.taskbarLyricsI18n.t("UI_PrimaryDisplay")}</span>` : "";
      const copy = `<span class="monitor-option-copy"><span class="monitor-option-title"><strong title="${displayName}">${displayName}</strong>${primaryBadge}</span><small>${Number(display.width) || 0} × ${Number(display.height) || 0}</small></span>`;
      if (mode === "All") {
        return `<div class="monitor-option is-readonly" data-display-id="${displayId}">${copy}</div>`;
      }

      return `<label class="monitor-option">${copy}<input type="checkbox" data-display-id="${displayId}" ${selected.has(display.id) ? "checked" : ""} aria-label="${window.taskbarLyricsI18n.t("UI_On")} ${displayName} ${window.taskbarLyricsI18n.t("UI_ShowLyrics")}"></label>`;
    }

    function renderDisplayMonitors() {
      if (!state) return;
      const mode = state.lyricsDisplayMode === "Selected" ? "Selected" : "All";
      const selected = new Set(Array.isArray(state.selectedDisplayIds) ? state.selectedDisplayIds : []);
      const displays = Array.isArray(state.availableDisplays) ? state.availableDisplays : [];
      $$('[data-display-mode]').forEach(control => { control.checked = control.value === mode; });
      const list = $("#displayMonitorList");
      const focusedDisplayId = document.activeElement?.dataset?.displayId;
      list.dataset.mode = mode;
      list.innerHTML = displays.length
        ? displays.map(display => renderDisplayMonitorOption(display, mode, selected)).join("")
        : `<div class="monitor-hint">${window.taskbarLyricsI18n.t("UI_NoDisplaysDetected")}</div>`;
      const focusedDisplay = [...list.querySelectorAll("input[data-display-id]")]
        .find(control => control.dataset.displayId === focusedDisplayId);
      focusedDisplay?.focus({ preventScroll: true });
      const connectedIds = new Set(displays.map(display => display.id));
      const connectedSelectedCount = [...selected].filter(id => connectedIds.has(id)).length;
      $("#displayMonitorHint").textContent = displays.length === 0
        ? window.taskbarLyricsI18n.t("UI_ThisListUpdatesAutomaticallyWhenDisplaysAreConnected")
        : mode === "All"
          ? window.taskbarLyricsI18n.format("UI_DisplaysConnectedLyricsWillAppearOnAllDisplays", displays.length)
          : connectedSelectedCount > 0
            ? window.taskbarLyricsI18n.format("UI_DisplaysSelected", connectedSelectedCount, displays.length)
            : window.taskbarLyricsI18n.t("UI_IfNoneAreSelectedLyricsTemporarilyAppearOnThe");
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
      button.textContent = window.taskbarLyricsI18n.t("UI_PressAKeyCombination");
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
      const definition = state.mediaHotkeys?.find(item => item.settingKey === key);
      if (definition) (state.mediaHotkeyStatuses ??= {})[definition.statusKey] = "notRegistered";
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

    function setState(nextState, fonts = [], languages = []) {
      pendingRangePreviews.clear();
      liveRangeValues.clear();
      const previousPage = state?.page ?? (initialPageId || "sources");
      const previousCustom = state?.customForegroundColor;
      const previousTrackOffsetSourceFilter = state?.trackOffsetSourceFilter ?? "All";
      const previousTrackOffsetSort = state?.trackOffsetSort ?? "updated";
      const foregroundColor = fromArgb(nextState.foregroundColor);
      state = window.taskbarLyricsSettingsState.create(nextState, {
        page: previousPage,
        trackOffsetSourceFilter: previousTrackOffsetSourceFilter,
        trackOffsetSort: previousTrackOffsetSort
      }, foregroundColor);
      const uiLanguageOptions = (Array.isArray(languages) ? languages : [])
        .filter(option => option && typeof option.value === "string" && typeof option.name === "string" && option.name.length > 0)
        .map(option => ({ value: option.value, label: option.name }));
      selectOptions.uiLanguage = uiLanguageOptions;
      if (uiLanguageOptions.length > 0 && !uiLanguageOptions.some(option => option.value === state.uiLanguage)) state.uiLanguage = "";
      $("#uiLanguageRow")?.toggleAttribute("hidden", uiLanguageOptions.length === 0);
      const incomingOffsets = nextState.playerLyricOffsets ?? {};
      const incomingDefaults = nextState.defaultPlayerLyricOffsets ?? {};
      const defaultOffsetFor = source => {
        const value = Number(incomingDefaults[source.adapter]);
        return Number.isFinite(value) ? Math.max(-5000, Math.min(5000, Math.round(value))) : 0;
      };
      const customSources = Array.isArray(nextState.customPlayerSources) ? nextState.customPlayerSources : [];
      const customProfiles = new Set(customSources.map(source => window.taskbarLyricsPlayerCompatibility.resolveSourceProfile(source.sourceAppUserModelId)));
      const defaults = sourceCatalogDefaults.filter(source => !["applemusic", "sodamusic", "foobar2000"].includes(source.id) || !customProfiles.has(source.id));
      const allSources = defaults.concat(customSources.map(source => ({
        id: `custom-${encodeURIComponent(source.sourceAppUserModelId)}`,
        adapter: source.sourceAppUserModelId,
        name: source.displayName,
        icon: source.iconDataUrl ?? "",
        presetIconId: source.presetIconId ?? "",
        presetIconColor: source.presetIconColor ?? "",
        custom: true,
        enabled: source.enabled === true
      })));
      state.playerLyricOffsets = Object.fromEntries(allSources.map(source => {
        const value = Number(incomingOffsets[source.adapter]);
        return [source.adapter, Number.isFinite(value) ? Math.max(-5000, Math.min(5000, Math.round(value))) : defaultOffsetFor(source)];
      }));
      state.playerLyricProviders = Object.fromEntries(allSources.map(source => [
        source.adapter, normalizeLyricProviders(nextState.playerLyricProviders?.[source.adapter], source.adapter)
      ]));
      state.customForegroundColor = nextState.foregroundColorMode === "Custom"
        ? foregroundColor
        : previousCustom ?? foregroundColor;
      repositoryUrl = nextState.repositoryUrl ?? "";
      sourceCatalog = allSources.map(source => ({
        ...source,
        defaultOffset: defaultOffsetFor(source),
        enabled: source.custom ? source.enabled : Boolean(nextState[source.settingKey])
      }));
      const order = (Array.isArray(nextState.sourceRecognitionOrder) ? nextState.sourceRecognitionOrder : [])
        .map(adapter => typeof adapter === "string" ? allSources.find(source => source.adapter.toLowerCase() === adapter.toLowerCase())?.adapter : undefined)
        .filter(Boolean);
      for (const source of allSources) {
        if (order.includes(source.adapter)) continue;
        const browser = order.indexOf("Browser");
        if (!source.custom && source.id !== "browser" && browser >= 0) order.splice(browser, 0, source.adapter);
        else order.push(source.adapter);
      }
      sourceCatalog.sort((a, b) => {
        const aIndex = order.indexOf(a.adapter);
        const bIndex = order.indexOf(b.adapter);
        return (aIndex < 0 ? 99 : aIndex) - (bIndex < 0 ? 99 : bIndex);
      });
      if (pendingPlayerSourceAction) {
        const updated = sourceCatalog.find(source => source.custom &&
          source.adapter.toLowerCase() === pendingPlayerSourceAction.sourceId.toLowerCase());
        if (updated) {
          playerInfoBusy = false;
          pendingPlayerSourceAction = null;
          if (playerDrawerMode === "add") {
            closeDialogWithAnimation($("#playerSettingsDialog"));
          } else {
            activePlayerSourceId = updated.id;
            loadPlayerInfoForm(updated);
            $("#playerDrawerBody").scrollTop = 0;
          }
        }
      }
      if (activePlayerSourceId && !sourceCatalog.some(source => source.id === activePlayerSourceId)) {
        $("#removePlayerDialog").close();
        $("#playerSettingsDialog").close();
        $("#addPlayerSourceButton").focus({ preventScroll: true });
      }
      setAvailableFonts(fonts);
      const version = nextState.appVersion || "--";
      $(".version-badge").textContent = `Version ${version}`;
      if (updateState === "idle") $("#updateStatusDetail").textContent = window.taskbarLyricsI18n.format("UI_CurrentVersion_14", version);
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
      if (["floatingPanelBlurAmount", "embeddedPanelBlurAmount"].includes(key))
        state[key] = Number.isFinite(Number(value)) ? Math.min(64, Math.max(0, Number(value))) : window.taskbarLyricsSettingsState.panelMaterialDefaults[key];
      if (["floatingPanelBrightness", "floatingPanelTintOpacity", "embeddedPanelBrightness", "embeddedPanelTintOpacity"].includes(key))
        state[key] = Number.isFinite(Number(value)) ? Math.min(1, Math.max(0, Number(value))) : window.taskbarLyricsSettingsState.panelMaterialDefaults[key];
      if (key === "foregroundColor") {
        state.foregroundColor = fromArgb(value);
        state.customForegroundColor = state.foregroundColor;
        state.foregroundColorMode = "Custom";
      }
      if (key === "lyricsFallback") state.spectrumDisplayMode = fallbackSpectrumMode(value);
      if (key === "coverCornerRadius") state.coverCornerRadius = Math.min(state.coverCornerRadius, state.coverSize / 2);
      syncColorMode(); applyDependencies(); syncLayoutBounds(); syncWindowBounds(); updateOutputs(); syncControls(); renderSpectrumAudioAccess(); renderFallbackRules();
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
      if (value === false &&
          (key === "controlPanelTriggerLyrics" && !(state.showCover !== false && state.controlPanelTriggerCover) ||
           key === "controlPanelTriggerCover" && !state.controlPanelTriggerLyrics)) {
        syncControls();
        showToast(window.taskbarLyricsI18n.t("UI_SelectAtLeastOneAvailableActivationArea"));
        return;
      }
      pendingRangePreviews.delete(key);
      const liveRangeValue = liveRangeValues.get(key);
      const previousCornerRadius = applySettingLocally(key, value);
      if (liveRangeValue) {
        if (["lyricsLayoutScalePercent", "coverGap", "coverCornerRadius"].includes(key)) {
          const committedRangeValue = { value: state[key], editing: false };
          liveRangeValues.set(key, committedRangeValue);
          setTimeout(() => {
            if (liveRangeValues.get(key) !== committedRangeValue) return;
            liveRangeValues.delete(key);
            $$(`[data-setting="${key}"]`).forEach(control => {
              setControlValue(control, state[key]);
              if (control.type === "range") syncSliderProgress(control);
            });
          }, 1000);
        } else {
          liveRangeValues.delete(key);
        }
      }
      if (key === "lyricsLayoutScalePercent") announceNextLayoutPreview = true;
      const payload = key === "foregroundColor" ? toArgb(state.foregroundColor) : state[key];
      bridge.post({ type: "update", key, value: payload });
      if (key !== "coverCornerRadius" && previousCornerRadius !== state.coverCornerRadius) {
        bridge.post({ type: "update", key: "coverCornerRadius", value: state.coverCornerRadius });
      }
      markSaved();
    }

    function commitCoverDisplay(value) {
      if (!state || !["Hidden", "Left", "Right"].includes(value)) return;
      const showCover = value !== "Hidden";
      // Set the side before revealing the cover; hiding preserves its stored position.
      if (showCover && state.coverPosition !== value) commitSetting("coverPosition", value);
      if ((state.showCover !== false) !== showCover) commitSetting("showCover", showCover);
    }

    function scheduleSettingPreview(key, value, source) {
      if (!state) return;
      state[key] = value;
      liveRangeValues.set(key, { value, editing: true });
      updateOutputs();
      $$(`[data-setting="${key}"]`).forEach(control => {
        if (control !== source) setControlValue(control, value);
        if (control.type === "range") syncSliderProgress(control);
      });
      pendingRangePreviews.set(key, value);
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
      title.textContent = status === "checking" ? window.taskbarLyricsI18n.t("UI_CheckingForUpdates")
        : status === "available" ? window.taskbarLyricsI18n.format("UI_NewVersionAvailable", payload.version ?? "")
        : status === "latest" ? window.taskbarLyricsI18n.t("UI_YouAreUpToDate")
        : status === "error" ? window.taskbarLyricsI18n.t("UI_UpdateCheckFailed")
        : window.taskbarLyricsI18n.t("UI_UpdatesNotCheckedYet");
      detail.textContent = payload.message || window.taskbarLyricsI18n.format("UI_CurrentVersion_14", version);
      checkButton.disabled = status === "checking";
      checkButton.innerHTML = status === "checking" ? `<span class="spinner" aria-hidden="true"></span>${window.taskbarLyricsI18n.t("UI_Checking")}` : status === "available" ? window.taskbarLyricsI18n.t("UI_CheckAgain") : window.taskbarLyricsI18n.t("UI_CheckForUpdates");
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
      state.windowWidth = Math.min(1400, Math.max(100, Number(state.windowWidth) || 420));
      const embedded = state.useFloatingWindow === false;
      const limitOr = (value, fallback) => Number.isFinite(Number(value)) ? Number(value) : fallback;
      const bound = (key, min, max) => {
        const safeMin = Number.isFinite(Number(min)) ? Number(min) : 0;
        const safeMax = Number.isFinite(Number(max)) ? Math.max(safeMin, Number(max)) : safeMin;
        $$(`[data-setting="${key}"]`).forEach(control => {
          control.min = String(safeMin);
          control.max = String(safeMax);
        });
        state[key] = Math.min(safeMax, Math.max(safeMin, limitOr(state[key], safeMin)));
      };
      bound(
        "lyricsLayoutScalePercent",
        25,
        embedded ? limitOr(state.taskbarMaxScalePercent, 300) : 300);
      bound(
        "fontSize",
        6,
        embedded ? limitOr(state.taskbarMaxFontSize, 96) : 96);
      bound(
        "coverSize",
        12,
        embedded ? limitOr(state.taskbarMaxCoverSize, 200) : 200);
      bound(
        "coverGap",
        0,
        embedded ? limitOr(state.taskbarMaxCoverGap, 240) : 240);
      bound(
        "windowWidth",
        100,
        embedded ? limitOr(state.taskbarMaxWindowWidth, 1400) : 1400);
      state.coverCornerRadius = Math.min(
        Math.max(0, Number(state.coverCornerRadius) || 0),
        state.coverSize / 2);
      $$('[data-setting="coverCornerRadius"]').forEach(control => {
        control.max = String(state.coverSize / 2);
      });
      bound(
        "xOffset",
        embedded ? limitOr(state.taskbarMinXOffset, -2000) : -2000,
        embedded ? limitOr(state.taskbarMaxXOffset, 2000) : 2000);
      bound(
        "yOffset",
        embedded ? limitOr(state.taskbarMinYOffset, -2000) : -2000,
        embedded ? limitOr(state.taskbarMaxYOffset, 2000) : 2000);
    }

    function applyDependencies() {
      window.taskbarLyricsSettingsState.normalizeControlPanelTriggers(state);
      $$('[data-depends], [data-embedded-only], [data-floating-only]').forEach(row => {
        const enabled = (!row.dataset.depends || Boolean(state[row.dataset.depends])) &&
          (!row.hasAttribute("data-embedded-only") || state.useFloatingWindow !== true) &&
          (!row.hasAttribute("data-floating-only") || state.useFloatingWindow === true);
        row.classList.toggle("is-disabled", !enabled);
        row.querySelectorAll("input, select, textarea, button").forEach(control => { control.disabled = !enabled; });
      });
      const coverTrigger = $('[data-setting="controlPanelTriggerCover"]');
      if (coverTrigger) coverTrigger.disabled ||= state.showCover === false;
      window.taskbarLyricsDisplayPresentation?.sync(state);
    }

    function updateOutputs() {
      if ($("[data-floating-width]")) $("[data-floating-width]").textContent = `${formatLayoutMetric(state.floatingWindowWidth)} px`;
      const appearanceSummary = $("[data-embedded-appearance-summary]");
      if (appearanceSummary) {
        appearanceSummary.textContent = window.taskbarLyricsI18n.format("UI_BackgroundBorder", state.showBackground ? window.taskbarLyricsI18n.t("UI_On_15") : window.taskbarLyricsI18n.t("UI_Close"), state.showBorder ? window.taskbarLyricsI18n.t("UI_On_15") : window.taskbarLyricsI18n.t("UI_Close"));
        appearanceSummary.title = appearanceSummary.textContent;
      }
      if ($("[data-floating-scale]")) $("[data-floating-scale]").textContent = `${formatLayoutMetric(state.floatingLyricsLayoutScalePercent)}%`;
      if ($("[data-effective-font-size]")) {
        const showCover = state.showCover !== false;
        $("[data-effective-font-size]").textContent = `${formatLayoutMetric(state.effectiveFontSize)} px`;
        $("[data-effective-cover-size]").textContent = showCover
          ? `${formatLayoutMetric(state.effectiveCoverSize)} px`
          : window.taskbarLyricsI18n.t("UI_Hidden");
        $("[data-effective-cover-gap]").textContent = `${formatLayoutMetric(state.effectiveCoverGap)} px`;
        $("[data-effective-cover-gap-item]").hidden = !showCover;
      }
    }

    function renderTaskbarEmbeddingStatus() {
      const status = $("#taskbarEmbeddingStatus");
      if (!status) return;
      const message = typeof state.taskbarEmbeddingStatus === "string"
        ? state.taskbarEmbeddingStatus.trim()
        : "";
      status.hidden = !message;
      status.textContent = message;
      if (message) showToast(message);
    }

    function formatLayoutMetric(value) {
      const numeric = Number(value);
      return Number.isFinite(numeric)
        ? numeric.toLocaleString(window.taskbarLyricsI18n.locale, { maximumFractionDigits: 2 })
        : "--";
    }

    function updateLayoutPreview(payload = {}) {
      if (!state) return;
      ["scalePercent", "floatingLyricsLayoutScalePercent", "floatingWindowWidth", "fontSize", "coverSize", "coverGap", "coverCornerRadius", "effectiveFontSize", "effectiveCoverSize", "effectiveCoverGap", "effectiveCoverCornerRadius", "effectiveWindowWidth"].forEach(key => {
        const value = Number(payload[key]);
        if (!Number.isFinite(value)) return;
        const stateKey = key === "scalePercent" ? "lyricsLayoutScalePercent" : key;
        const liveRangeValue = liveRangeValues.get(stateKey);
        if (liveRangeValue) {
          if (liveRangeValue.editing || value !== liveRangeValue.value) return;
          liveRangeValues.delete(stateKey);
        }
        state[stateKey] = value;
      });
      ["taskbarEmbeddingAvailable", "taskbarMaxWidth", "taskbarMaxHeight", "taskbarMaxScalePercent", "taskbarMaxFontSize", "taskbarMaxCoverSize", "taskbarMaxCoverGap", "taskbarMaxWindowWidth", "taskbarMinXOffset", "taskbarMaxXOffset", "taskbarMinYOffset", "taskbarMaxYOffset"].forEach(key => {
        if (Object.prototype.hasOwnProperty.call(payload, key)) state[key] = payload[key];
      });
      syncLayoutBounds();
      syncWindowBounds();
      liveRangeValues.forEach((liveValue, key) => {
        if (!liveValue.editing && state[key] !== liveValue.value) liveRangeValues.delete(key);
      });
      if (liveRangeValues.size === 0) syncControls();
      updateOutputs();
      if (announceNextLayoutPreview) {
        const coverAnnouncement = state.showCover === false
          ? window.taskbarLyricsI18n.t("UI_CoverArtHidden")
          : window.taskbarLyricsI18n.format("UI_CoverPxSpacingPx", formatLayoutMetric(state.effectiveCoverSize), formatLayoutMetric(state.effectiveCoverGap));
        $("#layoutScaleAnnouncement").textContent = window.taskbarLyricsI18n.format("UI_LyricScaleSetToEffectiveFontSizePx", formatLayoutMetric(state.lyricsLayoutScalePercent), formatLayoutMetric(state.effectiveFontSize), coverAnnouncement);
        announceNextLayoutPreview = false;
      }
    }

    function refresh() {
      renderSources();
      renderGettingStarted();
      renderTrackOffsets();
      if ($("#playerSettingsDialog").open) renderPlayerSettings();
      syncLayoutBounds();
      syncWindowBounds();
      syncColorMode();
      syncControls();
      renderMediaHotkeys();
      renderDisplayMonitors();
      applyDependencies();
      renderSpectrumAudioAccess();
      updateOutputs();
      renderFallbackRules();
      renderTaskbarEmbeddingStatus();
      activatePage(state.page, false);
    }

    function resetState() {
      bridge.post({ type: "resetDefaults" });
      markSaved();
    }

    document.addEventListener("click", event => {
      if (event.target.closest("[data-getting-started-done]")) {
        if (!state) return;
        const profile = window.taskbarLyricsPlayerCompatibility.getProfile(gettingStartedPlayer);
        if (profile.level === "setup") bridge.post({ type: "openExternalLink", value: profile.link });
        else startGettingStartedPlayback();
        return;
      }
      if (event.target.closest("[data-getting-started-later]")) {
        navigateToPage("sources");
        return;
      }
      const hotkeyBinding = event.target.closest("[data-hotkey-binding]");
      if (hotkeyBinding) { beginHotkeyRecording(hotkeyBinding); return; }

      const hotkeyReset = event.target.closest("[data-hotkey-reset]");
      if (hotkeyReset) {
        cancelHotkeyRecording();
        bridge.post({ type: "resetMediaHotkey", value: hotkeyReset.dataset.hotkeyReset });
        markSaved();
        return;
      }

      const hotkeyClear = event.target.closest("[data-hotkey-clear]");
      if (hotkeyClear) {
        cancelHotkeyRecording();
        commitHotkeyBinding(hotkeyClear.dataset.hotkeyClear, "");
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

      const modeReset = event.target.closest("[data-reset-window-mode]");
      if (modeReset && state) {
        pendingRangePreviews.forEach((value, key) => bridge.post({ type: "update", key, value }));
        pendingRangePreviews.clear();
        liveRangeValues.clear();
        bridge.post({ type: "resetWindowPresentation", value: modeReset.dataset.resetWindowMode });
        return;
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
      const playerHandle = event.target.closest("[data-player-drag-id]");
      if (playerHandle && state) {
        draggedPlayerSourceId = playerHandle.dataset.playerDragId;
        event.dataTransfer.effectAllowed = "move";
        event.dataTransfer.setData("text/plain", draggedPlayerSourceId);
        playerHandle.closest("[data-player-card]").classList.add("dragging");
        return;
      }
      const lyricHandle = event.target.closest("[data-lyric-provider-drag]");
      if (lyricHandle && (activePlayerSourceId || playerDrawerMode === "add")) {
        draggedLyricProviderId = lyricHandle.dataset.lyricProviderDrag;
        event.dataTransfer.effectAllowed = "move";
        event.dataTransfer.setData("text/plain", draggedLyricProviderId);
        lyricHandle.closest("[data-lyric-provider-item]").classList.add("dragging");
        return;
      }
    });

    document.addEventListener("dragover", event => {
      const playerGrid = event.target.closest("#sourceGrid");
      if (playerGrid && draggedPlayerSourceId) {
        event.preventDefault();
        event.dataTransfer.dropEffect = "move";
        const playerCard = event.target.closest("[data-player-card]");
        const draggedCard = playerGrid.querySelector(`[data-player-card="${draggedPlayerSourceId}"]`);
        if (playerCard && draggedCard && playerCard !== draggedCard) {
          const cards = [...playerGrid.children];
          playerGrid.insertBefore(draggedCard, cards.indexOf(draggedCard) < cards.indexOf(playerCard) ? playerCard.nextSibling : playerCard);
        }
        return;
      }
      const lyricList = event.target.closest("#lyricProviderList");
      if (lyricList && draggedLyricProviderId) {
        event.preventDefault();
        event.dataTransfer.dropEffect = "move";
        const lyricItem = event.target.closest("[data-lyric-provider-item]");
        const draggedItem = lyricList.querySelector(`[data-lyric-provider-item="${draggedLyricProviderId}"]`);
        if (lyricItem && draggedItem && lyricItem !== draggedItem) {
          const rect = lyricItem.getBoundingClientRect();
          lyricList.insertBefore(draggedItem, event.clientY > rect.top + rect.height / 2 ? lyricItem.nextSibling : lyricItem);
          [...lyricList.children].forEach((item, index) => { item.querySelector(".priority-number").textContent = index + 1; });
        }
        return;
      }
    });

    document.addEventListener("drop", event => {
      const playerGrid = event.target.closest("#sourceGrid");
      if (playerGrid && draggedPlayerSourceId) {
        event.preventDefault();
        const movedId = draggedPlayerSourceId;
        const previewOrder = [...playerGrid.children].map(card => card.dataset.playerCard);
        draggedPlayerSourceId = null;
        if (previewOrder.some((id, index) => id !== sourceCatalog[index].id)) {
          commitPlayerSourceOrder(previewOrder);
          announcePlayerSourcePosition(movedId);
        } else {
          renderSources();
        }
        return;
      }
      const lyricList = event.target.closest("#lyricProviderList");
      if (lyricList && draggedLyricProviderId) {
        event.preventDefault();
        const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
        const providers = currentLyricProviders();
        const previewOrder = [...lyricList.children].map(item => item.dataset.lyricProviderItem);
        if (previewOrder.some((id, index) => id !== providers[index].providerId)) {
          const reordered = previewOrder.map(id => providers.find(item => item.providerId === id));
          if (playerDrawerMode === "add") newPlayerLyricProviders = reordered;
          else state.playerLyricProviders[source.adapter] = reordered;
          commitLyricProviders();
          announceLyricProviderPosition(draggedLyricProviderId);
        } else {
          renderLyricProviders();
        }
        draggedLyricProviderId = null;
        return;
      }
    });

    document.addEventListener("dragend", () => {
      if (draggedPlayerSourceId) renderSources();
      draggedPlayerSourceId = null;
      if (draggedLyricProviderId) {
        renderLyricProviders();
      }
      draggedLyricProviderId = null;
      $$('[data-lyric-provider-item]').forEach(node => node.classList.remove("dragging", "drag-over"));
    });

    document.addEventListener("keydown", event => {
      if (activeHotkeyRecorder) {
        event.preventDefault();
        event.stopPropagation();
        if (event.key === "Escape") { cancelHotkeyRecording(); return; }
        if (!event.ctrlKey && !event.altKey && !event.shiftKey && !event.metaKey &&
            ["Backspace", "Delete"].includes(event.key)) {
          const key = activeHotkeyRecorder.dataset.hotkeyBinding;
          activeHotkeyRecorder = null;
          commitHotkeyBinding(key, "");
          return;
        }
        const binding = getRecordedHotkey(event);
        if (!binding) {
          if (!["Control", "Alt", "Shift", "Meta"].includes(event.key)) activeHotkeyRecorder.textContent = window.taskbarLyricsI18n.t("UI_UseCtrlAltOrShift");
          return;
        }
        const button = activeHotkeyRecorder;
        button.classList.remove("recording");
        activeHotkeyRecorder = null;
        commitHotkeyBinding(button.dataset.hotkeyBinding, binding);
        return;
      }

      const playerHandle = event.target.closest("[data-player-drag-id]");
      if (playerHandle && event.altKey && ["ArrowUp", "ArrowDown", "ArrowLeft", "ArrowRight"].includes(event.key)) {
        event.preventDefault();
        const sourceId = playerHandle.dataset.playerDragId;
        const current = sourceCatalog.findIndex(source => source.id === sourceId);
        const target = current + (["ArrowUp", "ArrowLeft"].includes(event.key) ? -1 : 1);
        if (current < 0 || target < 0 || target >= sourceCatalog.length) return;
        const order = sourceCatalog.map(source => source.id);
        [order[current], order[target]] = [order[target], order[current]];
        commitPlayerSourceOrder(order);
        announcePlayerSourcePosition(sourceId);
        requestAnimationFrame(() => document.querySelector(`[data-player-drag-id="${sourceId}"]`)?.focus());
        return;
      }

      const lyricHandle = event.target.closest("[data-lyric-provider-drag]");
      if (lyricHandle && event.altKey && ["ArrowUp", "ArrowDown"].includes(event.key)) {
        event.preventDefault();
        const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
        const providers = currentLyricProviders();
        const current = providers?.findIndex(item => item.providerId === lyricHandle.dataset.lyricProviderDrag) ?? -1;
        const target = current + (event.key === "ArrowUp" ? -1 : 1);
        if (!providers || target < 0 || target >= providers.length) return;
        [providers[current], providers[target]] = [providers[target], providers[current]];
        if (playerDrawerMode !== "add") state.playerLyricProviders[source.adapter] = providers;
        commitLyricProviders();
        announceLyricProviderPosition(lyricHandle.dataset.lyricProviderDrag);
        return;
      }
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
      const windowModeOption = event.target.closest("[data-window-mode]");
      const layoutOption = event.target.closest('.layout-options input[type="radio"]');
      if (layoutOption && !layoutOption.disabled && ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"].includes(event.key)) {
        event.preventDefault();
        const options = Array.from(layoutOption.closest(".layout-options").querySelectorAll('input:not(:disabled)'));
        const current = options.indexOf(layoutOption);
        const next = event.key === "Home" ? 0 : event.key === "End" ? options.length - 1
          : (current + (["ArrowRight", "ArrowDown"].includes(event.key) ? 1 : -1) + options.length) % options.length;
        options[next].focus({ preventScroll: true });
        if (!options[next].checked) {
          if (options[next].hasAttribute("data-cover-display")) commitCoverDisplay(options[next].value);
          else commitSetting(options[next].dataset.setting, options[next].value);
        }
        return;
      }
      if (windowModeOption && ["ArrowLeft", "ArrowRight", "ArrowUp", "ArrowDown", "Home", "End"].includes(event.key)) {
        event.preventDefault();
        const options = $$('[data-window-mode]');
        const current = options.indexOf(windowModeOption);
        const next = event.key === "Home"
          ? 0
          : event.key === "End"
            ? options.length - 1
            : (current + (["ArrowRight", "ArrowDown"].includes(event.key) ? 1 : -1) + options.length) % options.length;
        options[next].focus({ preventScroll: true });
        if (!options[next].checked) commitSetting("useFloatingWindow", options[next].dataset.windowMode === "floating");
        return;
      }

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
      const hotkeyToggle = event.target.closest("[data-hotkey-enabled]");
      if (hotkeyToggle) {
        const definition = state?.mediaHotkeys?.find(item => item.settingKey === hotkeyToggle.dataset.hotkeyEnabled);
        if (!definition) return;
        definition.isEnabled = hotkeyToggle.checked;
        (state.mediaHotkeyStatuses ??= {})[definition.statusKey] = "notRegistered";
        bridge.post({ type: "update", key: definition.enabledSettingKey || definition.settingKey + "Enabled", value: hotkeyToggle.checked });
        cancelHotkeyRecording();
        renderMediaHotkeys();
        markSaved();
        return;
      }
      if (event.target.matches('input[name="gettingStartedPlayer"]')) {
        gettingStartedPlayer = event.target.value;
        syncGettingStartedTabStops();
        renderGettingStarted();
        return;
      }
      const lyricToggle = event.target.closest("[data-lyric-provider-toggle]");
      if (lyricToggle) {
        const provider = currentLyricProviders()
          .find(item => item.providerId === lyricToggle.dataset.lyricProviderToggle);
        if (provider) {
          provider.enabled = lyricToggle.checked;
          commitLyricProviders();
        }
        return;
      }
      const displayMode = event.target.closest("[data-display-mode]");
      if (displayMode) {
        commitSetting("lyricsDisplayMode", displayMode.value);
        renderDisplayMonitors();
        return;
      }

      const displayOption = event.target.closest("[data-display-id]");
      if (displayOption) {
        const selected = new Set(Array.isArray(state.selectedDisplayIds) ? state.selectedDisplayIds : []);
        if (displayOption.checked) selected.add(displayOption.dataset.displayId);
        else selected.delete(displayOption.dataset.displayId);
        commitSetting("selectedDisplayIds", [...selected]);
        renderDisplayMonitors();
        return;
      }

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
        if (playerDrawerMode === "add") {
          newPlayerEnabled = event.target.checked;
          return;
        }
        const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
        if (source) {
          source.enabled = event.target.checked;
          if (source.custom) {
            bridge.post({ type: "update", key: `customPlayerEnabled:${source.adapter}`, value: source.enabled });
          } else {
            state[source.settingKey] = source.enabled;
            bridge.post({ type: "update", key: source.settingKey, value: source.enabled });
          }
        }
        renderSources(); renderPlayerSettings(); markSaved(); return;
      }

      if (event.target === $("#playerOffsetInput")) { commitPlayerOffset(event.target.value); return; }

      const coverDisplayOption = event.target.closest("[data-cover-display]");
      if (coverDisplayOption) {
        if (coverDisplayOption.checked) commitCoverDisplay(coverDisplayOption.value);
        return;
      }

      const windowModeOption = event.target.closest("[data-window-mode]");
      if (windowModeOption) {
        commitSetting("useFloatingWindow", windowModeOption.dataset.windowMode === "floating");
        return;
      }

      const control = event.target.closest("[data-setting]");
      if (!control) return;
      if (control.disabled || (control.type === "radio" && !control.checked)) return;
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
      if (control && !control.disabled) {
        scheduleSettingPreview(control.dataset.setting, readSettingControlValue(control), control);
        return;
      }
      const numberControl = event.target.closest('.slider-number-control input[type="number"][data-setting]');
      if (!numberControl || numberControl.disabled || numberControl.value === "" || !numberControl.validity.valid) return;
      scheduleSettingPreview(numberControl.dataset.setting, readSettingControlValue(numberControl), numberControl);
    });

    $$('[data-color-text="foregroundColor"]').forEach(input => input.addEventListener("change", () => {
      const valid = /^#[0-9a-f]{6}$/i.test(input.value);
      setColorInputValidity(input, $("#foregroundColorError"), valid);
      if (valid) commitSetting("foregroundColor", input.value.toUpperCase());
    }));

    function updateNavigationTooltip(button) {
      if (!button) return;
      const rect = button.getBoundingClientRect();
      const nav = $(".nav").getBoundingClientRect();
      button.style.setProperty("--nav-tooltip-left", `${rect.right + 12}px`);
      button.style.setProperty("--nav-tooltip-top", `${Math.max(18, Math.min(window.innerHeight - 18, rect.top + rect.height / 2))}px`);
      button.style.setProperty("--nav-tooltip-visibility", rect.bottom > nav.top && rect.top < nav.bottom ? "visible" : "hidden");
    }

    for (const eventName of ["pointerover", "focusin"]) {
      $(".nav").addEventListener(eventName, event => updateNavigationTooltip(event.target.closest?.("[data-nav]")));
    }
    const refreshNavigationTooltip = () => {
      const buttons = new Set([
        $(".nav-item:hover"), document.activeElement?.closest?.("[data-nav]")
      ]);
      buttons.forEach(updateNavigationTooltip);
    };
    $(".nav").addEventListener("scroll", refreshNavigationTooltip, { passive: true });
    window.addEventListener("resize", refreshNavigationTooltip);

    $("#sidebarToggle").addEventListener("click", () => {
      const collapsed = $("#appShell").classList.toggle("sidebar-collapsed");
      $("#sidebarToggle").setAttribute("aria-label", collapsed ? window.taskbarLyricsI18n.t("UI_ExpandSidebar") : window.taskbarLyricsI18n.t("UI_CollapseSidebar"));
    });
    $$("dialog").forEach(d => d.addEventListener("cancel", event => {
      if (event.target !== d) return;
      event.preventDefault();
      closeDialogWithAnimation(d);
    }));
    $("#restoreButton").addEventListener("click", () => $("#restoreDialog").showModal());
    $("#clearCacheButton").addEventListener("click", () => $("#clearDialog").showModal());
    $("#confirmRestore").addEventListener("click", () => { closeDialogWithAnimation($("#restoreDialog")); resetState(); });
    $("#confirmClear").addEventListener("click", () => { closeDialogWithAnimation($("#clearDialog")); bridge.post({ type: "clearCache" }); showToast(window.taskbarLyricsI18n.t("UI_LyricCacheCoverCacheAndSavedSelectionsCleared")); });
    $("#confirmSpectrumAudioAccess").addEventListener("click", () => {
      const mode = pendingSpectrumDisplayMode;
      if (!mode) return;
      const fallback = pendingFallbackSettings;
      pendingFallbackSettings = null;
      pendingSpectrumDisplayMode = null;
      closeDialogWithAnimation($("#spectrumAudioConsentDialog"));
      bridge.post({ type: "confirmSpectrumAudioAccess", value: fallback ? { spectrumDisplayMode: mode, lyricsFallback: fallback } : mode });
      markSaved();
    });
    $("#spectrumAudioConsentDialog").addEventListener("close", () => { pendingSpectrumDisplayMode = null; pendingFallbackSettings = null; });
    $("#revokeSpectrumAudioAccessButton").addEventListener("click", () => {
      bridge.post({ type: "revokeSpectrumAudioAccess" });
      markSaved();
    });
    $$('[data-retry-spectrum-capture]').forEach(button => button.addEventListener("click", () => {
      spectrumCaptureState = { state: "waiting", message: window.taskbarLyricsI18n.t("UI_RetryingSystemAudioCapture") };
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
      pendingPlayerSourceAction = null;
      playerInfoBusy = false;
      const sourceId = activePlayerSourceId;
      activePlayerSourceId = null;
      (sourceId ? document.querySelector(`[data-player-settings="${sourceId}"]`) : $("#addPlayerSourceButton"))?.focus({ preventScroll: true });
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
    document.addEventListener("click", event => {
      const button = event.target.closest?.("[data-lyric-diagnostics-apply]");
      if (!button || button.disabled) return;
      const providerId = button.dataset.providerId;
      const candidateId = button.dataset.candidateId;
      const mode = button.dataset.applyMode === "remember" ? "remember" : "current";
      if (!providerId || !candidateId) return;
      const key = diagnosticCandidateKey(providerId, candidateId, mode);
      diagnosticApplyFocus = document.activeElement === button ? button : null;
      lyricDiagnosticsState.apply = {
        status: "running",
        key,
        message: mode === "remember" ? window.taskbarLyricsI18n.t("UI_FetchingApplyingAndCachingLyrics") : window.taskbarLyricsI18n.t("UI_FetchingAndApplyingLyrics")
      };
      renderLyricDiagnosticsState(true);
      bridge.post({
        type: "applyLyricDiagnosticCandidate",
        value: { providerId, candidateId, mode }
      });
    });
    const openRepository = () => { if (repositoryUrl) bridge.post({ type: "openExternalLink", value: repositoryUrl }); };
    $("#repositoryButton").addEventListener("click", openRepository);
    $$('[data-repository-link]').forEach(button => button.addEventListener("click", openRepository));
    $("#checkUpdateButton").addEventListener("click", () => {
      setUpdateStatus({ state: "checking", message: window.taskbarLyricsI18n.t("UI_ConnectingToGitHubReleases") });
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

    $("#colorPresets").innerHTML = presetColors.map(color => `<button class="color-preset" type="button" style="--preset:${color}" data-preset-color-value="${color}" aria-label="${window.taskbarLyricsI18n.t("UI_Select_16")} ${color}"></button>`).join("");
    $("#addPlayerSourceButton").addEventListener("click", openAddPlayerDrawer);
    $("#refreshPlayerSessionsButton").addEventListener("click", () => requestPlayerSessions());
    $("#availablePlayerSessions").addEventListener("click", event => {
      const button = event.target.closest("[data-player-session-index]");
      if (!button) return;
      const session = availablePlayerSessions[Number(button.dataset.playerSessionIndex)];
      if (!session) return;
      selectedPlayerSessionId = session.sourceAppUserModelId;
      selectedSessionAutoIconDataUrl = safeSessionIcon(session.iconDataUrl);
      resetSelectedPlayerIcon();
      $("#customPlayerName").value = session.displayName;
      updateAddPlayerButton();
      $$('[data-player-session-index]').forEach(item => item.setAttribute("aria-pressed", String(item === button)));
      $("#customPlayerName").focus({ preventScroll: true });
    });
    $("#customPlayerName").addEventListener("input", updateAddPlayerButton);
    $("#chooseCustomPlayerIconButton").addEventListener("click", () => $("#customPlayerIcon").click());
    $("#clearCustomPlayerIconButton").addEventListener("click", clearCustomPlayerIcon);
    $("#customPlayerIcon").addEventListener("change", event => {
      const file = event.target.files?.[0];
      if (!file) return;
      if (!["image/png", "image/jpeg", "image/webp"].includes(file.type) || file.size > 128 * 1024 || file.size === 0) {
        $("#customPlayerIconStatus").textContent = window.taskbarLyricsI18n.t("UI_ChooseAPNGJPEGOrWebPImageUpTo");
        event.target.value = "";
        return;
      }
      const readToken = ++playerIconReadToken;
      playerIconReading = true;
      updateAddPlayerButton();
      const reader = new FileReader();
      reader.onload = () => {
        if (readToken !== playerIconReadToken || typeof reader.result !== "string") return;
        playerIconReading = false;
        selectedPlayerIconMode = "custom";
        selectedPlayerIconDataUrl = reader.result;
        $("#customPlayerIconStatus").textContent = file.name;
        event.target.value = "";
        renderSelectedPlayerIcon();
      };
      reader.onerror = () => {
        if (readToken !== playerIconReadToken) return;
        playerIconReading = false;
        $("#customPlayerIconStatus").textContent = window.taskbarLyricsI18n.t("UI_CannotReadTheIconPleaseRetry");
        event.target.value = "";
        updateAddPlayerButton();
      };
      reader.readAsDataURL(file);
    });
    $("#confirmAddPlayerSource").addEventListener("click", () => {
      if ($("#confirmAddPlayerSource").disabled) return;
      pendingPlayerSourceAction = { sourceId: selectedPlayerSessionId };
      playerInfoBusy = true;
      updateAddPlayerButton();
      bridge.post({ type: "addPlayerSource", value: playerInfoPayload() });
      $("#playerDiscoveryStatus").textContent = window.taskbarLyricsI18n.t("UI_AddingPlayer");
    });
    $("#savePlayerInfoButton").addEventListener("click", () => {
      if ($("#savePlayerInfoButton").disabled) return;
      pendingPlayerSourceAction = { sourceId: selectedPlayerSessionId };
      playerInfoBusy = true;
      updateAddPlayerButton();
      bridge.post({ type: "updatePlayerSource", value: playerInfoPayload() });
    });
    $("#removePlayerSourceButton").addEventListener("click", () => $("#removePlayerDialog").showModal());
    $("#confirmRemovePlayerSource").addEventListener("click", () => {
      const source = sourceCatalog.find(item => item.id === activePlayerSourceId);
      if (!source?.custom) return;
      bridge.post({ type: "removePlayerSource", value: source.adapter });
      closeDialogWithAnimation($("#removePlayerDialog"));
    });
    function receive(message) {
      if (message?.version !== 1 || !message.type) return;
      switch (message.type) {
        case "settingsState":
          setState(message.payload?.settings ?? {}, message.payload?.fonts ?? [], message.payload?.uiLanguages ?? []);
          break;
        case "playerSessions":
          renderAvailablePlayerSessions(message.payload);
          break;
        case "playerSourceActionResult":
          pendingPlayerSourceAction = null;
          playerInfoBusy = false;
          if (playerDrawerMode === "add") $("#playerDiscoveryStatus").textContent = message.payload?.message ?? window.taskbarLyricsI18n.t("UI_OperationIncompletePleaseRetry");
          else showToast(message.payload?.message ?? window.taskbarLyricsI18n.t("UI_OperationIncompletePleaseRetry"));
          updateAddPlayerButton();
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

    function applyInitialPage(pageId) {
      const pages = $$("[data-page]");
      const nextPage = pages.find(page => page.dataset.page === pageId);
      if (!nextPage) return;
      pages.forEach(page => {
        page.classList.remove("transitioning");
        page.classList.toggle("active", page === nextPage);
        page.style.transform = "";
      });
      setPageInteractionState(pages, nextPage);
      $$("[data-nav]").forEach(button => {
        const active = button.dataset.nav === pageId;
        button.classList.toggle("active", active);
        if (active) button.setAttribute("aria-current", "page");
        else button.removeAttribute("aria-current");
      });
      $("#pageTitle").textContent = pageMeta[pageId][0];
      $("#pageSubtitle").textContent = pageMeta[pageId][1];
    }

    initialPageId = typeof window.taskbarLyricsInitialPage === "string" ? window.taskbarLyricsInitialPage : "";
    if (pageMeta[initialPageId]) applyInitialPage(initialPageId);
    document.documentElement.removeAttribute("data-settings-boot");
    initializeGettingStarted();
    initializeFallbackRules();
    window.settingsApp = { receive };
    setPageInteractionState($$("[data-page]"), document.querySelector("[data-page].active"));
    bridge.post({ type: "ready" });
