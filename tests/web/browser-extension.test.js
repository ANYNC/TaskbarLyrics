import { readFileSync } from "node:fs";
import { describe, expect, it, vi } from "vitest";
import { JSDOM, catalogs } from "./helpers/localized-dom.js";
import * as rules from "../../BrowserExtension/rules.js";
const { originOf, senderOrigin, permissionPattern, allowedOrigin, normaliseMedia, mediaText } = rules;

const root = new URL("../../BrowserExtension/", import.meta.url);
const read = file => readFileSync(new URL(file, root), "utf8");

describe("browser companion", () => {
  it("passes inherited-origin frame messages through the relay without a null target origin", async () => {
    const dom = new JSDOM("", { url: "about:blank" });
    try {
      const w = dom.window;
      Object.defineProperty(w, "origin", { value: "https://music.test" });
      Object.defineProperty(w.navigator, "mediaSession", { value: { metadata: { title: "Frame Song", artist: "Artist" },
        playbackState: "playing", setActionHandler() {} } });
      const targets = [];
      const packets = [];
      w.postMessage = (data, targetOrigin) => {
        targets.push(targetOrigin);
        w.dispatchEvent(new w.MessageEvent("message", { source: w, origin: "https://music.test", data }));
      };
      w.chrome = { runtime: { onMessage: { addListener() {} }, sendMessage: async message => {
        packets.push(message);
        return { configuration: { enabled: true, sites: [{ enabled: true, origin: "https://music.test" }] } };
      } } };
      w.eval(read("relay.js"));
      w.eval(read("media-probe.js"));
      for (let i = 0; i < 5; i++) await Promise.resolve();
      expect(w.location.origin).toBe("null");
      expect(targets.length).toBeGreaterThan(0);
      expect(targets.every(target => target === "https://music.test")).toBe(true);
      expect(packets.some(message => message.type === "media" && message.payload.title === "Frame Song")).toBe(true);
      // Reinjection must also use the inherited origin in the already-installed branch.
      expect(() => w.eval(read("media-probe.js"))).not.toThrow();
    } finally { dom.window.close(); }
  });

  it("uses trusted browser origins for inherited frames and rejects opaque or conflicting origins", () => {
    expect(senderOrigin({ url: "about:blank", origin: "https://music.test" })).toBe("https://music.test");
    expect(senderOrigin({ url: "about:srcdoc", origin: "https://music.test" })).toBe("https://music.test");
    expect(senderOrigin({ url: "blob:https://music.test/id", origin: "https://music.test" })).toBe("https://music.test");
    expect(senderOrigin({ url: "about:blank", origin: "null", tab: { url: "https://music.test" } })).toBeNull();
    expect(senderOrigin({ url: "about:blank", tab: { url: "https://music.test" } })).toBeNull();
    expect(senderOrigin({ url: "https://music.test", origin: "null" })).toBeNull();
    expect(senderOrigin({ url: "https://other.test", origin: "https://music.test" })).toBeNull();
    expect(senderOrigin({ url: "file:///music", origin: "https://music.test" })).toBeNull();
  });
  it("observes a detached audio player without requiring a declared Media Session playback state", async () => {
    const dom = new JSDOM("", { url: "https://music.test/" });
    try {
      const w = dom.window;
      const sent = [];
      w.document.title = "Detached Song";
      w.postMessage = message => sent.push(message);
      const promise = Promise.resolve();
      w.HTMLMediaElement.prototype.play = function() { this.dispatchEvent(new w.Event("play")); return promise; };
      Object.defineProperty(w.navigator, "mediaSession", { value: { metadata: { title: "Detached Song", artist: "Artist" },
        playbackState: "none", setActionHandler() {} } });
      w.eval(read("media-probe.js"));
      w.dispatchEvent(new w.MessageEvent("message", { source: w, origin: w.location.origin,
        data: { channel: "taskbarlyrics-config-v1", enabled: true } }));
      const audio = new w.Audio();
      Object.defineProperties(audio, { paused: { get: () => false }, duration: { get: () => 180 }, ended: { get: () => false } });
      audio.currentTime = 32;
      expect(audio.play()).toBe(promise);
      await promise;
      const latest = sent.filter(message => message.channel === "taskbarlyrics-media-v1").at(-1).payload;
      expect(w.document.querySelectorAll("audio,video")).toHaveLength(0);
      expect(latest).toMatchObject({ title: "Detached Song", artist: "Artist", isPlaying: true,
        positionSeconds: 32, durationSeconds: 180, canSeek: false,
        details: { source: "associated-element", titleSource: "media-session" } });
    } finally { dom.window.close(); }
  });

  it("reports a Media Session without DOM audio and keeps time, pause, track changes and controls coherent", async () => {
    const dom = new JSDOM("", { url: "https://music.test/" });
    try {
      const w = dom.window;
      w.document.title = "Song";
      let now = 0;
      Object.defineProperty(w.performance, "now", { value: () => now });
      class Session {
        metadata = { title: "Session Song", artist: "Artist", album: "Album" };
        state = "playing";
        get playbackState() { return this.state; }
        set playbackState(value) { if (!["playing", "paused", "none"].includes(value)) throw new TypeError(); this.state = value; }
        setActionHandler() {}
        setPositionState(value) { if (value?.duration < 0) throw new TypeError(); }
      }
      const session = new Session();
      Object.defineProperty(w.navigator, "mediaSession", { value: session });
      const sent = [];
      w.postMessage = message => sent.push(message);
      w.eval(read("media-probe.js"));
      const emit = data => w.dispatchEvent(new w.MessageEvent("message", { source: w, origin: w.location.origin, data }));
      const report = () => { emit({ channel: "taskbarlyrics-config-v1", enabled: true });
        return sent.filter(message => message.channel === "taskbarlyrics-media-v1").at(-1).payload; };
      expect(report()).toMatchObject({ mediaId: "media-session", title: "Session Song", isPlaying: true,
        positionSeconds: 0, durationSeconds: 0, canSeek: false });
      const seek = vi.fn();
      session.setActionHandler("seekto", seek);
      session.setPositionState({ position: 20, duration: 100, playbackRate: 2 });
      now = 1000;
      expect(report()).toMatchObject({ positionSeconds: 22, durationSeconds: 100, canSeek: true });
      session.playbackState = "paused";
      now = 5000;
      expect(report()).toMatchObject({ positionSeconds: 22, isPlaying: false });
      session.playbackState = "playing";
      now = 6000;
      expect(report().positionSeconds).toBe(24);
      expect(() => session.setPositionState({ duration: -1 })).toThrow();
      expect(report().durationSeconds).toBe(100);
      emit({ channel: "taskbarlyrics-command-v1", payload: { requestId: "seek", mediaId: "media-session", action: "SeekTo", seconds: 40 } });
      await Promise.resolve();
      expect(seek).toHaveBeenCalledWith(expect.objectContaining({ action: "seekto", seekTime: 40 }));
      session.metadata = { title: "Next Song", artist: "Other" };
      expect(report()).toMatchObject({ title: "Next Song", positionSeconds: 0, durationSeconds: 0, canSeek: false });
      session.metadata = null;
      const before = sent.filter(message => message.channel === "taskbarlyrics-media-v1").length;
      report();
      expect(sent.filter(message => message.channel === "taskbarlyrics-media-v1")).toHaveLength(before);
      emit({ channel: "taskbarlyrics-command-v1", payload: { requestId: "stale", mediaId: "media-session", action: "SeekTo", seconds: 50 } });
      await Promise.resolve();
      expect(seek).toHaveBeenCalledOnce();
    } finally { dom.window.close(); }
  });
  it("reports a browser permission error in the popup without sending a desktop request", async () => {
    const dom = new JSDOM(read("popup.html"));
    try {
      const sendMessage = vi.fn();
      const request = vi.fn().mockRejectedValue(new Error("Permission unavailable"));
      const chrome = { i18n: { getMessage: key => key }, storage: { local: { get: async () => ({ connected: true, configuration: { sites: [] } }) } },
        tabs: { query: async () => [{ url: "https://music.test/song" }] }, permissions: { request }, runtime: { sendMessage } };
      const AsyncFunction = dom.window.eval("(async function() {}).constructor");
      await new AsyncFunction("chrome", "originOf", "permissionPattern", read("popup.js").replace(/^import .*?;\s*/, ""))(chrome, originOf, permissionPattern);
      dom.window.document.querySelector("#add").click();
      await new Promise(resolve => setTimeout(resolve, 0));
      expect(request).toHaveBeenCalledWith({ origins: ["https://music.test/*"] });
      expect(sendMessage).not.toHaveBeenCalled();
      expect(dom.window.document.querySelector("#status").textContent).toBe("EXT_Retry");
    } finally { dom.window.close(); }
  });
  it("stops accepting existing page reports immediately when website permission is revoked", async () => {
    vi.useFakeTimers();
    try {
      const handlers = {};
      let authorized = true;
      const packets = [];
      const report = { type: "media", payload: { mediaId: "media-1", title: "Song", artist: "Artist", album: "", isPlaying: true,
        positionSeconds: 1, durationSeconds: 100, canSeek: true } };
      const sender = { tab: { id: 1 }, documentId: "doc", frameId: 0, url: "https://music.test/song" };
      const event = name => ({ addListener: callback => { handlers[name] = callback; } });
      const port = { postMessage: packet => packets.push(packet), onMessage: event("nativeMessage"), onDisconnect: event("disconnect") };
      const chrome = {
        storage: { local: { get: async () => ({ clientId: "profile" }), set: async () => {} } },
        runtime: { connectNative: () => port, onMessage: event("runtimeMessage"), onStartup: event("startup"), onInstalled: event("installed") },
        permissions: { contains: async () => authorized, onRemoved: event("permissionRemoved"), onAdded: event("permissionAdded") },
        scripting: { getRegisteredContentScripts: async () => [], unregisterContentScripts: async () => {}, registerContentScripts: vi.fn(async () => {}), executeScript: async () => {} },
        tabs: { query: async () => [{ id: 1 }], sendMessage: async () => {
          handlers.runtimeMessage(report, sender, () => {});
        }, onRemoved: event("tabRemoved"), onUpdated: event("tabUpdated") },
        alarms: { create: async () => {}, onAlarm: event("alarm") }
      };
      const source = read("worker.js").replace(/^import .*?;\s*/, "");
      new Function("chrome", ...Object.keys(rules), source)(chrome, ...Object.values(rules));
      const flush = async () => { for (let i = 0; i < 30; i++) await Promise.resolve(); };
      await flush();
      handlers.nativeMessage({ version: 1, type: "configuration", payload: { revision: 1, enabled: true,
        sites: [{ id: "music", origin: "https://music.test", enabled: true }] } });
      await flush();
      expect(packets.at(-1)).toMatchObject({ type: "ready", payload: { origins: ["https://music.test"] } });
      expect(chrome.scripting.registerContentScripts).toHaveBeenCalledWith(expect.arrayContaining([
        expect.objectContaining({ world: "MAIN", matchOriginAsFallback: true }),
        expect.objectContaining({ world: "ISOLATED", matchOriginAsFallback: true })
      ]));
      expect(packets.filter(packet => packet.type === "session")).toHaveLength(0);
      handlers.runtimeMessage(report, sender, () => {});
      expect(packets.at(-1).type).toBe("session");
      handlers.runtimeMessage(report, { ...sender, documentId: "inherited-doc", url: "about:blank", origin: "https://music.test" }, () => {});
      expect(packets.at(-1)).toMatchObject({ type: "session", payload: { documentId: "inherited-doc", url: "https://music.test" } });
      const count = packets.length;
      authorized = false;
      handlers.permissionRemoved();
      handlers.runtimeMessage(report, sender, () => {});
      expect(packets).toHaveLength(count);
      await flush();
      expect(packets.at(-1)).toMatchObject({ type: "ready", payload: { origins: [] } });
      handlers.runtimeMessage(report, sender, () => {});
      expect(packets.at(-1).type).toBe("ready");
    } finally { vi.clearAllTimers(); vi.useRealTimers(); }
  });
  it("matches exact enabled origins, while browser grants may cover multiple ports", () => {
    const config = { enabled: true, sites: [{ origin: "http://localhost:8096", enabled: true }] };
    expect(originOf("http://localhost:8096/song?secret=private")).toBe("http://localhost:8096");
    expect(permissionPattern(config.sites[0].origin)).toBe("http://localhost/*");
    expect(allowedOrigin(config, "http://localhost:8096/song")).toBe(true);
    expect(allowedOrigin(config, "http://localhost:9000/song")).toBe(false);
    expect(allowedOrigin({ ...config, enabled: false }, config.sites[0].origin)).toBe(false);
    expect(allowedOrigin({ enabled: true, sites: [] }, config.sites[0].origin)).toBe(false);
    expect(originOf("https://user:password@music.test")).toBeNull();
    expect(originOf("file:///music")).toBeNull();
  });

  it("bounds and sanitizes untrusted page metadata", () => {
    const valid = { mediaId: "media-1", title: "Song\nTitle", artist: "A", album: "B", isPlaying: true,
      positionSeconds: 1, durationSeconds: 100, canSeek: true };
    expect(normaliseMedia(valid).title).toBe("Song Title");
    expect(mediaText("a".repeat(511) + "🎵")).toBe("a".repeat(511));
    expect(mediaText("Song\u0085Title")).toBe("Song Title");
    for (const change of [{ positionSeconds: NaN }, { durationSeconds: Infinity }, { mediaId: "x".repeat(65) }, { isPlaying: "true" }, { title: "" }])
      expect(normaliseMedia({ ...valid, ...change })).toBeNull();
  });

  it("keeps generated extension languages synchronized with the canonical catalog", () => {
    const manifest = JSON.parse(read("manifest.json"));
    expect(manifest.default_locale).toBe("en");
    expect(manifest.permissions).not.toContain("tabs");
    expect(manifest.host_permissions).toBeUndefined();
    for (const [desktop, browser] of [["EN", "en"], ["Zh-CN", "zh_CN"], ["Zh-Hant", "zh_TW"]]) {
      const generated = JSON.parse(read(`_locales/${browser}/messages.json`));
      const canonical = Object.fromEntries(Object.entries(catalogs[desktop]).filter(([key]) => key.startsWith("EXT_"))
        .map(([key, message]) => [key, { message }]));
      expect(generated).toEqual(canonical);
    }
  });

  it("reports only after configuration and routes controls to the selected media element", async () => {
    const dom = new JSDOM('<audio></audio><video></video>', { url: "https://music.test/" });
    try {
      const w = dom.window;
      w.document.title = "Song";
      const media = w.document.querySelector("audio");
      Object.defineProperties(media, { paused: { get: () => false }, ended: { get: () => false }, duration: { get: () => 120 } });
      media.currentTime = 10;
      media.pause = vi.fn();
      const handlers = new Map();
      Object.defineProperty(w.navigator, "mediaSession", { value: { metadata: { title: "Song", artist: "Artist" },
        playbackState: "none",
        setActionHandler: (name, handler) => handlers.set(name, handler) } });
      const sent = [];
      w.postMessage = message => sent.push(message);
      w.eval(read("media-probe.js"));
      const emit = data => w.dispatchEvent(new w.MessageEvent("message", { source: w, origin: w.location.origin, data }));
      expect(sent.filter(message => message.channel === "taskbarlyrics-media-v1")).toHaveLength(0);
      emit({ channel: "taskbarlyrics-config-v1", enabled: true });
      const snapshot = sent.find(message => message.channel === "taskbarlyrics-media-v1").payload;
      expect(snapshot.title).toBe("Song");
      expect(snapshot.isPlaying).toBe(true);
      emit({ channel: "taskbarlyrics-command-v1", payload: { requestId: "wrong", mediaId: "stale-id", action: "TogglePlayPause" } });
      for (let i = 0; i < 5; i++) await Promise.resolve();
      expect(media.pause).not.toHaveBeenCalled();
      emit({ channel: "taskbarlyrics-command-v1", payload: { requestId: "right", mediaId: snapshot.mediaId, action: "TogglePlayPause" } });
      await Promise.resolve();
      expect(media.pause).toHaveBeenCalledOnce();
      emit({ channel: "taskbarlyrics-config-v1", enabled: false });
      const count = sent.length;
      media.dispatchEvent(new w.Event("play", { bubbles: true }));
      expect(sent).toHaveLength(count);
    } finally { dom.window.close(); }
  });
});
