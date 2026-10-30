(() => {
  const textAlignments = new Set(["Left", "Center", "Right"]);
  const panelMaterialDefaults = Object.freeze({
    embeddedPanelBrightness: 0.20,
    embeddedPanelTintOpacity: 0.65,
    embeddedPanelBlurAmount: 24,
    floatingPanelBrightness: 0.25,
    floatingPanelTintOpacity: 0.55,
    floatingPanelBlurAmount: 32
  });

  function normalizeLyricsTextAlignment(value) {
    return textAlignments.has(value) ? value : "Left";
  }

  function normalizeControlPanelTriggers(state) {
    state.controlPanelTriggerMethod = state.controlPanelTriggerMethod === "Hover" ? "Hover" : "Click";
    state.controlPanelTriggerCover = state.controlPanelTriggerCover !== false;
    state.controlPanelTriggerLyrics = state.controlPanelTriggerLyrics === true;
    if (!state.controlPanelTriggerLyrics && (!state.controlPanelTriggerCover || state.showCover === false)) {
      if (state.showCover === false) state.controlPanelTriggerLyrics = true;
      else state.controlPanelTriggerCover = true;
    }
    return state;
  }

  window.taskbarLyricsSettingsState = {
    panelMaterialDefaults,
    normalizeControlPanelTriggers,
    create(nextState, previousState, foregroundColor) {
      return normalizeControlPanelTriggers({
        ...nextState,
        floatingWindowLayer: ["Desktop", "Window", "Fullscreen"].includes(nextState?.floatingWindowLayer)
          ? nextState.floatingWindowLayer : nextState?.forceAlwaysOnTop === false ? "Window" : "Fullscreen",
        floatingWindowWidth: nextState?.floatingWindowWidth ?? nextState?.windowWidth ?? 420,
        enableFloatingControlPanel: nextState?.enableFloatingControlPanel ?? nextState?.enableControlPanel ?? true,
        showFloatingBackground: nextState?.showFloatingBackground === true,
        floatingLyricsLayoutScalePercent: nextState?.floatingLyricsLayoutScalePercent ?? nextState?.lyricsLayoutScalePercent ?? 100,
        lyricsTextAlignment: normalizeLyricsTextAlignment(nextState?.lyricsTextAlignment),
        singleLineLyrics: nextState?.singleLineLyrics === true,
        showLyricSearchStatus: nextState?.showLyricSearchStatus !== false,
        taskbarAvoidanceEnabled: nextState?.taskbarAvoidanceEnabled !== false,
        coverPosition: nextState?.coverPosition === "Right" ? "Right" : "Left",
        embeddedPanelBrightness: Number.isFinite(Number(nextState?.embeddedPanelBrightness)) ? Math.min(1, Math.max(0, Number(nextState.embeddedPanelBrightness))) : panelMaterialDefaults.embeddedPanelBrightness,
        embeddedPanelTintOpacity: Number.isFinite(Number(nextState?.embeddedPanelTintOpacity)) ? Math.min(1, Math.max(0, Number(nextState.embeddedPanelTintOpacity))) : panelMaterialDefaults.embeddedPanelTintOpacity,
        embeddedPanelBlurAmount: Number.isFinite(Number(nextState?.embeddedPanelBlurAmount)) ? Math.min(64, Math.max(0, Number(nextState.embeddedPanelBlurAmount))) : panelMaterialDefaults.embeddedPanelBlurAmount,
        floatingPanelBrightness: Number.isFinite(Number(nextState?.floatingPanelBrightness)) ? Math.min(1, Math.max(0, Number(nextState.floatingPanelBrightness))) : panelMaterialDefaults.floatingPanelBrightness,
        floatingPanelTintOpacity: Number.isFinite(Number(nextState?.floatingPanelTintOpacity)) ? Math.min(1, Math.max(0, Number(nextState.floatingPanelTintOpacity))) : panelMaterialDefaults.floatingPanelTintOpacity,
        floatingPanelBlurAmount: Number.isFinite(Number(nextState?.floatingPanelBlurAmount)) ? Math.min(64, Math.max(0, Number(nextState.floatingPanelBlurAmount))) : panelMaterialDefaults.floatingPanelBlurAmount,
        page: previousState?.page ?? "sources",
        foregroundColor,
        trackOffsetSourceFilter: previousState?.trackOffsetSourceFilter ?? "All",
        trackOffsetSort: previousState?.trackOffsetSort ?? "updated"
      });
    }
  };
})();
