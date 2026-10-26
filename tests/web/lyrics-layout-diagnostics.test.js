import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const script = await readFile(new URL("../../TaskbarLyrics.App/Diagnostics/lyrics-layout-snapshot.js", import.meta.url), "utf8");

function createPage() {
  return new JSDOM(`<div class="layout"><div class="shell"><button class="cover"><img src="data:image/png;base64,private-cover"></button>
    <div class="lyrics-pane"><div class="lyrics-layer"><div class="viewport"><div class="track">
    <div class="current-line"><span class="line-text">private lyrics</span></div>
    <div class="next-line"><span class="line-text">private translation</span></div>
    </div></div></div></div></div></div>`, { runScripts: "outside-only" });
}

describe("temporary layout diagnostics", () => {
  it("collects viewport and clipping geometry without song content or artwork", () => {
    const dom = createPage();
    try {
      const cover = dom.window.document.querySelector(".cover");
      cover.getBoundingClientRect = () => ({ x: 0, y: -8, width: 34, height: 34, right: 34, bottom: 26 });
      const result = dom.window.eval(script);
      expect(result.innerHeight).toBe(dom.window.innerHeight);
      expect(result.devicePixelRatio).toBe(dom.window.devicePixelRatio);
      expect(result.elements[".cover"].bounds.y).toBe(-8);
      expect(result.elements[".current-line .line-text"]).not.toBeNull();
      expect(JSON.stringify(result)).not.toMatch(/private lyrics|private translation|private-cover/);
    } finally { dom.window.close(); }
  });

  it("records pointer events once, drains them and never intercepts clicks", () => {
    const dom = createPage();
    try {
      const cover = dom.window.document.querySelector(".cover");
      dom.window.eval(script);
      dom.window.eval(script);
      const event = new dom.window.MouseEvent("click", { bubbles: true, cancelable: true, clientX: 5, clientY: 6 });
      expect(cover.dispatchEvent(event)).toBe(true);
      expect(event.defaultPrevented).toBe(false);
      const result = dom.window.eval(script);
      expect(result.pointerEvents).toHaveLength(1);
      expect(result.pointerEvents[0]).toMatchObject({ type: "click", target: "cover", x: 5, y: 6 });
      expect(dom.window.eval(script).pointerEvents).toHaveLength(0);
    } finally { dom.window.close(); }
  });

  it("bounds queued events and reports drops while the host is busy", () => {
    const dom = createPage();
    try {
      dom.window.eval(script);
      const pane = dom.window.document.querySelector(".lyrics-pane");
      for (let i = 0; i < 40; i++) pane.dispatchEvent(new dom.window.MouseEvent("pointerdown", { bubbles: true }));
      const result = dom.window.eval(script);
      expect(result.pointerEvents).toHaveLength(24);
      expect(result.droppedEvents).toBe(16);
      expect(dom.window.eval(script).droppedEvents).toBe(0);
    } finally { dom.window.close(); }
  });
});
