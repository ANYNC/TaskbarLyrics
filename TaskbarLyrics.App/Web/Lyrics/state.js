(() => {
  window.taskbarLyricsState = {
    normalizeWeight(weight) {
      switch (String(weight || "").trim().toLowerCase()) {
        case "light": return "300";
        case "medium": return "500";
        case "semibold": return "600";
        case "bold": return "700";
        default: return "500";
      }
    }
  };
})();
