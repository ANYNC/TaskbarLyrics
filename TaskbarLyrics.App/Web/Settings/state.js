(() => {
  const textAlignments = new Set(["Left", "Center", "Right"]);

  function normalizeLyricsTextAlignment(value) {
    return textAlignments.has(value) ? value : "Left";
  }

  window.taskbarLyricsSettingsState = {
    create(nextState, previousState, foregroundColor) {
      return {
        ...nextState,
        lyricsTextAlignment: normalizeLyricsTextAlignment(nextState?.lyricsTextAlignment),
        coverPosition: nextState?.coverPosition === "Right" ? "Right" : "Left",
        floatingPanelBrightness: Number.isFinite(Number(nextState?.floatingPanelBrightness)) ? Math.min(1, Math.max(0, Number(nextState.floatingPanelBrightness))) : 0.45,
        floatingPanelTintOpacity: Number.isFinite(Number(nextState?.floatingPanelTintOpacity)) ? Math.min(1, Math.max(0, Number(nextState.floatingPanelTintOpacity))) : 0.55,
        floatingPanelBlurAmount: Number.isFinite(Number(nextState?.floatingPanelBlurAmount)) ? Math.min(64, Math.max(0, Number(nextState.floatingPanelBlurAmount))) : 24,
        page: previousState?.page ?? "sources",
        foregroundColor,
        trackOffsetSourceFilter: previousState?.trackOffsetSourceFilter ?? "All",
        trackOffsetSort: previousState?.trackOffsetSort ?? "updated"
      };
    }
  };
})();
