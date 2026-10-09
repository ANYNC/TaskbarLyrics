import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");

const appIconUri = "data:image/png;base64,iVBORw0KGgo=";
const artworkUri = "data:image/jpeg;base64,BBBB";

async function createHarness() {
  const [html, bridge, state, presentation, script, style] = await Promise.all([
    read("TaskbarLyrics.App/Web/Lyrics/index.html"),
    read("TaskbarLyrics.App/Web/Lyrics/bridge.js"),
    read("TaskbarLyrics.App/Web/Lyrics/state.js"),
    read("TaskbarLyrics.App/Web/Lyrics/presentation.js"),
    read("TaskbarLyrics.App/Web/Lyrics/app.js"),
    read("TaskbarLyrics.App/Web/Lyrics/style.css")
  ]);
  const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
    runScripts: "outside-only"
  });
  dom.window.CSS = { supports: () => true };
  dom.window.requestAnimationFrame = callback => {
    callback(0);
    return 1;
  };
  dom.window.cancelAnimationFrame = () => {};

  const timers = new Map();
  let nextTimerId = 1;
  dom.window.setTimeout = (callback, delay) => {
    const id = nextTimerId++;
    timers.set(id, { callback, delay });
    return id;
  };
  dom.window.clearTimeout = id => timers.delete(id);

  const preloaders = [];
  dom.window.Image = class {
    constructor() {
      preloaders.push(this);
    }
  };

  dom.window.eval(bridge);
  dom.window.eval(state);
  dom.window.eval(presentation);
  dom.window.eval(script);

  const flushTimers = () => {
    for (let guard = 0; timers.size > 0 && guard < 32; guard += 1) {
      const [id, timer] = timers.entries().next().value;
      timers.delete(id);
      timer.callback();
    }
  };

  const receive = payload => dom.window.taskbarLyrics.receive({
    version: 1,
    type: "cover",
    payload
  });

  const showCover = payload => {
    receive(payload);
    flushTimers();
    preloaders.at(-1)?.onload?.();
    flushTimers();
  };

  return { dom, preloaders, receive, flushTimers, showCover, style };
}

