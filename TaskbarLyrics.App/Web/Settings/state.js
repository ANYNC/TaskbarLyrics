(() => {
  window.taskbarLyricsSettingsState = {
    create(nextState, previousState, foregroundColor) {
      return {
        ...nextState,
        page: previousState?.page ?? "sources",
        foregroundColor,
        trackOffsetSourceFilter: previousState?.trackOffsetSourceFilter ?? "All",
        trackOffsetSort: previousState?.trackOffsetSort ?? "updated"
      };
    }
  };
})();
