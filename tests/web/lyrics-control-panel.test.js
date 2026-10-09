import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = path => readFile(new URL(path, root), "utf8");

async function createPanel() {
  const [html, script] = await Promise.all([
    read("TaskbarLyrics.App/Web/ControlPanel/index.html"),
    read("TaskbarLyrics.App/Web/ControlPanel/app.js")
  ]);
  const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
    runScripts: "outside-only"
  });
  const sent = [];
  dom.window.chrome = { webview: { postMessage: message => sent.push(JSON.parse(message)) } };
  dom.window.eval(script);
  return { dom, sent };
}

describe("lyrics control panel", () => {
  it("shows track state and routes media controls through V1 messages", async () => {
    const { dom, sent } = await createPanel();
    const doc = dom.window.document;
    const receive = (type, payload) => dom.window.controlPanel.receive({ version: 1, type, payload });

    receive("snapshot", {
      title: "Song", artist: "Artist", coverDataUri: "data:image/png;base64,AAAA",
      fallbackIconDataUri: "", hasTrack: true, isPlaying: true, light: false
    });

    expect(doc.querySelector("#title").textContent).toBe("Song");
    expect(doc.querySelector("#title").title).toBe("Song");
    expect(doc.querySelector("#artworkImage").classList.contains("visible")).toBe(true);
    expect(doc.querySelector("#playPause").getAttribute("aria-label")).toBe("暂停");
    expect(doc.querySelector("#playIcon").hasAttribute("hidden")).toBe(true);
    expect(doc.querySelector("#pauseIcon").hasAttribute("hidden")).toBe(false);
    for (const id of ["previous", "playPause", "next"]) doc.querySelector(`#${id}`).click();
    expect(sent.map(message => message.payload.action)).toEqual(["previous", "toggle", "next"]);
    expect(sent.every(message => message.version === 1 && message.type === "mediaAction")).toBe(true);

    receive("snapshot", { hasTrack: true, isPlaying: false });
    expect(doc.querySelector("#playIcon").hasAttribute("hidden")).toBe(false);
    expect(doc.querySelector("#pauseIcon").hasAttribute("hidden")).toBe(true);

    doc.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(sent.at(-1)).toEqual({ version: 1, type: "dismiss", payload: {} });
  });

  it("disables transport buttons without a track", async () => {
    const { dom, sent } = await createPanel();
    dom.window.controlPanel.receive({ version: 1, type: "snapshot", payload: { hasTrack: false } });
    expect(dom.window.document.querySelector("#title").textContent).toBe("暂无播放");
    dom.window.document.querySelector("#next").click();
    expect(sent).toHaveLength(0);
  });

  it("changes playback state without replacing the cover image", async () => {
    const { dom } = await createPanel();
    const doc = dom.window.document;
    const artwork = doc.querySelector("#artworkImage");
    const receive = (type, payload) => dom.window.controlPanel.receive({ version: 1, type, payload });
    const cover = "data:image/png;base64,AAAA";
    receive("snapshot", { hasTrack: true, title: "Song", coverDataUri: cover, isPlaying: true });
    let coverAssignments = 0;
    Object.defineProperty(artwork, "src", {
      configurable: true,
      get: () => cover,
      set: () => { coverAssignments++; }
    });

    receive("playback", { isPlaying: false });

    expect(doc.querySelector("#playPause").getAttribute("aria-label")).toBe("播放");
    expect(doc.querySelector("#playIcon").hasAttribute("hidden")).toBe(false);
    expect(artwork.classList.contains("visible")).toBe(true);
    expect(coverAssignments).toBe(0);
  });

  it("routes settings, translation, seeking and system volume through V1 messages", async () => {
    const { dom, sent } = await createPanel();
    const doc = dom.window.document;
    const receive = (type, payload) => dom.window.controlPanel.receive({ version: 1, type, payload });

    receive("snapshot", {
      hasTrack: true, translationEnabled: true, source: "网易云音乐",
      sourceIconDataUri: "data:image/png;base64,AAAA"
    });
    receive("timeline", { positionMs: 30000, durationMs: 120000, isPlaying: false, canSeek: true });
    receive("volume", { level: 35, muted: false });
    expect(doc.querySelector("#source").textContent).toBe("网易云音乐");
    expect(doc.querySelector("#sourceIcon").hidden).toBe(false);
    expect(doc.querySelector("#sourceIcon").src).toBe("data:image/png;base64,AAAA");
    expect(doc.querySelector("#sourceFallback").hasAttribute("hidden")).toBe(true);
    expect(doc.querySelector("#translation").getAttribute("aria-pressed")).toBe("true");
    expect(doc.querySelector("#elapsed").textContent).toBe("0:30");
    expect(doc.querySelector("#duration").textContent).toBe("2:00");
    expect(doc.querySelector("#volume").value).toBe("35");

    doc.querySelector("#settings").click();
    doc.querySelector("#translation").click();
    const seek = doc.querySelector("#seek");
    seek.value = "500";
    seek.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    const volume = doc.querySelector("#volume");
    volume.value = "60";
    volume.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    doc.querySelector("#mute").click();

    expect(sent).toEqual([
      { version: 1, type: "openSettings", payload: {} },
      { version: 1, type: "toggleTranslation", payload: {} },
      { version: 1, type: "seek", payload: { positionMs: 60000 } },
      { version: 1, type: "setVolume", payload: { level: 60 } },
      { version: 1, type: "toggleMute", payload: {} }
    ]);
  });

  it("disables seeking when the player does not support it", async () => {
    const { dom, sent } = await createPanel();
    dom.window.controlPanel.receive({ version: 1, type: "timeline", payload: {
      positionMs: 0, durationMs: 120000, canSeek: false
    } });
    expect(dom.window.document.querySelector("#seek").disabled).toBe(true);
    expect(sent).toHaveLength(0);
  });

  it("uses the local fallback icon when no valid source image is available", async () => {
    const { dom } = await createPanel();
    dom.window.controlPanel.receive({ version: 1, type: "snapshot", payload: {
      hasTrack: true, source: "Custom", sourceIconDataUri: "https://example.com/icon.png"
    } });

    expect(dom.window.document.querySelector("#sourceIcon").hidden).toBe(true);
    expect(dom.window.document.querySelector("#sourceFallback").hasAttribute("hidden")).toBe(false);
  });

});
