import { afterEach, describe, expect, it, vi } from "vitest";
import { loadLyricsAssets, createLyricsDom, runLyricsScripts } from "./helpers/lyrics-dom.js";

let dom;
afterEach(() => {
  dom?.window.close();
  dom = null;
  vi.useRealTimers();
});

async function startRoll() {
  const assets = await loadLyricsAssets();
  dom = createLyricsDom(assets.html);
  const { window } = dom;
  let now = 0;
  let sequence = 0;
  const frames = new Map();
  vi.spyOn(window.performance, "now").mockImplementation(() => now);
  window.requestAnimationFrame = callback => { frames.set(++sequence, callback); return sequence; };
  window.cancelAnimationFrame = id => frames.delete(id);
  runLyricsScripts(dom, assets);
  const document = window.document;
  const track = document.querySelector("#track");
  const current = document.querySelector("#currentLine");
  const next = document.querySelector("#nextLine");
  const currentText = document.querySelector("#currentLineText");
  const receive = (index, trackId = "timing", animateTransition = true) => window.taskbarLyrics.receive({
    version: 1, type: "lyrics", payload: {
      current: index === 0 ? "First line" : "Second line",
      next: index === 0 ? "Second line" : "Third line",
      currentLineIndex: index, trackId, progress: 0.2, isPlaying: true, animateTransition
    }
  });
  const advance = timestamp => {
    now = timestamp;
    const pending = Array.from(frames.values());
    frames.clear();
    pending.forEach(callback => callback(timestamp));
  };
  const end = (element, propertyName = "transform") => {
    const event = new window.Event("transitionend", { bubbles: true });
    Object.defineProperty(event, "propertyName", { value: propertyName });
    element.dispatchEvent(event);
  };
  receive(0, "timing", false);
  receive(1);
  return { window, track, current, next, currentText, frames, receive, advance, end };
}

describe("lyric roll timing", () => {
  it("starts scaling and track travel in the same frame", async () => {
    const { track, next, advance } = await startRoll();
    expect(track.style.transform).toBe("");
    expect(next.style.getPropertyValue("--promotion-scale")).not.toBe("1");
    advance(0);
    expect(next.style.getPropertyValue("--promotion-scale")).toBe("1");
    expect(track.classList.contains("animating")).toBe(true);
    expect(track.style.transform).toMatch(/^translateY\(-\d+(?:\.\d+)?px\)$/);
  });

  it("waits for promotion after track travel ends and ignores unrelated events", async () => {
    const { track, current, next, currentText, advance, end } = await startRoll();
    advance(0);
    track.getAnimations = () => [];
    next.getAnimations = () => [{ transitionProperty: "transform", playState: "running" }];
    end(track, "opacity");
    end(current);
    end(track);
    expect(currentText.textContent).toBe("First line");
    expect(track.classList.contains("animating")).toBe(true);
    next.getAnimations = () => [];
    end(next);
    expect(currentText.textContent).toBe("Second line");
    expect(track.classList.contains("animating")).toBe(false);
  });

  it("waits for track travel when promotion ends first", async () => {
    const { track, next, currentText, advance, end } = await startRoll();
    advance(0);
    track.getAnimations = () => [{ transitionProperty: "transform", playState: "running" }];
    next.getAnimations = () => [];
    end(next);
    expect(currentText.textContent).toBe("First line");
    track.getAnimations = () => [];
    end(track);
    expect(currentText.textContent).toBe("Second line");
  });

  it("does not wait for a nonexistent scale animation or other animation channels", async () => {
    const { track, next, currentText, advance, end } = await startRoll();
    advance(0);
    track.getAnimations = () => [{ transitionProperty: "transform", playState: "finished" }];
    next.getAnimations = () => [{ transitionProperty: "--word-scan-progress", playState: "running" }];
    end(track);
    expect(currentText.textContent).toBe("Second line");
  });

  it("retains the fallback if transition events are missing", async () => {
    vi.useFakeTimers();
    const { currentText, advance } = await startRoll();
    advance(0);
    vi.advanceTimersByTime(681);
    expect(currentText.textContent).toBe("Second line");
  });

  it("does not let a cancelled startup move replacement content", async () => {
    const { track, next, currentText, frames, receive } = await startRoll();
    const staleFrames = Array.from(frames.values());
    receive(0, "replacement", false);
    staleFrames.forEach(callback => callback(16));
    expect(currentText.textContent).toBe("First line");
    expect(track.style.transform).toBe("");
    expect(next.classList.contains("promoting")).toBe(false);
  });
});
