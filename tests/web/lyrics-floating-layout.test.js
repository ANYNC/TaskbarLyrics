import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");

async function createLyricsDom() {
  const [html, ...scripts] = await Promise.all([
    read("TaskbarLyrics.App/Web/Lyrics/index.html"),
    ...["bridge", "state", "presentation", "app"].map(name => read(`TaskbarLyrics.App/Web/Lyrics/${name}.js`))
  ]);
  const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
    runScripts: "outside-only"
  });
  dom.window.CSS = { supports: () => true };
  dom.window.requestAnimationFrame = () => 1;
  dom.window.cancelAnimationFrame = () => {};
  scripts.forEach(script => dom.window.eval(script));
  return dom;
}

describe("floating lyrics block height", () => {
  it("applies and replaces the host height without changing typography or artwork", async () => {
    const dom = await createLyricsDom();
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
    const dom = await createLyricsDom();
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
