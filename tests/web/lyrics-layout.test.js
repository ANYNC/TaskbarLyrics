import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");

describe("lyrics responsive layout", () => {
  it("keeps every spectrum bar visible when scaled geometry exceeds the viewport", async () => {
    const [html, state, script] = await Promise.all([
      read("TaskbarLyrics.App/Web/Lyrics/index.html"),
      read("TaskbarLyrics.App/Web/Lyrics/state.js"),
      read("TaskbarLyrics.App/Web/Lyrics/app.js")
    ]);
    const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
      runScripts: "outside-only"
    });
    const spectrum = dom.window.document.querySelector(".spectrum");
    Object.defineProperty(spectrum, "clientWidth", { configurable: true, value: 120 });
    dom.window.CSS = { supports: () => true };
    dom.window.requestAnimationFrame = () => 1;
    dom.window.cancelAnimationFrame = () => {};
    dom.window.eval(state);
    dom.window.eval(script);

    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "spectrumTuning",
      payload: { barCount: 32 }
    });
    dom.window.taskbarLyrics.receive({
      version: 1,
      type: "style",
      payload: {
        layoutScalePercent: 300,
        spectrumWidth: 630,
        spectrumGap: 9,
        spectrumBarWidth: 9
      }
    });

    const fittedBarWidth = Number.parseFloat(spectrum.style.getPropertyValue("--spectrum-fitted-bar-width"));
    const fittedGap = Number.parseFloat(spectrum.style.getPropertyValue("--spectrum-fitted-gap"));
    const fittedTotalWidth = (fittedBarWidth * 32) + (fittedGap * 31);

    expect(spectrum.children).toHaveLength(32);
    expect(fittedBarWidth).toBeGreaterThanOrEqual(1);
    expect(fittedGap).toBeGreaterThanOrEqual(0);
    expect(fittedTotalWidth).toBeLessThanOrEqual(120);
  });
});
