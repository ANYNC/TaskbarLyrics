import { describe, expect, it, vi } from "vitest";
import { loadLyricsAssets, createLyricsDom, runLyricsScripts } from "./helpers/lyrics-dom.js";

describe("lyric roll opacity continuity", () => {
  it.each(["complete", "cancel"])("seeds the visible opacity before rolling and clears it on %s", async ending => {
    const assets = await loadLyricsAssets();
    const dom = createLyricsDom(assets.html);
    try {
      const { window } = dom;
      const current = window.document.querySelector("#currentLine");
      const next = window.document.querySelector("#nextLine");
      const track = window.document.querySelector("#track");
      const getComputedStyle = window.getComputedStyle.bind(window);
      let rolling = false;
      window.getComputedStyle = element => {
        const style = getComputedStyle(element);
        if (element !== next) return style;
        return new Proxy(style, {
          get(target, property) {
            // Simulate a secondary opacity still interpolating toward its
            // target. The roll must begin from what is actually visible.
            if (property === "opacity") return rolling ? next.style.opacity || "0.643" : "0.643";
            const value = Reflect.get(target, property, target);
            return typeof value === "function" ? value.bind(target) : value;
          }
        });
      };
      let now = 0;
      let sequence = 0;
      const frames = new Map();
      vi.spyOn(window.performance, "now").mockImplementation(() => now);
      window.requestAnimationFrame = callback => { frames.set(++sequence, callback); return sequence; };
      window.cancelAnimationFrame = id => frames.delete(id);
      const advance = timestamp => {
        now = timestamp;
        const pending = Array.from(frames.values());
        frames.clear();
        pending.forEach(callback => callback(timestamp));
      };
      runLyricsScripts(dom, assets);
      const receive = (index, trackId = "opacity", animateTransition = true) => window.taskbarLyrics.receive({
        version: 1, type: "lyrics", payload: {
          current: index === 0 ? "First line" : "Second line",
          next: index === 0 ? "Second line" : "Third line",
          currentLineIndex: index, trackId, isPlaying: true, progress: 0.2, animateTransition
        }
      });
      receive(0, "opacity", false);
      receive(1);
      rolling = true;
      expect(Number(current.style.opacity)).toBe(0.98);
      expect(Number(next.style.opacity)).toBe(0.643);
      advance(0);
      expect(current.classList.contains("leaving")).toBe(true);
      expect(Number(current.style.opacity)).toBe(0.98);
      expect(Number(next.style.opacity)).toBe(0.643);
      advance(16);
      expect(Number(current.style.opacity)).toBeLessThanOrEqual(0.98);
      expect(Number(current.style.opacity)).toBeGreaterThan(0.16);
      expect(Number(next.style.opacity)).toBeGreaterThanOrEqual(0.643);
      if (ending === "complete") {
        advance(560);
        expect(Number(next.style.opacity)).toBe(0.98);
        const event = new window.Event("transitionend", { bubbles: true });
        Object.defineProperty(event, "propertyName", { value: "transform" });
        track.dispatchEvent(event);
      } else {
        receive(0, "replacement", false);
      }
      expect(track.classList.contains("animating")).toBe(false);
      expect(current.style.opacity).toBe("");
      expect(next.classList.contains("promoting")).toBe(false);
      expect(Number(next.style.opacity)).toBeGreaterThanOrEqual(0.58);
      expect(Number(next.style.opacity)).toBeLessThanOrEqual(0.74);
    } finally {
      dom.window.close();
    }
  });
});
