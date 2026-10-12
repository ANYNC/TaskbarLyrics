import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { beforeAll, beforeEach, describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");

async function createHarness() {
  const [html, bridge, state, presentation, panel, script, styleCss] = await Promise.all([
    read("TaskbarLyrics.App/Web/Lyrics/index.html"),
    read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
    read("TaskbarLyrics.App/Web/Lyrics/state.js"),
    read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
    read("TaskbarLyrics.App/Web/Lyrics/floatingPanel.js"),
    read("TaskbarLyrics.App/Web/Lyrics/app.js"),
    read("TaskbarLyrics.App/Web/Lyrics/style.css")
  ]);
  const dom = new JSDOM(html.replace("{{STYLE_CSS}}", styleCss).replace("{{APP_JS}}", ""), {
    runScripts: "outside-only"
  });
  dom.window.CSS = { supports: () => true };
  dom.window.requestAnimationFrame = callback => {
    callback(0);
    return 1;
  };
  dom.window.cancelAnimationFrame = () => {};

  const timers = new Map();
  let nextTimerId = 1;
  dom.window.setTimeout = (callback, delay) => {
    const id = nextTimerId++;
    timers.set(id, { callback, delay });
    return id;
  };
  dom.window.clearTimeout = id => {
    timers.delete(id);
  };

  const sent = [];
  dom.window.chrome = { webview: { postMessage: message => sent.push(JSON.parse(message)) } };

  dom.window.eval(bridge);
  dom.window.eval(state);
  dom.window.eval(presentation);
  dom.window.eval(panel);
  dom.window.eval(script);

  const flushTimers = () => {
    for (const [id, timer] of [...timers]) {
      timers.delete(id);
      timer.callback();
    }
  };
  const receive = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
  const applyStyle = payload => receive("style", payload);
  const enablePanel = (extra = {}) => applyStyle({
    floatingPanel: true,
    layoutScalePercent: 100,
    surfaceTone: 0,
    themeLight: true,
    ...extra
  });

  return { dom, sent, receive, applyStyle, enablePanel, flushTimers };
}

describe("floating control panel", () => {
  // One shared document keeps this file cheap; every test resets the panel state and
  // the recorded messages itself.
  let shared;

  beforeAll(async () => {
    shared = await createHarness();
  });

  beforeEach(() => {
    shared.sent.length = 0;
    shared.flushTimers();
    shared.receive("floatingPanelLayout", { open: false });
    shared.applyStyle({ floatingPanel: false, layoutScalePercent: 100, surfaceTone: 0, themeLight: true });
    shared.sent.length = 0;
  });

  const harness = () => shared;

  it("requests the panel geometry and pins the lyrics height when the pointer enters", async () => {
    const { dom, sent, enablePanel } = harness();
    enablePanel();

    const layout = dom.window.document.getElementById("layout");
    layout.dispatchEvent(new dom.window.MouseEvent("mouseenter"));

    const request = sent.at(-1);
    expect(request.type).toBe("floatingPanelToggle");
    expect(request.payload).toMatchObject({ open: true, height: 48, inset: 4, gap: 4 });
    expect(["down", "up"]).toContain(request.payload.direction);
    // The surface reserves the panel space itself, so the host can resize afterwards.
    expect(layout.classList.contains("panel-expanded")).toBe(true);
    expect(layout.style.getPropertyValue("--panel-shell-height")).toMatch(/^\d+px$/);
  });

  it("stays inert while the panel is unavailable", async () => {
    const { dom, sent, applyStyle } = harness();
    applyStyle({ floatingPanel: false, layoutScalePercent: 100, surfaceTone: 0, themeLight: true });

    dom.window.document.getElementById("layout").dispatchEvent(new dom.window.MouseEvent("mouseenter"));

    expect(sent.some(message => message.type === "floatingPanelToggle")).toBe(false);
  });

  it("applies the direction reported by the host", async () => {
    const { dom, receive, enablePanel } = harness();
    enablePanel();
    receive("floatingPanelLayout", { open: true, direction: "up" });

    const layout = dom.window.document.getElementById("layout");
    expect(layout.classList.contains("panel-expanded")).toBe(true);
    expect(layout.classList.contains("panel-above")).toBe(true);

    receive("floatingPanelLayout", { open: false });
    expect(layout.classList.contains("panel-expanded")).toBe(false);
    expect(layout.classList.contains("panel-above")).toBe(false);
    expect(layout.style.getPropertyValue("--panel-shell-height")).toBe("");
  });

  it("collapses 1.5 seconds after the pointer leaves the expanded surface", async () => {
    const { dom, sent, receive, enablePanel, flushTimers } = harness();
    enablePanel();
    const layout = dom.window.document.getElementById("layout");
    layout.dispatchEvent(new dom.window.MouseEvent("mouseenter"));
    receive("floatingPanelLayout", { open: true, direction: "down" });

    layout.dispatchEvent(new dom.window.MouseEvent("mouseleave"));
    flushTimers();

    // The panel animates out, but the reserved lyrics height stays until the host has
    // shrunk the window.
    expect(layout.classList.contains("panel-closing")).toBe(true);
    expect(layout.classList.contains("panel-expanded")).toBe(true);

    flushTimers();
    expect(sent.at(-1).payload).toMatchObject({ open: false, height: 48, inset: 4, gap: 4 });

    receive("floatingPanelLayout", { open: false });
    expect(layout.classList.contains("panel-expanded")).toBe(false);
    expect(layout.classList.contains("panel-closing")).toBe(false);
    expect(layout.style.getPropertyValue("--panel-shell-height")).toBe("");
  });

  it("scales the requested height with the lyrics layout scale", async () => {
    const { dom, sent, enablePanel } = harness();
    enablePanel({ layoutScalePercent: 150 });
    dom.window.document.getElementById("layout").dispatchEvent(new dom.window.MouseEvent("mouseenter"));

    expect(sent.at(-1).payload.height).toBe(72);
  });

  it("deepens a faint surface to 80% and keeps a dense one", async () => {
    const { dom, enablePanel } = harness();
    const root = dom.window.document.documentElement;

    enablePanel({ surfaceTone: 40 });
    expect(root.style.getPropertyValue("--surface-color-expanded")).toBe("rgba(0, 0, 0, 0.800)");
    expect(root.style.getPropertyValue("--surface-color-frame")).toBe("rgba(0, 0, 0, 0.750)");

    enablePanel({ surfaceTone: -35 });
    expect(root.style.getPropertyValue("--surface-color-expanded")).toBe("rgba(255, 255, 255, 0.800)");
    expect(root.style.getPropertyValue("--surface-color-frame")).toBe("rgba(255, 255, 255, 0.750)");

    enablePanel({ surfaceTone: 90 });
    expect(root.style.getPropertyValue("--surface-color-expanded")).toBe("rgba(0, 0, 0, 0.900)");
    expect(root.style.getPropertyValue("--surface-color-frame")).toBe("rgba(0, 0, 0, 0.850)");
  });

  it("falls back to the interface theme for a transparent surface", async () => {
    const { dom, enablePanel } = harness();
    const root = dom.window.document.documentElement;

    enablePanel({ surfaceTone: 0, themeLight: true });
    expect(root.style.getPropertyValue("--surface-color-expanded")).toBe("rgba(255, 255, 255, 0.800)");

    enablePanel({ surfaceTone: 0, themeLight: false });
    expect(root.style.getPropertyValue("--surface-color-expanded")).toBe("rgba(0, 0, 0, 0.800)");
  });

  it("renders the track and playback state pushed by the host", async () => {
    const { dom, receive } = harness();
    receive("panelTrack", {
      title: "夜曲",
      artist: "周杰伦",
      hasTrack: true,
      isPlaying: true,
      translationEnabled: false
    });
    receive("panelTimeline", { position: 95.5, duration: 200, isPlaying: true, canSeek: true });

    const document = dom.window.document;
    expect(document.getElementById("panelTitle").textContent).toBe("夜曲");
    expect(document.getElementById("panelArtist").textContent).toBe("周杰伦");
    expect(document.getElementById("panelElapsed").textContent).toBe("1:35");
    expect(document.getElementById("panelDuration").textContent).toBe("3:20");
    expect(document.getElementById("panelSeek").disabled).toBe(false);
    expect(document.getElementById("panelPlayIcon").classList.contains("is-hidden")).toBe(true);
    expect(document.getElementById("panelPauseIcon").classList.contains("is-hidden")).toBe(false);
  });

  it("sends transport actions and seeks through the bridge", async () => {
    const { dom, sent, receive, enablePanel } = harness();
    enablePanel();
    receive("panelTrack", { title: "夜曲", artist: "", hasTrack: true, isPlaying: true });
    receive("panelTimeline", { position: 20, duration: 200, isPlaying: true, canSeek: true });

    dom.window.document.getElementById("panelNext").click();
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "floatingPanelAction",
      payload: { action: "next" }
    });

    const seek = dom.window.document.getElementById("panelSeek");
    seek.value = "500";
    seek.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "floatingPanelSeek",
      payload: { position: 100 }
    });
  });

  it("keeps window dragging out of the panel", async () => {
    const { dom, sent, enablePanel } = harness();
    enablePanel({ freeDrag: true });

    dom.window.document.getElementById("floatingPanel").dispatchEvent(
      new dom.window.MouseEvent("mousedown", { bubbles: true, button: 0 }));
    expect(sent.some(message => message.type === "windowDrag")).toBe(false);

    dom.window.document.getElementById("layout").dispatchEvent(
      new dom.window.MouseEvent("mousedown", { bubbles: true, button: 0 }));
    expect(sent.some(message => message.type === "windowDrag")).toBe(true);
  });
});
