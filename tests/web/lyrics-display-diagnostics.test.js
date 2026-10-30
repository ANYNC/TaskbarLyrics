import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

async function readDiagnosticScript() {
  const source = await readFile(new URL("../../TaskbarLyrics.App/LyricsDisplayDiagnostics.cs", import.meta.url), "utf8");
  return source.match(/ReadPageScript = """\r?\n([\s\S]*?)\r?\n\s*""";/)[1];
}

describe("display diagnostic page reader", () => {
  it("reports hidden or clipped content without exposing lyrics or changing the page", async () => {
    const dom = new JSDOM('<html><body><div id="layout" style="opacity:0"><div id="viewport" style="overflow:hidden"><span id="currentLineText">private lyric text</span></div></div></body></html>', { runScripts: "outside-only" });
    try {
      dom.window.performance.getEntriesByType = () => [];
      const before = dom.window.document.documentElement.outerHTML;
      const result = dom.window.eval(await readDiagnosticScript());
      expect(result.elements.find(e => e.selector === "#layout").opacity).toBe("0");
      expect(result.elements.find(e => e.selector === "#viewport").overflow).toBe("hidden");
      expect(result.elements.find(e => e.selector === "#currentLineText").textLength).toBe(18);
      expect(result.elements.find(e => e.selector === "#cover").missing).toBe(true);
      expect(JSON.stringify(result)).not.toContain("private lyric text");
      expect(dom.window.document.documentElement.outerHTML).toBe(before);
    } finally { dom.window.close(); }
  });
});
