import { readFile } from "node:fs/promises";
import { JSDOM } from "./helpers/localized-dom.js";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = name => readFile(new URL(`TaskbarLyrics.App/Web/Lyrics/${name}`, root), "utf8");

async function harness(reducedMotion = false) {
  const [html, ...scripts] = await Promise.all(["index.html", "bridge.js", "state.js", "presentation.js", "app.js"].map(read));
  const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), { runScripts: "outside-only" });
  const frames = new Map();
  const timers = new Map();
  const sent = [];
  const preloaders = [];
  let id = 0;
  dom.window.CSS = { supports: () => true };
  dom.window.matchMedia = () => ({ matches: reducedMotion });
  dom.window.Image = class { constructor() { preloaders.push(this); } };
  dom.window.chrome = { webview: { postMessage: value => sent.push(JSON.parse(value)) } };
  dom.window.requestAnimationFrame = callback => { frames.set(++id, callback); return id; };
  dom.window.cancelAnimationFrame = token => frames.delete(token);
  dom.window.setTimeout = callback => { timers.set(++id, callback); return id; };
  dom.window.clearTimeout = token => timers.delete(token);
  scripts.forEach(script => dom.window.eval(script));
  const receive = (type, payload, version = 1) => dom.window.taskbarLyrics.receive({ version, type, payload });
  const layout = dom.window.document.querySelector("#layout");
  const track = dom.window.document.querySelector("#track");
  const current = () => dom.window.document.querySelector("#currentLineText").textContent;
  const region = (phase, generation = 1, edge = "bottom") => receive("regionVisibility", { phase, generation, edge });
  const flushFrames = () => { const pending = [...frames.values()]; frames.clear(); pending.forEach(callback => callback(0)); };
  const flushTimers = () => {
    for (let i = 0; timers.size && i < 32; i++) {
      const [token, callback] = timers.entries().next().value;
      timers.delete(token);
      callback();
    }
  };
  const end = (target = layout) => {
    const event = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(event, "propertyName", { value: "transform" });
    target.dispatchEvent(event);
  };
  return { dom, sent, preloaders, receive, layout, track, current, region, flushFrames, flushTimers, end };
}

const frame = { current: "首帧", next: "下一句", currentLineIndex: 0, trackId: "region-track", isPlaying: true, wordScanProgress: 0.2 };

