import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");

async function createSettingsDom() {
  const [html, bridge, hotkeys, state, script] = await Promise.all([
    read("TaskbarLyrics.App/Web/Settings/settings.html"),
    read("TaskbarLyrics.App/Web/Settings/bridge.js"),
    read("TaskbarLyrics.App/Web/Settings/hotkeys.js"),
    read("TaskbarLyrics.App/Web/Settings/state.js"),
    read("TaskbarLyrics.App/Web/Settings/settings.js")
  ]);
  const dom = new JSDOM(html, { runScripts: "outside-only" });
  const sent = [];
  dom.window.chrome = { webview: { postMessage: value => sent.push(JSON.parse(value)) } };
  dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  dom.window.requestAnimationFrame = callback => callback(0);
  dom.window.eval(bridge);
  dom.window.eval(hotkeys);
  dom.window.eval(state);
  return { dom, sent, script };
}

describe("settings WebView bridge", () => {
  it("sends every command in the V1 envelope", async () => {
    const { dom, sent } = await createSettingsDom();

    dom.window.taskbarLyricsBridge.post({ type: "update", key: "fontSize", value: 18 });

    expect(sent).toEqual([{
      version: 1,
      type: "update",
      payload: { key: "fontSize", value: 18 }
    }]);
  });

  it("maps stable hotkey states to localized presentation", async () => {
    const { dom } = await createSettingsDom();

    expect(dom.window.taskbarLyricsHotkeys.label("registered")).toBe("已注册");
    expect(dom.window.taskbarLyricsHotkeys.visualState("registered")).toBe("ready");
    expect(dom.window.taskbarLyricsHotkeys.label("unknown")).toBe("未注册");
  });

  it("dispatches a V1 navigation message through the public receive entry", async () => {
    const { dom, script } = await createSettingsDom();

    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "navigate", payload: { page: "lyrics", focusCurrentTrack: false } });

    expect(dom.window.document.querySelector('[data-nav="lyrics"]').classList.contains("active")).toBe(true);
  });

  it("uses visible navigation order for page transition direction", async () => {
    const { dom, script } = await createSettingsDom();
    const document = dom.window.document;
    const pendingAnimation = new Promise(() => {});

    document.querySelectorAll("[data-page]").forEach(page => {
      page.animate = () => ({ cancel() {}, finished: pendingAnimation });
    });
    dom.window.eval(script);

    document.querySelector('[data-page="sources"]').classList.remove("active");
    document.querySelector('[data-page="general"]').classList.add("active");
    document.querySelector('[data-nav="shortcuts"]').click();

    expect(document.querySelector('[data-page="shortcuts"]').style.transform).toBe("translateX(28px)");
  });
});
