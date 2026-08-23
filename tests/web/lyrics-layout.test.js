import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");

describe("lyrics responsive layout", () => {
  it("renders and clears word-scan progress without changing the secondary line", async () => {
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
    dom.window.requestAnimationFrame = () => 1;
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    expect(style).toContain(".current-line.word-scanning");
    expect(style).toContain(".line-text-base");
    expect(style).toContain(".line-text-scan");
    expect(style).toMatch(/@property\s+--line-scroll-offset\s*\{[^}]*syntax:\s*"<length>"[^}]*initial-value:\s*0px/s);
    expect(style).toMatch(/\.line-text-viewport\s*\{[^}]*overflow:\s*hidden/s);
    expect(style).toMatch(/\.line-text\s*\{[^}]*text-overflow:\s*ellipsis/s);
    expect(style).toMatch(/\.line\.horizontal-scrolling \.line-text-stack\s*\{[^}]*transform:\s*translateX\(var\(--line-scroll-offset\)\)/s);
    expect(style).toMatch(/\.line\.horizontal-scrolling \.line-text\s*\{[^}]*text-overflow:\s*clip/s);
    expect(style).toMatch(/\.line\.horizontal-scrolling\.word-scan-smoothing \.line-text-stack\s*\{[^}]*transition:\s*transform 90ms linear;[^}]*will-change:\s*transform/s);
    expect(style).toMatch(/@media\s*\(prefers-reduced-motion:\s*reduce\)[\s\S]*\.line\.word-scan-smoothing\s*\{[^}]*transition:\s*none/s);
    expect(style).toMatch(/@media\s*\(prefers-reduced-motion:\s*reduce\)[\s\S]*\.line\.horizontal-scrolling\.word-scan-smoothing \.line-text-stack\s*\{[^}]*transition:\s*none;[^}]*will-change:\s*auto/s);
    expect(style).toContain("--primary: rgba(255, 255, 255, 0.90)");
    expect(style).toContain("--secondary: rgba(255, 255, 255, 0.60)");
    expect(style).toContain("--word-scan-overlay: rgba(255, 255, 255, 0.75)");
    expect(style).toContain("--word-scan-feather-width: 0.12em");
    const primaryFallback = style.match(/--primary:\s*rgba\((\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\)/);
    const secondaryFallback = style.match(/--secondary:\s*rgba\((\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\)/);
    expect(primaryFallback).not.toBeNull();
    expect(secondaryFallback).not.toBeNull();
    expect(secondaryFallback?.slice(1, 4)).toEqual(primaryFallback?.slice(1, 4));
    expect(Number(primaryFallback?.[4])).toBeCloseTo(0.90, 5);
    expect(Number(secondaryFallback?.[4])).toBeCloseTo(0.60, 5);
    expect(style).toMatch(/@property\s+--word-scan-progress\s*\{[^}]*syntax:\s*"<percentage>"[^}]*initial-value:\s*0%/s);
    expect(style).toMatch(/\.line\.word-scan-smoothing\s*\{[^}]*--word-scan-progress\s+90ms\s+linear/s);
    expect(style).toMatch(/\.line-text-scan\s*\{[^}]*visibility:\s*hidden/s);
    expect(style).toContain("clip-path: inset(0 100% 0 0)");
    expect(style).toContain("clip-path: inset(0 0 0 var(--word-scan-progress))");
    expect(style).toContain("clip-path: inset(0 calc(100% - var(--word-scan-progress)) 0 0)");
    expect(style).toContain("@supports ((mask-image: linear-gradient(to right, #000, transparent)) or");
    expect(style).toContain("(-webkit-mask-image: linear-gradient(to right, #000, transparent)))");
    expect(style).toMatch(/--word-scan-effective-feather:\s*min\(\s*var\(--word-scan-feather-width\),\s*var\(--word-scan-progress\),\s*calc\(100% - var\(--word-scan-progress\)\)\)/s);
    expect(style).toMatch(/-webkit-mask-image:\s*linear-gradient\(\s*to right,\s*#000 0,\s*#000 calc\(var\(--word-scan-progress\) - var\(--word-scan-effective-feather\)\),\s*transparent calc\(var\(--word-scan-progress\) \+ var\(--word-scan-effective-feather\)\),\s*transparent 100%\)/s);
    expect(style).toMatch(/(?<!-webkit-)mask-image:\s*linear-gradient\(\s*to right,\s*#000 0,\s*#000 calc\(var\(--word-scan-progress\) - var\(--word-scan-effective-feather\)\),\s*transparent calc\(var\(--word-scan-progress\) \+ var\(--word-scan-effective-feather\)\),\s*transparent 100%\)/s);
    expect(style).toContain("-webkit-mask-repeat: no-repeat");
    expect(style).toContain("mask-repeat: no-repeat");
    expect(style).toMatch(/@supports[\s\S]*\.current-line\.word-scanning \.line-text-scan,[\s\S]*color:\s*var\(--word-scan-overlay\)/s);
    expect(style).not.toContain("word-scan-active");
    expect(style).not.toContain("word-scan-complete");
    expect(style).not.toContain("background-clip: text");
    expect(style).not.toContain("-webkit-background-clip: text");
    expect(style).not.toContain("-webkit-text-fill-color: transparent");
    expect(style).toMatch(/\.track\s*\{[^}]*transform:\s*none/s);
    expect(style).not.toMatch(/\.track\s*\{[^}]*will-change\s*:/s);
    expect(style).toMatch(/\.track\.animating\s*\{[^}]*will-change:\s*transform/s);

    const currentLine = dom.window.document.querySelector("#currentLine");
    const currentLineText = dom.window.document.querySelector("#currentLineText");
    const currentLineScanText = dom.window.document.querySelector("#currentLineScanText");
    const currentLineViewport = currentLine.querySelector(".line-text-viewport");
    const currentLineStack = currentLine.querySelector(".line-text-stack");
    const nextLine = dom.window.document.querySelector("#nextLine");
    const nextLineText = dom.window.document.querySelector("#nextLineText");
    const nextLineScanText = dom.window.document.querySelector("#nextLineScanText");
    const nextLineStack = nextLine.querySelector(".line-text-stack");
    expect(currentLineViewport).not.toBeNull();
    expect(currentLineStack).not.toBeNull();
    expect(nextLine.querySelector(".line-text-viewport")).not.toBeNull();
    expect(nextLine.querySelector(".line-text-stack")).not.toBeNull();
    expect(nextLineStack).not.toBeNull();
    Object.defineProperty(currentLineViewport, "clientWidth", { configurable: true, value: 100 });
    Object.defineProperty(currentLineText, "scrollWidth", { configurable: true, value: 300 });
    Object.defineProperty(dom.window, "devicePixelRatio", { configurable: true, value: 1.25 });
    expect(currentLineText.classList.contains("line-text-base")).toBe(true);
    expect(currentLineScanText.classList.contains("line-text-scan")).toBe(true);
    expect(currentLineScanText.getAttribute("aria-hidden")).toBe("true");
    expect(nextLineText.classList.contains("line-text-base")).toBe(true);
    expect(nextLineScanText.classList.contains("line-text-scan")).toBe(true);
    expect(nextLineScanText.getAttribute("aria-hidden")).toBe("true");
    expect(currentLine.classList.contains("word-scanning")).toBe(false);
    expect(currentLineScanText.textContent).toBe(currentLineText.textContent);
    expect(nextLineScanText.textContent).toBe(nextLineText.textContent);
    expect(nextLine.style.getPropertyValue("--word-scan-progress")).toBe("");
    const receiveLyrics = (wordScanProgress, isPlaying = true) => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        current: "Scanned line",
        next: "Next line",
        progress: 0.5,
        currentLineIndex: 0,
        trackId: "",
        isPureMusic: false,
        isPlaying,
        ...(wordScanProgress === undefined ? {} : { wordScanProgress })
      }
    });

    receiveLyrics(0);
    expect(currentLine.classList.contains("word-scanning")).toBe(true);
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(false);
    expect(currentLine.classList.contains("horizontal-scrolling")).toBe(true);
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("0px");
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("0.000%");
    expect(currentLineText.textContent).toBe("Scanned line");
    expect(currentLineScanText.textContent).toBe("Scanned line");
    expect(nextLineText.textContent).toBe("Next line");
    expect(nextLineScanText.textContent).toBe("Next line");

    receiveLyrics(0.1);
    expect(currentLine.classList.contains("word-scanning")).toBe(true);
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(true);
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("10.000%");
    expect(currentLineScanText.textContent).toBe(currentLineText.textContent);
    expect(nextLineText.textContent).toBe("Next line");
    expect(nextLineScanText.textContent).toBe("Next line");

    receiveLyrics(0.625);
    expect(currentLine.classList.contains("word-scanning")).toBe(true);
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(false);
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("62.500%");
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("-122.4px");
    expect(currentLineScanText.textContent).toBe(currentLineText.textContent);
    expect(nextLine.classList.contains("horizontal-scrolling")).toBe(false);
    expect(nextLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("");

    receiveLyrics(0.8, false);
    expect(currentLine.classList.contains("word-scanning")).toBe(true);
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(false);
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("80.000%");
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("-175.2px");

    receiveLyrics(0.9, true);
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(true);
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("80.000%");
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("-175.2px");

    receiveLyrics(0.2, false);
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(false);
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("0px");

    receiveLyrics(0.8, true);
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("20.000%");

    receiveLyrics(null);
    expect(currentLine.classList.contains("word-scanning")).toBe(false);
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(false);
    expect(currentLine.classList.contains("horizontal-scrolling")).toBe(false);
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("");
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("");
    expect(currentLineScanText.textContent).toBe(currentLineText.textContent);
    expect(nextLineText.textContent).toBe("Next line");
    expect(nextLineScanText.textContent).toBe("Next line");

    receiveLyrics();
    expect(currentLine.classList.contains("word-scanning")).toBe(false);
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("");

    Object.defineProperty(currentLineText, "scrollWidth", { configurable: true, value: 100 });
    receiveLyrics(0.5, false);
    expect(currentLine.classList.contains("horizontal-scrolling")).toBe(false);
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("");
  });

  it("disables word-scan interpolation when reduced motion is requested", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    dom.window.matchMedia = query => ({
      matches: query.includes("prefers-reduced-motion"),
      addEventListener() {},
      removeEventListener() {}
    });
    dom.window.requestAnimationFrame = () => 1;
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receiveLyrics = wordScanProgress => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        current: "Reduced motion line",
        next: "Next line",
        progress: 0.5,
        currentLineIndex: 0,
        trackId: "",
        isPureMusic: false,
        isPlaying: true,
        wordScanProgress
      }
    });

    receiveLyrics(0);
    receiveLyrics(0.1);
    const currentLine = dom.window.document.querySelector("#currentLine");
    expect(currentLine.classList.contains("word-scanning")).toBe(true);
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(false);
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("10.000%");

    receiveLyrics(0.2, false);
    receiveLyrics(0.3, true);
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("30.000%");
  });

  it("freezes the interpolated word-scan value across pause frames", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = () => 1;
    dom.window.cancelAnimationFrame = () => {};
    let computedProgress = "24.500%";
    let nowMs = 1000;
    Object.defineProperty(dom.window.performance, "now", {
      configurable: true,
      value: () => nowMs
    });
    dom.window.getComputedStyle = () => ({
      getPropertyValue: property => property === "--word-scan-progress"
        ? computedProgress
        : ""
    });
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const currentLine = dom.window.document.querySelector("#currentLine");
    const currentLineText = dom.window.document.querySelector("#currentLineText");
    const currentLineViewport = currentLine.querySelector(".line-text-viewport");
    const currentLineStack = currentLine.querySelector(".line-text-stack");
    Object.defineProperty(currentLineViewport, "clientWidth", { configurable: true, value: 100 });
    Object.defineProperty(currentLineText, "scrollWidth", { configurable: true, value: 300 });
    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        current: "Interpolated line",
        next: "Next line",
        progress: 0.5,
        currentLineIndex: 0,
        trackId: "track-a",
        isPureMusic: false,
        ...payload
      }
    });

    receive({ isPlaying: true, wordScanProgress: 0.2, animateTransition: false });
    receive({ isPlaying: true, wordScanProgress: 0.3 });
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(true);

    receive({ isPlaying: false, wordScanProgress: 0.4 });
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(false);
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("24.500%");
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("-9px");

    computedProgress = "30.000%";
    receive({ isPlaying: false, wordScanProgress: 0.4 });
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("24.500%");

    receive({ isPlaying: false, wordScanProgress: 0.6 });
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("60.000%");

    receive({ isPlaying: true, wordScanProgress: 0.7 });
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(true);
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("60.000%");

    nowMs += 90;
    receive({ isPlaying: true, wordScanProgress: 0.75 });
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("70.000%");

    nowMs += 90;
    receive({ isPlaying: true, wordScanProgress: 0.8 });
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("80.000%");
  });

  it("carries word-scan progress through queued line transitions", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    const pendingAnimationFrames = [];
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const document = dom.window.document;
    const track = document.querySelector("#track");
    const currentLine = document.querySelector("#currentLine");
    const currentLineText = document.querySelector("#currentLineText");
    const currentLineScanText = document.querySelector("#currentLineScanText");
    const nextLine = document.querySelector("#nextLine");
    const currentLineViewport = currentLine.querySelector(".line-text-viewport");
    const currentLineStack = currentLine.querySelector(".line-text-stack");
    const nextLineViewport = nextLine.querySelector(".line-text-viewport");
    const nextLineStack = nextLine.querySelector(".line-text-stack");
    expect(currentLineViewport).not.toBeNull();
    expect(currentLineStack).not.toBeNull();
    expect(nextLineViewport).not.toBeNull();
    expect(nextLineStack).not.toBeNull();
    Object.defineProperty(currentLineViewport, "clientWidth", { configurable: true, value: 100 });
    Object.defineProperty(nextLineViewport, "clientWidth", { configurable: true, value: 100 });
    Object.defineProperty(currentLineText, "scrollWidth", { configurable: true, value: 300 });
    Object.defineProperty(nextLine.querySelector("#nextLineText"), "scrollWidth", { configurable: true, value: 300 });
    Object.defineProperty(dom.window, "devicePixelRatio", { configurable: true, value: 1.25 });
    const receive = (current, next, currentLineIndex, wordScanProgress) => {
      dom.window.taskbarLyrics.receive({
        version: 1,
        type: "lyrics",
        payload: {
          current,
          next,
          progress: 0.25,
          currentLineIndex,
          trackId: "",
          isPureMusic: false,
          isPlaying: true,
          wordScanProgress
        }
      });
    };
    const completeTransition = () => {
      const event = new dom.window.Event("transitionend", { bubbles: true });
      Object.defineProperty(event, "propertyName", { value: "transform" });
      track.dispatchEvent(event);
    };

    receive("First line", "Second line", 0, 0.25);
    receive("Second line", "Third line", 1, 0.5);

    const initialFontSize = nextLine.style.fontSize;
    expect(initialFontSize).toBe("13px");
    expect(Number.parseFloat(nextLine.style.getPropertyValue("--promotion-scale"))).toBeCloseTo(12 / 13, 3);
    expect(nextLine.style.transform).toBe("translateY(0px) scale(var(--promotion-scale))");
    expect(track.classList.contains("animating")).toBe(false);
    const startTransitionFrame = pendingAnimationFrames.shift();
    expect(startTransitionFrame).toBeTypeOf("function");
    startTransitionFrame(0);
    expect(nextLine.style.getPropertyValue("--promotion-scale")).toBe("1");
    expect(nextLine.style.fontSize).toBe(initialFontSize);
    expect(nextLine.style.transform).toBe("");
    expect(track.classList.contains("animating")).toBe(true);

    receive("Second line", "Third line", 1, 0.625);
    expect(nextLine.style.getPropertyValue("--word-scan-progress")).toBe("62.500%");
    expect(nextLine.classList.contains("word-scanning")).toBe(true);
    expect(nextLine.classList.contains("word-scan-smoothing")).toBe(true);
    expect(nextLine.classList.contains("horizontal-scrolling")).toBe(true);
    expect(nextLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("-122.4px");
    receive("Third line", "Fourth line", 2, 0.75);
    expect(track.classList.contains("word-scan-transition")).toBe(true);

    completeTransition();
    expect(currentLineText.textContent).toBe("Second line");
    expect(currentLineScanText.textContent).toBe("Second line");
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("62.500%");

    completeTransition();
    expect(currentLineText.textContent).toBe("Third line");
    expect(currentLineScanText.textContent).toBe("Third line");
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("75.000%");
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(false);
    expect(currentLine.classList.contains("horizontal-scrolling")).toBe(true);
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("-160px");
    expect(nextLine.style.fontSize).toBe("");
    expect(nextLine.style.transform).toBe("");
    expect(nextLine.style.getPropertyValue("--promotion-scale")).toBe("");
    expect(track.classList.contains("animating")).toBe(false);
    expect(track.style.transform).toBe("");
    expect(nextLine.classList.contains("horizontal-scrolling")).toBe(false);
    expect(nextLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("");

    receive("Third line", "Fourth line", 2, null);
    expect(currentLine.classList.contains("horizontal-scrolling")).toBe(false);
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("");
  });

  it("renders structured translation pairs and cleans reduced-motion transitions", async () => {
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
    const pendingAnimationFrames = [];
    let prefersReducedMotion = false;
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.matchMedia = query => ({
      matches: query.includes("prefers-reduced-motion") && prefersReducedMotion,
      media: query,
      addEventListener: () => {},
      removeEventListener: () => {}
    });
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const document = dom.window.document;
    const layout = document.querySelector("#layout");
    const track = document.querySelector("#track");
    const currentLine = document.querySelector("#currentLine");
    const currentLineText = document.querySelector("#currentLineText");
    const currentLineViewport = currentLine.querySelector(".line-text-viewport");
    const currentLineStack = currentLine.querySelector(".line-text-stack");
    const currentLineScanText = document.querySelector("#currentLineScanText");
    const nextLine = document.querySelector("#nextLine");
    const nextLineText = document.querySelector("#nextLineText");
    const nextLineStack = nextLine.querySelector(".line-text-stack");
    const incomingPair = document.querySelector("#incomingTranslationPair");
    const incomingOriginalLine = document.querySelector("#incomingTranslationOriginalLine");
    const incomingOriginalText = document.querySelector("#incomingTranslationOriginalText");
    const incomingOriginalScanText = document.querySelector("#incomingTranslationOriginalScanText");
    const incomingOriginalViewport = incomingOriginalLine.querySelector(".line-text-viewport");
    const incomingOriginalStack = incomingOriginalLine.querySelector(".line-text-stack");
    const incomingTranslationLine = document.querySelector("#incomingTranslationLine");
    const incomingTranslationText = document.querySelector("#incomingTranslationText");
    expect(currentLineViewport).not.toBeNull();
    expect(currentLineStack).not.toBeNull();
    expect(nextLineStack).not.toBeNull();
    expect(incomingOriginalViewport).not.toBeNull();
    expect(incomingOriginalStack).not.toBeNull();
    expect(incomingPair.parentElement).toBe(track);
    expect(incomingPair.style.opacity).toBe("");
    expect(incomingPair.style.transform).toBe("");
    Object.defineProperty(currentLineViewport, "clientWidth", { configurable: true, value: 100 });
    Object.defineProperty(currentLineText, "scrollWidth", { configurable: true, value: 300 });
    Object.defineProperty(incomingOriginalViewport, "clientWidth", { configurable: true, value: 100 });
    Object.defineProperty(incomingOriginalText, "scrollWidth", { configurable: true, value: 300 });

    expect(style).toContain("--translation: rgba(255, 255, 255, 0.70)");
    expect(style).toMatch(/\.layout\.translation-mode \.track > \.next-line:not\(\.incoming-line\)[\s\S]*color:\s*var\(--translation\)/s);
    expect(style).toMatch(/\.translation-line\s*\{[^}]*color:\s*var\(--translation\)/s);
    expect(style).toMatch(/\.translation-line\.translation-placeholder\s*\{[^}]*opacity:\s*0\.55/s);
    expect(style).toMatch(/\.track\.animating\s*\{[^}]*transition:\s*transform 560ms cubic-bezier\(0\.22, 0\.72, 0\.24, 1\)/s);
    const translationTrackRule = style.match(/\.track\.animating\.translation-pair-animating\s*\{([^}]*)\}/s)?.[1] ?? "";
    expect(translationTrackRule).toContain("transition-duration: 760ms");
    expect(translationTrackRule).not.toMatch(/\btransition(?:-timing-function)?\s*:/);
    const translationPairRule = style.match(/\.incoming-translation-pair\s*\{([^}]*)\}/s)?.[1] ?? "";
    expect(translationPairRule).toContain("display: none");
    expect(translationPairRule).not.toMatch(/\b(?:position|opacity|transform|transition)\s*:/);
    expect(style).not.toMatch(/^\.incoming-translation-pair\.entering\s*\{/m);
    expect(style).toMatch(/@media\s*\(prefers-reduced-motion:\s*reduce\)[\s\S]*\.track\.animating\s*\{[^}]*transition:\s*none/s);

    const receive = (
      current,
      next,
      currentLineIndex,
      wordScanProgress,
      currentTranslation = "",
      nextTranslation = "",
      translationMode = false) => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        current,
        next,
        progress: 0.25,
        currentLineIndex,
        trackId: "",
        isPureMusic: false,
        isPlaying: true,
        wordScanProgress,
        currentTranslation,
        nextTranslation,
        translationMode
      }
    });

    receive("Line one", "Line two", 0, 0.25, "译一", "译二", true);
    expect(layout.classList.contains("translation-mode")).toBe(true);
    expect(incomingPair.classList.contains("preparing")).toBe(false);
    expect(incomingPair.classList.contains("entering")).toBe(false);
    expect(track.classList.contains("animating")).toBe(false);
    expect(track.classList.contains("translation-pair-animating")).toBe(false);
    expect(currentLineText.textContent).toBe("Line one");
    expect(currentLineScanText.textContent).toBe("Line one");
    expect(currentLine.classList.contains("word-scanning")).toBe(true);
    expect(currentLine.classList.contains("horizontal-scrolling")).toBe(true);
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("-10px");
    expect(nextLineText.textContent).toBe("译一");
    expect(nextLine.classList.contains("word-scanning")).toBe(false);
    expect(nextLine.classList.contains("horizontal-scrolling")).toBe(false);
    expect(nextLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("");

    receive("Line one", "Line two", 0, 0.5, "", "译二", true);
    expect(nextLineText.textContent).toBe("…");
    expect(nextLine.classList.contains("translation-placeholder")).toBe(true);
    expect(currentLineScanText.textContent).toBe(currentLineText.textContent);

    receive("Line two", "Line three", 1, 0.4, "译二", "译三", true);
    expect(incomingPair.classList.contains("preparing")).toBe(true);
    expect(incomingOriginalText.textContent).toBe("Line two");
    expect(incomingOriginalScanText.textContent).toBe("Line two");
    expect(incomingTranslationText.textContent).toBe("译二");
    expect(incomingTranslationLine.classList.contains("translation-placeholder")).toBe(false);
    expect(incomingOriginalLine.classList.contains("word-scanning")).toBe(true);
    expect(incomingOriginalLine.classList.contains("horizontal-scrolling")).toBe(true);
    expect(incomingOriginalStack.style.getPropertyValue("--line-scroll-offset")).toBe("-55px");
    expect(incomingPair.style.opacity).toBe("");
    expect(incomingPair.style.transform).toBe("");
    expect(nextLine.style.getPropertyValue("--promotion-scale")).toBe("");
    expect(nextLine.style.transform).toBe("");
    expect(track.classList.contains("animating")).toBe(false);

    receive("Line two", "Line three", 1, 0.6, "译二", "译三", true);
    expect(incomingOriginalLine.style.getPropertyValue("--word-scan-progress")).toBe("60.000%");
    expect(incomingOriginalStack.style.getPropertyValue("--line-scroll-offset")).toBe("-115px");

    const startTranslationFrame = pendingAnimationFrames.shift();
    expect(startTranslationFrame).toBeTypeOf("function");
    startTranslationFrame(0);
    expect(incomingPair.classList.contains("entering")).toBe(true);
    expect(track.classList.contains("animating")).toBe(true);
    expect(track.classList.contains("translation-pair-animating")).toBe(true);
    expect(track.classList.contains("translation-pair-leaving")).toBe(false);
    expect(nextLine.style.getPropertyValue("--promotion-scale")).toBe("");
    expect(nextLine.style.transform).toBe("");
    expect(incomingPair.style.opacity).toBe("");
    expect(incomingPair.style.transform).toBe("");

    const trackOffsetFrame = pendingAnimationFrames.shift();
    expect(trackOffsetFrame).toBeTypeOf("function");
    trackOffsetFrame(0);
    const linePitchPx = Number.parseFloat(document.documentElement.style.getPropertyValue("--line-pitch"));
    expect(Number.isFinite(linePitchPx)).toBe(true);
    expect(track.style.transform).toBe(`translateY(${-linePitchPx * 2}px)`);
    expect(track.style.transform).not.toContain("scale");
    expect(incomingPair.style.transform).toBe("");

    const transitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(transitionEnd, "propertyName", { value: "transform" });
    track.dispatchEvent(transitionEnd);
    expect(track.style.transform).toBe("");
    expect(track.classList.contains("animating")).toBe(false);
    expect(track.classList.contains("translation-pair-animating")).toBe(false);
    expect(track.classList.contains("no-anim")).toBe(false);
    expect(incomingPair.classList.contains("preparing")).toBe(false);
    expect(incomingPair.classList.contains("entering")).toBe(false);
    expect(incomingPair.style.opacity).toBe("");
    expect(incomingPair.style.transform).toBe("");
    expect(pendingAnimationFrames).toHaveLength(0);
    expect(incomingOriginalText.textContent).toBe(" ");
    expect(currentLineText.textContent).toBe("Line two");
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("60.000%");
    expect(nextLineText.textContent).toBe("译二");
    expect(nextLine.classList.contains("word-scanning")).toBe(false);

    prefersReducedMotion = true;
    receive("Line three", "Line four", 2, 0.2, "", "译四", true);
    expect(track.style.transform).toBe("");
    expect(track.classList.contains("animating")).toBe(false);
    expect(track.classList.contains("translation-pair-animating")).toBe(false);
    expect(incomingPair.classList.contains("preparing")).toBe(false);
    expect(incomingPair.classList.contains("entering")).toBe(false);
    expect(pendingAnimationFrames).toHaveLength(0);
    expect(currentLineText.textContent).toBe("Line three");
    expect(nextLineText.textContent).toBe("…");
    expect(nextLine.classList.contains("translation-placeholder")).toBe(true);

    receive("Line three", "Line four", 2, 0.2, "", "", false);
    expect(layout.classList.contains("translation-mode")).toBe(false);
    expect(incomingPair.classList.contains("preparing")).toBe(false);
    expect(incomingPair.classList.contains("entering")).toBe(false);
    expect(track.classList.contains("translation-pair-leaving")).toBe(false);
    expect(currentLineText.textContent).toBe("Line three");
    expect(nextLine.classList.contains("translation-placeholder")).toBe(false);
    expect(nextLineText.textContent).toBe("Line four");
    expect(currentLine.classList.contains("word-scanning")).toBe(true);
    expect(nextLine.classList.contains("word-scanning")).toBe(false);
    while (pendingAnimationFrames.length > 0) {
      pendingAnimationFrames.shift()(0);
    }
  });

  it("animates a translation result out of the search state", async () => {
    const [html, bridge, state, presentation, script, style] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js"),
      read("TaskbarLyrics.App/Web/Lyrics/style.css")
    ]);
    const searchingLine = "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd...";
    const searchingHtml = html
      .replace(/TaskbarLyrics started/g, searchingLine)
      .replace(/Waiting for lyrics\.\.\./g, " ");
    const dom = new JSDOM(searchingHtml.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    const pendingAnimationFrames = [];
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const document = dom.window.document;
    const layout = document.querySelector("#layout");
    const track = document.querySelector("#track");
    const currentLineText = document.querySelector("#currentLineText");
    const nextLineText = document.querySelector("#nextLineText");
    const incomingPair = document.querySelector("#incomingTranslationPair");
    expect(currentLineText.textContent).toBe(searchingLine);
    expect(nextLineText.textContent.trim()).toBe("");
    expect(style).toMatch(/\.track\.animating\.translation-pair-animating\s*\{[^}]*transition-duration:\s*760ms/s);

    const receive = (translationMode, current, next, currentTranslation, nextTranslation) =>
      dom.window.taskbarLyrics.receive({
        version: 1,
        type: "lyrics",
        payload: {
          current,
          next,
          progress: 0.25,
          currentLineIndex: 0,
          trackId: "",
          isPureMusic: false,
          isPlaying: true,
          wordScanProgress: 0.4,
          currentTranslation,
          nextTranslation,
          translationMode
        }
      });

    receive(true, "Found line", "Following line", "Translated line", "Following translation");
    expect(layout.classList.contains("translation-mode")).toBe(true);
    expect(currentLineText.textContent).toBe(searchingLine);
    expect(nextLineText.textContent.trim()).toBe("");
    expect(incomingPair.classList.contains("preparing")).toBe(true);
    expect(track.classList.contains("animating")).toBe(false);
    expect(track.classList.contains("translation-pair-animating")).toBe(false);

    const enterFrame = pendingAnimationFrames.shift();
    expect(enterFrame).toBeTypeOf("function");
    enterFrame(0);
    expect(currentLineText.textContent).toBe(searchingLine);
    expect(incomingPair.classList.contains("entering")).toBe(true);
    expect(track.classList.contains("animating")).toBe(true);
    expect(track.classList.contains("translation-pair-animating")).toBe(true);

    const offsetFrame = pendingAnimationFrames.shift();
    expect(offsetFrame).toBeTypeOf("function");
    offsetFrame(0);
    expect(currentLineText.textContent).toBe(searchingLine);
    expect(track.style.transform).toMatch(/^translateY\(-\d+(?:\.\d+)?px\)$/);

    const transitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(transitionEnd, "propertyName", { value: "transform" });
    track.dispatchEvent(transitionEnd);

    expect(currentLineText.textContent).toBe("Found line");
    expect(nextLineText.textContent).toBe("Translated line");
    expect(incomingPair.classList.contains("preparing")).toBe(false);
    expect(incomingPair.classList.contains("entering")).toBe(false);
    expect(track.classList.contains("animating")).toBe(false);
    expect(track.classList.contains("translation-pair-animating")).toBe(false);
    expect(track.style.transform).toBe("");

    // A normal lyric line still toggles translation mode in place.
    receive(false, "Found line", "Following line", "Translated line", "Following translation");
    expect(layout.classList.contains("translation-mode")).toBe(false);
    expect(currentLineText.textContent).toBe("Found line");
    expect(nextLineText.textContent).toBe("Following line");
    expect(incomingPair.classList.contains("preparing")).toBe(false);
    expect(incomingPair.classList.contains("entering")).toBe(false);
    expect(track.classList.contains("animating")).toBe(false);

    receive(true, "Found line", "Following line", "Translated line", "Following translation");
    expect(layout.classList.contains("translation-mode")).toBe(true);
    expect(currentLineText.textContent).toBe("Found line");
    expect(nextLineText.textContent).toBe("Translated line");
    expect(incomingPair.classList.contains("preparing")).toBe(false);
    expect(incomingPair.classList.contains("entering")).toBe(false);
    expect(track.classList.contains("animating")).toBe(false);
  });

  it("rolls into and out of no playback while updating its countdown in place", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    const pendingAnimationFrames = [];
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const track = dom.window.document.querySelector("#track");
    const currentLineText = dom.window.document.querySelector("#currentLineText");
    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload
    });
    const completeTransition = () => {
      const startFrame = pendingAnimationFrames.shift();
      expect(startFrame).toBeTypeOf("function");
      startFrame(0);
      expect(track.classList.contains("animating")).toBe(true);
      const event = new dom.window.Event("transitionend", { bubbles: true });
      Object.defineProperty(event, "propertyName", { value: "transform" });
      track.dispatchEvent(event);
      expect(track.classList.contains("animating")).toBe(false);
      pendingAnimationFrames.length = 0;
    };
    const playbackFrame = {
      current: "First line",
      next: "Next line",
      progress: 0.25,
      currentLineIndex: 0,
      trackId: "track-a",
      isPureMusic: false,
      isPlaying: true,
      scene: "lyrics"
    };

    receive({ ...playbackFrame, animateTransition: false });
    receive({
      current: "First line - Artist",
      next: "正在检索歌词...",
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-a",
      isPureMusic: false,
      isPlaying: true,
      scene: "searching"
    });
    expect(currentLineText.textContent).toBe("First line");
    expect(track.classList.contains("animating")).toBe(false);
    expect(pendingAnimationFrames).toHaveLength(0);
    receive({
      current: "暂无播放内容，3 秒后自动隐藏",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "",
      isPureMusic: false,
      isPlaying: false,
      animateTransition: true,
      scene: "noPlayback"
    });
    expect(pendingAnimationFrames).toHaveLength(1);
    completeTransition();
    expect(currentLineText.textContent).toBe("暂无播放内容，3 秒后自动隐藏");
    expect(track.classList.contains("translation-pair-animating")).toBe(false);
    expect(track.classList.contains("no-anim")).toBe(false);
    expect(track.style.transform).toBe("");
    expect(pendingAnimationFrames).toHaveLength(0);

    receive({
      current: "暂无播放内容，2 秒后自动隐藏",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "",
      isPureMusic: false,
      isPlaying: false,
      animateTransition: false,
      scene: "noPlayback"
    });
    expect(currentLineText.textContent).toBe("暂无播放内容，2 秒后自动隐藏");
    expect(track.classList.contains("animating")).toBe(false);
    expect(track.classList.contains("translation-pair-animating")).toBe(false);
    expect(track.style.transform).toBe("");
    expect(pendingAnimationFrames).toHaveLength(0);

    receive(playbackFrame);
    completeTransition();
    expect(currentLineText.textContent).toBe("First line");
    expect(pendingAnimationFrames).toHaveLength(0);
  });

  it("keeps every spectrum bar visible when scaled geometry exceeds the viewport", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    const spectrum = dom.window.document.querySelector(".spectrum");
    Object.defineProperty(spectrum, "clientWidth", { configurable: true, value: 120 });
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = () => 1;
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "spectrumTuning",
      payload: { barCount: 32 }
    });
    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "style",
      payload: {
        layoutScalePercent: 300,
        spectrumWidth: 630,
        spectrumGap: 9,
        spectrumBarWidth: 9,
        primaryColor: "rgba(255, 255, 255, 0.9)",
        secondaryColor: "rgba(255, 255, 255, 0.6)",
        wordScanOverlayColor: "rgba(255, 255, 255, 0.75)"
      }
    });

    const fittedBarWidth = Number.parseFloat(spectrum.style.getPropertyValue("--spectrum-fitted-bar-width"));
    const fittedGap = Number.parseFloat(spectrum.style.getPropertyValue("--spectrum-fitted-gap"));
    const fittedTotalWidth = (fittedBarWidth * 32) + (fittedGap * 31);

    expect(spectrum.children).toHaveLength(32);
    expect(fittedBarWidth).toBeGreaterThanOrEqual(1);
    expect(fittedGap).toBeGreaterThanOrEqual(0);
    expect(fittedTotalWidth).toBeLessThanOrEqual(120);
    expect(dom.window.document.documentElement.style.getPropertyValue("--primary"))
      .toBe("rgba(255, 255, 255, 0.9)");
    expect(dom.window.document.documentElement.style.getPropertyValue("--secondary"))
      .toBe("rgba(255, 255, 255, 0.6)");
    expect(dom.window.document.documentElement.style.getPropertyValue("--word-scan-overlay"))
      .toBe("rgba(255, 255, 255, 0.75)");
  });

  it("selects presentation transitions from scene and layout without DOM dependencies", async () => {
    const presentation = await read("TaskbarLyrics.App/Web/Lyrics/presentation.js");
    const dom = new JSDOM("<!doctype html><html><body></body></html>", {
      runScripts: "outside-only"
    });
    dom.window.eval(presentation);
    const {
      PresentationPlanner,
      TRANSITIONS,
      SCENES,
      LAYOUTS,
      DEFAULT_DURATION_MS
    } = dom.window.taskbarLyricsPresentation;
    const planner = new PresentationPlanner();
    const searching = {
      scene: SCENES.SEARCHING,
      current: "正在检索歌词...",
      trackId: "track-a"
    };
    const spectrum = { scene: SCENES.SPECTRUM, isPureMusic: true };
    const single = { scene: SCENES.LYRICS, layout: LAYOUTS.SINGLE, currentLineIndex: 0, trackId: "track" };
    const translationPair = {
      scene: SCENES.LYRICS,
      layout: LAYOUTS.TRANSLATION_PAIR,
      currentLineIndex: 1,
      trackId: "track"
    };

    expect(planner.plan(searching, spectrum)).toMatchObject({
      kind: TRANSITIONS.SEARCHING_TO_SPECTRUM_ROLL,
      durationMs: DEFAULT_DURATION_MS.translationPairRoll
    });
    expect(planner.plan(spectrum, searching)).toMatchObject({
      kind: TRANSITIONS.LAYER_SWITCH,
      durationMs: DEFAULT_DURATION_MS.translationPairRoll
    });
    expect(planner.plan(spectrum, single)).toMatchObject({
      kind: TRANSITIONS.LAYER_SWITCH,
      durationMs: DEFAULT_DURATION_MS.layerSwitch
    });
    expect(planner.plan(single, translationPair).kind).toBe(TRANSITIONS.TRANSLATION_PAIR_ROLL);
    expect(planner.plan(single, { ...single, progress: 0.75 }).kind).toBe(TRANSITIONS.PROGRESS_PATCH);
    expect(planner.plan(single, { ...single, current: "updated" }).kind).toBe(TRANSITIONS.REPLACE_IN_PLACE);
    expect(planner.plan(single, searching).kind).toBe(TRANSITIONS.SINGLE_ROLL);
    expect(planner.plan(searching, { ...searching, progress: 0.5 }).kind).toBe(TRANSITIONS.PROGRESS_PATCH);
    expect(planner.plan(searching, { ...searching, current: "new search" }).kind).toBe(TRANSITIONS.REPLACE_IN_PLACE);
    expect(planner.plan(searching, {
      ...searching,
      current: "next track",
      trackId: "track-b"
    })).toMatchObject({
      kind: TRANSITIONS.SINGLE_ROLL,
      durationMs: DEFAULT_DURATION_MS.singleRoll
    });
    const message = { scene: SCENES.MESSAGE, current: "No lyrics", currentLineIndex: -1 };
    const noPlayback = { scene: SCENES.NO_PLAYBACK, current: "No playback", currentLineIndex: -1 };
    expect(planner.plan(searching, message).kind).toBe(TRANSITIONS.SINGLE_ROLL);
    expect(planner.plan(message, { ...message, current: "Still no lyrics" }).kind).toBe(TRANSITIONS.SINGLE_ROLL);
    expect(planner.plan(noPlayback, single)).toMatchObject({
      kind: TRANSITIONS.SINGLE_ROLL,
      durationMs: DEFAULT_DURATION_MS.singleRoll
    });
    expect(planner.plan(noPlayback, translationPair)).toMatchObject({
      kind: TRANSITIONS.TRANSLATION_PAIR_ROLL,
      durationMs: DEFAULT_DURATION_MS.translationPairRoll
    });
    expect(planner.plan(noPlayback, spectrum)).toMatchObject({
      kind: TRANSITIONS.NO_PLAYBACK_TO_SPECTRUM_ROLL,
      durationMs: DEFAULT_DURATION_MS.searchingSpectrumRoll
    });
    expect(planner.plan(searching, spectrum, { reducedMotion: true }).kind).toBe(TRANSITIONS.IMMEDIATE);
    expect(dom.window.taskbarLyricsPresentation.normalizeFrame({
      scene: SCENES.LYRICS,
      current: searching.current,
      currentLineIndex: -1
    }).scene).toBe(SCENES.LYRICS);
  });

  it("consumes planner decisions for ordinary lyric patches, replacements, and rolls", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    const pendingAnimationFrames = [];
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        next: "next",
        progress: 0.25,
        currentLineIndex: 0,
        trackId: "",
        isPureMusic: false,
        isPlaying: true,
        ...payload
      }
    });
    const track = dom.window.document.querySelector("#track");
    const currentLineText = dom.window.document.querySelector("#currentLineText");
    const nextLine = dom.window.document.querySelector("#nextLine");
    const nextLineText = dom.window.document.querySelector("#nextLineText");

    receive({ current: "line", wordScanProgress: 0.2 });
    receive({ current: "line", progress: 0.4, wordScanProgress: 0.4 });
    expect(nextLine.classList.contains("promoting")).toBe(false);
    expect(currentLineText.textContent).toBe("line");

    receive({ current: "corrected line", progress: 0.4 });
    expect(nextLine.classList.contains("promoting")).toBe(false);
    expect(currentLineText.textContent).toBe("corrected line");

    receive({ current: "next line", next: "following", currentLineIndex: 1, progress: 0.1 });
    expect(currentLineText.textContent).toBe("corrected line");
    expect(nextLineText.textContent).toBe("next line");
    const transitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(transitionEnd, "propertyName", { value: "transform" });
    track.dispatchEvent(transitionEnd);
    expect(currentLineText.textContent).toBe("next line");
    receive({
      current: "No lyrics available",
      next: "",
      currentLineIndex: -1,
      scene: "message",
      progress: 0
    });
    expect(currentLineText.textContent).toBe("next line");
    expect(nextLineText.textContent).toBe("No lyrics available");
    track.dispatchEvent(transitionEnd);
    expect(currentLineText.textContent).toBe("No lyrics available");
    pendingAnimationFrames.splice(0);
  });

  it("applies ordinary lyric rolls immediately when reduced motion is requested", async () => {
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
    dom.window.matchMedia = () => ({ matches: true, addEventListener() {}, removeEventListener() {} });
    dom.window.requestAnimationFrame = () => 1;
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        next: "next",
        progress: 0.25,
        currentLineIndex: 0,
        trackId: "",
        isPureMusic: false,
        isPlaying: true,
        ...payload
      }
    });
    receive({ current: "first line" });
    receive({ current: "second line", next: "following", currentLineIndex: 1 });

    const track = dom.window.document.querySelector("#track");
    expect(track.classList.contains("animating")).toBe(false);
    expect(dom.window.document.querySelector("#currentLineText").textContent).toBe("second line");
    expect(dom.window.document.querySelector("#nextLineText").textContent).toBe("following");

    const reducedMotionStyles = style.slice(style.indexOf("@media (prefers-reduced-motion: reduce)"));
    expect(reducedMotionStyles).toMatch(/\.lyrics-layer,\s*\.spectrum-layer,[\s\S]*transition:\s*none/);
    expect(reducedMotionStyles).toMatch(/\.track\.animating\s*\{[^}]*transition:\s*none[^}]*will-change:\s*auto/s);
  });

  it("keeps searching scene and track identity while a track-switch result rolls in", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const searchingLine = "正在检索歌词...";
    const dom = new JSDOM(html
      .replace("TaskbarLyrics started", searchingLine)
      .replace("Waiting for lyrics...", " ")
      .replace("{{STYLE_CSS}}", "")
      .replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    const pendingAnimationFrames = [];
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({ version: 1, type: "lyrics", payload });
    Object.defineProperty(dom.window.performance, "now", {
      configurable: true,
      value: () => 1
    });
    receive({
      current: searchingLine,
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-a",
      isPureMusic: false,
      isPlaying: true,
      scene: "searching"
    });
    const track = dom.window.document.querySelector("#track");
    const currentLineText = dom.window.document.querySelector("#currentLineText");
    const nextLine = dom.window.document.querySelector("#nextLine");
    const nextLineText = dom.window.document.querySelector("#nextLineText");
    expect(currentLineText.textContent).toBe(searchingLine);

    Object.defineProperty(dom.window.performance, "now", {
      configurable: true,
      value: () => 1000
    });
    receive({
      current: "found line",
      next: "next line",
      progress: 0.2,
      currentLineIndex: 0,
      trackId: "track-a",
      isPureMusic: false,
      isPlaying: true,
      scene: "lyrics"
    });
    await new Promise(resolve => dom.window.setTimeout(resolve, 0));
    expect(currentLineText.textContent).toBe(searchingLine);
    expect(nextLineText.textContent).toBe("found line");
    const transitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(transitionEnd, "propertyName", { value: "transform" });
    track.dispatchEvent(transitionEnd);
    expect(currentLineText.textContent).toBe("found line");
    receive({
      current: "found line",
      next: "next line",
      progress: 0.4,
      currentLineIndex: 0,
      trackId: "track-a",
      isPureMusic: false,
      isPlaying: true,
      scene: "lyrics"
    });
    expect(nextLine.classList.contains("promoting")).toBe(false);
    expect(currentLineText.textContent).toBe("found line");
    while (pendingAnimationFrames.length > 0) {
      pendingAnimationFrames.shift()(0);
    }
  });

  it("enters lyrics without a separate dwell and coalesces duplicate result frames", async () => {
      const [html, bridge, state, presentation, script] = await Promise.all([
        read("TaskbarLyrics.App/Web/Lyrics/index.html"),
        read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
        read("TaskbarLyrics.App/Web/Lyrics/state.js"),
        read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
        read("TaskbarLyrics.App/Web/Lyrics/app.js")
      ]);
      const searchingLine = "正在检索歌词...";
      const dom = new JSDOM(html
        .replace("TaskbarLyrics started", searchingLine)
        .replace("Waiting for lyrics...", " ")
        .replace("{{STYLE_CSS}}", "")
        .replace("{{APP_JS}}", ""), {
        runScripts: "outside-only"
      });
      const pendingAnimationFrames = [];
      let now = 100;
      dom.window.CSS = { supports: () => true };
      dom.window.requestAnimationFrame = callback => {
        pendingAnimationFrames.push(callback);
        return pendingAnimationFrames.length;
      };
      dom.window.cancelAnimationFrame = () => {};
      let nextTimerId = 1;
      const scheduledTimers = new Map();
      dom.window.setTimeout = (callback, delay) => {
        const id = nextTimerId++;
        scheduledTimers.set(id, { callback, delay });
        return id;
      };
      dom.window.clearTimeout = id => scheduledTimers.delete(id);
      Object.defineProperty(dom.window.performance, "now", {
        configurable: true,
        value: () => now
      });
      dom.window.eval(bridge);
      dom.window.eval(state);
      dom.window.eval(presentation);
      dom.window.eval(script);

      const receive = current => dom.window.taskbarLyrics.receive({
        version: 1,
        type: "lyrics",
        payload: {
          current,
          next: "next",
          progress: 0.2,
          currentLineIndex: current === searchingLine ? -1 : 0,
          trackId: "track-dwell",
          isPureMusic: false,
          isPlaying: true,
          scene: current === searchingLine ? "searching" : "lyrics"
        }
      });

      receive(searchingLine);
      now = 200;
      receive("found line");
      const currentLineText = dom.window.document.querySelector("#currentLineText");
      const nextLine = dom.window.document.querySelector("#nextLine");
      const nextLineText = dom.window.document.querySelector("#nextLineText");
      expect(nextLine.classList.contains("promoting")).toBe(false);
      expect(currentLineText.textContent).toBe(searchingLine);
      expect(nextLineText.textContent).toBe("found line");
      const fallbackTimerId = [...scheduledTimers.keys()][0];
      expect(scheduledTimers.get(fallbackTimerId)?.delay).toBe(680);

      now = 300;
      receive("found line");
      expect(nextLine.classList.contains("promoting")).toBe(false);
      expect(currentLineText.textContent).toBe(searchingLine);
      expect(nextLineText.textContent).toBe("found line");
      expect(scheduledTimers.size).toBe(1);

      const transitionEnd = new dom.window.Event("transitionend", { bubbles: true });
      Object.defineProperty(transitionEnd, "propertyName", { value: "transform" });
      dom.window.document.querySelector("#track").dispatchEvent(transitionEnd);
      expect(currentLineText.textContent).toBe("found line");
  });

  it.each([
    { label: "searching", scene: "searching", sourceText: "正在检索歌词..." },
    { label: "no-playback", scene: "noPlayback", sourceText: "暂无播放内容，3 秒后自动隐藏" }
  ])("plans and executes a $label-to-spectrum line-pitch roll without rendering pure-music text", async ({ scene, sourceText }) => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const dom = new JSDOM(html
      .replace("TaskbarLyrics started", sourceText)
      .replace("Waiting for lyrics...", " ")
      .replace("{{STYLE_CSS}}", "")
      .replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    const pendingAnimationFrames = [];
    dom.window.CSS = { supports: () => true };
    dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        current: sourceText,
        next: "",
        progress: 0,
        currentLineIndex: -1,
        trackId: "",
        isPureMusic: false,
        isPlaying: true,
        animateTransition: false,
        scene
      }
    });
    const pureMusicText = "纯音乐";
    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        current: pureMusicText,
        next: "",
        progress: 0,
        currentLineIndex: -1,
        trackId: "",
        isPureMusic: true,
        isPlaying: true
      }
    });

    const layout = dom.window.document.querySelector("#layout");
    const lyricsLayer = dom.window.document.querySelector("#lyricsLayer");
    const spectrumLayer = dom.window.document.querySelector("#spectrumLayer");
    const currentLineText = dom.window.document.querySelector("#currentLineText");
    expect(layout.classList.contains("spectrum-mode")).toBe(true);
    expect(layout.classList.contains("spectrum-transitioning")).toBe(true);
    expect(currentLineText.textContent).toBe(sourceText);
    expect(currentLineText.textContent).not.toBe(pureMusicText);
    expect(lyricsLayer.style.transform).toBe("translateY(0)");
    expect(spectrumLayer.style.transform).toMatch(/translateY\(\d+(?:\.\d+)?px\)/);

    const startupFrames = pendingAnimationFrames.splice(0);
    expect(startupFrames.length).toBeGreaterThan(0);
    startupFrames.forEach(callback => callback(0));
    expect(lyricsLayer.style.opacity).toBe("");
    expect(lyricsLayer.style.transform).toBe("");
    expect(spectrumLayer.style.opacity).toBe("");
    expect(spectrumLayer.style.transform).toBe("");
    expect(layout.classList.contains("spectrum-transitioning")).toBe(true);
    const transitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(transitionEnd, "propertyName", { value: "transform" });
    spectrumLayer.dispatchEvent(transitionEnd);
    expect(layout.classList.contains("spectrum-transitioning")).toBe(false);
    expect(layout.classList.contains("spectrum-mode")).toBe(true);
    expect(currentLineText.textContent).toBe(sourceText);
  });

  it("switches searching to spectrum immediately when reduced motion is requested", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const searchingLine = "正在检索歌词...";
    const dom = new JSDOM(html
      .replace("TaskbarLyrics started", searchingLine)
      .replace("Waiting for lyrics...", " ")
      .replace("{{STYLE_CSS}}", "")
      .replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    dom.window.matchMedia = () => ({ matches: true, addEventListener() {}, removeEventListener() {} });
    dom.window.requestAnimationFrame = () => 1;
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        current: searchingLine,
        next: "",
        progress: 0,
        currentLineIndex: -1,
        trackId: "",
        isPureMusic: false,
        isPlaying: true
      }
    });
    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        current: "纯音乐",
        next: "",
        progress: 0,
        currentLineIndex: -1,
        trackId: "",
        isPureMusic: true,
        isPlaying: true
      }
    });

    const layout = dom.window.document.querySelector("#layout");
    expect(layout.classList.contains("spectrum-mode")).toBe(true);
    expect(layout.classList.contains("spectrum-transitioning")).toBe(false);
  });

  it("cancels a searching-to-spectrum roll when a lyric frame arrives", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const searchingLine = "正在检索歌词...";
    const dom = new JSDOM(html
      .replace("TaskbarLyrics started", searchingLine)
      .replace("Waiting for lyrics...", " ")
      .replace("{{STYLE_CSS}}", "")
      .replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    const pendingAnimationFrames = [];
    dom.window.CSS = { supports: () => true };
    dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({ version: 1, type: "lyrics", payload });
    receive({
      current: searchingLine,
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "",
      isPureMusic: false,
      isPlaying: true
    });
    receive({
      current: "纯音乐",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "",
      isPureMusic: true,
      isPlaying: true
    });
    const layout = dom.window.document.querySelector("#layout");
    const spectrumLayer = dom.window.document.querySelector("#spectrumLayer");
    expect(layout.classList.contains("spectrum-transitioning")).toBe(true);
    const staleStartupFrame = pendingAnimationFrames[0];

    receive({
      current: "Recovered lyric",
      next: "Next lyric",
      progress: 0,
      currentLineIndex: 0,
      trackId: "",
      isPureMusic: false,
      isPlaying: true
    });
    spectrumLayer.style.opacity = "0.31";
    staleStartupFrame?.(0);
    expect(spectrumLayer.style.opacity).toBe("0.31");
    expect(layout.classList.contains("spectrum-transitioning")).toBe(false);
    expect(layout.classList.contains("spectrum-mode")).toBe(false);
    // Stale spectrum frames are cancelled; only the new lyric transition may remain queued.
    expect(pendingAnimationFrames.length).toBeGreaterThan(0);
  });

  it("renders search metadata above the prompt and enters lyrics without an extra dwell", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const searchingPrompt = "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd...";
    const dom = new JSDOM(html
      .replace("TaskbarLyrics started", searchingPrompt)
      .replace("Waiting for lyrics...", " ")
      .replace("{{STYLE_CSS}}", "")
      .replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = () => 1;
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload
    });
    const currentLineText = dom.window.document.querySelector("#currentLineText");
    const nextLineText = dom.window.document.querySelector("#nextLineText");
    receive({
      current: "Song - Artist",
      next: searchingPrompt,
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-search",
      isPureMusic: false,
      isPlaying: true,
      scene: "searching"
    });
    expect(currentLineText.textContent).toBe("Song - Artist");
    expect(nextLineText.textContent).toBe(searchingPrompt);

    receive({
      current: "Found lyric",
      next: "Next lyric",
      progress: 0.2,
      currentLineIndex: 0,
      trackId: "track-search",
      isPureMusic: false,
      isPlaying: true,
      scene: "lyrics"
    });
    expect(currentLineText.textContent).toBe("Song - Artist");
    expect(nextLineText.textContent).toBe("Found lyric");
    const transitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(transitionEnd, "propertyName", { value: "transform" });
    dom.window.document.querySelector("#track").dispatchEvent(transitionEnd);
    expect(currentLineText.textContent).toBe("Found lyric");
  });

  it("uses the searching scene for the search-to-spectrum roll even when metadata is shown", async () => {
    const [html, bridge, state, presentation, script, style] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js"),
      read("TaskbarLyrics.App/Web/Lyrics/style.css")
    ]);
    const searchingPrompt = "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd...";
    const dom = new JSDOM(html
      .replace("TaskbarLyrics started", searchingPrompt)
      .replace("Waiting for lyrics...", " ")
      .replace("{{STYLE_CSS}}", "")
      .replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    const pendingAnimationFrames = [];
    dom.window.CSS = { supports: () => true };
    dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload
    });
    receive({
      current: "Song - Artist",
      next: searchingPrompt,
      progress: 0,
      currentLineIndex: -1,
      trackId: "",
      isPureMusic: false,
      isPlaying: true,
      scene: "searching"
    });
    const spectrumPayload = {
      current: "Pure music marker",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "",
      isPureMusic: true,
      isPlaying: true,
      scene: "spectrum"
    };
    receive(spectrumPayload);

    const layout = dom.window.document.querySelector("#layout");
    const spectrumLayer = dom.window.document.querySelector("#spectrumLayer");
    expect(layout.classList.contains("spectrum-transitioning")).toBe(true);
    expect(layout.classList.contains("spectrum-entry-active")).toBe(false);
    expect(layout.style.getPropertyValue("--layer-transition-duration")).toBe("760ms");
    expect(spectrumLayer.style.transform).toMatch(/^translateY\(\d+(?:\.\d+)?px\)$/);
    expect(style).toMatch(/\.layout\.spectrum-transitioning \.lyrics-layer,\s*\.layout\.spectrum-transitioning \.spectrum-layer\s*\{\s*transition:\s*none/s);
    expect(dom.window.document.querySelector("#currentLineText").textContent).toBe("Song - Artist");
    receive({ ...spectrumPayload, trackId: "latest-spectrum-frame" });
    expect(pendingAnimationFrames).toHaveLength(1);
    const startEntry = pendingAnimationFrames.shift();
    expect(startEntry).toBeTypeOf("function");
    startEntry(0);
    expect(layout.classList.contains("spectrum-entry-active")).toBe(true);
    expect(spectrumLayer.style.transform).toBe("");
    pendingAnimationFrames.splice(0).forEach(callback => callback(0));
  });

  it("finishes a direct searching-track roll before applying a fast lyric result", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const searchingPrompt = "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd...";
    const dom = new JSDOM(html
      .replace("TaskbarLyrics started", searchingPrompt)
      .replace("Waiting for lyrics...", " ")
      .replace("{{STYLE_CSS}}", "")
      .replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    const pendingAnimationFrames = [];
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload
    });
    const searchPayload = (trackId, current) => ({
      current,
      next: searchingPrompt,
      progress: 0,
      currentLineIndex: -1,
      trackId,
      isPureMusic: false,
      isPlaying: true,
      scene: "searching"
    });
    receive(searchPayload("track-a", "Song A - Artist A"));
    receive(searchPayload("track-b", "Song B - Artist B"));

    expect(dom.window.document.querySelector("#currentLineText").textContent)
      .toBe("Song A - Artist A");
    expect(dom.window.document.querySelector("#nextLineText").textContent)
      .toBe("Song B - Artist B");

    receive({
      current: "Found lyric",
      next: "Next lyric",
      progress: 0.2,
      currentLineIndex: 0,
      trackId: "track-b",
      isPureMusic: false,
      isPlaying: true,
      scene: "lyrics"
    });
    expect(dom.window.document.querySelector("#currentLineText").textContent)
      .toBe("Song A - Artist A");
    expect(dom.window.document.querySelector("#nextLineText").textContent)
      .toBe("Song B - Artist B");

    pendingAnimationFrames.shift()(0);
    expect(dom.window.document.querySelector("#track").classList.contains("animating")).toBe(true);
    const transitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(transitionEnd, "propertyName", { value: "transform" });
    dom.window.document.querySelector("#track").dispatchEvent(transitionEnd);

    expect(dom.window.document.querySelector("#currentLineText").textContent)
      .toBe("Song B - Artist B");
    expect(dom.window.document.querySelector("#nextLineText").textContent)
      .toBe("Found lyric");
  });

  it("finishes a direct searching-track roll before entering a fast spectrum result", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const searchingPrompt = "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd...";
    const dom = new JSDOM(html
      .replace("TaskbarLyrics started", searchingPrompt)
      .replace("Waiting for lyrics...", " ")
      .replace("{{STYLE_CSS}}", "")
      .replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    const pendingAnimationFrames = [];
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload
    });
    const searchPayload = (trackId, current) => ({
      current,
      next: searchingPrompt,
      progress: 0,
      currentLineIndex: -1,
      trackId,
      isPureMusic: false,
      isPlaying: true,
      scene: "searching"
    });
    const layout = dom.window.document.querySelector("#layout");
    const track = dom.window.document.querySelector("#track");
    const currentLineText = dom.window.document.querySelector("#currentLineText");
    const nextLineText = dom.window.document.querySelector("#nextLineText");

    receive(searchPayload("track-a", "Song A - Artist A"));
    receive(searchPayload("track-b", "Song B - Artist B"));
    receive({
      current: "Pure music",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-b",
      isPureMusic: true,
      isPlaying: true,
      scene: "spectrum"
    });

    expect(layout.classList.contains("spectrum-transitioning")).toBe(false);
    expect(currentLineText.textContent).toBe("Song A - Artist A");
    expect(nextLineText.textContent).toBe("Song B - Artist B");

    const transitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(transitionEnd, "propertyName", { value: "transform" });
    track.dispatchEvent(transitionEnd);

    expect(currentLineText.textContent).toBe("Song B - Artist B");
    expect(nextLineText.textContent).toBe(searchingPrompt);
    expect(layout.classList.contains("spectrum-transitioning")).toBe(true);
  });

  it("exits a stable spectrum toward searching from below and clears exit styles on completion", async () => {
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
    const pendingAnimationFrames = [];
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload
    });
    const searchingPrompt = "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd...";
    receive({
      current: "Pure music",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-a",
      isPureMusic: true,
      isPlaying: true,
      scene: "spectrum",
      animateTransition: false
    });
    const layout = dom.window.document.querySelector("#layout");
    const lyricsLayer = dom.window.document.querySelector("#lyricsLayer");
    const spectrumLayer = dom.window.document.querySelector("#spectrumLayer");
    const track = dom.window.document.querySelector("#track");
    const currentLineText = dom.window.document.querySelector("#currentLineText");
    const nextLineText = dom.window.document.querySelector("#nextLineText");
    expect(style).toMatch(/\.layout\.spectrum-mode\.spectrum-exiting\.spectrum-exit-active \.lyrics-layer/);
    expect(style).toMatch(/\.layout\.spectrum-mode\.spectrum-exiting\.spectrum-exit-active \.spectrum-layer/);
    expect(layout.classList.contains("spectrum-mode")).toBe(true);

    receive({
      current: "Song A - Artist A",
      next: searchingPrompt,
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-a",
      isPureMusic: false,
      isPlaying: true,
      scene: "searching"
    });
    expect(layout.classList.contains("spectrum-mode")).toBe(true);
    expect(layout.classList.contains("spectrum-exiting")).toBe(false);
    expect(currentLineText.textContent).not.toBe("Song A - Artist A");

    receive({
      current: "Song B - Artist B",
      next: searchingPrompt,
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-b",
      isPureMusic: false,
      isPlaying: true,
      scene: "searching"
    });
    expect(layout.classList.contains("spectrum-mode")).toBe(true);
    expect(layout.classList.contains("spectrum-exiting")).toBe(true);
    expect(layout.style.getPropertyValue("--layer-transition-duration")).toBe("760ms");
    expect(lyricsLayer.style.transform).toMatch(/^translateY\(\d+(?:\.\d+)?px\)$/);
    expect(spectrumLayer.style.transform).toBe("translateY(0)");
    expect(currentLineText.textContent).toBe("Song B - Artist B");
    expect(nextLineText.textContent).toBe(searchingPrompt);

    const startExit = pendingAnimationFrames.shift();
    expect(startExit).toBeTypeOf("function");
    startExit(0);
    expect(lyricsLayer.style.transform).toBe("translateY(0)");
    expect(spectrumLayer.style.transform).toMatch(/^translateY\(-\d+(?:\.\d+)?px\)$/);

    receive({
      current: "暂无播放内容，3 秒后自动隐藏",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "",
      isPureMusic: false,
      isPlaying: false,
      scene: "noPlayback"
    });
    expect(currentLineText.textContent).toBe("暂无播放内容，3 秒后自动隐藏");
    expect(nextLineText.textContent.trim()).toBe("");

    const transitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(transitionEnd, "propertyName", { value: "transform" });
    spectrumLayer.dispatchEvent(transitionEnd);
    expect(layout.classList.contains("spectrum-mode")).toBe(false);
    expect(layout.classList.contains("spectrum-exiting")).toBe(false);
    expect(lyricsLayer.style.transform).toBe("");
    expect(spectrumLayer.style.transform).toBe("");
    expect(currentLineText.textContent).toBe("暂无播放内容，3 秒后自动隐藏");
    expect(nextLineText.textContent.trim()).toBe("");
    expect(track.classList.contains("animating")).toBe(false);
  });

  it("applies queued lyrics as soon as the spectrum exit completes", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const searchingPrompt = "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd...";
    const pendingAnimationFrames = [];
    const dom = new JSDOM(html
      .replace("TaskbarLyrics started", searchingPrompt)
      .replace("Waiting for lyrics...", " ")
      .replace("{{STYLE_CSS}}", "")
      .replace("{{APP_JS}}", ""), {
        runScripts: "outside-only"
      });
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload
    });
    const spectrumLayer = dom.window.document.querySelector("#spectrumLayer");
    const lyricsLayer = dom.window.document.querySelector("#lyricsLayer");
    const layout = dom.window.document.querySelector("#layout");
    const track = dom.window.document.querySelector("#track");
    const currentLineText = dom.window.document.querySelector("#currentLineText");
    const nextLineText = dom.window.document.querySelector("#nextLineText");
    receive({
      current: "Pure music",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-a",
      isPureMusic: true,
      isPlaying: true,
      scene: "spectrum",
      animateTransition: false
    });
    receive({
      current: "Song B - Artist B",
      next: searchingPrompt,
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-b",
      isPureMusic: false,
      isPlaying: true,
      scene: "searching"
    });
    expect(layout.classList.contains("spectrum-exiting")).toBe(true);
    expect(currentLineText.textContent).toBe("Song B - Artist B");
    expect(nextLineText.textContent).toBe(searchingPrompt);

    receive({
      current: "Found lyric",
      next: "Next lyric",
      progress: 0.2,
      currentLineIndex: 0,
      trackId: "track-b",
      isPureMusic: false,
      isPlaying: true,
      scene: "lyrics"
    });
    expect(currentLineText.textContent).toBe("Song B - Artist B");
    expect(nextLineText.textContent).toBe(searchingPrompt);

    const startExit = pendingAnimationFrames.shift();
    expect(startExit).toBeTypeOf("function");
    startExit(0);
    const spectrumTransitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(spectrumTransitionEnd, "propertyName", { value: "transform" });
    spectrumLayer.dispatchEvent(spectrumTransitionEnd);
    expect(layout.classList.contains("spectrum-mode")).toBe(false);
    expect(currentLineText.textContent).toBe("Song B - Artist B");
    expect(nextLineText.textContent).toBe("Found lyric");
    const lyricTransitionEnd = new dom.window.Event("transitionend", { bubbles: true });
    Object.defineProperty(lyricTransitionEnd, "propertyName", { value: "transform" });
    track.dispatchEvent(lyricTransitionEnd);
    expect(currentLineText.textContent).toBe("Found lyric");
  });

  it("switches out of stable spectrum immediately for animate=false and reduced motion", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    let reducedMotion = false;
    const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    dom.window.matchMedia = () => ({
      matches: reducedMotion,
      addEventListener() {},
      removeEventListener() {}
    });
    dom.window.requestAnimationFrame = () => 1;
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload
    });
    const spectrumPayload = {
      current: "Pure music",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-a",
      isPureMusic: true,
      isPlaying: true,
      scene: "spectrum"
    };
    receive({ ...spectrumPayload, animateTransition: false });
    receive({
      current: "Immediate lyric",
      next: "Next lyric",
      progress: 0,
      currentLineIndex: 0,
      trackId: "track-b",
      isPureMusic: false,
      isPlaying: true,
      scene: "lyrics",
      animateTransition: false
    });
    const layout = dom.window.document.querySelector("#layout");
    expect(layout.classList.contains("spectrum-mode")).toBe(false);
    expect(layout.classList.contains("spectrum-exiting")).toBe(false);
    expect(dom.window.document.querySelector("#currentLineText").textContent).toBe("Immediate lyric");

    receive({ ...spectrumPayload, animateTransition: false });
    reducedMotion = true;
    receive({
      current: "Reduced motion message",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-c",
      isPureMusic: false,
      isPlaying: true,
      scene: "message"
    });
    expect(layout.classList.contains("spectrum-mode")).toBe(false);
    expect(layout.classList.contains("spectrum-exiting")).toBe(false);
    expect(dom.window.document.querySelector("#currentLineText").textContent)
      .toBe("Reduced motion message");
  });

  it("does not let a cancelled spectrum exit rAF restore stale styles", async () => {
    const [html, bridge, state, presentation, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const pendingAnimationFrames = [];
    const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = callback => {
      pendingAnimationFrames.push(callback);
      return pendingAnimationFrames.length;
    };
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(bridge);
    dom.window.eval(state);
    dom.window.eval(presentation);
    dom.window.eval(script);

    const receive = payload => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload
    });
    receive({
      current: "Pure music",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-a",
      isPureMusic: true,
      isPlaying: true,
      scene: "spectrum",
      animateTransition: false
    });
    receive({
      current: "First target",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-b",
      isPureMusic: false,
      isPlaying: true,
      scene: "message"
    });
    const staleStart = pendingAnimationFrames.shift();
    expect(staleStart).toBeTypeOf("function");

    receive({
      current: "Pure music again",
      next: "",
      progress: 0,
      currentLineIndex: -1,
      trackId: "track-c",
      isPureMusic: true,
      isPlaying: true,
      scene: "spectrum"
    });
    staleStart(0);
    const layout = dom.window.document.querySelector("#layout");
    const lyricsLayer = dom.window.document.querySelector("#lyricsLayer");
    const spectrumLayer = dom.window.document.querySelector("#spectrumLayer");
    expect(layout.classList.contains("spectrum-mode")).toBe(true);
    expect(layout.classList.contains("spectrum-exiting")).toBe(false);
    expect(lyricsLayer.style.transform).toBe("");
    expect(spectrumLayer.style.transform).toBe("");
  });
});
