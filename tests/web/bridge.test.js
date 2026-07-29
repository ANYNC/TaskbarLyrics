import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");

async function createSettingsDom({ deferAnimationFrames = false } = {}) {
  const [html, bridge, hotkeys, state, script] = await Promise.all([
    read("TaskbarLyrics.App/Web/Settings/settings.html"),
    read("TaskbarLyrics.App/Web/Settings/bridge.js"),
    read("TaskbarLyrics.App/Web/Settings/hotkeys.js"),
    read("TaskbarLyrics.App/Web/Settings/state.js"),
    read("TaskbarLyrics.App/Web/Settings/settings.js")
  ]);
  const dom = new JSDOM(html, { runScripts: "outside-only" });
  const sent = [];
  const animationFrames = [];
  dom.window.chrome = { webview: { postMessage: value => sent.push(JSON.parse(value)) } };
  dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  dom.window.requestAnimationFrame = callback => {
    if (!deferAnimationFrames) {
      callback(0);
      return 0;
    }
    animationFrames.push(callback);
    return animationFrames.length;
  };
  dom.window.eval(bridge);
  dom.window.eval(hotkeys);
  dom.window.eval(state);
  return {
    dom,
    sent,
    script,
    runAnimationFrames() {
      animationFrames.splice(0).forEach(callback => callback(0));
    }
  };
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

  it("posts layout scale updates and renders host-calculated effective metrics", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: {
          sourceRecognitionOrder: [],
          playerLyricOffsets: {},
          defaultPlayerLyricOffsets: {},
          mediaHotkeys: [],
          mediaHotkeyStatuses: {},
          fontFamily: "Source Han Sans SC",
          fontWeight: "Bold",
          foregroundColorMode: "Light",
          foregroundColor: "#FFFFFFFF",
          backgroundOpacity: 0.55,
          lyricsLayoutScalePercent: 100,
          fontSize: 14,
          coverSize: 34,
          coverGap: 8,
          coverCornerRadius: 6,
          effectiveFontSize: 14,
          effectiveCoverSize: 34,
          effectiveCoverGap: 8,
          effectiveCoverCornerRadius: 6
        },
        fonts: ["Source Han Sans SC"]
      }
    });

    const slider = document.querySelector('input[type="range"][data-setting="lyricsLayoutScalePercent"]');
    slider.value = "125";
    slider.dispatchEvent(new dom.window.Event("input", { bubbles: true }));

    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "previewUpdate",
      payload: { key: "lyricsLayoutScalePercent", value: 125 }
    });

    slider.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "lyricsLayoutScalePercent", value: 125 }
    });

    dom.window.settingsApp.receive({
      version: 1,
      type: "lyricsLayoutPreview",
      payload: {
        scalePercent: 125,
        fontSize: 14,
        coverSize: 34,
        coverGap: 8,
        coverCornerRadius: 6,
        effectiveFontSize: 17.5,
        effectiveCoverSize: 43,
        effectiveCoverGap: 10,
        effectiveCoverCornerRadius: 8
      }
    });

    expect(document.querySelector("[data-effective-font-size]").textContent).toBe("17.5 px");
    expect(document.querySelector("[data-effective-cover-size]").textContent).toBe("43 px");
    expect(document.querySelector("#layoutScaleAnnouncement").textContent).toContain("缩放已设为 125%");
  });

  it("keeps cover values while disabling cover-specific controls", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: {
          sourceRecognitionOrder: [],
          playerLyricOffsets: {},
          defaultPlayerLyricOffsets: {},
          mediaHotkeys: [],
          mediaHotkeyStatuses: {},
          foregroundColorMode: "Light",
          foregroundColor: "#FFFFFFFF",
          backgroundOpacity: 0.55,
          showCover: true,
          coverSize: 34,
          coverGap: 8,
          coverCornerRadius: 6
        },
        fonts: []
      }
    });

    const toggle = document.querySelector('input[data-setting="showCover"]');
    toggle.checked = false;
    toggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "showCover", value: false }
    });
    expect(document.querySelector('input[data-setting="coverSize"]').value).toBe("34");
    expect(document.querySelector("[data-effective-cover-size]").textContent).toBe("已隐藏");
    expect(document.querySelector("[data-effective-cover-gap-item]").hidden).toBe(true);
    document.querySelectorAll('[data-depends="showCover"]').forEach(row => {
      expect(row.classList.contains("is-disabled")).toBe(true);
      row.querySelectorAll("input, button").forEach(control => expect(control.disabled).toBe(true));
    });
  });

  it("keeps X and Y offset sliders synchronized with numeric inputs", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: {
          sourceRecognitionOrder: [],
          playerLyricOffsets: {},
          defaultPlayerLyricOffsets: {},
          mediaHotkeys: [],
          mediaHotkeyStatuses: {},
          foregroundColorMode: "Light",
          foregroundColor: "#FFFFFFFF",
          backgroundOpacity: 0.55,
          xOffset: 30,
          yOffset: -20
        },
        fonts: []
      }
    });

    const xSlider = document.querySelector('input[type="range"][data-setting="xOffset"]');
    const xNumber = document.querySelector('input[type="number"][data-setting="xOffset"]');
    const ySlider = document.querySelector('input[type="range"][data-setting="yOffset"]');
    const yNumber = document.querySelector('input[type="number"][data-setting="yOffset"]');

    expect(xSlider.value).toBe("30");
    expect(xNumber.value).toBe("30");
    expect(ySlider.value).toBe("-20");
    expect(yNumber.value).toBe("-20");

    xSlider.value = "120";
    xSlider.dispatchEvent(new dom.window.Event("input", { bubbles: true }));

    expect(xNumber.value).toBe("120");
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "previewUpdate",
      payload: { key: "xOffset", value: 120 }
    });

    xSlider.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "xOffset", value: 120 }
    });

    yNumber.value = "-75";
    yNumber.dispatchEvent(new dom.window.Event("input", { bubbles: true }));

    expect(ySlider.value).toBe("-75");
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "previewUpdate",
      payload: { key: "yOffset", value: -75 }
    });

    yNumber.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

    expect(ySlider.value).toBe("-75");
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "yOffset", value: -75 }
    });

    yNumber.value = "3000";
    yNumber.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

    expect(yNumber.value).toBe("2000");
    expect(ySlider.value).toBe("2000");
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "yOffset", value: 2000 }
    });
  });

  it("coalesces repeated slider input into one preview per animation frame", async () => {
    const { dom, sent, script, runAnimationFrames } = await createSettingsDom({ deferAnimationFrames: true });
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: {
          sourceRecognitionOrder: [],
          playerLyricOffsets: {},
          defaultPlayerLyricOffsets: {},
          mediaHotkeys: [],
          mediaHotkeyStatuses: {},
          foregroundColorMode: "Light",
          foregroundColor: "#FFFFFFFF",
          backgroundOpacity: 0.55,
          xOffset: 0,
          yOffset: 0
        },
        fonts: []
      }
    });
    const slider = document.querySelector('input[type="range"][data-setting="xOffset"]');
    const beforeInputCount = sent.length;

    slider.value = "10";
    slider.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    slider.value = "20";
    slider.dispatchEvent(new dom.window.Event("input", { bubbles: true }));

    expect(sent).toHaveLength(beforeInputCount);
    runAnimationFrames();
    expect(sent.slice(beforeInputCount)).toEqual([{
      version: 1,
      type: "previewUpdate",
      payload: { key: "xOffset", value: 20 }
    }]);
  });

  it("pairs every settings slider with a quiet numeric input", async () => {
    const { dom } = await createSettingsDom();
    const document = dom.window.document;
    const sliders = Array.from(document.querySelectorAll('input[type="range"][data-setting]'));

    expect(sliders).toHaveLength(7);
    sliders.forEach(slider => {
      const control = slider.closest(".slider-number-control");
      expect(control).not.toBeNull();
      expect(control.querySelector(`input[type="number"][data-setting="${slider.dataset.setting}"]`)).not.toBeNull();
      expect(control.querySelector(".compact-number-input")).not.toBeNull();
    });
    expect(document.querySelector("#hueSlider").closest(".slider-number-control").querySelector("#hueNumberInput")).not.toBeNull();
  });

  it("previews scaled numeric input and commits the canonical setting value", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: {
          sourceRecognitionOrder: [],
          playerLyricOffsets: {},
          defaultPlayerLyricOffsets: {},
          mediaHotkeys: [],
          mediaHotkeyStatuses: {},
          foregroundColorMode: "Light",
          foregroundColor: "#FFFFFFFF",
          showBackground: true,
          backgroundOpacity: 0.55
        },
        fonts: []
      }
    });
    const input = document.querySelector('input[type="number"][data-setting="backgroundOpacity"]');

    expect(input.value).toBe("55");
    input.value = "70";
    input.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "previewUpdate",
      payload: { key: "backgroundOpacity", value: 0.7 }
    });

    input.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "backgroundOpacity", value: 0.7 }
    });
  });

  it("supports radiogroup keyboard navigation for the tool theme", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: {
          sourceRecognitionOrder: [],
          playerLyricOffsets: {},
          defaultPlayerLyricOffsets: {},
          mediaHotkeys: [],
          mediaHotkeyStatuses: {},
          foregroundColorMode: "Light",
          foregroundColor: "#FFFFFFFF",
          toolWindowTheme: "System"
        },
        fonts: []
      }
    });
    const system = document.querySelector('[data-theme-value="System"]');

    system.focus();
    system.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true }));

    expect(document.activeElement.dataset.themeValue).toBe("Light");
    expect(document.activeElement.getAttribute("aria-checked")).toBe("true");
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "toolWindowTheme", value: "Light" }
    });
  });

  it("shows the actual host save result and persistent color validation", async () => {
    const { dom, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);

    dom.window.settingsApp.receive({ version: 1, type: "settingsSaveResult", payload: { success: false } });
    expect(document.querySelector("#saveState").dataset.state).toBe("error");
    expect(document.querySelector("#saveState").textContent).toContain("保存失败");

    const color = document.querySelector('[data-color-text="foregroundColor"]');
    color.value = "invalid";
    color.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(color.getAttribute("aria-invalid")).toBe("true");
    expect(document.querySelector("#foregroundColorError").hidden).toBe(false);

    color.value = "#123456";
    color.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(color.getAttribute("aria-invalid")).toBe("false");
    expect(document.querySelector("#foregroundColorError").hidden).toBe(true);
  });
});
