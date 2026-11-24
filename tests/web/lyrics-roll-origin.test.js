import { afterEach, describe, expect, it } from "vitest";
import { loadLyricsAssets, createLyricsDom, runLyricsScripts } from "./helpers/lyrics-dom.js";

let dom;
afterEach(() => { dom?.window.close(); dom = null; });

async function prepare({
  dpr = 1.5, vertical = false, singleLine = false, translationMode = false,
  translations = ["译一", "译二", "译三"], wordScanProgress = null
} = {}) {
  const assets = await loadLyricsAssets();
  dom = createLyricsDom(assets.html);
  const { window } = dom;
  const root = window.document.documentElement;
  if (vertical) root.classList.add("vertical-taskbar");
  if (singleLine) root.classList.add("single-line");
  Object.defineProperty(window, "devicePixelRatio", { configurable: true, value: dpr });
  const viewport = window.document.querySelector("#viewport");
  Object.defineProperty(viewport, "clientHeight", { configurable: true, value: singleLine ? 35 : 36 });
  Object.defineProperty(viewport, "clientWidth", { configurable: true, value: 34 });
  let frames = [];
  window.requestAnimationFrame = callback => { frames.push(callback); return frames.length; };
  window.cancelAnimationFrame = () => {};
  runLyricsScripts(dom, assets);
  const track = window.document.querySelector("#track");
  const lyrics = ["First line", "Second line", "Third line"];
  const receive = (index, trackId = "origin", animateTransition = true) => window.taskbarLyrics.receive({
    version: 1, type: "lyrics", payload: {
      current: lyrics[index], next: lyrics[index + 1],
      currentTranslation: translations[index], nextTranslation: translations[index + 1], translationMode,
      currentLineIndex: index, trackId, progress: 0.2, wordScanProgress, isPlaying: true, animateTransition
    }
  });
  const advance = () => { const pending = frames; frames = []; pending.forEach(callback => callback(0)); };
  const finish = () => {
    const event = new window.Event("transitionend", { bubbles: true });
    Object.defineProperty(event, "propertyName", { value: "transform" });
    track.dispatchEvent(event);
  };
  receive(0, "origin", false);
  receive(1);
  return { root, track, receive, advance, finish };
}

describe("lyric roll painting origin", () => {
  it.each([1, 1.25, 1.5, 2])("keeps horizontal travel equal to the CSS pitch at DPR %s", async dpr => {
    const { root, track, advance, finish } = await prepare({ dpr });
    expect(track.classList.contains("stable-promotion")).toBe(true);
    const pitch = Number.parseFloat(root.style.getPropertyValue("--line-pitch"));
    expect(pitch).toBe(17);
    advance();
    expect(track.style.transform).toBe(`translateY(${-pitch}px)`);
    finish();
    expect(track.style.transform).toBe("");
    expect(track.classList.contains("stable-promotion")).toBe(false);
  });

  it("clears the painting-origin state when the roll is cancelled", async () => {
    const { track, receive, advance } = await prepare();
    advance();
    receive(0, "replacement", false);
    expect(track.classList.contains("stable-promotion")).toBe(false);
    expect(track.style.transform).toBe("");
  });

  it("preserves vertical taskbar travel", async () => {
    const { root, track, advance } = await prepare({ vertical: true });
    advance();
    const pitch = Number.parseFloat(root.style.getPropertyValue("--line-pitch"));
    expect(track.classList.contains("stable-promotion")).toBe(false);
    expect(track.style.transform).toBe(`translateX(${Math.round(pitch * 1.5) / 1.5}px)`);
  });

  it("preserves single-line travel", async () => {
    const { root, track, advance } = await prepare({ singleLine: true });
    advance();
    const pitch = Number.parseFloat(root.style.getPropertyValue("--line-pitch"));
    expect(track.classList.contains("stable-promotion")).toBe(false);
    expect(track.style.transform).toBe(`translateY(${Math.round(-pitch * 1.5) / 1.5}px)`);
  });

  it("preserves translation-pair travel", async () => {
    const { root, track, advance } = await prepare({ dpr: 1.25, translationMode: true });
    advance(); advance();
    const pitch = Number.parseFloat(root.style.getPropertyValue("--line-pitch"));
    expect(track.classList.contains("stable-promotion")).toBe(false);
    expect(track.style.transform).toBe(`translateY(${Math.round(-2 * pitch * 1.25) / 1.25}px)`);
  });

  it.each([false, true])("rolls two rows out of a translation and one out of a preview (vertical=%s)", async vertical => {
    const { root, track, receive, advance, finish } = await prepare({
      dpr: 1, vertical, translationMode: true, translations: ["译一", " ", "译三"], wordScanProgress: 0.4
    });
    const element = id => dom.window.document.getElementById(id);
    const text = id => element(id).textContent;
    const pitch = Number.parseFloat(root.style.getPropertyValue("--line-pitch"));
    const offset = rows => vertical ? `translateX(${rows * pitch}px)` : `translateY(${-rows * pitch}px)`;

    expect(text("currentLineText")).toBe("First line");
    expect(text("nextLineText")).toBe("译一");
    expect(text("incomingTranslationOriginalText")).toBe("Second line");
    expect(text("incomingTranslationText")).toBe("Third line");
    expect(element("incomingTranslationLine").classList.contains("next-line")).toBe(true);
    advance();
    advance();
    expect(track.style.transform).toBe(offset(2));
    finish();
    expect(text("currentLineText")).toBe("Second line");
    expect(text("nextLineText")).toBe("Third line");

    const previewText = element("nextLineText").firstChild;
    receive(2);
    expect(element("nextLineText").firstChild).toBe(previewText);
    expect(text("incomingLineText")).toBe("译三");
    expect(element("incomingLine").classList.contains("translation-line")).toBe(true);
    expect(element("nextLine").style.getPropertyValue("--word-scan-progress")).toBe("40.000%");
    advance();
    expect(track.style.transform).toBe(offset(1));
    finish();
    expect(text("currentLineText")).toBe("Third line");
    expect(text("nextLineText")).toBe("译三");
    expect(element("currentLine").style.getPropertyValue("--word-scan-progress")).toBe("40.000%");
    expect(element("incomingLine").classList.contains("translation-line")).toBe(false);
    expect(track.style.transform).toBe("");
  });

  it("leaves the secondary row empty after seeking to an untranslated final line", async () => {
    const { track, receive } = await prepare({ translationMode: true, translations: ["译一", "", ""] });
    receive(2, "origin", false);
    expect(dom.window.document.getElementById("currentLineText").textContent).toBe("Third line");
    expect(dom.window.document.getElementById("nextLineText").textContent.trim()).toBe("");
    expect(track.style.transform).toBe("");
    expect(track.classList.contains("animating")).toBe(false);
  });
});
