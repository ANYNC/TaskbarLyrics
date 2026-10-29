(() => {
  const labels = {
    disabled: window.taskbarLyricsI18n.t("UI_Off"),
    unassigned: window.taskbarLyricsI18n.t("UI_NotSet"),
    invalid: window.taskbarLyricsI18n.t("UI_InvalidCombination"),
    duplicate: window.taskbarLyricsI18n.t("UI_DuplicateShortcut"),
    registered: window.taskbarLyricsI18n.t("UI_Registered"),
    occupied: window.taskbarLyricsI18n.t("UI_InUseByWindowsOrAnotherApp"),
    notRegistered: window.taskbarLyricsI18n.t("UI_NotRegistered")
  };

  window.taskbarLyricsHotkeys = {
    label(state) {
      return labels[state] ?? labels.notRegistered;
    },
    visualState(state) {
      return state === "registered" ? "ready" : ["disabled", "unassigned"].includes(state) ? "off" : "warning";
    }
  };
})();
