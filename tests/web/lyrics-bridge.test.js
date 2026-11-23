import { readFile } from "node:fs/promises";
import { JSDOM } from "./helpers/localized-dom.js";
import { describe, expect, it, vi } from "vitest";
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
    vi.useFakeTimers();
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
      runLyricsScripts(dom, assets);
      const receive = (type, payload) => window.taskbarLyrics.receive({ version: 1, type, payload });
      const countSamples = () => sent.filter(message => message.type === "lyricBackground").length;
      receive("style", { adaptiveForeground: false, primaryColor: "#ffffff" });
      receive("resampleLyricBackground", {});
      vi.advanceTimersByTime(2000);
      expect(countSamples()).toBe(0);

      receive("style", { adaptiveForeground: true, primaryColor: "#ffffff", fontFamily: "Segoe UI" });
      vi.advanceTimersByTime(0);
      const request = sent.find(message => message.type === "lyricBackground");
      expect(request?.version).toBe(1);
      expect(request?.payload.region).toEqual({
        x: 80 / window.innerWidth, y: 10 / window.innerHeight,
        width: 400 / window.innerWidth, height: 40 / window.innerHeight
      });
      expect(request?.payload.text[0].width).toBe(20 / window.innerWidth);

      receive("adaptiveForeground", { primaryColor: "#111827" });
      expect(document.documentElement.style.getPropertyValue("--primary")).toBe("#ffffff");
      const palette = {
        primaryColor: "#111827", secondaryColor: "rgba(17,24,39,0.6)",
        translationColor: "rgba(17,24,39,0.7)", wordScanOverlayColor: "rgba(17,24,39,0.75)",
        outlineColor: "#ffffff"
      };
      vi.advanceTimersByTime(899);
      expect(countSamples()).toBe(1);
      vi.advanceTimersByTime(1);
      expect(countSamples()).toBe(2);
      receive("adaptiveForeground", { ...palette, confirming: true });
      vi.advanceTimersByTime(179);
      expect(countSamples()).toBe(2);
      vi.advanceTimersByTime(1);
      expect(countSamples()).toBe(3);
      receive("adaptiveForeground", { ...palette, confirming: false });
      vi.advanceTimersByTime(180);
      expect(countSamples()).toBe(3);
      receive("resampleLyricBackground", {});
      vi.advanceTimersByTime(180);
      expect(countSamples()).toBe(4);
      receive("regionVisibility", { generation: 0, phase: "hidden", edge: "bottom" });
      vi.advanceTimersByTime(1800);
      expect(countSamples()).toBe(4);
      receive("regionVisibility", { generation: 1, phase: "visible", edge: "bottom" });
      vi.advanceTimersByTime(0);
      expect(countSamples()).toBe(5);
      expect(document.documentElement.style.getPropertyValue("--primary")).toBe("#111827");
      expect(document.documentElement.style.getPropertyValue("--font-family")).toBe("Segoe UI");
      expect(document.documentElement.style.getPropertyValue("--adaptive-lyric-outline")).toBe("");
      receive("style", { adaptiveForeground: false, primaryColor: "#345678" });
      receive("adaptiveForeground", { ...palette, confirming: true });
      receive("resampleLyricBackground", {});
      vi.advanceTimersByTime(2000);
      expect(countSamples()).toBe(5);
      expect(document.documentElement.style.getPropertyValue("--primary")).toBe("#345678");
      expect(document.documentElement.style.getPropertyValue("--adaptive-lyric-outline")).toBe("");
    } finally {
      dom.window.close();
      vi.useRealTimers();
    }
  });

  it("samples the visible spectrum layer and masks the moving bars across their full height", async () => {
    vi.useFakeTimers();
    const assets = await loadLyricsAssets();
    const dom = createLyricsDom(assets.html);
    const { window } = dom;
    try {
      const { document } = window;
      const sent = [];
      window.chrome = { webview: { postMessage: value => sent.push(JSON.parse(value)) } };
      Object.defineProperty(document, "hidden", { value: false });
      const bounds = { left: 80, top: 10, right: 480, bottom: 50 };
      document.getElementById("spectrumLayer").getBoundingClientRect = () => bounds;
      const spectrum = document.querySelector(".spectrum");
      spectrum.getBoundingClientRect = () => ({ left: 84, top: 20, right: 288, bottom: 40 });
      spectrum.style.opacity = "0.9";
      const firstBar = spectrum.querySelector("span");
      firstBar.getBoundingClientRect = () => ({ left: 84, top: 27, right: 87, bottom: 34 });
      firstBar.style.backgroundColor = "rgba(255, 255, 255, 0.9)";
      runLyricsScripts(dom, assets);
      const receive = (type, payload) => window.taskbarLyrics.receive({ version: 1, type, payload });

      receive("style", { adaptiveForeground: true });
      receive("lyrics", { scene: "spectrum", isPureMusic: true, animateTransition: false });
      firstBar.style.opacity = "0.8";
      vi.advanceTimersByTime(0);
      const request = sent.at(-1);
      expect(request).toMatchObject({ version: 1, type: "lyricBackground", payload: { spectrum: true } });
      expect(request.payload.region.width).toBe(400 / window.innerWidth);
      expect(request.payload.text[0].width).toBe(204 / window.innerWidth);
      expect(request.payload.ink).toEqual([
        { x: 84 / window.innerWidth, y: 10 / window.innerHeight,
          width: 3 / window.innerWidth, height: 40 / window.innerHeight }
      ]);

      receive("adaptiveForeground", {
        primaryColor: "#111827", secondaryColor: "#111827", translationColor: "#111827", wordScanOverlayColor: "#111827"
      });
      expect(document.documentElement.style.getPropertyValue("--primary")).toBe("#111827");
    } finally {
      dom.window.close();
      vi.useRealTimers();
    }
  });
});
