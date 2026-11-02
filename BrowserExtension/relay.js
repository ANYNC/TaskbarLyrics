(() => {
  const pageOrigin = location.origin === "null" ? window.origin : location.origin;
  if (typeof pageOrigin !== "string" || !/^https?:\/\//.test(pageOrigin)) return;
  if (globalThis.__taskbarLyricsRelay) return;
  globalThis.__taskbarLyricsRelay = true;
  let configuration = { enabled: false, sites: [] };
  const pending = new Map();
  let stopped = false;
  const sendConfiguration = () => window.postMessage({ channel: "taskbarlyrics-config-v1",
    enabled: configuration.enabled && configuration.sites.some(site => site.enabled && site.origin === pageOrigin) }, pageOrigin);
  function stopInvalidContext(error) {
    if (!String(error?.message).includes("Extension context invalidated")) return;
    stopped = true;
    configuration = { enabled: false, sites: [] };
    window.removeEventListener("message", onPageMessage);
    for (const finish of [...pending.values()]) finish({ ok: false, status: "rejected", reason: "context-invalidated" });
    sendConfiguration();
  }
  function sendRuntime(message) {
    if (stopped) return Promise.resolve(null);
    try { return chrome.runtime.sendMessage(message).catch(error => { stopInvalidContext(error); return null; }); }
    catch (error) { stopInvalidContext(error); return Promise.resolve(null); }
  }
  chrome.runtime.onMessage.addListener((message, _sender, respond) => {
    if (message?.type === "configuration") { configuration = message.configuration; sendConfiguration(); return false; }
    if (message?.type !== "command") return false;
    const id = message.payload?.requestId;
    if (typeof id !== "string" || pending.size >= 16) { respond({ ok: false }); return false; }
    if (pending.has(id)) { respond({ ok: false, status: "rejected", reason: "duplicate-request" }); return false; }
    const timer = setTimeout(() => { pending.delete(id); respond({ ok: false, status: "unconfirmed", reason: "timeout" }); }, 2000);
    pending.set(id, result => { clearTimeout(timer); pending.delete(id); respond(result); });
    window.postMessage({ channel: "taskbarlyrics-command-v1", payload: message.payload }, pageOrigin);
    return true;
  });
  function onPageMessage(event) {
    if (event.source !== window || event.origin !== pageOrigin) return;
    if (event.data?.channel === "taskbarlyrics-ready-v1") sendConfiguration();
    if (event.data?.channel === "taskbarlyrics-result-v1") pending.get(event.data.requestId)?.({ ok: event.data.ok === true,
      status: event.data.status, reason: event.data.reason, observed: event.data.observed });
    if (event.data?.channel === "taskbarlyrics-media-v1") {
      const payload = event.data.payload;
      if (payload?.details && Number.isFinite(event.data.sampledAt)) payload.details.sampleAgeSeconds = Math.max(0, (performance.now() - event.data.sampledAt) / 1000);
      void sendRuntime({ type: "media", payload, sentAt: Date.now() });
    }
  }
  window.addEventListener("message", onPageMessage);
  sendRuntime({ type: "configuration" }).then(response => {
    if (stopped) return;
    if (response?.configuration) configuration = response.configuration;
    sendConfiguration();
  }).catch(() => {});
})();
