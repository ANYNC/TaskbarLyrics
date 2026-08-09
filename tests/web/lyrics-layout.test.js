import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");

describe("lyrics responsive layout", () => {
  it("renders and clears word-scan progress without changing the secondary line", async () => {
    const [html, state, script, style] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js"),
      read("TaskbarLyrics.App/Web/Lyrics/style.css")
    ]);
    const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = () => 1;
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(state);
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
    expect(style).toMatch(/@media\s*\(prefers-reduced-motion:\s*reduce\)[\s\S]*\.line\.horizontal-scrolling\.word-scan-smoothing \.line-text-stack\s*\{[^}]*transition:\s*none;[^}]*will-change:\s*auto/s);
    expect(style).toContain("--primary: rgba(255, 255, 255, 1)");
    expect(style).toContain("--secondary: rgba(255, 255, 255, 0.60)");
    const primaryFallback = style.match(/--primary:\s*rgba\((\d+),\s*(\d+),\s*(\d+),/);
    const secondaryFallback = style.match(/--secondary:\s*rgba\((\d+),\s*(\d+),\s*(\d+),\s*([\d.]+)\)/);
    expect(primaryFallback).not.toBeNull();
    expect(secondaryFallback).not.toBeNull();
    expect(secondaryFallback?.slice(1, 4)).toEqual(primaryFallback?.slice(1, 4));
    expect(Number(secondaryFallback?.[4])).toBeCloseTo(0.60, 5);
    expect(style).toMatch(/@property\s+--word-scan-progress\s*\{[^}]*syntax:\s*"<percentage>"[^}]*initial-value:\s*0%/s);
    expect(style).toMatch(/\.line\.word-scan-smoothing\s*\{[^}]*--word-scan-progress\s+90ms\s+linear/s);
    expect(style).toMatch(/\.line-text-scan\s*\{[^}]*visibility:\s*hidden/s);
    expect(style).toContain("clip-path: inset(0 100% 0 0)");
    expect(style).toContain("clip-path: inset(0 0 0 var(--word-scan-progress))");
    expect(style).toContain("clip-path: inset(0 calc(100% - var(--word-scan-progress)) 0 0)");
    expect(style).not.toContain("mask-image");
    expect(style).not.toContain("-webkit-mask-image");
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
    expect(currentLine.style.getPropertyValue("--word-scan-progress")).toBe("90.000%");
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("-200px");

    receiveLyrics(0.2, false);
    expect(currentLine.classList.contains("word-scan-smoothing")).toBe(false);
    expect(currentLineStack.style.getPropertyValue("--line-scroll-offset")).toBe("0px");

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

  it("carries word-scan progress through queued line transitions", async () => {
    const [html, state, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
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
    dom.window.eval(state);
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
    expect(nextLine.style.transform).toBe("scale(var(--promotion-scale))");
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
    const [html, state, script, style] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
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
    dom.window.eval(state);
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
    expect(style).toMatch(/@media\s*\(prefers-reduced-motion:\s*reduce\)[\s\S]*\.track\.animating\.translation-pair-animating[\s\S]*transition:\s*none/s);

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
    expect(incomingPair.classList.contains("preparing")).toBe(true);
    expect(incomingTranslationText.textContent).toBe("…");
    expect(incomingTranslationLine.classList.contains("translation-placeholder")).toBe(true);
    const reducedMotionStart = pendingAnimationFrames.shift();
    expect(reducedMotionStart).toBeTypeOf("function");
    reducedMotionStart(0);
    const reducedMotionOffset = pendingAnimationFrames.shift();
    expect(reducedMotionOffset).toBeTypeOf("function");
    reducedMotionOffset(0);
    const reducedMotionFinish = pendingAnimationFrames.shift();
    expect(reducedMotionFinish).toBeTypeOf("function");
    reducedMotionFinish(0);
    expect(track.style.transform).toBe("");
    expect(track.classList.contains("animating")).toBe(false);
    expect(track.classList.contains("translation-pair-animating")).toBe(false);
    expect(incomingPair.classList.contains("preparing")).toBe(false);
    expect(incomingPair.classList.contains("entering")).toBe(false);
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
    const [html, state, script, style] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
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
    dom.window.eval(state);
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

  it("keeps every spectrum bar visible when scaled geometry exceeds the viewport", async () => {
    const [html, state, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
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
    dom.window.eval(state);
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
        spectrumBarWidth: 9
      }
    });

    const fittedBarWidth = Number.parseFloat(spectrum.style.getPropertyValue("--spectrum-fitted-bar-width"));
    const fittedGap = Number.parseFloat(spectrum.style.getPropertyValue("--spectrum-fitted-gap"));
    const fittedTotalWidth = (fittedBarWidth * 32) + (fittedGap * 31);

    expect(spectrum.children).toHaveLength(32);
    expect(fittedBarWidth).toBeGreaterThanOrEqual(1);
    expect(fittedGap).toBeGreaterThanOrEqual(0);
    expect(fittedTotalWidth).toBeLessThanOrEqual(120);
  });
});
