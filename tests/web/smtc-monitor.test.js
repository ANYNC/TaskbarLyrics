import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);

describe("SMTC monitor long metadata", () => {
  it("keeps full player names available while rendering them in truncatable fields", async () => {
    const html = await readFile(
      new URL("TaskbarLyrics.App/Web/SmtcMonitor/index.html", root),
      "utf8");
    const dom = new JSDOM(html, { runScripts: "dangerously", beforeParse(window) {
      window.chrome = { webview: { postMessage() {} } };
    } });
    const longSource = "Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic";

    dom.window.smtcMonitor.setData({
      resolvedSource: longSource,
      sourceAppUserModelId: longSource,
      title: "夢遊",
      artist: "tayori"
    });

    const sourceName = dom.window.document.querySelector("#sourceName");
    expect(sourceName.textContent).toBe(longSource);
    expect(sourceName.dataset.fullValue).toBe(longSource);
    expect(sourceName.title).toBe("");

    const sourceValue = [...dom.window.document.querySelectorAll(".diagnostic-row")]
      .find(row => row.querySelector(".k")?.textContent.trimEnd() === "SourceAppId:")
      ?.querySelector(".diagnostic-value");
    expect(sourceValue?.textContent).toBe(longSource);
    expect(sourceValue?.dataset.fullValue).toBe(longSource);
    expect(sourceValue?.title).toBe("");
    expect(dom.window.document.querySelector("#log").textContent).toContain(
      `SourceAppId:        ${longSource}`);

    Object.defineProperties(sourceName, {
      scrollWidth: { value: 480, configurable: true },
      clientWidth: { value: 160, configurable: true }
    });
    sourceName.dispatchEvent(new dom.window.Event("focusin", { bubbles: true }));
    const tooltip = dom.window.document.querySelector("#hoverTooltip");
    expect(tooltip.textContent).toBe(longSource);
    expect(tooltip.dataset.state).toBe("open");
    expect(sourceName.getAttribute("aria-describedby")).toBe("hoverTooltip");
  });
});
