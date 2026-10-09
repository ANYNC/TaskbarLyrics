import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = path => readFile(new URL(path, root), "utf8");

async function createPanel() {
  const [html, script] = await Promise.all([
    read("TaskbarLyrics.App/Web/ControlPanel/index.html"),
    read("TaskbarLyrics.App/Web/ControlPanel/app.js")
  ]);
  const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
    runScripts: "outside-only"
  });
  const sent = [];
  dom.window.chrome = { webview: { postMessage: message => sent.push(JSON.parse(message)) } };
  dom.window.eval(script);
  return { dom, sent };
}

describe("lyrics control panel", () => {
  it("shows track state and routes media controls through V1 messages", async () => {
    const { dom, sent } = await createPanel();
    const doc = dom.window.document;
    const receive = (type, payload) => dom.window.controlPanel.receive({ version: 1, type, payload });

    receive("snapshot", {
      title: "Song", artist: "Artist", coverDataUri: "data:image/png;base64,AAAA",
      fallbackIconDataUri: "", hasTrack: true, isPlaying: true, light: false
    });

    expect(doc.querySelector("#title").textContent).toBe("Song");
    expect(doc.querySelector("#title").hasAttribute("title")).toBe(false);
    expect(doc.querySelector("#artworkImage").classList.contains("visible")).toBe(true);
    expect(doc.querySelector("#playPause").getAttribute("aria-label")).toBe("暂停");
    expect(doc.querySelector("#playIcon").hasAttribute("hidden")).toBe(true);
    expect(doc.querySelector("#pauseIcon").hasAttribute("hidden")).toBe(false);
    for (const id of ["previous", "playPause", "next"]) doc.querySelector(`#${id}`).click();
    expect(sent.map(message => message.payload.action)).toEqual(["previous", "toggle", "next"]);
    expect(sent.every(message => message.version === 1 && message.type === "mediaAction")).toBe(true);

    receive("snapshot", { hasTrack: true, isPlaying: false });
    expect(doc.querySelector("#playIcon").hasAttribute("hidden")).toBe(false);
    expect(doc.querySelector("#pauseIcon").hasAttribute("hidden")).toBe(true);

    doc.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(sent.at(-1)).toEqual({ version: 1, type: "dismiss", payload: {} });
  });

  it("disables transport buttons without a track", async () => {
    const { dom, sent } = await createPanel();
    dom.window.controlPanel.receive({ version: 1, type: "snapshot", payload: { hasTrack: false } });
    expect(dom.window.document.querySelector("#title").textContent).toBe("暂无播放");
    dom.window.document.querySelector("#next").click();
    expect(sent).toHaveLength(0);
  });

  it("changes playback state without replacing the cover image", async () => {
    const { dom } = await createPanel();
    const doc = dom.window.document;
    const artwork = doc.querySelector("#artworkImage");
    const receive = (type, payload) => dom.window.controlPanel.receive({ version: 1, type, payload });
    const cover = "data:image/png;base64,AAAA";
    receive("snapshot", { hasTrack: true, title: "Song", coverDataUri: cover, isPlaying: true });
    let coverAssignments = 0;
    Object.defineProperty(artwork, "src", {
      configurable: true,
      get: () => cover,
      set: () => { coverAssignments++; }
    });

    receive("playback", { isPlaying: false });

    expect(doc.querySelector("#playPause").getAttribute("aria-label")).toBe("播放");
    expect(doc.querySelector("#playIcon").hasAttribute("hidden")).toBe(false);
    expect(artwork.classList.contains("visible")).toBe(true);
    expect(coverAssignments).toBe(0);
  });

  it("keeps the compact panel focused on media controls and seeking", async () => {
    const { dom, sent } = await createPanel();
    const doc = dom.window.document;
    const receive = (type, payload) => dom.window.controlPanel.receive({ version: 1, type, payload });

    receive("snapshot", {
      hasTrack: true, translationEnabled: true, source: "网易云音乐",
      sourceIconDataUri: "data:image/png;base64,AAAA"
    });
    receive("timeline", { positionMs: 30000, durationMs: 120000, isPlaying: false, canSeek: true });
    // Older hosts can still send volume snapshots; this media-only view ignores them.
    expect(() => receive("volume", { level: 35, muted: false })).not.toThrow();
    expect(doc.querySelector("#source").textContent).toBe("网易云音乐");
    expect(doc.querySelector("#sourceIcon").hidden).toBe(false);
    expect(doc.querySelector("#sourceIcon").src).toBe("data:image/png;base64,AAAA");
    expect(doc.querySelector("#sourceFallback").hasAttribute("hidden")).toBe(true);
    expect(doc.querySelector(".track .actions")).not.toBeNull();
    expect([...doc.querySelectorAll("button")].map(button => button.id)).toEqual(["previous", "playPause", "next"]);
    expect(doc.querySelector("#settings, #translation, #volume, #mute, .utilities")).toBeNull();
    expect(doc.querySelector("#elapsed").textContent).toBe("0:30");
    expect(doc.querySelector("#duration").textContent).toBe("2:00");
    const seek = doc.querySelector("#seek");
    seek.value = "500";
    seek.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

    expect(sent).toEqual([
      { version: 1, type: "seek", payload: { positionMs: 60000 } }
    ]);
  });

  it("disables seeking when the player does not support it", async () => {
    const { dom, sent } = await createPanel();
    dom.window.controlPanel.receive({ version: 1, type: "timeline", payload: {
      positionMs: 0, durationMs: 120000, canSeek: false
    } });
    expect(dom.window.document.querySelector("#seek").disabled).toBe(true);
    expect(sent).toHaveLength(0);
  });

  it("uses the local fallback icon when no valid source image is available", async () => {
    const { dom } = await createPanel();
    dom.window.controlPanel.receive({ version: 1, type: "snapshot", payload: {
      hasTrack: true, source: "Custom", sourceIconDataUri: "https://example.com/icon.png"
    } });

    expect(dom.window.document.querySelector("#sourceIcon").hidden).toBe(true);
    expect(dom.window.document.querySelector("#sourceFallback").hasAttribute("hidden")).toBe(false);
  });

  it("shows full metadata on focus without HTML injection or native tooltips and clamps its position", async () => {
    const { dom } = await createPanel();
    const doc = dom.window.document;
    const title = doc.querySelector("#title");
    const artist = doc.querySelector("#artist");
    const tooltip = doc.querySelector("#metadataTooltip");
    const text = "ワタシノミカタ <b>(feat. Hanon & Kotoha)</b>";
    dom.window.controlPanel.receive({ version: 1, type: "snapshot", payload: {
      hasTrack: true, title: text, artist: "HoneyWorks / Kotoha / Hanon"
    } });
    title.getBoundingClientRect = () => ({ left: 960, top: 760, bottom: 780 });
    tooltip.getBoundingClientRect = () => ({ width: 300, height: 40 });

    title.focus();

    expect(tooltip.hidden).toBe(false);
    expect(tooltip.getAttribute("role")).toBe("tooltip");
    expect(tooltip.textContent).toBe(text);
    expect(tooltip.children).toHaveLength(0);
    expect(tooltip.style.left).toBe("716px");
    expect(tooltip.style.top).toBe("714px");
    expect(title.getAttribute("aria-describedby")).toBe(tooltip.id);
    expect(title.hasAttribute("title")).toBe(false);
    artist.focus();
    expect(tooltip.textContent).toBe("HoneyWorks / Kotoha / Hanon");
    expect(title.hasAttribute("aria-describedby")).toBe(false);
    expect(artist.getAttribute("aria-describedby")).toBe(tooltip.id);
    artist.blur();
    expect(tooltip.hidden).toBe(true);
    dom.window.close();
  });

  it("delays hover tooltips, keeps them open while hovered and cancels a pending tooltip on track change", async () => {
    const { dom } = await createPanel();
    const doc = dom.window.document;
    const title = doc.querySelector("#title");
    const tooltip = doc.querySelector("#metadataTooltip");
    const timers = new Map();
    let timerId = 0;
    dom.window.setTimeout = (callback, delay) => {
      timers.set(++timerId, { callback, delay });
      return timerId;
    };
    dom.window.clearTimeout = id => timers.delete(id);
    const fireTimer = () => {
      const [id, timer] = timers.entries().next().value;
      timers.delete(id);
      timer.callback();
    };
    const event = name => new dom.window.Event(name);

    title.dispatchEvent(event("pointerenter"));
    expect(tooltip.hidden).toBe(true);
    expect([...timers.values()][0].delay).toBe(400);
    fireTimer();
    expect(tooltip.hidden).toBe(false);
    title.dispatchEvent(event("pointerleave"));
    tooltip.dispatchEvent(event("pointerenter"));
    expect(timers.size).toBe(0);
    expect(tooltip.hidden).toBe(false);
    tooltip.dispatchEvent(event("pointerleave"));
    fireTimer();
    expect(tooltip.hidden).toBe(true);

    title.dispatchEvent(event("pointerenter"));
    dom.window.controlPanel.receive({ version: 1, type: "snapshot", payload: { hasTrack: true, title: "Next song" } });
    expect(timers.size).toBe(0);
    expect(tooltip.hidden).toBe(true);
    dom.window.close();
  });

  it("clears stale metadata tooltips when resized, deactivated, scrolled or dismissed", async () => {
    const { dom, sent } = await createPanel();
    const title = dom.window.document.querySelector("#title");
    const tooltip = dom.window.document.querySelector("#metadataTooltip");
    for (const name of ["resize", "blur"]) {
      title.blur();
      title.focus();
      expect(tooltip.hidden).toBe(false);
      dom.window.dispatchEvent(new dom.window.Event(name));
      expect(tooltip.hidden).toBe(true);
    }
    title.blur();
    title.focus();
    dom.window.document.dispatchEvent(new dom.window.Event("scroll"));
    expect(tooltip.hidden).toBe(true);
    title.blur();
    title.focus();
    dom.window.document.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape" }));
    expect(tooltip.hidden).toBe(true);
    expect(sent.at(-1)).toEqual({ version: 1, type: "dismiss", payload: {} });
    dom.window.close();
  });

});
