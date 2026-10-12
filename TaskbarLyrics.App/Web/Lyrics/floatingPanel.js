(function () {
  "use strict";

  // Floating-window control panel. The host owns the native window geometry and drives
  // the reveal progress frame by frame, so the panel height and the window height always
  // advance together; this module owns the surface layering and the hover timing.
  const PANEL_BASE_HEIGHT = 48;
  const PANEL_INSET = 4;
  const PANEL_GAP = 4;
  const COLLAPSE_DELAY_MS = 1500;
  const REVEAL_DURATION_MS = 200;
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
  let panelHeight = PANEL_BASE_HEIGHT;
  let expandRequested = false;
  let expanded = false;
  let revealDepth = false;
  // True while the surface keeps the panel slot reserved: the lyrics stay pinned to their
  // resting height and the panel owns the extra window height.
  let slotReserved = false;
  // Resting surface height, recorded with the resting lyrics pin so a release can wait for
  // the viewport to actually shrink back to it.
  let restingViewportHeight = 0;
  let releasePending = false;
  let collapseTimer = 0;
  let hasTrack = false;
  let isPlaying = false;
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
   * deepened to 80% so the panel keeps contrast, while an already dense surface keeps its
   * tone. The outer frame stays 5% lighter in density to separate the two layers. A
   * transparent surface follows the interface theme colour instead.
   */
  function resolveExpandedSurfaces(tone) {
    const channelIsBlack = tone === 0 ? !themeLight : tone > 0;
    const density = Math.abs(tone) / 100;
    const inner = density >= 0.8 ? Math.min(1, density) : 0.8;
    const frame = Math.max(0, inner - DEPTH_LAYER_STEP);
    const channel = channelIsBlack ? 0 : 255;
    return {
      // The lyrics container sits *inside* the frame container, so its own alpha composites
      // over the frame's instead of replacing it: solve for the alpha that lands the two
      // layers on `inner`, otherwise the lyrics area reads far denser than the frame around
      // it (80% over 75% is 95%, not 80%).
      inner: toRgba(channel, channel, channel, frame >= 1 ? 1 : (inner - frame) / (1 - frame)),
      frame: toRgba(channel, channel, channel, frame)
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
    root.style.setProperty("--panel-duration", `${REVEAL_DURATION_MS}ms`);
  }

  /**
   * The host asks for the reveal and this module answers with an acknowledgement once the
   * panel is laid out below the lyrics; only then does the host grow the window, so the
   * reveal cannot stretch the lyrics for a frame.
   */
  function applyReveal(open) {
    expanded = open;
    expandRequested = open;
    revealDepth = open;
    // A reveal always wins over a pending release, and it keeps the slot reserved until the
    // host reports the window has shrunk again.
    releasePending = false;
    if (open) {
      slotReserved = true;
    }

    applyExpandedClasses();
    post("floatingPanelLayoutApplied", { open });
  }

  /**
   * Drops the panel slot, once the surface really is back at its resting size: the host
   * reports the window is collapsed as soon as it has shrunk it, but the WebView only sees
   * the matching viewport a frame or two later, and unpinning in between stretched the
   * lyrics into the height the surface still had. The panel height is left alone while
   * retracting as well — the host drives it frame by frame, and zeroing it here made the
   * panel vanish for a frame before the first frame report put it back.
   */
  function releaseSlot(force = false) {
    if (!force && slotReserved && restingViewportHeight > 0 &&
      window.innerHeight > restingViewportHeight + 1) {
      releasePending = true;
      return;
    }

    releasePending = false;
    clearCollapseTimer();
    expanded = false;
    expandRequested = false;
    revealDepth = false;
    slotReserved = false;
    applyExpandedClasses();
    document.documentElement.style.setProperty("--panel-progress-height", "0px");
  }

  function measureRestingShellHeight() {
    // Measure the resting lyrics height while the surface is collapsed, then pin it: any
    // window growth belongs to the panel slot, so the lyrics never stretch or shift while
    // the reveal animates. Measuring after the frame padding is applied matters — before
    // that the shell still spans the full window height.
    const shell = layout.querySelector(".shell");
    const measured = shell ? Math.round(shell.getBoundingClientRect().height) : 0;
    if (measured > 0) {
      document.documentElement.style.setProperty("--panel-shell-height", `${measured}px`);
      restingViewportHeight = Math.round(window.innerHeight);
    }
  }

  function clearCollapseTimer() {
    if (collapseTimer) {
      window.clearTimeout(collapseTimer);
      collapseTimer = 0;
    }
  }

  function applyExpandedClasses() {
    // The slot outlives the reveal: it is released when the host reports the window is back
    // at its resting size, not when the retract starts.
    layout.classList.toggle("panel-expanded", slotReserved);
    // The deepening fades with the reveal: it turns on as soon as the panel starts and off
    // as soon as the retract starts, so opening and closing follow the same path.
    layout.classList.toggle("panel-revealed", revealDepth);
  }

  /**
   * The host reports the reveal state
   * (the page only has an estimate of its own screen position, which lagged a drag).
   */
  function requestExpand() {
    if (!enabled || expandRequested) {
      return;
    }

    expandRequested = true;
    post("floatingPanelToggle", {
      open: true,
      height: panelHeight,
      inset: PANEL_INSET,
      gap: PANEL_GAP
    });
  }

  function collapseNow() {
    releaseSlot(true);
  }

  function requestCollapse() {
    if (!enabled || !expandRequested) {
      return;
    }

    // The host owns the window size and drives the retract frame by frame from here. The
    // panel slot stays reserved until the host reports the window has shrunk, otherwise the
    // lyrics stretch into the height the window still has.
    applyReveal(false);
    post("floatingPanelToggle", {
      open: false,
      height: panelHeight,
      inset: PANEL_INSET,
      gap: PANEL_GAP
    });
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
      playPauseEl.disabled = !hasTrack;
    }

    if (previousEl) {
      previousEl.disabled = !hasTrack;
    }

    if (nextEl) {
      nextEl.disabled = !hasTrack;
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

    post("floatingPanelSeek", {
      position: (Number(seekEl.value) / 1000) * durationSeconds
    });
  });
  seekEl.addEventListener("input", () => {
    if (elapsedEl && durationSeconds > 0) {
      elapsedEl.textContent = formatTime((Number(seekEl.value) / 1000) * durationSeconds);
    }
  });

  layout.addEventListener("mouseenter", onPointerEnter);
  layout.addEventListener("mouseleave", onPointerLeave);
  // The surface shrinks a frame or two after the host reports it done; that resize is the
  // moment a pending release can be applied without stretching the lyrics.
  window.addEventListener("resize", () => {
    if (releasePending) {
      releaseSlot();
    }
  });

  updatePlaybackArt();
  updateTimelineUi();

  window.taskbarLyricsFloatingPanel = {
    applyStyle(payload) {
      if (!payload || typeof payload !== "object") {
        return;
      }

      const scale = Number(payload.layoutScalePercent);
      panelHeight = Math.round(PANEL_BASE_HEIGHT * (Number.isFinite(scale) && scale > 0 ? scale / 100 : 1));
      surfaceTone = Number(payload.surfaceTone) || 0;
      themeLight = payload.themeLight === true;
      enabled = payload.floatingPanel === true;
      applySurfaceColors();
      layout.classList.toggle("panel-capable", enabled);
      layout.classList.toggle("panel-unavailable", !enabled);
      if (!enabled) {
        // Leaving the floating surface must not leave the lyrics pinned to a panel slot, no
        // matter what the surface height is doing.
        releaseSlot(true);
      } else if (!slotReserved) {
        // Measure the resting lyrics height (the frame padding is applied by now) and pin
        // it: window growth belongs to the panel slot, so the lyrics neither stretch nor
        // shift while the reveal animates.
        measureRestingShellHeight();
      }
    },
    applyTrack(payload) {
      if (!payload || typeof payload !== "object") {
        return;
      }

      hasTrack = payload.hasTrack === true;
      isPlaying = payload.isPlaying === true;
      if (titleEl) {
        titleEl.textContent = hasTrack ? (payload.title || "未知曲目") : "暂无播放";
      }

      if (artistEl) {
        artistEl.textContent = hasTrack ? (payload.artist || "") : "当前没有播放内容";
      }

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

      if (payload.open === true && enabled) {
        // The host answers before it grows the window; reserving the slot now is what keeps
        // the lyrics from stretching while the window grows around them, and the host then
        // drives the panel height frame by frame.
        applyReveal(true);
        return;
      }

      // The retract ends here: the window is back at its resting size, so the lyrics may
      // fill the surface again.
      releaseSlot();
    },
    /**
     * Reveal progress driven by the host, so the panel and the window are always the same
     * fraction of the animation (a CSS-only transition ran on its own clock and the panel
     * outran the window it lives in).
     */
    applyProgress(value) {
      const progress = Math.max(0, Math.min(1, Number(value) || 0));
      document.documentElement.style.setProperty(
        "--panel-progress-height",
        `${Math.round(panelHeight * progress)}px`);
    },
    /**
     * Pointer presence reported by the host. The floating surface only becomes hit-testable
     * while the cursor is over it (the host adds its invisible fill then), so a cursor that
     * arrives without moving afterwards cannot raise a DOM enter event by itself. Leaving is
     * reported for the same reason, and both paths are idempotent with the DOM events.
     */
    setPointerInside(inside) {
      if (inside) {
        onPointerEnter();
        return;
      }

      onPointerLeave();
    },
    reset() {
      collapseNow();
    }
  };
})();
