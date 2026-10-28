import { readFile } from "node:fs/promises";
import { JSDOM } from "./helpers/localized-dom.js";
import { describe, expect, it } from "vitest";

const root = new URL("../../TaskbarLyrics.App/Web/Settings/", import.meta.url);
const modeKey = "hotkeyToggleDisplayMode";
const modeDefinition = {
  action: "ToggleDisplayMode", settingKey: modeKey, statusKey: "toggleDisplayMode",
  displayName: "切换歌词显示方式（嵌入 / 浮窗）", enabledSettingKey: modeKey + "Enabled", isEnabled: true
};

async function createHotkeysDom(settings = {}) {
  const names = ["settings.html", "bridge.js", "hotkeys.js", "state.js", "display-presentation.js", "player-compatibility.js", "settings.js"];
  const [html, ...scripts] = await Promise.all(names.map(name => readFile(new URL(name, root), "utf8")));
  const dom = new JSDOM(html, { runScripts: "outside-only" });
  const sent = [];
  dom.window.chrome = { webview: { postMessage: value => sent.push(JSON.parse(value)) } };
  dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  dom.window.requestAnimationFrame = callback => { callback(0); return 0; };
  dom.window.HTMLMediaElement.prototype.load = () => {};
  dom.window.HTMLMediaElement.prototype.pause = () => {};
  dom.window.HTMLMediaElement.prototype.play = () => Promise.resolve();
  dom.window.HTMLElement.prototype.scrollIntoView = () => {};
  scripts.forEach(script => dom.window.eval(script));
  const baseSettings = {
    enableGlobalMediaHotkeys: true, [modeKey]: "Alt+Shift+C",
    mediaHotkeys: [modeDefinition], mediaHotkeyStatuses: { toggleDisplayMode: "registered" }, ...settings
  };
  const hydrate = (next = {}) => dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: {
    settings: structuredClone({ ...baseSettings, ...next }), fonts: []
  } });
  hydrate();
  dom.window.settingsApp.receive({ version: 1, type: "navigate", payload: { page: "shortcuts" } });
  const document = dom.window.document;
  const get = name => document.querySelector(`[data-hotkey-${name}]`);
  const key = (value, modifiers = {}) => document.activeElement.dispatchEvent(new dom.window.KeyboardEvent("keydown", {
    key: value, bubbles: true, cancelable: true, ...modifiers
  }));
  return { dom, document, sent, hydrate, get, key };
}

describe("individual global hotkeys", () => {
  it("shows the mode shortcut, enabled switch and clear action", async () => {
    const { dom, get } = await createHotkeysDom();
    expect(get("binding").textContent).toBe("Alt+Shift+C");
    expect(get("enabled").checked).toBe(true);
    expect(get("clear").getAttribute("aria-label")).toContain("清空切换歌词显示方式");
    expect(get("status").textContent).toBe("已注册");
    dom.window.close();
  });

  it("disables one shortcut without clearing its binding and preserves focus", async () => {
    const { dom, get, sent } = await createHotkeysDom();
    get("enabled").focus();
    get("enabled").checked = false;
    get("enabled").dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1)).toEqual({ version: 1, type: "update", payload: { key: modeKey + "Enabled", value: false } });
    expect(get("binding").textContent).toBe("Alt+Shift+C");
    expect(get("status").textContent).toBe("已关闭");
    expect(dom.window.document.activeElement).toBe(get("enabled"));
    get("enabled").checked = true;
    get("enabled").dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1).payload.value).toBe(true);
    expect(get("binding").textContent).toBe("Alt+Shift+C");
    dom.window.close();
  });

  it("clears and echoes an empty binding without restoring its default", async () => {
    const { dom, get, sent, hydrate } = await createHotkeysDom();
    get("clear").click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "update", payload: { key: modeKey, value: "" } });
    expect(get("binding").textContent).toBe("未设置");
    expect(get("status").textContent).toBe("未设置");
    expect(get("status").dataset.state).toBe("off");
    hydrate({ [modeKey]: "", mediaHotkeyStatuses: { toggleDisplayMode: "unassigned" } });
    expect(get("binding").textContent).toBe("未设置");
    get("reset").click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "resetMediaHotkey", payload: "ToggleDisplayMode" });
    hydrate();
    expect(get("binding").textContent).toBe("Alt+Shift+C");
    dom.window.close();
  });

  it.each(["Backspace", "Delete"])("clears using %s during recording", async value => {
    const { dom, get, sent, key } = await createHotkeysDom();
    get("binding").click();
    key(value);
    expect(sent.at(-1).payload).toEqual({ key: modeKey, value: "" });
    expect(get("binding").classList.contains("recording")).toBe(false);
    expect(get("binding").textContent).toBe("未设置");
    dom.window.close();
  });

  it("keeps modified Delete recordable and Escape cancelable", async () => {
    const { dom, get, sent, key } = await createHotkeysDom();
    get("binding").click();
    key("Delete", { ctrlKey: true });
    expect(sent.at(-1).payload).toEqual({ key: modeKey, value: "Ctrl+Delete" });
    get("binding").click();
    const count = sent.length;
    key("Escape");
    expect(sent).toHaveLength(count);
    expect(get("binding").textContent).toBe("Ctrl+Delete");
    dom.window.close();
  });

  it("keeps recording across settings refresh and still accepts a new binding", async () => {
    const { dom, get, sent, key, hydrate } = await createHotkeysDom();
    get("binding").click();
    hydrate();
    expect(get("binding").classList.contains("recording")).toBe(true);
    key("k", { altKey: true, shiftKey: true });
    expect(sent.at(-1).payload).toEqual({ key: modeKey, value: "Alt+Shift+K" });
    dom.window.close();
  });

  it("ends recording when restoring the default shortcut", async () => {
    const { dom, get, sent, key } = await createHotkeysDom();
    get("binding").click();
    get("reset").click();
    expect(get("binding").classList.contains("recording")).toBe(false);
    expect(sent.at(-1).type).toBe("resetMediaHotkey");
    const count = sent.length;
    key("x", { ctrlKey: true });
    expect(sent).toHaveLength(count);
    dom.window.close();
  });

  it("applies the master switch and individual switches to other existing hotkeys", async () => {
    const definition = { action: "ToggleLyricsVisibility", settingKey: "hotkeyToggleLyricsVisibility",
      statusKey: "toggleLyricsVisibility", displayName: "显示 / 隐藏歌词", isEnabled: false };
    const { dom, get, sent, hydrate } = await createHotkeysDom({
      mediaHotkeys: [definition], hotkeyToggleLyricsVisibility: "Alt+Shift+D"
    });
    expect(get("enabled").checked).toBe(false);
    expect(get("status").textContent).toBe("已关闭");
    get("enabled").checked = true;
    get("enabled").dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1).payload).toEqual({ key: "hotkeyToggleLyricsVisibilityEnabled", value: true });
    hydrate({ enableGlobalMediaHotkeys: false, mediaHotkeys: [{ ...definition, isEnabled: true }] });
    expect(get("status").textContent).toBe("已关闭");
    expect(get("binding").textContent).toBe("Alt+Shift+D");
    dom.window.close();
  });
});
