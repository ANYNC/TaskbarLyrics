import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const pageUrl = new URL("../../TaskbarLyrics.App/Web/SpectrumTuning/index.html", import.meta.url);

async function createTuningPage() {
  const html = await readFile(pageUrl, "utf8");
  const dom = new JSDOM(html, { runScripts: "outside-only" });
  const sent = [];
  dom.window.chrome = { webview: { postMessage: message => sent.push(JSON.parse(message)) } };
  dom.window.matchMedia = () => ({ matches: false, addEventListener() {} });
  dom.window.ResizeObserver = class { observe() {} };
  dom.window.HTMLCanvasElement.prototype.getContext = () => ({ setTransform() {} });
  dom.window.requestAnimationFrame = () => 1;
  dom.window.eval(dom.window.document.querySelector("script").textContent);

  const document = dom.window.document;
  const rowFor = label => [...document.querySelectorAll("#sliders .row")]
    .find(row => row.querySelector(".label-text")?.textContent === label);
  return { dom, document, sent, rowFor };
}

describe("spectrum tuning defaults and help", () => {
  it("restores each frequency default while keeping the adjustable range", async () => {
    const { dom, sent, rowFor } = await createTuningPage();
    dom.window.spectrumTuning.setSettings({ MinFrequency: 35, MaxFrequency: 5000 });

    const minimum = rowFor("最低频率");
    const maximum = rowFor("最高频率");
    expect(minimum.querySelector(".value").textContent).toBe("35 Hz");
    expect(maximum.querySelector(".value").textContent).toBe("5000 Hz");

    minimum.querySelector(".reset").click();
    expect(minimum.querySelector(".value").textContent).toBe("28 Hz");
    expect(sent.at(-1)).toEqual({ type: "update", key: "MinFrequency", value: 28 });

    maximum.querySelector(".reset").click();
    expect(maximum.querySelector(".value").textContent).toBe("4000 Hz");
    expect(sent.at(-1)).toEqual({ type: "update", key: "MaxFrequency", value: 4000 });

    const frequencyHelp = minimum.querySelector(".info-tip").dataset.tip;
    expect(frequencyHelp).toContain("范围 20–20000 · 默认 28");

    dom.window.document.querySelector("#resetBtn").click();
    expect(sent.at(-1)).toEqual({ type: "reset" });
  });

  it("shows the custom help overlay on hover and keyboard focus", async () => {
    const { dom, document, rowFor } = await createTuningPage();
    const tip = rowFor("最低频率").querySelector(".info-tip");
    const tooltip = document.querySelector("#parameterTooltip");
    expect(document.querySelectorAll("#sliders .row .info-tip")).toHaveLength(
      document.querySelectorAll("#sliders .row").length);
    expect(tip.closest(".lbl").hasAttribute("title")).toBe(false);
    expect(tooltip.parentElement).toBe(document.body);

    tip.dispatchEvent(new dom.window.Event("pointerover", { bubbles: true }));
    expect(tooltip.dataset.state).toBe("open");
    expect(tooltip.textContent).toContain("默认 28");
    expect(tip.getAttribute("aria-describedby")).toBe("parameterTooltip");

    document.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(tooltip.dataset.state).toBe("closed");
    expect(tip.hasAttribute("aria-describedby")).toBe(false);

    tip.focus();
    expect(tooltip.dataset.state).toBe("open");
    tip.blur();
    expect(tooltip.dataset.state).toBe("closed");
  });
});
