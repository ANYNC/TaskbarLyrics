(() => {
  const labels = {
    disabled: "已关闭",
    invalid: "组合无效",
    duplicate: "与其他快捷键重复",
    registered: "已注册",
    occupied: "已被系统或其他应用占用",
    notRegistered: "未注册"
  };

  window.taskbarLyricsHotkeys = {
    label(state) {
      return labels[state] ?? labels.notRegistered;
    },
    visualState(state) {
      return state === "registered" ? "ready" : state === "disabled" ? "off" : "warning";
    }
  };
})();
