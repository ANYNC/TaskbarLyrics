import { describe, expect, it } from "vitest";
import { loadLyricsAssets, createLyricsDom, runLyricsScripts } from "./helpers/lyrics-dom.js";

async function harness({ dpi = 1.5, textScale = 1.4, height = 29, width = 289 } = {}) {
  const assets = await loadLyricsAssets();
  const dom = createLyricsDom(assets.html);
  const { window } = dom;
  const doc = window.document;
  const root = doc.documentElement;
  const sent = [];
  let resized;
  let rasterScale = dpi * textScale;
  Object.defineProperty(window, "devicePixelRatio", { get: () => rasterScale });
  Object.defineProperty(window, "innerHeight", { value: height });
  Object.defineProperty(window, "innerWidth", { value: width });
  window.requestAnimationFrame = () => 1;
  window.cancelAnimationFrame = () => {};
  window.ResizeObserver = class { constructor(callback) { resized = callback; } observe() {} };
  window.chrome = { webview: { postMessage: json => sent.push(JSON.parse(json)) } };
  const metric = name => Number.parseFloat(root.style.getPropertyValue(name)) || 0;
  const rect = (x, y, w, h) => ({ x, y, width: w, height: h, right: x + w, bottom: y + h });
  doc.querySelector(".layout").getBoundingClientRect = () => rect(0, 0, width, height);
  const viewport = doc.querySelector(".viewport");
  viewport.getBoundingClientRect = () => rect(0, 0, width, height - metric("--lyrics-pane-padding-top"));
  const cover = doc.querySelector(".cover");
  cover.getBoundingClientRect = () => rect(0,
    (height - metric("--viewport-descender-buffer") - metric("--cover-size")) / 2,
    metric("--cover-size"), metric("--cover-size"));
  runLyricsScripts(dom, assets);
  const style = payload => window.taskbarLyrics.receive({ version: 1, type: "style", payload: {
    hostPixelsPerDip: dpi, fontSize: 14, layoutScalePercent: 100,
    showCover: true, coverSize: 34, coverGap: 8, coverCornerRadius: 6,
    lyricsPaneTopPadding: 10 / 3, primaryOffsetY: 4 / 3, secondaryOffsetY: 4 / 3,
    lineTextBottomPadding: 2, viewportDescenderBuffer: 2,
    ...payload
  } });
  return { dom, window, root, sent, metric, cover, style, rect, resize: scale => { rasterScale = dpi * scale; resized(); } };
}

describe("Windows text scaling in the lyrics viewport", () => {
  it("converts WPF artwork geometry while retaining the requested text size", async () => {
    const h = await harness();
    try {
      h.style();
      expect(h.metric("--cover-size") * 2.1).toBeCloseTo(34 * 1.5);
      expect(h.metric("--cover-gap") * 2.1).toBeCloseTo(8 * 1.5);
      expect(h.metric("--font-size")).toBe(14);
      expect(h.cover.getBoundingClientRect().bottom).toBeLessThanOrEqual(29);
      h.cover.click();
      expect(h.sent.at(-1)).toMatchObject({ version: 1, type: "coverClick" });
      const { y, height, viewportHeight } = h.sent.at(-1).payload;
      expect(y).toBeGreaterThanOrEqual(0);
      expect(y + height).toBeLessThanOrEqual(viewportHeight);
    } finally { h.dom.window.close(); }
  });

  it.each([1.4, 2.25])("fits both rows at text scale %s without a minimum-font overflow", async textScale => {
    const h = await harness({ textScale });
    try {
      h.style();
      const row = h.metric("--row-height");
      const offset = h.metric("--secondary-offset-y");
      const textHeight = h.metric("--current-size") * 1.12 + h.metric("--line-text-padding-bottom");
      const bottom = h.metric("--lyrics-pane-padding-top") + h.metric("--line-pitch") + offset + (row + textHeight) / 2;
      expect(bottom).toBeLessThanOrEqual(29 + 0.001);
      expect(h.metric("--current-size")).toBeGreaterThan(0);
    } finally { h.dom.window.close(); }
  });

  it("allows the full larger single-line text when there is enough room", async () => {
    const h = await harness();
    try {
      h.style({ singleLine: true, fontSize: 19.6, lyricsPaneTopPadding: 0, primaryOffsetY: 0, secondaryOffsetY: 0 });
      expect(h.metric("--current-size")).toBe(19.6);
      expect(h.metric("--current-size") * 2.1).toBeCloseTo(19.6 * 1.5 * 1.4);
    } finally { h.dom.window.close(); }
  });

  it("fits the user's 25% layout and keeps both lines inside its eight-pixel viewport", async () => {
    const h = await harness({ height: 8, width: 298 });
    try {
      h.style({ layoutScalePercent: 25, fontSize: 3.5, coverSize: 26 / 3, coverGap: 2,
        viewportDescenderBuffer: 2 / 3, lyricsPaneTopPadding: 2 / 3,
        primaryOffsetY: 0, secondaryOffsetY: 0, lineTextBottomPadding: 2 / 3 });
      const row = h.metric("--row-height");
      const textHeight = h.metric("--current-size") * 1.12 + h.metric("--line-text-padding-bottom");
      expect(h.metric("--lyrics-pane-padding-top") + h.metric("--line-pitch") + (row + textHeight) / 2).toBeLessThanOrEqual(8);
      h.cover.click();
      expect(h.sent.at(-1).payload.y + h.sent.at(-1).payload.height).toBeLessThanOrEqual(8);
    } finally { h.dom.window.close(); }
  });

  it("recalculates from saved geometry on live text-scale changes without accumulating shrinkage", async () => {
    const h = await harness({ textScale: 1, height: 40 });
    try {
      h.style();
      expect(h.metric("--cover-size")).toBe(34);
      h.resize(1.4);
      expect(h.metric("--cover-size")).toBeCloseTo(34 / 1.4);
      h.resize(2.25);
      expect(h.metric("--cover-size")).toBeCloseTo(34 / 2.25);
      h.resize(1);
      expect(h.metric("--cover-size")).toBe(34);
      expect(h.metric("--font-size")).toBe(14);
    } finally { h.dom.window.close(); }
  });

  it("keeps spectrum geometry in the same physical units as artwork", async () => {
    const h = await harness();
    try {
      const spectrum = h.window.document.querySelector(".spectrum");
      Object.defineProperty(spectrum, "clientWidth", { value: 150 });
      h.style({ spectrumWidth: 210, spectrumHeight: 22, spectrumBarWidth: 3, spectrumGap: 3 });
      expect(h.metric("--spectrum-height") * 2.1).toBeCloseTo(22 * 1.5);
      expect(Number.parseFloat(spectrum.style.getPropertyValue("--spectrum-fitted-bar-width")) * 2.1).toBeCloseTo(3 * 1.5);
    } finally { h.dom.window.close(); }
  });

  it("sends only the visible intersection and ignores fully hidden trigger elements", async () => {
    const h = await harness();
    try {
      h.style();
      h.cover.getBoundingClientRect = () => h.rect(-2, -3, 40, 36);
      h.cover.click();
      expect(h.sent.at(-1).payload).toMatchObject({ x: 0, y: 0, width: 38, height: 29 });
      h.cover.getBoundingClientRect = () => h.rect(0, -40, 34, 34);
      h.cover.click();
      expect(h.sent).toHaveLength(1);
    } finally { h.dom.window.close(); }
  });
});
