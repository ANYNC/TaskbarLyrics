import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

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
});
