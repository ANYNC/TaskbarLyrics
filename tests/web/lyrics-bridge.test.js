import { readFile } from "node:fs/promises";
import { JSDOM } from "./helpers/localized-dom.js";
import { describe, expect, it } from "vitest";
import { createLyricsDom, loadLyricsAssets, runLyricsScripts } from "./helpers/lyrics-dom.js";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");

describe("lyrics WebView bridge", () => {
  it("uses the V1 envelope for diagnostics", async () => {
    const bridge = await read("TaskbarLyrics.App/Web/Lyrics/bridge.js");
    const dom = new JSDOM("<!doctype html>", { runScripts: "outside-only" });
    const sent = [];
    dom.window.chrome = { webview: { postMessage: value => sent.push(JSON.parse(value)) } };
    dom.window.eval(bridge);

    dom.window.taskbarLyricsBridge.post("coverDecodeError", { trackId: "track-1" });

    expect(sent).toEqual([{
      version: 1,
      type: "coverDecodeError",
      payload: { trackId: "track-1" }
    }]);
  });

  it("samples the whole lyrics viewport and restores fixed colors when adaptive mode is disabled", async () => {
    const assets = await loadLyricsAssets();
    const dom = createLyricsDom(assets.html);
    const { window } = dom;
    try {
      const { document } = window;
      const sent = [];
      window.chrome = { webview: { postMessage: value => sent.push(JSON.parse(value)) } };
      Object.defineProperty(document, "hidden", { value: false });
      const bounds = { left: 80, top: 10, right: 480, bottom: 50 };
      document.getElementById("viewport").getBoundingClientRect = () => bounds;
      window.Range.prototype.getBoundingClientRect = () => ({ left: 85, top: 15, right: 105, bottom: 25 });
      window.HTMLCanvasElement.prototype.getContext = () => null;
      document.getElementById("currentLine").style.opacity = "1";
      document.getElementById("nextLineText").textContent = " ";
      let tick;
      let cleared = false;
      window.setInterval = callback => { tick = callback; return 42; };
      window.clearInterval = id => { cleared = id === 42; };
      runLyricsScripts(dom, assets);
      const receive = (type, payload) => window.taskbarLyrics.receive({ version: 1, type, payload });

      receive("style", { adaptiveForeground: true, primaryColor: "#ffffff", fontFamily: "Segoe UI" });
      tick();
      const request = sent.find(message => message.type === "lyricBackground");
      expect(request?.version).toBe(1);
      expect(request?.payload.region).toEqual({
        x: 80 / window.innerWidth, y: 10 / window.innerHeight,
        width: 400 / window.innerWidth, height: 40 / window.innerHeight
      });
      expect(request?.payload.text[0].width).toBe(20 / window.innerWidth);

      receive("adaptiveForeground", { primaryColor: "#111827" });
      expect(document.documentElement.style.getPropertyValue("--primary")).toBe("#ffffff");
      receive("adaptiveForeground", {
        primaryColor: "#111827", secondaryColor: "rgba(17,24,39,0.6)",
        translationColor: "rgba(17,24,39,0.7)", wordScanOverlayColor: "rgba(17,24,39,0.75)",
        outlineColor: "#ffffff"
      });
      expect(document.documentElement.style.getPropertyValue("--primary")).toBe("#111827");
      expect(document.documentElement.style.getPropertyValue("--font-family")).toBe("Segoe UI");
      expect(document.documentElement.style.getPropertyValue("--adaptive-lyric-outline")).toBe("");
      receive("style", { adaptiveForeground: false, primaryColor: "#345678" });
      expect(cleared).toBe(true);
      expect(document.documentElement.style.getPropertyValue("--primary")).toBe("#345678");
      expect(document.documentElement.style.getPropertyValue("--adaptive-lyric-outline")).toBe("");
    } finally {
      dom.window.close();
    }
  });
});
