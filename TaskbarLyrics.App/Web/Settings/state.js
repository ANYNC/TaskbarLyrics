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
        page: previousState?.page ?? "sources",
        foregroundColor,
        trackOffsetSourceFilter: previousState?.trackOffsetSourceFilter ?? "All",
        trackOffsetSort: previousState?.trackOffsetSort ?? "updated"
      };
    }
  };
})();
