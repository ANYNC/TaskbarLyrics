import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it, vi } from "vitest";

async function createPresentation({ reducedMotion = false } = {}) {
  const root = new URL("../../TaskbarLyrics.App/Web/Settings/", import.meta.url);
  const [html, script] = await Promise.all([
    readFile(new URL("settings.html", root), "utf8"),
    readFile(new URL("display-presentation.js", root), "utf8")
  ]);
  const dom = new JSDOM(html, { url: "https://settings.local/", runScripts: "outside-only" });
  const { window } = dom;
  let hidden = false;
  let paused = true;
  const motionListeners = [];
  const motion = { matches: reducedMotion, addEventListener: (_, listener) => motionListeners.push(listener) };
  Object.defineProperty(window.document, "hidden", { get: () => hidden });
  const video = window.document.querySelector("video");
  Object.defineProperty(video, "paused", { get: () => paused });
  video.load = vi.fn();
  video.pause = vi.fn(() => { paused = true; });
  video.play = vi.fn(() => { paused = false; return Promise.resolve(); });
  window.matchMedia = () => motion;
  window.eval(script);
  return {
    dom, video, motion,
    sync: window.taskbarLyricsDisplayPresentation.sync,
    setHidden(value) { hidden = value; window.document.dispatchEvent(new window.Event("visibilitychange")); },
    reduceMotion() { motion.matches = true; motionListeners.forEach(listener => listener()); }
  };
}

describe("display presentation", () => {
  it.each([false, true])("only moves groups visible in both modes when switching from floating=%s", async floating => {
    const { dom, sync } = await createPresentation();
    const document = dom.window.document;
    const groups = [...document.querySelectorAll(".display-setting-group")];
    const background = document.querySelector('[data-display-group="floating-background"]');
    const appearance = document.querySelector('[data-display-group="appearance"]');
    const controls = document.querySelector('[data-display-group="controls"]');
    const size = document.querySelector('[data-display-group="size"]');
    for (const [index, group] of groups.entries()) {
      group.getBoundingClientRect = () => {
        if (group.closest("[hidden]")) return { top: 0, width: 0, height: 0 };
        const offset = group === controls && !background.hidden ? 60 : 0;
        return { top: 200 + index * 150 + offset, width: 380, height: 140 };
      };
      group.animate = vi.fn(() => ({ cancel: vi.fn() }));
    }
    sync({ page: "displayArea", useFloatingWindow: floating });
    sync({ page: "displayArea", useFloatingWindow: !floating });

    expect(background.hidden).toBe(floating);
    expect(appearance.hidden).toBe(!floating);
    expect(background.animate).not.toHaveBeenCalled();
    expect(appearance.animate).not.toHaveBeenCalled();
    expect(size.animate).not.toHaveBeenCalled();
    expect(controls.animate).toHaveBeenCalledExactlyOnceWith(
      [{ transform: `translateY(${floating ? 60 : -60}px)` }, { transform: "translateY(0)" }],
      { duration: 220, easing: "cubic-bezier(.2,.75,.25,1)" }
    );
    dom.window.close();
  });

  it("plays on the first display-page visit and mode changes, without replaying for setting edits or page revisits", async () => {
    const { dom, video, sync } = await createPresentation();
    const state = { page: "sources", useFloatingWindow: false, enableControlPanel: true, showCover: true };
    sync(state);
    expect(video.load).not.toHaveBeenCalled();
    sync({ ...state, page: "displayArea" });
    expect(video.play).toHaveBeenCalledTimes(1);
    expect(video.src).toContain("/media/embedded.mp4");
    sync({ ...state, page: "displayArea", embeddedPanelBrightness: 0.4 });
    expect(video.play).toHaveBeenCalledTimes(1);
    sync(state);
    expect(video.paused).toBe(true);
    sync({ ...state, page: "displayArea" });
    expect(video.play).toHaveBeenCalledTimes(1);
    sync({ ...state, page: "displayArea", useFloatingWindow: true });
    expect(video.play).toHaveBeenCalledTimes(2);
    expect(video.src).toContain("/media/floating.mp4");
    dom.window.close();
  });

  it("keeps reduced-motion previews still and allows explicit replay, pausing when hidden", async () => {
    const { dom, video, sync, setHidden } = await createPresentation({ reducedMotion: true });
    sync({ page: "displayArea", useFloatingWindow: true });
    expect(video.play).not.toHaveBeenCalled();
    expect(video.poster).toContain("floating-poster.png");
    dom.window.document.querySelector("[data-replay-presentation]").click();
    expect(video.play).toHaveBeenCalledTimes(1);
    setHidden(true);
    expect(video.paused).toBe(true);
    setHidden(false);
    expect(video.play).toHaveBeenCalledTimes(1);
    dom.window.close();
  });

  it("stops an active preview when reduced motion is enabled and ignores stale playback failures", async () => {
    const { dom, video, sync, reduceMotion } = await createPresentation();
    let rejectPlay;
    video.play.mockImplementationOnce(() => new Promise((_, reject) => { rejectPlay = reject; }));
    sync({ page: "displayArea", useFloatingWindow: false });
    sync({ page: "displayArea", useFloatingWindow: true });
    rejectPlay(new Error("previous source interrupted"));
    await Promise.resolve();
    expect(dom.window.document.querySelector("[data-presentation-status]").textContent).toContain("8 秒");
    reduceMotion();
    expect(video.paused).toBe(true);
    dom.window.close();
  });

  it("shows only the active profile without hiding disabled panel options or clearing hidden values", async () => {
    const { dom, sync } = await createPresentation();
    const document = dom.window.document;
    const cover = document.querySelector('[data-cover-display][value="Hidden"]');
    cover.checked = true;
    sync({ page: "sources", useFloatingWindow: false, enableControlPanel: false, showCover: false });
    expect(document.querySelector("[data-panel-options]").hidden).toBe(false);
    expect([...document.querySelectorAll('[data-presentation-mode="floating"]')].every(element => element.hidden)).toBe(true);
    sync({ page: "sources", useFloatingWindow: true, enableControlPanel: true, showCover: true });
    expect(document.querySelector("[data-panel-options]").hidden).toBe(false);
    expect([...document.querySelectorAll('[data-presentation-mode="embedded"]')].every(element => element.hidden)).toBe(true);
    expect(cover.checked).toBe(true);
    expect(document.querySelector('[data-setting="floatingLyricsLayoutScalePercent"]')).toBeNull();
    for (const key of ["xOffset", "yOffset"]) {
      expect(document.querySelector(`[data-setting="${key}"]`).closest("details")).toBeNull();
    }
    dom.window.close();
  });
});
