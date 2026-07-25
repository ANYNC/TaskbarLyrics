(() => {
  window.taskbarLyricsBridge = {
    post(type, payload) {
      window.chrome?.webview?.postMessage(JSON.stringify({
        version: 1,
        type,
        payload
      }));
    }
  };
})();
