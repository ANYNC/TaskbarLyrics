(function () {
  "use strict";

  // Floating-window control panel. The host owns the native window geometry; this
  // module owns the expanded layout, the surface layering and the hover timing.
  const PANEL_BASE_HEIGHT = 48;
  const PANEL_INSET = 4;
  const PANEL_GAP = 4;
  const COLLAPSE_DELAY_MS = 1500;
  const DEPTH_LAYER_STEP = 0.05;

  const layout = document.getElementById("layout");
  const panelEl = document.getElementById("floatingPanel");
  const titleEl = document.getElementById("panelTitle");
  const artistEl = document.getElementById("panelArtist");
  const seekEl = document.getElementById("panelSeek");
  const elapsedEl = document.getElementById("panelElapsed");
  const durationEl = document.getElementById("panelDuration");
  const previousEl = document.getElementById("panelPrevious");
  const playPauseEl = document.getElementById("panelPlayPause");
  const nextEl = document.getElementById("panelNext");
  const playIconEl = document.getElementById("panelPlayIcon");
  const pauseIconEl = document.getElementById("panelPauseIcon");

  if (!layout || !panelEl || !seekEl) {
    return;
  }

  let enabled = false;
  let surfaceTone = 0;
  let themeLight = false;
  let scaleFactor = 1;
  let panelHeight = PANEL_BASE_HEIGHT;
  let expandRequested = false;
  let expanded = false;
  let direction = "down";
  let collapseTimer = 0;
  let hasTrack = false;
  let isPlaying = false;
  let translationEnabled = false;
  let positionSeconds = 0;
  let durationSeconds = 0;
  let canSeek = false;

  function post(type, payload) {
    window.taskbarLyricsBridge?.post(type, payload);
  }

  function formatTime(seconds) {
    const total = Number.isFinite(seconds) && seconds > 0 ? Math.floor(seconds) : 0;
    const minutes = Math.floor(total / 60);
    return `${minutes}:${String(total % 60).padStart(2, "0")}`;
  }

  function toRgba(red, green, blue, alpha) {
    const clamped = Math.max(0, Math.min(1, alpha));
    return `rgba(${red}, ${green}, ${blue}, ${clamped.toFixed(3)})`;
  }

  /**
   * Expanded surfaces always carry their own background: a nearly transparent one is
   * deepened to 80% so the panel keeps contrast, while an already dense surface keeps
   * its tone. The outer frame stays 5% lighter in density to separate the two layers.
   */
  function resolveExpandedSurfaces(tone) {
    // A transparent surface (tone 0) falls back to the interface theme colour, so the
    // light theme deepens towards white and the dark theme towards black.
    const channelIsBlack = tone === 0 ? !themeLight : tone > 0;
    const density = Math.abs(tone) / 100;
    const inner = density >= 0.8
      ? Math.min(1, density)
      : 0.8;
    const red = channelIsBlack ? 0 : 255;
    const green = channelIsBlack ? 0 : 255;
    const blue = channelIsBlack ? 0 : 255;
    return {
      inner: toRgba(red, green, blue, inner),
      frame: toRgba(red, green, blue, inner - DEPTH_LAYER_STEP)
    };
  }

  function applySurfaceColors() {
    const root = document.documentElement;
    const surfaces = resolveExpandedSurfaces(surfaceTone);
    root.style.setProperty("--surface-color-expanded", surfaces.inner);
    root.style.setProperty("--surface-color-frame", surfaces.frame);
    root.style.setProperty("--panel-inset", `${PANEL_INSET}px`);
    root.style.setProperty("--panel-gap", `${PANEL_GAP}px`);
    root.style.setProperty("--panel-height", `${panelHeight}px`);
    root.style.setProperty(
      "--panel-growth",
      `${PANEL_INSET + PANEL_GAP + panelHeight}px`);
  }

  function clearCollapseTimer() {
    if (collapseTimer) {
      window.clearTimeout(collapseTimer);
      collapseTimer = 0;
    }
  }

  function applyExpandedClasses() {
    layout.classList.toggle("panel-expanded", expanded);
    layout.classList.toggle("panel-above", expanded && direction === "up");
  }

  /**
   * The page owns the panel direction so the lyrics never shift: the fixed shell height
   * and the class change land before the host resizes the window, in one frame.
   */
  function resolveDirection(growth) {
    const viewportHeight = Number(window.innerHeight) || 0;
    const screenTop = Number(window.screenY) || 0;
    const availableBottom = Number(window.screen?.availTop || 0) + (Number(window.screen?.availHeight) || 0);
    const spaceBelow = availableBottom - (screenTop + viewportHeight);
    return spaceBelow >= growth ? "down" : "up";
  }

  function requestExpand() {
    if (!enabled || expandRequested) {
      return;
    }

    expandRequested = true;
    direction = resolveDirection(PANEL_INSET + PANEL_GAP + panelHeight);
    // Pin the lyrics to their current height before the window grows, so the extra
    // space is added around them instead of stretching them.
    layout.style.setProperty("--panel-shell-height", `${Math.max(1, layout.clientHeight)}px`);
    expanded = true;
    applyExpandedClasses();
    post("floatingPanelToggle", {
      open: true,
      height: panelHeight,
      inset: PANEL_INSET,
      gap: PANEL_GAP,
      direction
    });
  }

  function collapseNow() {
    clearCollapseTimer();
    expandRequested = false;
    expanded = false;
    layout.classList.remove("panel-closing");
    layout.style.removeProperty("--panel-shell-height");
    applyExpandedClasses();
  }

  function requestCollapse() {
    if (!enabled || !expandRequested) {
      return;
    }

    expandRequested = false;
    // Animate the panel out but keep the reserved lyrics height until the host has
    // shrunk the window, so the lyrics stay put during the whole transition.
    layout.classList.add("panel-closing");
    window.setTimeout(() => {
      if (expandRequested) {
        return;
      }

      post("floatingPanelToggle", {
        open: false,
        height: panelHeight,
        inset: PANEL_INSET,
        gap: PANEL_GAP,
        direction
      });
    }, 240);
  }

  function onPointerEnter() {
    clearCollapseTimer();
    requestExpand();
  }

  function onPointerLeave() {
    if (!expanded) {
      return;
    }

    clearCollapseTimer();
    collapseTimer = window.setTimeout(() => {
      collapseTimer = 0;
      requestCollapse();
    }, COLLAPSE_DELAY_MS);
  }

  function sendAction(action) {
    if (!enabled) {
      return;
    }

    post("floatingPanelAction", { action });
    clearCollapseTimer();
  }

  function updatePlaybackArt() {
    if (playIconEl) {
      // SVG elements have no `hidden` IDL property, so toggle a class instead.
      playIconEl.classList.toggle("is-hidden", isPlaying);
    }

    if (pauseIconEl) {
      pauseIconEl.classList.toggle("is-hidden", !isPlaying);
    }

    if (playPauseEl) {
      playPauseEl.setAttribute("aria-label", isPlaying ? "暂停" : "播放");
    }

    if (previousEl) {
      previousEl.disabled = !hasTrack;
    }

    if (nextEl) {
      nextEl.disabled = !hasTrack;
    }

    if (playPauseEl) {
      playPauseEl.disabled = !hasTrack;
    }
  }

  function updateTimelineUi() {
    if (seekEl) {
      seekEl.disabled = !canSeek;
      seekEl.value = durationSeconds > 0
        ? String(Math.round(Math.max(0, Math.min(1, positionSeconds / durationSeconds)) * 1000))
        : "0";
    }

    if (elapsedEl) {
      elapsedEl.textContent = formatTime(positionSeconds);
    }

    if (durationEl) {
      durationEl.textContent = formatTime(durationSeconds);
    }
  }

  previousEl?.addEventListener("click", () => sendAction("previous"));
  nextEl?.addEventListener("click", () => sendAction("next"));
  playPauseEl?.addEventListener("click", () => sendAction("playPause"));
  seekEl.addEventListener("change", () => {
    if (!canSeek || durationSeconds <= 0) {
      return;
    }

    sendAction("seek");
    post("floatingPanelSeek", {
      position: (Number(seekEl.value) / 1000) * durationSeconds
    });
  });
  seekEl.addEventListener("input", () => {
    if (elapsedEl && durationSeconds > 0) {
      const previewSeconds = (Number(seekEl.value) / 1000) * durationSeconds;
      elapsedEl.textContent = formatTime(previewSeconds);
    }
  });

  layout.addEventListener("mouseenter", onPointerEnter);
  layout.addEventListener("mouseleave", onPointerLeave);

  updatePlaybackArt();
  updateTimelineUi();

  window.taskbarLyricsFloatingPanel = {
    applyStyle(payload) {
      if (!payload || typeof payload !== "object") {
        return;
      }

      const scale = Number(payload.layoutScalePercent);
      scaleFactor = Number.isFinite(scale) && scale > 0 ? scale / 100 : 1;
      panelHeight = Math.round(PANEL_BASE_HEIGHT * scaleFactor);
      surfaceTone = Number(payload.surfaceTone) || 0;
      themeLight = payload.themeLight === true;
      enabled = payload.floatingPanel === true;
      applySurfaceColors();
      layout.classList.toggle("panel-unavailable", !enabled);
      if (!enabled) {
        // The host collapses the geometry itself when the panel becomes unavailable.
        collapseNow();
      }
    },
    applyTrack(payload) {
      if (!payload || typeof payload !== "object") {
        return;
      }

      hasTrack = payload.hasTrack === true;
      isPlaying = payload.isPlaying === true;
      translationEnabled = payload.translationEnabled === true;
      if (titleEl) {
        titleEl.textContent = hasTrack ? (payload.title || "未知曲目") : "暂无播放";
      }

      if (artistEl) {
        artistEl.textContent = hasTrack ? (payload.artist || "") : "当前没有播放内容";
      }

      panelEl.dataset.translation = translationEnabled ? "on" : "off";
      updatePlaybackArt();
    },
    applyTimeline(payload) {
      if (!payload || typeof payload !== "object") {
        return;
      }

      positionSeconds = Number(payload.position) || 0;
      durationSeconds = Number(payload.duration) || 0;
      canSeek = payload.canSeek === true;
      isPlaying = payload.isPlaying === true;
      updateTimelineUi();
      updatePlaybackArt();
    },
    applyLayout(payload) {
      if (!payload || typeof payload !== "object") {
        return;
      }

      if (payload.open === true) {
        direction = payload.direction === "up" ? "up" : "down";
        expanded = enabled;
        expandRequested = expanded;
        applyExpandedClasses();
        return;
      }

      collapseNow();
    },
    /** Forces the collapsed state without telling the host (it already knows). */
    reset() {
      collapseNow();
    },
    get expanded() {
      return expanded;
    },
    get panelHeight() {
      return panelHeight;
    }
  };
})();
