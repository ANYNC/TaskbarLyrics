import { loadLyricsAssets, createLyricsDom, runLyricsScripts } from "./helpers/lyrics-dom.js";
import { describe, expect, it } from "vitest";

async function createAlignmentDom() {
  const { html, bridge, state, presentation, script, style } = await loadLyricsAssets();
  const dom = createLyricsDom(html);
  dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  dom.window.requestAnimationFrame = () => 1;
  dom.window.cancelAnimationFrame = () => {};
  runLyricsScripts(dom, { bridge, state, presentation, script });
  return { dom, style };
}

describe("lyrics text alignment", () => {
  it.each([
    { showCover: false, coverPosition: "Left" },
    { showCover: true, coverPosition: "Left" },
    { showCover: true, coverPosition: "Right" }
  ])("aligns the spectrum container and its visible bars with cover $showCover/$coverPosition", async cover => {
    const { dom, style } = await createAlignmentDom();
    const document = dom.window.document;
    document.querySelector("style").textContent = style;
    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: { scene: "spectrum", isPureMusic: true, isPlaying: true, animateTransition: false }
    });

    try {
      // The fixed-width spectrum can contain fewer bars than fit its box.
      // Both flex levels must align, including live changes back to left.
      for (const [textAlignment, expected] of [
        ["Right", "flex-end"], ["Center", "center"], ["Left", "flex-start"], ["invalid", "flex-start"]
      ]) {
        dom.window.taskbarLyrics.receive({
          version: 1,
          type: "style",
          payload: { ...cover, textAlignment }
        });
        expect(document.querySelector("#layout").classList.contains("spectrum-mode")).toBe(true);
        for (const selector of [".spectrum-layer", ".spectrum"]) {
          expect(dom.window.getComputedStyle(document.querySelector(selector)).justifyContent).toBe(expected);
        }
      }
    } finally {
      dom.window.close();
    }
  });

  it("accepts the three style values and safely falls back to left", async () => {
    const { dom } = await createAlignmentDom();
    const layout = dom.window.document.querySelector("#layout");
    const receiveStyle = textAlignment => dom.window.taskbarLyrics.receive({
      version: 1,
      type: "style",
      payload: { textAlignment }
    });

    receiveStyle("Center");
    expect(layout.dataset.textAlignment).toBe("Center");
    expect(dom.window.document.documentElement.style.getPropertyValue("--line-transform-origin")).toBe("center center");

    receiveStyle("Right");
    expect(layout.dataset.textAlignment).toBe("Right");
    expect(dom.window.document.documentElement.style.getPropertyValue("--line-transform-origin")).toBe("right center");

    receiveStyle("invalid");
    expect(layout.dataset.textAlignment).toBe("Left");
    expect(dom.window.document.documentElement.style.getPropertyValue("--line-transform-origin")).toBe("left center");
  });

  it("keeps short lines aligned while horizontal scanning always starts from the sentence head", async () => {
    const { dom, style } = await createAlignmentDom();
    const document = dom.window.document;
    const currentLine = document.querySelector("#currentLine");
    const viewport = currentLine.querySelector(".line-text-viewport");
    const text = currentLine.querySelector(".line-text-base");
    Object.defineProperty(viewport, "clientWidth", { configurable: true, value: 100 });
    Object.defineProperty(text, "scrollWidth", { configurable: true, value: 400 });

    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "style",
      payload: { textAlignment: "Right" }
    });
    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "lyrics",
      payload: {
        current: "a long lyric sentence",
        next: "next",
        currentLineIndex: 0,
        progress: 0,
        wordScanProgress: 0.1,
        trackId: "track",
        isPureMusic: false,
        isPlaying: true,
        animateTransition: false
      }
    });

    expect(currentLine.classList.contains("horizontal-scrolling")).toBe(true);
    expect(currentLine.querySelector(".line-text-stack").style.getPropertyValue("--line-scroll-offset")).toBe("0px");
    expect(style).toMatch(/\.layout\[data-text-alignment="Right"\] \.line-text-stack\s*\{[^}]*margin-inline-start:\s*auto/s);
    expect(style).toMatch(/\.line\.horizontal-scrolling \.line-text-stack\s*\{[^}]*margin-inline-start:\s*0[^}]*margin-inline-end:\s*0/s);
  });

  it("uses the same alignment container for translation and incoming transition rows", async () => {
    const { dom, style } = await createAlignmentDom();
    const layout = dom.window.document.querySelector("#layout");

    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "style",
      payload: { textAlignment: "Center" }
    });

    expect(layout.querySelectorAll(".line-text-stack")).toHaveLength(5);
    expect(layout.querySelector("#incomingLine .line-text-stack")).not.toBeNull();
    expect(layout.querySelector("#incomingTranslationPair .line-text-stack")).not.toBeNull();
    expect(style).toMatch(/\.layout\[data-text-alignment="Center"\] \.line-text-stack/);
    expect(style).toMatch(/\.line\s*\{[^}]*transform-origin:\s*var\(--line-transform-origin\)/s);
  });
});
