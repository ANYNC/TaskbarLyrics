import { loadLyricsAssets, createLyricsDom, runLyricsScripts } from "./helpers/lyrics-dom.js";
import { describe, expect, it } from "vitest";

async function createFloatingLyricsDom() {
  const sources = await loadLyricsAssets();
  const { html } = sources;
  const dom = createLyricsDom(html);
  dom.window.requestAnimationFrame = () => 1;
  dom.window.cancelAnimationFrame = () => {};
  runLyricsScripts(dom, sources);
  return dom;
}

describe("floating lyrics block height", () => {
  it("applies and replaces the host height without changing typography or artwork", async () => {
    const dom = await createFloatingLyricsDom();
    try {
      const root = dom.window.document.documentElement;
      const style = payload => dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload });
      style({ floatingControlPanel: true, lyricsPaneHeight: 40, coverSize: 34, fontSize: 14 });
      expect(root.style.getPropertyValue("--lyrics-pane-height")).toBe("40px");
      expect(root.style.getPropertyValue("--cover-size")).toBe("34px");
      expect(root.style.getPropertyValue("--font-size")).toBe("14px");
      style({ floatingControlPanel: true, lyricsPaneHeight: 60 });
      expect(root.style.getPropertyValue("--lyrics-pane-height")).toBe("60px");
    } finally {
      dom.window.close();
    }
  });

  it.each([undefined, null, 0, -2, "invalid", Infinity])("clears a previous height for invalid or absent value %s", async value => {
    const dom = await createFloatingLyricsDom();
    try {
      const style = payload => dom.window.taskbarLyrics.receive({ version: 1, type: "style", payload });
      style({ floatingControlPanel: true, lyricsPaneHeight: 40 });
      style({ floatingControlPanel: false, lyricsPaneHeight: value });
      expect(dom.window.document.documentElement.style.getPropertyValue("--lyrics-pane-height")).toBe("");
    } finally {
      dom.window.close();
    }
  });
});
