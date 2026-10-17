import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");

const appIconUri = "data:image/png;base64,iVBORw0KGgo=";
const artworkUri = "data:image/jpeg;base64,BBBB";

async function createHarness({ deferAnimationFrames = false } = {}) {
  const [html, bridge, state, presentation, script, style] = await Promise.all([
    read("TaskbarLyrics.App/Web/Lyrics/index.html"),
    read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
    read("TaskbarLyrics.App/Web/Lyrics/state.js"),
    read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
    read("TaskbarLyrics.App/Web/Lyrics/app.js"),
    read("TaskbarLyrics.App/Web/Lyrics/style.css")
  ]);
  const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
    runScripts: "outside-only"
  });
  dom.window.CSS = { supports: () => true };
  const frames = new Map();
  let nextFrameId = 1;
  dom.window.requestAnimationFrame = callback => {
    if (deferAnimationFrames) {
      const id = nextFrameId++;
      frames.set(id, callback);
      return id;
    }
    callback(0);
    return 1;
  };
  dom.window.cancelAnimationFrame = id => frames.delete(id);
  const flushFrames = () => {
    const pending = [...frames.values()];
    frames.clear();
    pending.forEach(callback => callback(0));
  };

  const timers = new Map();
  let nextTimerId = 1;
  dom.window.setTimeout = (callback, delay) => {
    const id = nextTimerId++;
    timers.set(id, { callback, delay });
    return id;
  };
  dom.window.clearTimeout = id => timers.delete(id);

  const preloaders = [];
  dom.window.Image = class {
    constructor() {
      preloaders.push(this);
    }
  };

  dom.window.eval(bridge);
  dom.window.eval(state);
  dom.window.eval(presentation);
  dom.window.eval(script);

  const flushTimers = () => {
    for (let guard = 0; timers.size > 0 && guard < 32; guard += 1) {
      const [id, timer] = timers.entries().next().value;
      timers.delete(id);
      timer.callback();
    }
  };

  const receive = payload => dom.window.taskbarLyrics.receive({
    version: 1,
    type: "cover",
    payload
  });

  const showCover = payload => {
    receive(payload);
    flushTimers();
    preloaders.at(-1)?.onload?.();
    flushTimers();
  };

  return { dom, preloaders, receive, flushTimers, flushFrames, showCover, style };
}

