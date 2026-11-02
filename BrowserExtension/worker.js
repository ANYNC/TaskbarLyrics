import { HOST_NAME, PROTOCOL_VERSION, originOf, senderOrigin, permissionPattern, allowedOrigin, normaliseMedia } from "./rules.js";

let port = null;
let configuration = { revision: -1, enabled: false, sites: [] };
let syncing = Promise.resolve();
const documents = new Map();
let sequence = 0;
let grantedOrigins = [];
let acceptingMedia = false;
function relayConfiguration() {
  return { ...configuration, sites: configuration.sites.filter(site => grantedOrigins.includes(site.origin)) };
}
let heartbeat;
function post(type, payload) {
  if (!port) return false;
  try { port.postMessage({ version: PROTOCOL_VERSION, type, payload }); return true; }
  catch { return false; }
}
async function connect() {
  if (port) return;
  const stored = await chrome.storage.local.get("clientId");
  const clientId = stored.clientId || crypto.randomUUID();
  if (!stored.clientId) await chrome.storage.local.set({ clientId });
  if (port) return;
  const connected = chrome.runtime.connectNative(HOST_NAME);
  port = connected;
  connected.onMessage.addListener(message => {
    if (port !== connected || message?.version !== PROTOCOL_VERSION || typeof message.payload !== "object") return;
    if (message.type === "configuration") {
      syncing = syncing.then(() => applyConfiguration(message.payload, connected)).catch(error =>
        chrome.storage.local.set({ lastError: error.name || "ConfigurationError" }));
    } else if (message.type === "command") void command(message.payload);
  });
  connected.onDisconnect.addListener(() => {
    const reason = chrome.runtime.lastError?.message || "Disconnected";
    if (port !== connected) return;
    port = null;
    clearInterval(heartbeat);
    configuration = { ...configuration, enabled: false };
    documents.clear();
    grantedOrigins = [];
    acceptingMedia = false;
    syncing = syncing.then(() => syncScripts()).catch(() => {});
    void chrome.storage.local.set({ connected: false, lastError: reason }).catch(() => {});
    setTimeout(() => { connectSafely(); }, 5000);
  });
  post("hello", { clientId }); // browser identity is supplied by the native host, not page data.
  clearInterval(heartbeat);
  heartbeat = setInterval(() => post("heartbeat", {}), 2000);
}

async function applyConfiguration(payload, sourcePort = port) {
  if (!sourcePort || sourcePort !== port || !payload) return;
  if (!Number.isSafeInteger(payload.revision) || typeof payload.enabled !== "boolean" ||
      !Array.isArray(payload.sites) || payload.sites.length > 100) return;
  configuration = { revision: payload.revision, enabled: payload.enabled, mediaDetailsVersion: payload.mediaDetailsVersion === 1 ? 1 : 0,
    sites: payload.sites.filter(site => site && typeof site.id === "string" && originOf(site.origin) === site.origin && typeof site.enabled === "boolean") };
  documents.clear();
  grantedOrigins = [];
  acceptingMedia = false;
  let origins = [];
  let lastError = "";
  try { origins = await syncScripts(); }
  catch (error) { lastError = error.name || "ConfigurationError"; }
  if (!port || sourcePort !== port || !configuration.enabled && payload.enabled) return;
  if (!post("ready", { revision: configuration.revision, origins })) return;
  acceptingMedia = true;
  await chrome.storage.local.set({ connected: true, lastError, configuration, grantedOrigins: origins });
}

async function syncScripts() {
  const registered = await chrome.scripting.getRegisteredContentScripts();
  if (registered.length) await chrome.scripting.unregisterContentScripts({ ids: registered.map(script => script.id) });
  const origins = [];
  if (configuration.enabled) for (const site of configuration.sites.filter(site => site.enabled)) {
    if (await chrome.permissions.contains({ origins: [permissionPattern(site.origin)] })) origins.push(site.origin);
  }
  const matches = [...new Set(origins.map(permissionPattern))];
  if (matches.length) await chrome.scripting.registerContentScripts([
    { id: "taskbarlyrics-relay", matches, js: ["relay.js"], allFrames: true, matchOriginAsFallback: true, runAt: "document_start", world: "ISOLATED" },
    { id: "taskbarlyrics-media", matches, js: ["media-probe.js"], allFrames: true, matchOriginAsFallback: true, runAt: "document_start", world: "MAIN" }
  ]);
  grantedOrigins = origins;
  // Stop old probes even if their permission is no longer configured.
  for (const tab of await chrome.tabs.query({})) {
    try { await chrome.tabs.sendMessage(tab.id, { type: "configuration", configuration: relayConfiguration() }); } catch { /* No relay in this tab. */ }
  }
  if (matches.length) for (const tab of await chrome.tabs.query({ url: matches })) {
    try {
      await chrome.scripting.executeScript({ target: { tabId: tab.id, allFrames: true }, files: ["relay.js"] });
      await chrome.scripting.executeScript({ target: { tabId: tab.id, allFrames: true }, files: ["media-probe.js"], world: "MAIN" });
      await chrome.tabs.sendMessage(tab.id, { type: "configuration", configuration: relayConfiguration() });
    } catch { /* Frames without permission are not eligible sources. */ }
  }
  return origins;
}