describe("lyrics cover fallback", () => {
  it("shows an accessible expand affordance and sends a bounded cover click", async () => {
    const { dom } = await createHarness();
    const cover = dom.window.document.querySelector("#cover");
    const sent = [];
    dom.window.chrome = { webview: { postMessage: message => sent.push(JSON.parse(message)) } };
    cover.getBoundingClientRect = () => ({ x: 4, y: 3, width: 34, height: 34 });

    expect(cover.tagName).toBe("BUTTON");
    expect(cover.getAttribute("aria-expanded")).toBe("false");
    expect(cover.querySelector(".cover-action-icon path")).not.toBeNull();
    cover.click();
    expect(sent).toEqual([{
      version: 1,
      type: "coverClick",
      payload: {
        x: 4, y: 3, width: 34, height: 34,
        viewportWidth: dom.window.innerWidth,
        viewportHeight: dom.window.innerHeight
      }
    }]);

    dom.window.taskbarLyrics.receive({ version: 1, type: "controlPanelState", payload: { open: true } });
    expect(cover.classList.contains("panel-open")).toBe(true);
    expect(cover.getAttribute("aria-expanded")).toBe("true");
    dom.window.document.documentElement.classList.add("cover-hidden");
    cover.click();
    expect(sent).toHaveLength(1);
  });

  it("keeps the cover usable in spectrum mode and disables the panel affordance on request", async () => {
    const { dom, style } = await createHarness();
    const cover = dom.window.document.querySelector("#cover");
    const sent = [];
    dom.window.chrome = { webview: { postMessage: message => sent.push(JSON.parse(message)) } };
    cover.getBoundingClientRect = () => ({ x: 4, y: 3, width: 34, height: 34 });
    dom.window.taskbarLyrics.receive({ version: 1, type: "lyrics", payload: {
      current: "纯音乐", next: "", isPureMusic: true, isPlaying: true, animateTransition: false, scene: "spectrum"
    } });
    expect(dom.window.document.querySelector("#layout").classList.contains("spectrum-mode")).toBe(true);
    cover.click();
    expect(sent.at(-1).type).toBe("coverClick");
    dom.window.taskbarLyrics.receive({ version: 1, type: "controlPanelState", payload: { open: true } });
    dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload: { enableControlPanel: false } });
    expect(cover.disabled).toBe(true);
    expect(cover.getAttribute("aria-expanded")).toBe("false");
    const sentCount = sent.length;
    cover.click();
    cover.dispatchEvent(new dom.window.MouseEvent("click"));
    expect(sent).toHaveLength(sentCount);
    expect(style).toContain(".cover:not(:disabled):is(:hover, :focus-visible, .panel-open) .cover-interaction");
    dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload: { enableControlPanel: true } });
    cover.click();
    expect(sent).toHaveLength(sentCount + 1);
  });

  it("fills the cover slot with the app icon when album art is unavailable", async () => {
    const { dom, showCover, style } = await createHarness();
    const cover = dom.window.document.querySelector("#cover");
    const fallback = dom.window.document.querySelector("#coverFallback");
    const activeImage = dom.window.document.querySelector("#coverImageNext");
    const standbyImage = dom.window.document.querySelector("#coverImage");

    showCover({
      dataUri: "",
      fallbackText: "N",
      fallbackColor: "rgb(229, 57, 53)",
      fallbackIconDataUri: appIconUri,
      trackId: "track-icon"
    });

    expect(cover.classList.contains("app-icon")).toBe(true);
    expect(cover.style.backgroundColor).toBe("");
    expect(fallback.style.display).toBe("none");
    expect(activeImage.getAttribute("src")).toBe(appIconUri);
    expect(activeImage.style.opacity).toBe("1");
    expect(standbyImage.hasAttribute("src")).toBe(false);
    expect(style).toMatch(/\.cover\.app-icon\s*\{[^}]*background:\s*transparent/s);
  });

  it("keeps the letter fallback when no app icon can be resolved", async () => {
    const { dom, showCover } = await createHarness();
    const cover = dom.window.document.querySelector("#cover");
    const fallback = dom.window.document.querySelector("#coverFallback");
    const activeImage = dom.window.document.querySelector("#coverImageNext");

    showCover({
      dataUri: "",
      fallbackText: "N",
      fallbackColor: "rgb(229, 57, 53)",
      fallbackIconDataUri: "",
      trackId: "track-letter"
    });

    expect(cover.classList.contains("app-icon")).toBe(false);
    expect(cover.style.backgroundColor).toBe("rgb(229, 57, 53)");
    expect(fallback.style.display).toBe("flex");
    expect(fallback.textContent).toBe("N");
    expect(activeImage.hasAttribute("src")).toBe(false);
  });

  it("returns to album art rendering when artwork arrives after an app icon", async () => {
    const { dom, showCover } = await createHarness();
    const cover = dom.window.document.querySelector("#cover");

    showCover({
      dataUri: "",
      fallbackText: "N",
      fallbackColor: "rgb(229, 57, 53)",
      fallbackIconDataUri: appIconUri,
      trackId: "track-icon"
    });
    expect(cover.classList.contains("app-icon")).toBe(true);

    showCover({
      dataUri: artworkUri,
      fallbackText: "N",
      fallbackColor: "rgb(229, 57, 53)",
      fallbackIconDataUri: appIconUri,
      trackId: "track-artwork"
    });

    expect(cover.classList.contains("app-icon")).toBe(false);
    expect(dom.window.document.querySelector("#coverImage").getAttribute("src")).toBe(artworkUri);
  });

  it("ignores a repeated app icon payload without restarting the fallback timer", async () => {
    const { dom, preloaders, receive, flushTimers, showCover } = await createHarness();
    const payload = {
      dataUri: "",
      fallbackText: "N",
      fallbackColor: "rgb(229, 57, 53)",
      fallbackIconDataUri: appIconUri,
      trackId: "track-icon"
    };

    showCover(payload);
    const preloaderCount = preloaders.length;
    receive(payload);
    flushTimers();

    expect(preloaders.length).toBe(preloaderCount);
    expect(dom.window.document.querySelector("#cover").classList.contains("app-icon")).toBe(true);
    expect(dom.window.document.querySelector("#coverImageNext").getAttribute("src")).toBe(appIconUri);
  });
});