describe("taskbar line layout", () => {
  it.each([26, 42])("displays the larger single-line font within a %spx viewport", async height => {
    const { dom } = await createHarness();
    const document = dom.window.document;
    Object.defineProperty(document.querySelector(".viewport"), "clientHeight", { configurable: true, value: height });
    const receive = payload => dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload });
    receive({ singleLine: true, fontSize: 19.6, layoutScalePercent: 100, viewportDescenderBuffer: 2 });
    const size = () => Number.parseFloat(document.documentElement.style.getPropertyValue("--current-size"));
    expect(size()).toBe(19.6);
    receive({ singleLine: true, fontSize: 40, layoutScalePercent: 100, viewportDescenderBuffer: 2 });
    expect(size()).toBeLessThanOrEqual(height - 2);
    receive({ singleLine: false, fontSize: 14, layoutScalePercent: 100, viewportDescenderBuffer: 2 });
    expect(size()).toBeLessThanOrEqual(14);
    dom.window.close();
  });

  it("uses one complete row and rolls current lyrics upward in single-line mode", async () => {
    const { dom, flushFrames, flushTimers } = await createHarness({ deferAnimationFrames: true });
    const document = dom.window.document;
    const viewport = document.querySelector(".viewport");
    Object.defineProperty(viewport, "clientHeight", { configurable: true, value: 26 });
    Object.defineProperty(viewport, "clientWidth", { configurable: true, value: 24 });
    const receive = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
    receive("style", { singleLine: true, verticalTaskbar: false, fontSize: 14, viewportDescenderBuffer: 2 });
    expect(document.documentElement.classList.contains("single-line")).toBe(true);
    expect(document.documentElement.style.getPropertyValue("--row-height")).toBe("24px");
    receive("lyrics", { current: "第一句", next: "第二句", currentLineIndex: 0, trackId: "one", isPlaying: true });
    flushTimers();
    receive("lyrics", { current: "第二句", next: "第三句", currentLineIndex: 1, trackId: "one", isPlaying: true, wordScanProgress: 0.2 });
    flushFrames();
    flushFrames();
    const track = document.querySelector(".track");
    expect(track.classList.contains("single-line-roll")).toBe(true);
    expect(track.classList.contains("animating")).toBe(true);
    expect(track.style.transform).toBe("translateY(-24px)");
    flushTimers();
    expect(document.querySelector(".current-line .line-text-base").textContent).toBe("第二句");
    expect(track.classList.contains("single-line-roll")).toBe(false);
    expect(track.classList.contains("animating")).toBe(false);
    expect(track.style.transform).toBe("");
    receive("lyrics", { current: "第二句", next: "第三句", currentLineIndex: 1, trackId: "one", isPlaying: true, wordScanProgress: 0.25 });
    expect(document.querySelector(".current-line").classList.contains("word-scan-smoothing")).toBe(true);
    receive("style", { singleLine: false, verticalTaskbar: true });
    expect(document.documentElement.classList.contains("single-line")).toBe(false);
    expect(document.documentElement.classList.contains("vertical-taskbar")).toBe(true);
    expect(document.documentElement.style.getPropertyValue("--row-height")).toBe("12px");
  });

  it("rolls one lyric row with translation enabled and settles the latest queued line", async () => {
    const { dom, flushFrames, flushTimers, style } = await createHarness({ deferAnimationFrames: true });
    const document = dom.window.document;
    const receive = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
    receive("style", { singleLine: true, fontSize: 19.6 });
    const frame = { current: "旧句", next: "新句", currentLineIndex: 0, trackId: "single-translation", isPlaying: true,
      translationMode: true, currentTranslation: "旧译", nextTranslation: "新译", wordScanProgress: 0.2 };
    receive("lyrics", { ...frame, animateTransition: false });
    receive("lyrics", { ...frame, current: "新句", currentLineIndex: 1, currentTranslation: "新译" });
    flushFrames();
    flushFrames();
    const track = document.querySelector(".track");
    expect(track.classList.contains("single-line-roll")).toBe(true);
    expect(track.classList.contains("translation-pair-animating")).toBe(false);
    expect(document.querySelector("#nextLineText").textContent).toBe("新句");
    receive("lyrics", { ...frame, current: "最新句", currentLineIndex: 2, currentTranslation: "最新译" });
    flushTimers();
    flushFrames();
    flushFrames();
    flushTimers();
    expect(document.querySelector("#currentLineText").textContent).toBe("最新句");
    expect(track.classList.contains("single-line-roll")).toBe(false);
    expect(style).toContain(".single-line .track.single-line-roll > .next-line:not(.incoming-line)");
    dom.window.close();
  });

  it("cancels single-line roll on a mode change and honors reduced motion", async () => {
    const { dom, flushFrames } = await createHarness({ deferAnimationFrames: true });
    const document = dom.window.document;
    const receive = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
    receive("style", { singleLine: true });
    const frame = { current: "旧句", next: "新句", currentLineIndex: 0, trackId: "single-cancel", isPlaying: true };
    receive("lyrics", { ...frame, animateTransition: false });
    receive("lyrics", { ...frame, current: "新句", currentLineIndex: 1 });
    flushFrames();
    flushFrames();
    const track = document.querySelector(".track");
    expect(track.classList.contains("single-line-roll")).toBe(true);
    receive("style", { singleLine: false });
    expect(track.classList.contains("single-line-roll")).toBe(false);
    expect(track.style.transform).toBe("");
    dom.window.matchMedia = () => ({ matches: true });
    receive("style", { singleLine: true });
    receive("lyrics", { ...frame, current: "静态句", currentLineIndex: 2 });
    expect(track.classList.contains("animating")).toBe(false);
    expect(document.querySelector("#currentLineText").textContent).toBe("静态句");
    dom.window.close();
  });

  it("lays out vertical columns by width and scrolls long lyrics along their height", async () => {
    const { dom, style } = await createHarness();
    const document = dom.window.document;
    const viewport = document.querySelector(".viewport");
    Object.defineProperty(viewport, "clientWidth", { configurable: true, value: 32 });
    Object.defineProperty(viewport, "clientHeight", { configurable: true, value: 350 });
    const line = document.querySelector(".current-line");
    const textViewport = line.querySelector(".line-text-viewport");
    const text = line.querySelector(".line-text-base");
    Object.defineProperty(textViewport, "clientHeight", { configurable: true, value: 200 });
    Object.defineProperty(textViewport, "clientWidth", { configurable: true, value: 12 });
    Object.defineProperty(text, "scrollHeight", { configurable: true, value: 600 });
    Object.defineProperty(text, "scrollWidth", { configurable: true, value: 12 });
    const receive = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
    receive("style", { verticalTaskbar: true, singleLine: false, fontSize: 8, layoutScalePercent: 300 });
    expect(document.documentElement.style.getPropertyValue("--row-height")).toBe("16px");
    expect(Number.parseFloat(document.documentElement.style.getPropertyValue("--next-size"))).toBeLessThanOrEqual(8);
    receive("lyrics", { current: "竖排长句", next: "下一列", currentLineIndex: 0, trackId: "vertical", isPlaying: true, wordScanProgress: 0.5, animateTransition: false });
    expect(line.classList.contains("horizontal-scrolling")).toBe(true);
    expect(line.querySelector(".line-text-stack").style.getPropertyValue("--line-scroll-offset")).toBe("-170px");
    expect(document.querySelector(".track").classList.contains("animating")).toBe(false);
    expect(style).toContain("writing-mode: vertical-rl");
    expect(style).toContain("--word-scan-direction: to bottom");
    expect(style).toContain("transform: translateY(var(--line-scroll-offset))");
    receive("style", { verticalTaskbar: false, singleLine: false });
    expect(line.classList.contains("horizontal-scrolling")).toBe(false);
  });

  it.each([[false, false], [true, false], [false, true], [true, true]])(
    "rolls vertical columns right and settles their text (single=%s, translation=%s)", async (singleLine, translationMode) => {
      const { dom, flushFrames, flushTimers } = await createHarness({ deferAnimationFrames: true });
      const document = dom.window.document;
      Object.defineProperty(document.querySelector(".viewport"), "clientWidth", { configurable: true, value: 32 });
      const receive = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
      receive("style", { verticalTaskbar: true, singleLine });
      const frame = { current: "旧句", next: "新句", currentLineIndex: 0, trackId: "roll", isPlaying: true,
        translationMode, currentTranslation: "旧译", nextTranslation: "新译", wordScanProgress: 0.2 };
      receive("lyrics", { ...frame, animateTransition: false });
      receive("lyrics", { ...frame, current: "新句", next: "后句", currentLineIndex: 1, currentTranslation: "新译" });
      flushFrames();
      flushFrames();
      const track = document.querySelector(".track");
      expect(track.classList.contains("vertical-roll")).toBe(true);
      expect(track.classList.contains("animating")).toBe(true);
      const expectedDistance = singleLine || translationMode ? 32 : 16;
      expect(track.style.transform).toBe(`translateX(${expectedDistance}px)`);
      flushTimers();
      expect(document.querySelector("#currentLineText").textContent).toBe("新句");
      expect(document.querySelector("#nextLineText").textContent).toBe(translationMode ? "新译" : "后句");
      expect(track.classList.contains("vertical-roll")).toBe(false);
      expect(track.classList.contains("animating")).toBe(false);
      expect(track.style.transform).toBe("");
    });

  it("cancels vertical roll state when orientation changes and honors reduced motion", async () => {
    const { dom, flushFrames } = await createHarness({ deferAnimationFrames: true });
    const document = dom.window.document;
    const receive = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
    receive("style", { verticalTaskbar: true, singleLine: true });
    const frame = { current: "旧句", next: "新句", currentLineIndex: 0, trackId: "cancel", isPlaying: true };
    receive("lyrics", { ...frame, animateTransition: false });
    receive("lyrics", { ...frame, current: "新句", currentLineIndex: 1 });
    flushFrames();
    flushFrames();
    const track = document.querySelector(".track");
    expect(track.classList.contains("vertical-roll")).toBe(true);
    receive("style", { verticalTaskbar: false, singleLine: true });
    expect(track.classList.contains("vertical-roll")).toBe(false);
    expect(track.style.transform).toBe("");
    dom.window.matchMedia = () => ({ matches: true });
    receive("style", { verticalTaskbar: true, singleLine: true });
    receive("lyrics", { ...frame, current: "静态句", currentLineIndex: 2 });
    expect(track.classList.contains("animating")).toBe(false);
    expect(document.querySelector("#currentLineText").textContent).toBe("静态句");
  });
});