chrome.runtime.onMessage.addListener((message, sender, respond) => {
  if (message?.type === "configuration") { respond({ configuration: relayConfiguration() }); return false; }
  if (message?.type === "permissionsChanged" && !sender.tab) {
    syncing = syncing.then(() => applyConfiguration(configuration));
    syncing.then(() => respond({ ok: true }), () => respond({ ok: false }));
    return true;
  }
  if (message?.type === "suggestSite" && !sender.tab) {
    const origin = originOf(message.url);
    respond({ ok: Boolean(origin && post("suggestSite", { origin })) });
    return false;
  }
  const origin = senderOrigin(sender);
  if (message?.type !== "media" || !acceptingMedia || !sender.tab || !sender.documentId ||
      !allowedOrigin(configuration, origin) || !grantedOrigins.includes(origin)) return false;
  const media = normaliseMedia(message.payload);
  if (!media) return false;
  if (media.details) {
    if (Number.isFinite(message.sentAt)) media.details.sampleAgeSeconds += Math.max(0, (Date.now() - message.sentAt) / 1000);
    if (media.details.sampleAgeSeconds > 6) return false;
    if (configuration.mediaDetailsVersion !== 1) delete media.details;
  }
  if (!documents.has(sender.documentId) && documents.size >= 128) return false;
  documents.set(sender.documentId, { tabId: sender.tab.id, frameId: sender.frameId });
  const serial = ++sequence;
  const revision = configuration.revision;
  const publish = audible => {
    // A muted/Web-Audio player can look paused to the page while its tab is
    // still audible. Use the browser's tab flag only as playing evidence for an
    // unknown page state; it never supplies a position or duration.
    if (audible && media.details?.playbackState === "unknown") {
      media.details.playbackState = "playing";
      media.isPlaying = true;
    }
    post("session", { ...media, documentId: sender.documentId, url: origin, sequence: serial, revision });
  };
  if (sender.tab.audible === true) publish(true);
  else try {
    chrome.tabs.get(sender.tab.id, tab => publish(!chrome.runtime.lastError && tab?.audible === true));
  } catch { publish(false); }
  return false;
});

async function command(payload) {
  const target = documents.get(payload.documentId);
  let result = { ok: false, status: "rejected", reason: "target-unavailable" };
  if (target) try {
    const reply = await chrome.tabs.sendMessage(target.tabId, { type: "command", payload }, { documentId: payload.documentId });
    const status = ["observed", "unconfirmed", "rejected"].includes(reply?.status) ? reply.status : "unconfirmed";
    result = { ok: reply?.ok === true && status === "observed", status,
      reason: /^[a-z-]{1,64}$/.test(reply?.reason ?? "") ? reply.reason : "",
      observed: ["playing", "paused", "ended", "seeking", "unknown"].includes(reply?.observed) ? reply.observed : undefined };
  } catch { /* A closed or navigated document must not redirect controls elsewhere. */ }
  post("result", { requestId: payload.requestId, ...result });
}
function removeTab(tabId) {
  for (const [documentId, target] of documents) if (target.tabId === tabId) {
    documents.delete(documentId); post("removed", { documentId });
  }
}
chrome.tabs.onRemoved.addListener(removeTab);
chrome.tabs.onUpdated.addListener((tabId, change) => { if (change.status === "loading") removeTab(tabId); });
chrome.permissions.onRemoved.addListener(() => {
  grantedOrigins = [];
  acceptingMedia = false;
  documents.clear();
  syncing = syncing.then(() => applyConfiguration(configuration)).catch(() => {});
});
chrome.permissions.onAdded.addListener(() => {
  syncing = syncing.then(() => applyConfiguration(configuration)).catch(() => {});
});
chrome.runtime.onStartup.addListener(() => { connectSafely(); });
chrome.runtime.onInstalled.addListener(() => { connectSafely(); });
chrome.alarms.onAlarm.addListener(alarm => { if (alarm.name === "reconnect") connectSafely(); });
void chrome.alarms.create("reconnect", { periodInMinutes: 0.5 }).catch(() => {});
connectSafely();

function connectSafely() { void connect().catch(() => {}); }
