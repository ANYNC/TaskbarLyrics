import { readFile } from "node:fs/promises";
import { JSDOM, VirtualConsole } from "jsdom";
import { describe, expect, it } from "vitest";
import { catalogs } from "./helpers/localized-dom.js";
import { loadLyricsAssets } from "./helpers/lyrics-dom.js";

describe("lyrics full-page startup", () => {
  it.each([1, 1.25, 1.5, 2])("initializes before accepting style and lyrics at DPR %s", async dpr => {
    const [assets, localization] = await Promise.all([
      loadLyricsAssets(),
      readFile(new URL("../../TaskbarLyrics.App/text.js", import.meta.url), "utf8")
    ]);
    // Match GetLyricsWebUiHtml's bootstrap order and execute the actual HTML.
    // Do not use localized-dom's beforeParse initialization: it would hide an
    // erroneous template call that runs before the injected bootstrap.
    const script = `window.taskbarLyricsLocale=${JSON.stringify({ language: "EN", strings: catalogs.EN })};\n` +
      localization + "\nwindow.taskbarLyricsI18n.translateDocument();\n" +
      [assets.bridge, assets.state, assets.presentation, assets.script].join("\n");
    const html = assets.html.replace("{{STYLE_CSS}}", assets.style).replace("{{APP_JS}}", script);
    const errors = [];
    const virtualConsole = new VirtualConsole();
    virtualConsole.on("jsdomError", error => {
      if (error.type === "unhandled exception") errors.push(error.detail?.message || error.message);
    });
    const dom = new JSDOM(html, {
      runScripts: "dangerously",
      virtualConsole,
      beforeParse(window) {
        Object.defineProperty(window, "devicePixelRatio", { configurable: true, value: dpr });
        window.CSS = { supports: () => true };
        window.requestAnimationFrame = () => 1;
        window.cancelAnimationFrame = () => {};
      }
    });
    try {
      expect(errors).toEqual([]);
      const { window } = dom;
      expect(window.taskbarLyrics?.receive).toBeTypeOf("function");
      const viewport = window.document.querySelector("#viewport");
      Object.defineProperty(viewport, "clientHeight", { configurable: true, value: 46 });
      const receive = (type, payload) => window.taskbarLyrics.receive({ version: 1, type, payload });
      receive("style", { fontSize: 18, fontWeight: "Bold", hostPixelsPerDip: dpr });
      expect(window.document.documentElement.style.getPropertyValue("--current-size")).toBe("18.00px");
      receive("lyrics", {
        current: "There ain't no kind of line",
        next: "That I wouldn't cross if you need me to",
        currentLineIndex: 0,
        trackId: "startup",
        isPlaying: true,
        progress: 0,
        animateTransition: false
      });
      expect(window.document.querySelector("#currentLineText").textContent).toBe("There ain't no kind of line");
      expect(window.document.querySelector("#nextLineText").textContent).toBe("That I wouldn't cross if you need me to");
    } finally {
      dom.window.close();
    }
  });
});