describe("embedded control panel triggers", () => {
  async function triggerHarness(style = {}) {
    const harness = await createHarness();
    const { dom } = harness;
    const document = dom.window.document;
    const sent = [];
    dom.window.chrome = { webview: { postMessage: message => sent.push(JSON.parse(message)) } };
    const cover = document.querySelector("#cover");
    const lyrics = document.querySelector(".lyrics-pane");
    cover.getBoundingClientRect = () => ({ x: 4, y: 3, width: 34, height: 34 });
    lyrics.getBoundingClientRect = () => ({ x: 46, y: 3, width: 300, height: 34 });
    const applyStyle = payload => dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload });
    applyStyle({ showCover: true, enableControlPanel: true, controlPanelTriggerCover: true,
      controlPanelTriggerLyrics: true, ...style });
    return { ...harness, sent, cover, lyrics, applyStyle };
  }

  it("sends selected region bounds and preserves keyboard activation and text selection", async () => {
    const { dom, sent, lyrics, cover, applyStyle } = await triggerHarness({ controlPanelTriggerCover: false });
    expect(cover.disabled).toBe(true);
    expect(lyrics.getAttribute("role")).toBe("button");
    expect(lyrics.tabIndex).toBe(0);
    lyrics.click();
    expect(sent.at(-1)).toMatchObject({ version: 1, type: "controlPanelTrigger", payload: {
      area: "Lyrics", activation: "Click", x: 46, width: 300
    } });
    dom.window.taskbarLyrics.receive({ version: 1, type: "controlPanelState", payload: { open: true } });
    expect(lyrics.getAttribute("aria-expanded")).toBe("true");
    applyStyle({ enableControlPanel: true, controlPanelTriggerCover: false, controlPanelTriggerLyrics: true, fontSize: 16 });
    expect(lyrics.getAttribute("aria-expanded")).toBe("true");
    const count = sent.length;
    dom.window.getSelection = () => ({ toString: () => "selected lyrics" });
    lyrics.click();
    expect(sent).toHaveLength(count);
    lyrics.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Enter", bubbles: true, cancelable: true }));
    expect(sent.at(-1).payload.activation).toBe("Keyboard");
    applyStyle({ enableControlPanel: false, controlPanelTriggerLyrics: true });
    expect(lyrics.hasAttribute("role")).toBe(false);
    const disabledCount = sent.length;
    lyrics.click();
    expect(sent).toHaveLength(disabledCount);
    dom.window.close();
  });

  it("waits before hover opening and cancels on exit, mode changes and disabling", async () => {
    const { dom, sent, lyrics, cover, flushTimers, applyStyle } = await triggerHarness({ controlPanelTriggerMethod: "Hover" });
    lyrics.dispatchEvent(new dom.window.MouseEvent("mouseenter"));
    expect(sent).toHaveLength(0);
    lyrics.dispatchEvent(new dom.window.MouseEvent("mouseleave"));
    flushTimers();
    expect(sent).toHaveLength(0);
    lyrics.dispatchEvent(new dom.window.MouseEvent("mouseenter"));
    flushTimers();
    expect(sent.at(-1)).toMatchObject({ type: "controlPanelTrigger", payload: { area: "Lyrics", activation: "Hover" } });
    lyrics.dispatchEvent(new dom.window.MouseEvent("click", { detail: 1 }));
    expect(sent).toHaveLength(1);
    cover.dispatchEvent(new dom.window.MouseEvent("mouseenter"));
    cover.dispatchEvent(new dom.window.MouseEvent("mouseleave"));
    lyrics.dispatchEvent(new dom.window.MouseEvent("mouseenter"));
    applyStyle({ floatingControlPanel: true, controlPanelTriggerMethod: "Hover", controlPanelTriggerLyrics: true });
    flushTimers();
    expect(sent).toHaveLength(1);
    expect(lyrics.hasAttribute("tabindex")).toBe(false);
    cover.click();
    expect(sent.at(-1).type).toBe("coverClick");
    applyStyle({ enableControlPanel: true, controlPanelTriggerMethod: "Hover", controlPanelTriggerLyrics: true });
    lyrics.dispatchEvent(new dom.window.MouseEvent("mouseenter"));
    applyStyle({ enableControlPanel: false });
    flushTimers();
    expect(sent).toHaveLength(2);
    dom.window.close();
  });

  it("keeps keyboard opening available in hover mode including with the cover hidden", async () => {
    const { dom, sent, lyrics, cover } = await triggerHarness({ showCover: false, controlPanelTriggerMethod: "Hover" });
    cover.click();
    expect(sent).toHaveLength(0);
    const event = new dom.window.KeyboardEvent("keydown", { key: " ", bubbles: true, cancelable: true });
    lyrics.dispatchEvent(event);
    expect(event.defaultPrevented).toBe(true);
    expect(sent.at(-1).payload).toMatchObject({ area: "Lyrics", activation: "Keyboard" });
    lyrics.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: " ", repeat: true, bubbles: true }));
    expect(sent).toHaveLength(1);
    dom.window.close();
  });
});

