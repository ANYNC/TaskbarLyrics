import { describe, expect, it } from "vitest";
import { loadLyricsAssets, createLyricsDom, runLyricsScripts } from "./helpers/lyrics-dom.js";

async function createStatusDom() {
  const assets = await loadLyricsAssets();
  const dom = createLyricsDom(assets.html);
  dom.window.requestAnimationFrame = () => 1;
  dom.window.cancelAnimationFrame = () => {};
  const timers = new Map();
  let timerId = 0;
  dom.window.setTimeout = callback => { timers.set(++timerId, callback); return timerId; };
  dom.window.clearTimeout = id => timers.delete(id);
  runLyricsScripts(dom, assets);
  const receive = (frame = {}) => dom.window.taskbarLyrics.receive({
    version: 1, type: "lyrics", payload: {
      scene: "searching", current: "Song - Artist", next: "正在检索歌词...",
      currentLineIndex: -1, trackId: "track-a", progress: 0, isPlaying: true,
      ...frame
    }
  });
  const style = payload => dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload });
  const finishTransitions = () => {
    for (let i = 0; timers.size && i < 32; i++) {
      const [id, callback] = timers.entries().next().value;
      timers.delete(id);
      callback();
    }
  };
  return { dom, receive, style, finishTransitions, document: dom.window.document };
}

describe("search-status display preference", () => {
  it("preserves the search presentation when the preference is absent", async () => {
    const { dom, receive, document } = await createStatusDom();
    receive({ animateTransition: false });
    expect(document.querySelector("#currentLineText").textContent).toBe("Song - Artist");
    expect(document.querySelector("#nextLineText").textContent).toBe("正在检索歌词...");
    dom.window.close();
  });

  it.each([[false, false], [true, false], [false, true]])("preserves song metadata while blanking the search prompt and restores lyric scanning (single=%s, vertical=%s)", async (singleLine, verticalTaskbar) => {
    const { dom, receive, style, document, finishTransitions } = await createStatusDom();
    style({ showLyricSearchStatus: false, singleLine, verticalTaskbar });
    receive();
    finishTransitions();
    expect(document.querySelector("#currentLineText").textContent).toBe("Song - Artist");
    expect(document.querySelector("#nextLineText").textContent).toBe("\u200b");
    expect(document.querySelector("#cover").hidden).toBe(false);
    expect(document.querySelector("#layout").classList.contains("region-concealed")).toBe(false);
    receive({ scene: "lyrics", current: "Actual lyric", next: "Next lyric", currentLineIndex: 3, wordScanProgress: 0.4 });
    finishTransitions();
    expect(document.querySelector("#currentLineText").textContent).toBe("Actual lyric");
    expect(document.querySelector("#currentLine").classList.contains("word-scanning")).toBe(true);
    expect(document.querySelector("#track").classList.contains("animating")).toBe(false);
    dom.window.close();
  });

  it("applies live changes while the current track is still searching", async () => {
    const { dom, receive, style, document, finishTransitions } = await createStatusDom();
    receive({ animateTransition: false });
    style({ showLyricSearchStatus: false });
    // The host republishes the current lyrics frame after applying style changes.
    receive();
    finishTransitions();
    expect(document.querySelector("#currentLineText").textContent).toBe("Song - Artist");
    expect(document.querySelector("#nextLineText").textContent).toBe("\u200b");
    style({ showLyricSearchStatus: true });
    receive();
    finishTransitions();
    expect(document.querySelector("#currentLineText").textContent).toBe("Song - Artist");
    expect(document.querySelector("#nextLineText").textContent).toBe("正在检索歌词...");
    dom.window.close();
  });

  it.each(["message", "noPlayback", "spectrum"])("restores %s results after a suppressed search", async scene => {
    const { dom, receive, style, document, finishTransitions } = await createStatusDom();
    style({ showLyricSearchStatus: false });
    receive();
    finishTransitions();
    receive({ scene, current: "Result", next: " ", isPureMusic: scene === "spectrum" });
    finishTransitions();
    if (scene === "spectrum") expect(document.querySelector("#layout").classList.contains("spectrum-mode")).toBe(true);
    else expect(document.querySelector("#currentLineText").textContent).toBe("Result");
    dom.window.close();
  });

  it("uses a blank synthetic search frame when lyrics for a new track arrive immediately", async () => {
    const { dom, receive, style, document, finishTransitions } = await createStatusDom();
    style({ showLyricSearchStatus: false });
    receive({ scene: "lyrics", current: "First track", currentLineIndex: 0, animateTransition: false });
    receive({ scene: "lyrics", current: "Second track", currentLineIndex: 1, trackId: "track-b" });
    expect(document.querySelector("#nextLineText").textContent).toBe("\u200b");
    finishTransitions();
    expect(document.querySelector("#currentLineText").textContent).toBe("Second track");
    expect(document.querySelector("#track").classList.contains("animating")).toBe(false);
    dom.window.close();
  });

  it("keeps the latest queued result when the preference changes during a search transition", async () => {
    const { dom, receive, style, document, finishTransitions } = await createStatusDom();
    receive({ scene: "lyrics", current: "Old lyric", currentLineIndex: 0, animateTransition: false });
    receive({ trackId: "track-b", current: "Next song - Artist" });
    receive({ scene: "lyrics", trackId: "track-b", current: "New lyric", currentLineIndex: 2 });
    style({ showLyricSearchStatus: false });
    receive({ scene: "lyrics", trackId: "track-b", current: "New lyric", currentLineIndex: 2 });
    finishTransitions();
    expect(document.querySelector("#currentLineText").textContent).toBe("New lyric");
    expect(document.querySelector("#track").classList.contains("animating")).toBe(false);
    document.querySelector("#track").dispatchEvent(new dom.window.Event("transitionend", { bubbles: true }));
    expect(document.querySelector("#currentLineText").textContent).toBe("New lyric");
    dom.window.close();
  });

  it.each(["正在检索歌词...", "正在匹配歌词...", "  正在检索歌词...  "])("recognizes legacy search text %s before replacing it with a blank character", async current => {
    const { dom, receive, style, document, finishTransitions } = await createStatusDom();
    style({ showLyricSearchStatus: false });
    receive({ scene: null, current });
    finishTransitions();
    expect(document.querySelector("#currentLineText").textContent).toBe("\u200b");
    expect(document.querySelector("#nextLineText").textContent).toBe("\u200b");
    receive({ scene: "lyrics", current: "Real lyric", currentLineIndex: 1 });
    finishTransitions();
    expect(document.querySelector("#currentLineText").textContent).toBe("Real lyric");
    dom.window.close();
  });

  it("preserves actual lyric text even if it matches the search prompt", async () => {
    const { dom, receive, style, document } = await createStatusDom();
    style({ showLyricSearchStatus: false });
    receive({ scene: "lyrics", current: "正在检索歌词...", next: "正在匹配歌词...", currentLineIndex: 1, animateTransition: false });
    expect(document.querySelector("#currentLineText").textContent).toBe("正在检索歌词...");
    expect(document.querySelector("#nextLineText").textContent).toBe("正在匹配歌词...");
    dom.window.close();
  });

  it("updates song metadata during a track switch with the prompt disabled", async () => {
    const { dom, receive, style, document, finishTransitions } = await createStatusDom();
    style({ showLyricSearchStatus: false });
    receive();
    finishTransitions();
    receive({ current: "Next song - Next artist", trackId: "track-b" });
    finishTransitions();
    expect(document.querySelector("#currentLineText").textContent).toBe("Next song - Next artist");
    expect(document.querySelector("#nextLineText").textContent).toBe("\u200b");
    dom.window.close();
  });
});
