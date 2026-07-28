(() => {
  const VERSION = 1;

  function toPayload(message) {
    if (message.type === "update" || message.type === "previewUpdate") {
      return { key: message.key, value: message.value };
    }
    return Object.prototype.hasOwnProperty.call(message, "value") ? message.value : {};
  }

  window.taskbarLyricsBridge = {
    post(message) {
      if (!message?.type) return;
      window.chrome?.webview?.postMessage(JSON.stringify({
        version: VERSION,
        type: message.type,
        payload: toPayload(message)
      }));
    }
  };
})();