describe("lyrics cover fallback", () => {
  it("makes floating cover and lyrics passive while retaining lock state and restoring interaction on enable", async () => {
    const { dom, style } = await createHarness();
    const sent = [];
    dom.window.chrome = { webview: { postMessage: message => sent.push(JSON.parse(message)) } };
    const document = dom.window.document;
    const cover = document.querySelector("#cover");
    const interaction = cover.querySelector(".cover-interaction");
    const css = document.createElement("style");
    css.textContent = style;
    document.head.append(css);
    const receive = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
    const receiveStyle = enabled => receive("style", {
      floatingControlPanel: true, enableControlPanel: true, floatingInputPassthrough: !enabled
    });
    try {
      receiveStyle(false);
      cover.focus();
      expect(dom.window.getComputedStyle(document.querySelector(".layout")).pointerEvents).toBe("none");
      expect(dom.window.getComputedStyle(interaction).visibility).toBe("hidden");
      expect(cover.disabled).toBe(false);
      cover.click();
      expect(sent.at(-1).type).toBe("coverClick");
      receive("floatingLockState", { locked: true, hovered: true });
      expect(dom.window.getComputedStyle(interaction).visibility).toBe("hidden");
      expect(cover.getAttribute("aria-label")).toBe("解锁浮窗");
      receiveStyle(true);
      expect(dom.window.getComputedStyle(document.querySelector(".layout")).pointerEvents).not.toBe("none");
      expect(dom.window.getComputedStyle(interaction).visibility).not.toBe("hidden");
      expect(dom.window.getComputedStyle(interaction).opacity).toBe("1");
      expect(dom.window.getComputedStyle(cover.querySelector(".cover-floating-icon")).display).toBe("block");
      receiveStyle(false);
      receive("floatingLockState", { locked: false, hovered: true });
      expect(dom.window.getComputedStyle(interaction).visibility).toBe("hidden");
      receive("style", { floatingControlPanel: false, enableControlPanel: true, floatingInputPassthrough: true });
      expect(dom.window.getComputedStyle(interaction).visibility).not.toBe("hidden");
      expect(cover.getAttribute("aria-label")).toBe("展开播放控制");
    } finally { dom.window.close(); }
  });

  it("clears pointer focus for floating cover clicks while keeping keyboard activation focused", async () => {
    const { dom, style } = await createHarness();
    const document = dom.window.document;
    const cover = document.querySelector("#cover");
    const receive = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
    receive("style", { floatingControlPanel: true, enableControlPanel: true });
    cover.focus();
    expect(document.activeElement).toBe(cover);
    const pointer = new dom.window.MouseEvent("mousedown", { button: 0, bubbles: true, cancelable: true });
    cover.dispatchEvent(pointer);
    expect(pointer.defaultPrevented).toBe(true);
    expect(document.activeElement).not.toBe(cover);
    receive("floatingLockState", { locked: true, hovered: true });
    receive("floatingLockState", { locked: false });
    expect(document.activeElement).not.toBe(cover);
    cover.focus();
    cover.click();
    expect(document.activeElement).toBe(cover);
    expect(style).toMatch(/\.cover:focus-visible\s*\{[^}]*outline-offset:\s*-2px/s);
    receive("style", { floatingControlPanel: false, enableControlPanel: true });
    const embeddedPointer = new dom.window.MouseEvent("mousedown", { button: 0, bubbles: true, cancelable: true });
    cover.dispatchEvent(embeddedPointer);
    expect(embeddedPointer.defaultPrevented).toBe(false);
    expect(document.activeElement).toBe(cover);
    dom.window.close();
  });

  it("aligns floating artwork with the native cover region while retaining the text descender buffer", async () => {
    const { dom, style } = await createHarness();
    const document = dom.window.document;
    const root = document.documentElement;
    const receiveStyle = payload => dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload });
    receiveStyle({ floatingControlPanel: true, enableControlPanel: true, viewportDescenderBuffer: 3 });
    expect(root.style.getPropertyValue("--viewport-descender-buffer")).toBe("3px");
    expect(root.classList.contains("floating-controls-enabled")).toBe(true);
    expect(style).toMatch(/:root:not\(\.vertical-taskbar\) \.cover\s*\{[^}]*margin-bottom:\s*var\(--viewport-descender-buffer\)/s);
    receiveStyle({ floatingControlPanel: false, viewportDescenderBuffer: 2 });
    expect(root.classList.contains("floating-controls-enabled")).toBe(false);
    expect(root.style.getPropertyValue("--viewport-descender-buffer")).toBe("2px");
  });

  it("shows subtle floating lock icons only during cover hover without tooltip text", async () => {
    const { dom, style } = await createHarness();
    const doc = dom.window.document;
    const recv = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
    const cover = doc.querySelector("#cover");
    const interaction = cover.querySelector(".cover-interaction");
    const css = doc.createElement("style");
    css.textContent = style;
    doc.head.append(css);
    try {
      recv("style", { floatingControlPanel: true, enableControlPanel: true });
      recv("controlPanelState", { open: true });
      expect(dom.window.getComputedStyle(cover.querySelector(".cover-locked-icon")).display).toBe("block");
      expect(dom.window.getComputedStyle(cover.querySelector(".cover-floating-icon")).display).toBe("none");
      expect(cover.hasAttribute("title")).toBe(false);
      expect(dom.window.getComputedStyle(interaction).opacity).toBe("0");
      recv("floatingLockState", { locked: true, hovered: true });
      expect(dom.window.getComputedStyle(cover.querySelector(".cover-locked-icon")).display).toBe("none");
      expect(dom.window.getComputedStyle(cover.querySelector(".cover-floating-icon")).display).toBe("block");
      expect(doc.documentElement.classList.contains("floating-lock-hovered")).toBe(true);
      expect(dom.window.getComputedStyle(interaction).opacity).toBe("1");
      recv("floatingLockState", { locked: true, hovered: false });
      expect(dom.window.getComputedStyle(interaction).opacity).toBe("0");
      expect(cover.hasAttribute("title")).toBe(false);
      expect(cover.getAttribute("aria-label")).toBe("解锁浮窗");
      recv("floatingLockState", { locked: false, hovered: true });
      expect(doc.documentElement.classList.contains("floating-lock-hovered")).toBe(false);
      expect(cover.hasAttribute("title")).toBe(false);
    } finally { dom.window.close(); }
  });

  it("shows Lucide lock states only in floating mode and keeps lock feedback accessible", async () => {
    const { dom, style } = await createHarness();
    const document = dom.window.document;
    const cover = document.querySelector("#cover");
    const recv = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
    try {
      recv("style", { floatingControlPanel: true, enableControlPanel: true });
      expect(cover.getAttribute("aria-label")).toBe("锁定浮窗");
      expect(cover.getAttribute("aria-pressed")).toBe("false");
      expect(cover.querySelector(".cover-locked-icon path")).not.toBeNull();
      recv("controlPanelState", { open: true });
      recv("floatingLockState", { locked: true });
      expect(document.documentElement.classList.contains("floating-locked")).toBe(true);
      expect(cover.getAttribute("aria-label")).toBe("解锁浮窗");
      expect(cover.getAttribute("aria-pressed")).toBe("true");
      expect(cover.getAttribute("aria-expanded")).toBe("false");
      recv("floatingLockState", { locked: "false" });
      expect(cover.getAttribute("aria-pressed")).toBe("true");
      recv("style", { floatingControlPanel: true, enableControlPanel: true, coverPosition: "Right" });
      expect(cover.getAttribute("aria-pressed")).toBe("true");
      recv("floatingLockState", { locked: false });
      expect(cover.getAttribute("aria-label")).toBe("锁定浮窗");
      recv("style", { floatingControlPanel: false, enableControlPanel: true });
      recv("floatingLockState", { locked: true });
      expect(document.documentElement.classList.contains("floating-locked")).toBe(false);
      expect(cover.hasAttribute("aria-pressed")).toBe(false);
      expect(cover.getAttribute("aria-label")).toBe("展开播放控制");
      expect(style).toContain(".floating-controls-enabled.floating-locked .cover-floating-icon { display: block; }");
    } finally { dom.window.close(); }
  });

  it.each(["dragstart", "copy", "contextmenu"])("blocks cover %s without changing lyric interactions", async eventName => {
    const { dom, showCover } = await createHarness();
    const document = dom.window.document;
    const cover = document.querySelector("#cover");
    try {
      showCover({ dataUri: artworkUri, trackId: "non-exportable-cover" });
      expect(cover.draggable).toBe(false);
      for (const image of cover.querySelectorAll("img")) {
        expect(image.draggable).toBe(false);
      }
      for (const target of [cover, ...cover.querySelectorAll("img, .cover-fallback, .cover-floating-icon")]) {
        const event = new dom.window.Event(eventName, { bubbles: true, cancelable: true });
        target.dispatchEvent(event);
        expect(event.defaultPrevented).toBe(true);
      }
      const lyricEvent = new dom.window.Event(eventName, { bubbles: true, cancelable: true });
      document.querySelector("#currentLineText").dispatchEvent(lyricEvent);
      expect(lyricEvent.defaultPrevented).toBe(false);
    } finally {
      dom.window.close();
    }
  });

  it("keeps the cover position while exposing floating controls and restoring the lyric surface", async () => {
    const { dom } = await createHarness();
    const recv = (type, payload) => dom.window.taskbarLyrics.receive({ version: 1, type, payload });
    recv("style", { floatingControlPanel: true, enableControlPanel: true, coverPosition: "Right" });
    expect(dom.window.document.documentElement.classList.contains("floating-controls-enabled")).toBe(true);
    expect(dom.window.document.querySelector("#cover").disabled).toBe(false);
    recv("floatingControlSurface", { open: true, light: true });
    expect(dom.window.document.documentElement.classList.contains("floating-controls-open")).toBe(true);
    expect(dom.window.document.documentElement.style.getPropertyValue("--floating-control-surface")).toBe("#f8f9fb");
    expect(dom.window.document.documentElement.dataset.coverPosition).toBe("Right");
    recv("floatingControlSurface", { open: false });
    expect(dom.window.document.documentElement.classList.contains("floating-controls-open")).toBe(false);
    dom.window.close();
  });
  it("shows an accessible expand affordance and sends a bounded cover click", async () => {
    const { dom } = await createHarness();
    const cover = dom.window.document.querySelector("#cover");
    const sent = [];
    dom.window.chrome = { webview: { postMessage: message => sent.push(JSON.parse(message)) } };
    cover.getBoundingClientRect = () => ({ x: 4, y: 3, width: 34, height: 34 });

    expect(cover.tagName).toBe("BUTTON");
    expect(cover.getAttribute("aria-expanded")).toBe("false");
    expect(cover.querySelector(".cover-action-icon path")).not.toBeNull();
    cover.click();
    expect(sent).toEqual([{
      version: 1,
      type: "coverClick",
      payload: {
        x: 4, y: 3, width: 34, height: 34,
        viewportWidth: dom.window.innerWidth,
        viewportHeight: dom.window.innerHeight
      }
    }]);

    dom.window.taskbarLyrics.receive({ version: 1, type: "controlPanelState", payload: { open: true } });
    expect(cover.classList.contains("panel-open")).toBe(true);
    expect(cover.getAttribute("aria-expanded")).toBe("true");
    dom.window.document.documentElement.classList.add("cover-hidden");
    cover.click();
    expect(sent).toHaveLength(1);
  });

  it("keeps the cover usable in spectrum mode and disables the panel affordance on request", async () => {
    const { dom, style } = await createHarness();
    const cover = dom.window.document.querySelector("#cover");
    const sent = [];
    dom.window.chrome = { webview: { postMessage: message => sent.push(JSON.parse(message)) } };
    cover.getBoundingClientRect = () => ({ x: 4, y: 3, width: 34, height: 34 });
    dom.window.taskbarLyrics.receive({ version: 1, type: "lyrics", payload: {
      current: "纯音乐", next: "", isPureMusic: true, isPlaying: true, animateTransition: false, scene: "spectrum"
    } });
    expect(dom.window.document.querySelector("#layout").classList.contains("spectrum-mode")).toBe(true);
    cover.click();
    expect(sent.at(-1).type).toBe("coverClick");
    dom.window.taskbarLyrics.receive({ version: 1, type: "controlPanelState", payload: { open: true } });
    dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload: { enableControlPanel: false } });
    expect(cover.disabled).toBe(true);
    expect(cover.getAttribute("aria-expanded")).toBe("false");
    const sentCount = sent.length;
    cover.click();
    cover.dispatchEvent(new dom.window.MouseEvent("click"));
    expect(sent).toHaveLength(sentCount);
    expect(style).toContain(".cover:not(:disabled):is(:hover, :focus-visible, .panel-open) .cover-interaction");
    dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload: { enableControlPanel: true } });
    cover.click();
    expect(sent).toHaveLength(sentCount + 1);
  });

  it("fills the cover slot with the app icon when album art is unavailable", async () => {
    const { dom, showCover, style } = await createHarness();
    const cover = dom.window.document.querySelector("#cover");
    const fallback = dom.window.document.querySelector("#coverFallback");
    const activeImage = dom.window.document.querySelector("#coverImageNext");
    const standbyImage = dom.window.document.querySelector("#coverImage");

    showCover({
      dataUri: "",
      fallbackText: "N",
      fallbackColor: "rgb(229, 57, 53)",
      fallbackIconDataUri: appIconUri,
      trackId: "track-icon"
    });

    expect(cover.classList.contains("app-icon")).toBe(true);
    expect(cover.style.backgroundColor).toBe("");
    expect(fallback.style.display).toBe("none");
    expect(activeImage.getAttribute("src")).toBe(appIconUri);
    expect(activeImage.style.opacity).toBe("1");
    expect(standbyImage.hasAttribute("src")).toBe(false);
    expect(style).toMatch(/\.cover\.app-icon\s*\{[^}]*background:\s*transparent/s);
  });

  it("keeps the letter fallback when no app icon can be resolved", async () => {
    const { dom, showCover } = await createHarness();
    const cover = dom.window.document.querySelector("#cover");
    const fallback = dom.window.document.querySelector("#coverFallback");
    const activeImage = dom.window.document.querySelector("#coverImageNext");

    showCover({
      dataUri: "",
      fallbackText: "N",
      fallbackColor: "rgb(229, 57, 53)",
      fallbackIconDataUri: "",
      trackId: "track-letter"
    });

    expect(cover.classList.contains("app-icon")).toBe(false);
    expect(cover.style.backgroundColor).toBe("rgb(229, 57, 53)");
    expect(fallback.style.display).toBe("flex");
    expect(fallback.textContent).toBe("N");
    expect(activeImage.hasAttribute("src")).toBe(false);
  });

  it("returns to album art rendering when artwork arrives after an app icon", async () => {
    const { dom, showCover } = await createHarness();
    const cover = dom.window.document.querySelector("#cover");

    showCover({
      dataUri: "",
      fallbackText: "N",
      fallbackColor: "rgb(229, 57, 53)",
      fallbackIconDataUri: appIconUri,
      trackId: "track-icon"
    });
    expect(cover.classList.contains("app-icon")).toBe(true);

    showCover({
      dataUri: artworkUri,
      fallbackText: "N",
      fallbackColor: "rgb(229, 57, 53)",
      fallbackIconDataUri: appIconUri,
      trackId: "track-artwork"
    });

    expect(cover.classList.contains("app-icon")).toBe(false);
    expect(dom.window.document.querySelector("#coverImage").getAttribute("src")).toBe(artworkUri);
  });

  it("ignores a repeated app icon payload without restarting the fallback timer", async () => {
    const { dom, preloaders, receive, flushTimers, showCover } = await createHarness();
    const payload = {
      dataUri: "",
      fallbackText: "N",
      fallbackColor: "rgb(229, 57, 53)",
      fallbackIconDataUri: appIconUri,
      trackId: "track-icon"
    };

    showCover(payload);
    const preloaderCount = preloaders.length;
    receive(payload);
    flushTimers();

    expect(preloaders.length).toBe(preloaderCount);
    expect(dom.window.document.querySelector("#cover").classList.contains("app-icon")).toBe(true);
    expect(dom.window.document.querySelector("#coverImageNext").getAttribute("src")).toBe(appIconUri);
  });
  it.each(["Left", "Center", "Right"])("applies both cover positions independently of %s lyric alignment", async alignment => {
    const { dom } = await createHarness();
    const document = dom.window.document;
    const style = payload => dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload });
    for (const position of ["Left", "Right"]) {
      style({ coverPosition: position, textAlignment: alignment, showCover: true });
      expect(document.documentElement.dataset.coverPosition).toBe(position);
      expect(document.querySelector("#layout").dataset.textAlignment).toBe(alignment);
      expect(document.documentElement.classList.contains("cover-hidden")).toBe(false);
      style({ coverPosition: position, textAlignment: alignment, showCover: false });
      expect(document.documentElement.classList.contains("cover-hidden")).toBe(true);
      expect(document.documentElement.dataset.coverPosition).toBe(position);
    }
    style({ coverPosition: "Unsupported", textAlignment: alignment });
    expect(document.documentElement.dataset.coverPosition).toBe("Left");
    style({ textAlignment: alignment });
    expect(document.documentElement.dataset.coverPosition).toBe("Left");
  });

});