describe("lyrics region entrance and exit", () => {
  it.each(["top", "bottom", "left", "right"])("uses the %s edge and stages the first frame without lyric rolling", async edge => {
    const h = await harness();
    h.receive("style", { singleLine: true, verticalTaskbar: edge === "left" || edge === "right" });
    h.region("preparing", 1, edge);
    expect(h.layout.classList.contains("region-concealed")).toBe(true);
    h.receive("lyrics", frame);
    expect(h.current()).toBe("首帧");
    h.region("entering", 1, edge);
    h.flushFrames();
    expect(h.layout.dataset.regionEdge).toBe(edge);
    expect(h.layout.inert).toBe(true);
    expect(h.layout.classList.contains("region-moving")).toBe(true);
    h.receive("lyrics", { ...frame, current: "入场中最新句", currentLineIndex: 1 });
    expect(h.current()).toBe("入场中最新句");
    expect(h.track.classList.contains("animating")).toBe(false);
    h.end(h.track);
    expect(h.sent).toEqual([]);
    h.end();
    expect(h.layout.inert).toBe(false);
    expect(h.sent).toEqual([{ version: 1, type: "regionVisibilityCompleted", payload: { generation: 1, visible: true } }]);
    h.receive("lyrics", { ...frame, current: "入场后的换句", currentLineIndex: 2 });
    h.flushFrames();
    h.flushFrames();
    expect(h.track.classList.contains("animating")).toBe(true);
    h.dom.window.close();
  });

  it("settles an active lyric roll before exit and holds its last frame through later updates", async () => {
    const h = await harness();
    h.receive("style", { singleLine: true });
    h.receive("lyrics", { ...frame, animateTransition: false });
    h.receive("cover", { fallbackText: "末" });
    h.flushTimers();
    h.receive("lyrics", { ...frame, current: "稳定末帧", currentLineIndex: 1 });
    h.flushFrames();
    h.flushFrames();
    expect(h.track.classList.contains("animating")).toBe(true);
    h.region("exiting", 2);
    expect(h.track.classList.contains("animating")).toBe(false);
    expect(h.track.style.transform).toBe("");
    expect(h.current()).toBe("稳定末帧");
    h.receive("lyrics", { ...frame, current: "等待播放...", scene: "noPlayback", trackId: "" });
    h.receive("cover", { fallbackText: "新" });
    expect(h.current()).toBe("稳定末帧");
    expect(h.dom.window.document.querySelector("#coverFallback").textContent).toBe("末");
    h.dom.window.document.querySelector("#cover").click();
    expect(h.sent).toEqual([]);
    h.flushFrames();
    h.flushTimers();
    expect(h.layout.classList.contains("region-concealed")).toBe(true);
    expect(h.layout.inert).toBe(true);
    expect(h.sent).toEqual([{ version: 1, type: "regionVisibilityCompleted", payload: { generation: 2, visible: false } }]);
    h.region("preparing", 3);
    h.receive("lyrics", { ...frame, current: "重新出现" });
    h.region("entering", 3);
    h.flushTimers();
    expect(h.dom.window.document.querySelector("#coverFallback").textContent).toBe("新");
    h.dom.window.close();
  });

  it("does not let an old cover preload mutate the final region frame", async () => {
    const h = await harness();
    h.receive("cover", { fallbackText: "旧" });
    h.flushTimers();
    h.receive("cover", { dataUri: "data:image/png;base64,AAAA", fallbackText: "候" });
    h.region("exiting");
    h.preloaders.at(-1).onload();
    h.flushFrames();
    h.flushTimers();
    expect(h.dom.window.document.querySelector("#coverImageNext").getAttribute("src")).toBe(null);
    h.dom.window.close();
  });

  it("keeps the accepted lyric frame at exit when a same-track search was intentionally ignored", async () => {
    const h = await harness();
    h.receive("lyrics", { ...frame, animateTransition: false });
    h.receive("lyrics", { ...frame, current: "正在检索歌词...", scene: "searching", currentLineIndex: -1 });
    expect(h.current()).toBe("首帧");
    h.region("exiting");
    expect(h.current()).toBe("首帧");
    h.dom.window.close();
  });

  it("reverses an exit without accepting stale generations or replaying a waiting frame", async () => {
    const h = await harness();
    h.receive("lyrics", { ...frame, animateTransition: false });
    h.region("exiting", 1);
    h.flushFrames();
    h.region("preparing", 2);
    h.receive("lyrics", { ...frame, current: "恢复播放的新首帧", trackId: "new-track", currentLineIndex: 0 });
    h.region("entering", 2);
    h.region("hidden", 1);
    h.flushFrames();
    h.flushTimers();
    expect(h.current()).toBe("恢复播放的新首帧");
    expect(h.layout.classList.contains("region-concealed")).toBe(false);
    expect(h.track.classList.contains("animating")).toBe(false);
    expect(h.sent).toEqual([{ version: 1, type: "regionVisibilityCompleted", payload: { generation: 2, visible: true } }]);
    h.dom.window.close();
  });

  it("keeps spectrum as a stable first and last frame without a second layer transition", async () => {
    const h = await harness();
    h.region("preparing");
    h.receive("lyrics", { ...frame, scene: "spectrum", isPureMusic: true });
    h.region("entering");
    h.flushFrames();
    expect(h.layout.classList.contains("spectrum-mode")).toBe(true);
    expect(h.track.classList.contains("animating")).toBe(false);
    h.end();
    h.region("exiting", 2);
    h.receive("lyrics", { ...frame, current: "等待播放", scene: "noPlayback" });
    expect(h.layout.classList.contains("spectrum-mode")).toBe(true);
    h.end();
    expect(h.sent.at(-1).payload).toEqual({ generation: 2, visible: false });
    h.dom.window.close();
  });

  it("honors reduced motion and the host fallback synchronization", async () => {
    const h = await harness(true);
    h.region("preparing");
    h.receive("lyrics", frame);
    h.region("entering");
    expect(h.layout.classList.contains("region-moving")).toBe(false);
    expect(h.layout.inert).toBe(false);
    h.region("exiting", 2);
    expect(h.layout.classList.contains("region-concealed")).toBe(true);
    expect(h.sent.map(message => message.payload)).toEqual([{ generation: 1, visible: true }, { generation: 2, visible: false }]);
    h.region("preparing", 3);
    h.receive("lyrics", { ...frame, current: "同步帧" });
    h.region("visible", 3);
    expect(h.layout.inert).toBe(false);
    expect(h.layout.classList.contains("region-concealed")).toBe(false);
    expect(h.current()).toBe("同步帧");
    h.dom.window.close();
  });

  it.each([
    null,
    { phase: "entering", generation: -1, edge: "bottom" },
    { phase: "entering", generation: "1", edge: "bottom" },
    { phase: "unknown", generation: 1, edge: "bottom" },
    { phase: "entering", generation: 1, edge: "unknown" }
  ])("rejects malformed region messages safely: %j", async payload => {
    const h = await harness();
    h.receive("regionVisibility", payload);
    expect(h.layout.classList.contains("region-moving")).toBe(false);
    expect(h.sent).toEqual([]);
    h.dom.window.close();
  });
});
