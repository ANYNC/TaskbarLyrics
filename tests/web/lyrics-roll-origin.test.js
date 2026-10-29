import { afterEach, describe, expect, it } from "vitest";
import { loadLyricsAssets, createLyricsDom, runLyricsScripts } from "./helpers/lyrics-dom.js";

let dom;
afterEach(() => { dom?.window.close(); dom = null; });

async function prepare({ dpr = 1.5, vertical = false, singleLine = false, translationMode = false } = {}) {
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
  const receive = (index, trackId = "origin", animateTransition = true) => window.taskbarLyrics.receive({
    version: 1, type: "lyrics", payload: {
      current: index === 0 ? "First line" : "Second line",
      next: index === 0 ? "Second line" : "Third line",
      currentTranslation: index === 0 ? "译一" : "译二", nextTranslation: "译三", translationMode,
      currentLineIndex: index, trackId, progress: 0.2, isPlaying: true, animateTransition
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
});
