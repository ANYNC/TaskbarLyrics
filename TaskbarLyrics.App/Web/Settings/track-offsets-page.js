(() => {
  window.taskbarLyricsTrackOffsets = {
    normalize(value, fallback = 0) {
      const numeric = Number(value);
      if (!Number.isFinite(numeric)) return fallback;
      return Math.max(-5000, Math.min(5000, Math.round(numeric / 10) * 10));
    },
    formatDuration(seconds) {
      const value = Math.max(0, Number(seconds) || 0);
      if (!value) return "时长未知";
      const minutes = Math.floor(value / 60);
      return `${String(minutes).padStart(2, "0")}:${String(Math.round(value % 60)).padStart(2, "0")}`;
    }
  };
})();
