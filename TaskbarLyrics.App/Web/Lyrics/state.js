(() => {
  window.taskbarLyricsState = {
    normalizeWeight(weight) {
      return ["Light", "Normal", "Medium", "SemiBold", "Bold"].includes(weight) ? weight : "SemiBold";
    }
  };
})();
