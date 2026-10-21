import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");
const settingsPages = ["gettingStarted", "sources", "shortcuts", "lyrics", "trackOffsets", "displayArea", "general", "advanced", "lyricDiagnostics", "about"];
const persistedSettings = [
  "enableLocalLyrics", "localMusicFolders", "enableGlobalMediaHotkeys", "showLyricsOnStartup", "autoHideWhenNoPlayback",
  "showLyricTranslation", "enableWordScanning", "spectrumDisplayMode", "lyricsLayoutScalePercent", "fontSize", "showCover", "singleLineLyrics",
  "enableControlPanel", "controlPanelTriggerMethod", "controlPanelTriggerCover", "controlPanelTriggerLyrics", "enableFloatingControlPanel", "floatingPanelBrightness", "floatingPanelTintOpacity", "floatingPanelBlurAmount", "embeddedPanelBrightness", "embeddedPanelTintOpacity", "embeddedPanelBlurAmount", "coverPosition", "coverSize", "coverGap", "coverCornerRadius", "fontFamily", "fontWeight", "lyricsTextAlignment", "foregroundColorMode",
  "showTextShadow", "toolWindowTheme", "showBackground", "backgroundOpacity", "showBorder", "windowWidth",
  "xOffset", "yOffset", "floatingWindowLayer", "startWithWindows", "autoCheckUpdates"
];

async function createSettingsDom({ deferAnimationFrames = false, reducedMotion = false } = {}) {
  const [html, bridge, hotkeys, state, presentation, compatibility, script] = await Promise.all([
    read("TaskbarLyrics.App/Web/Settings/settings.html"),
    read("TaskbarLyrics.App/Web/Settings/bridge.js"),
    read("TaskbarLyrics.App/Web/Settings/hotkeys.js"),
    read("TaskbarLyrics.App/Web/Settings/state.js"),
    read("TaskbarLyrics.App/Web/Settings/display-presentation.js"),
    read("TaskbarLyrics.App/Web/Settings/player-compatibility.js"),
    read("TaskbarLyrics.App/Web/Settings/settings.js")
  ]);
  const dom = new JSDOM(html, { runScripts: "outside-only" });
  const sent = [];
  const animationFrames = [];
  dom.window.chrome = { webview: { postMessage: value => sent.push(JSON.parse(value)) } };
  dom.window.matchMedia = () => ({ matches: reducedMotion, addEventListener() {}, removeEventListener() {} });
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
  dom.window.HTMLMediaElement.prototype.load = () => {};
  dom.window.HTMLMediaElement.prototype.pause = () => {};
  dom.window.HTMLMediaElement.prototype.play = () => Promise.resolve();
  dom.window.eval(state);
  dom.window.eval(presentation);
  dom.window.eval(compatibility);
  return {
    dom,
    sent,
    script,
    runAnimationFrames() {
      animationFrames.splice(0).forEach(callback => callback(0));
    }
  };
}

function enableDialogDomSupport(dom) {
  dom.window.HTMLElement.prototype.scrollIntoView = () => {};
  dom.window.HTMLDialogElement.prototype.showModal = function showModal() {
    this.open = true;
  };
  dom.window.HTMLDialogElement.prototype.close = function close() {
    this.open = false;
    this.dispatchEvent(new dom.window.Event("close"));
  };
}

describe("settings WebView bridge", () => {
  it("shows player-specific onboarding without changing playback settings or claiming a plugin is installed", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: { enableNetease: false } } });
    dom.window.settingsApp.receive({ version: 1, type: "navigate", payload: { page: "gettingStarted" } });
    const initialCount = sent.length;
    const guidance = document.querySelector("#gettingStartedGuidance");
    expect(guidance.hidden).toBe(false);
    expect(document.querySelector('input[name="gettingStartedPlayer"]:checked').value).toBe("netease");
    for (const [player, content] of [["netease", "inflink-rs"], ["kugou", "不支持"], ["qqmusic", "完美支持，直接播放即可"], ["spotify", "完美支持，直接播放即可"], ["applemusic", "时间轴信息不精准"], ["sodamusic", "完美支持，直接播放即可"], ["foobar2000", "foo_mediacontrol"], ["browser", "视网站而定"], ["other", "待确认"]]) {
      const radio = document.querySelector(`input[name="gettingStartedPlayer"][value="${player}"]`);
      radio.click();
      expect(guidance.hidden).toBe(false);
      expect(guidance.textContent).toContain(content);
      expect(guidance.textContent).not.toContain("未安装插件");
    }
    expect(sent).toHaveLength(initialCount);
    document.querySelector('input[name="gettingStartedPlayer"][value="netease"]').click();
    document.querySelector("[data-getting-started-done]").click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "openExternalLink", payload: "https://github.com/apoint123/inflink-rs" });
    document.querySelector('input[name="gettingStartedPlayer"][value="kugou"]').click();
    guidance.querySelector("[data-compatibility-link]").click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "openExternalLink", payload: "https://github.com/MoeKoeMusic/MoeKoeMusic" });
    expect(document.querySelector('[data-player-card="netease"] .source-info small').textContent).toContain("已停用");
    dom.window.close();
  });

  it("uses bundled player icons and a persistent other-player entry", async () => {
    const { dom, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    const options = [...document.querySelectorAll(".getting-started-option")];
    expect(options).toHaveLength(9);
    expect(document.querySelector("#gettingStartedCount").textContent).toBe("9");
    for (const option of options) {
      const icon = option.querySelector("img");
      expect(icon.alt).toBe("");
      expect(icon.getAttribute("src")).toMatch(/^\.\.\/\.\.\/Assets\/PlayerIcons\//);
      const iconPath = new URL(icon.getAttribute("src"), new URL("TaskbarLyrics.App/Web/Settings/settings.html", root));
      expect((await readFile(iconPath)).length).toBeGreaterThan(0);
    }
    expect(document.querySelector('#gettingStartedOther input').value).toBe("other");
    expect(document.querySelector("#gettingStartedGuidance h2").textContent).toBe("网易云音乐");
    expect(document.querySelector("#gettingStartedGuidance .compatibility-level").textContent).toContain("需配置插件");
    expect(document.querySelector("#gettingStartedGuidance [data-compatibility-link]")).toBeNull();
    dom.window.close();
  });

  it("filters player aliases without clearing the selection or changing settings and supports keyboard selection", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {} } });
    const search = document.querySelector("#gettingStartedSearch");
    dom.window.settingsApp.receive({ version: 1, type: "navigate", payload: { page: "gettingStarted" } });
    const initialCount = sent.length;
    const filter = query => {
      search.value = query;
      search.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    };
    filter("  APPLE MUSIC ");
    expect([...document.querySelectorAll('#gettingStartedPlayerList label:not([hidden]) input')].map(item => item.value)).toEqual(["applemusic"]);
    expect(document.querySelector('input[name="gettingStartedPlayer"]:checked').value).toBe("netease");
    expect(document.querySelector('#gettingStartedGuidance h2').textContent).toBe("网易云音乐");
    const apple = document.querySelector('input[name="gettingStartedPlayer"][value="applemusic"]');
    expect(apple.tabIndex).toBe(0);
    apple.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "End", bubbles: true }));
    expect(document.querySelector('input[name="gettingStartedPlayer"]:checked').value).toBe("other");
    expect(document.activeElement.value).toBe("other");
    filter("没有这个播放器");
    expect(document.querySelector("#gettingStartedNoResults").hidden).toBe(false);
    expect(document.querySelector('#gettingStartedOther input').tabIndex).toBe(0);
    expect(document.querySelector("#gettingStartedSearchStatus").textContent).toContain("找到 0 个播放器");
    search.focus();
    search.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
    expect(search.value).toBe("");
    expect(document.querySelector("#gettingStartedNoResults").hidden).toBe(true);
    expect(document.querySelectorAll('#gettingStartedPlayerList label:not([hidden])')).toHaveLength(8);
    expect(document.activeElement).toBe(search);
    expect(sent).toHaveLength(initialCount);
    dom.window.close();
  });

  it.each([
    ["netease", "https://github.com/apoint123/inflink-rs"],
    ["foobar2000", "https://github.com/ungive/foo_mediacontrol"]
  ])("opens %s setup documentation without changing settings or starting playback", async (player, url) => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {} } });
    document.querySelector(`input[name="gettingStartedPlayer"][value="${player}"]`).click();
    dom.window.settingsApp.receive({ version: 1, type: "navigate", payload: { page: "gettingStarted" } });
    const initialCount = sent.length;
    const button = document.querySelector("[data-getting-started-done]");
    expect(button.textContent).toBe("查看安装说明");
    button.click();
    expect(sent.slice(initialCount)).toEqual([{ version: 1, type: "openExternalLink", payload: url }]);
    expect(document.querySelector("[data-getting-started-ready]").hidden).toBe(false);
    dom.window.close();
  });

  it("routes other players to media-session discovery only after the user requests it", async () => {
    const player = "other";
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    dom.window.eval(script);
    const document = dom.window.document;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {} } });
    document.querySelector(`input[name="gettingStartedPlayer"][value="${player}"]`).click();
    expect(document.querySelector("#gettingStartedAddNote").hidden).toBe(false);
    dom.window.settingsApp.receive({ version: 1, type: "navigate", payload: { page: "gettingStarted" } });
    const initialCount = sent.length;
    document.querySelector("[data-getting-started-done]").click();
    expect(document.querySelector('[data-page="sources"]').classList.contains("active")).toBe(true);
    expect(document.querySelector("#playerSettingsDialog").open).toBe(true);
    expect(sent.slice(initialCount)).toEqual([
      { version: 1, type: "settingsPageChanged", payload: "sources" },
      { version: 1, type: "discoverPlayerSessions", payload: {} }
    ]);
    dom.window.close();
  });

  it("offers playback instead of duplicate registration for an already-added custom player and retains focus on refresh", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    const settings = { customPlayerSources: [{ sourceAppUserModelId: "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", displayName: "我的播放器", enabled: true }] };
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings } });
    const radio = document.querySelector('input[name="gettingStartedPlayer"][value="applemusic"]');
    radio.click();
    radio.focus();
    const guidance = document.querySelector("#gettingStartedGuidance").firstElementChild;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings } });
    expect(document.activeElement).toBe(radio);
    expect(document.querySelector("#gettingStartedGuidance").firstElementChild).toBe(guidance);
    expect(document.querySelector("#gettingStartedAddNote").hidden).toBe(true);
    dom.window.settingsApp.receive({ version: 1, type: "navigate", payload: { page: "gettingStarted" } });
    const button = document.querySelector("[data-getting-started-done]");
    expect(button.textContent).toBe("显示歌词");
    const initialCount = sent.length;
    button.click();
    expect(sent.slice(initialCount)).toEqual([
      { version: 1, type: "showLyricsWindow", payload: {} },
      { version: 1, type: "settingsPageChanged", payload: "sources" }
    ]);
    dom.window.close();
  });

  it("lets users skip onboarding and revisit it without posting configuration changes", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {} } });
    document.querySelector('[data-nav="gettingStarted"]').click();
    const initialCount = sent.length;
    document.querySelector("[data-getting-started-later]").click();
    expect(document.querySelector('[data-page="sources"]').classList.contains("active")).toBe(true);
    expect(sent.slice(initialCount)).toEqual([{ version: 1, type: "settingsPageChanged", payload: "sources" }]);
    document.querySelector('[data-nav="gettingStarted"]').click();
    expect(document.querySelector('[data-page="gettingStarted"]').classList.contains("active")).toBe(true);
    document.querySelector('input[name="gettingStartedPlayer"][value="qqmusic"]').click();
    document.querySelector("[data-getting-started-done]").click();
    expect(sent.slice(-2)).toEqual([
      { version: 1, type: "showLyricsWindow", payload: {} },
      { version: 1, type: "settingsPageChanged", payload: "sources" }
    ]);
    expect(document.querySelector('[data-page="sources"]').classList.contains("active")).toBe(true);
    dom.window.close();
  });

  it("keeps source cards compact without compatibility notices or extra actions", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: { enableNetease: false, enableKugou: false } } });
    const initialCount = sent.length;
    for (const card of document.querySelectorAll("[data-player-card]")) {
      expect(card.children).toHaveLength(2);
      expect(card.querySelector("[data-player-drag-id]")).not.toBeNull();
      expect(card.querySelector("[data-player-settings]")).not.toBeNull();
      expect(card.querySelector(".source-guidance, [data-player-help]")).toBeNull();
    }
    expect(document.querySelector("#playerGuidanceDialog")).toBeNull();
    expect(sent).toHaveLength(initialCount);
    dom.window.close();
  });

  it.each([
    ["applemusic", "AppleMusic", "enableAppleMusic", "Apple Music.png.png"],
    ["sodamusic", "SodaMusic", "enableSodaMusic", "Soda Music.png"],
    ["foobar2000", "Foobar2000", "enableFoobar2000", "Foobar2000.png"]
  ])("exposes %s as a built-in source with recognition and playback preferences", async (player, adapter, key, fileName) => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    dom.window.eval(script);
    const document = dom.window.document;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      [key]: true,
      sourceRecognitionOrder: ["QQMusic", "Netease", "Kugou", "Spotify", "Browser"],
      playerLyricOffsets: { [adapter]: 123 }
    } } });
    const cards = [...document.querySelectorAll("[data-player-card]")];
    expect(cards.map(card => card.dataset.playerCard)).toEqual(["qqmusic", "netease", "kugou", "spotify", "applemusic", "sodamusic", "foobar2000", "browser"]);
    const card = document.querySelector(`[data-player-card="${player}"]`);
    expect(card.classList.contains("enabled")).toBe(true);
    expect(card.querySelector("img").getAttribute("src")).toBe(`../../Assets/PlayerIcons/${fileName}`);
    document.querySelector(`[data-player-settings="${player}"]`).click();
    expect(document.querySelector("#playerOffsetInput").value).toBe("123");
    expect(document.querySelector("#playerCardInfoSection").hidden).toBe(true);
    expect(document.querySelector("#removePlayerSourceButton").hidden).toBe(true);
    const toggle = document.querySelector("#playerRecognitionToggle");
    expect(toggle.checked).toBe(true);
    toggle.checked = false;
    toggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1)).toEqual({ version: 1, type: "update", payload: { key, value: false } });
    document.querySelector("#playerSettingsDialog").close();
    document.querySelector(`input[name="gettingStartedPlayer"][value="${player}"]`).click();
    expect(document.querySelector("#gettingStartedAddNote").hidden).toBe(true);
    expect(document.querySelector(player === "foobar2000" ? "[data-getting-started-ready]" : "[data-getting-started-done]").textContent).toContain("显示歌词");
    dom.window.close();
  });

  it("keeps legacy custom player cards and their settings instead of duplicating built-in cards", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    dom.window.eval(script);
    const document = dom.window.document;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      customPlayerSources: [{ sourceAppUserModelId: "foobar2000", displayName: "我的 foobar", enabled: false }],
      playerLyricOffsets: { foobar2000: 456 },
      enableFoobar2000: true
    } } });
    expect(document.querySelector('[data-player-card="foobar2000"]')).toBeNull();
    const card = document.querySelector('[data-player-card="custom-foobar2000"]');
    expect(card.textContent).toContain("我的 foobar");
    expect(card.classList.contains("enabled")).toBe(false);
    document.querySelector('[data-player-settings="custom-foobar2000"]').click();
    expect(document.querySelector("#playerOffsetInput").value).toBe("456");
    expect(document.querySelector("#playerCompatibilitySection").textContent).toContain("foo_mediacontrol");
    const toggle = document.querySelector("#playerRecognitionToggle");
    toggle.checked = true;
    toggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1).payload).toEqual({ key: "customPlayerEnabled:foobar2000", value: true });
    dom.window.close();
  });

  it("reuses the same compatibility indicators in onboarding and every player drawer without changing settings", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    dom.window.eval(script);
    const document = dom.window.document;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      enableQQMusic: true, enableNetease: true, enableKugou: true, enableSpotify: true, enableBrowser: true,
      customPlayerSources: [
        { sourceAppUserModelId: "Example.Player", displayName: "Apple Music", enabled: true },
        { sourceAppUserModelId: "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", displayName: "自定义名称", enabled: true },
        { sourceAppUserModelId: "汽水音乐", displayName: "汽水音乐", enabled: true },
        { sourceAppUserModelId: "foobar2000.exe", displayName: "foobar2000", enabled: true }
      ]
    } } });
    const initialCount = sent.length;
    const guide = document.querySelector("#gettingStartedGuidance");
    const section = document.querySelector("#playerCompatibilitySection");
    const dialog = document.querySelector("#playerSettingsDialog");
    const customIds = { applemusic: "AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", sodamusic: "汽水音乐", foobar2000: "foobar2000.exe", other: "Example.Player" };
    for (const player of ["qqmusic", "netease", "kugou", "spotify", "applemusic", "sodamusic", "foobar2000", "browser", "other"]) {
      document.querySelector(`input[name="gettingStartedPlayer"][value="${player}"]`).click();
      const sourceId = customIds[player] ? `custom-${encodeURIComponent(customIds[player])}` : player;
      document.querySelector(`[data-player-settings="${sourceId}"]`).click();
      expect(section.hidden).toBe(false);
      expect(section.querySelector("dl").innerHTML).toBe(guide.querySelector("dl").innerHTML);
      expect(section.querySelector(".compatibility-summary").textContent).toBe(guide.querySelector(".compatibility-summary").textContent);
      expect([...section.querySelectorAll("li")].map(item => item.textContent)).toEqual([...guide.querySelectorAll("li")].map(item => item.textContent));
      expect(section.querySelectorAll("dl > div")).toHaveLength(3);
      dialog.close();
    }
    expect(sent).toHaveLength(initialCount);
    document.querySelector('[data-player-settings="netease"]').click();
    section.querySelector("[data-compatibility-link]").click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "openExternalLink", payload: "https://github.com/apoint123/inflink-rs" });
    dialog.close();
    document.querySelector("#addPlayerSourceButton").click();
    expect(section.hidden).toBe(true);
    expect(section.children).toHaveLength(0);
    dom.window.close();
  });

  it("refreshes single-line zoom and its upper bound in both taskbar resize directions without posting edits", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const count = sent.length;
    for (const [height, scale, maximum] of [[32, 70, 72], [48, 105, 109], [32, 70, 72]]) {
      dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: {
        settings: { sourceRecognitionOrder: [], playerLyricOffsets: {}, defaultPlayerLyricOffsets: {}, mediaHotkeys: [],
          mediaHotkeyStatuses: {}, useFloatingWindow: false, singleLineLyrics: true,
          lyricsLayoutScalePercent: scale, fontSize: 14, coverSize: 34,
          taskbarEmbeddingAvailable: true, taskbarMaxHeight: height, taskbarMaxScalePercent: maximum }, fonts: []
      } });
      const controls = [...dom.window.document.querySelectorAll('[data-setting="lyricsLayoutScalePercent"]')];
      expect(controls.every(control => Number(control.value) === scale)).toBe(true);
      expect(controls.every(control => Number(control.max) === maximum)).toBe(true);
      expect(dom.window.document.querySelector('[data-setting="singleLineLyrics"][value="true"]').checked).toBe(true);
    }
    expect(sent).toHaveLength(count);
    dom.window.close();
  });

  it("refreshes floating readouts, direct offsets and collapsed appearance without posting edits", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    const receive = settings => dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings } });
    receive({ useFloatingWindow: true, windowWidth: 420, floatingWindowWidth: 680,
      floatingLyricsLayoutScalePercent: 125, showBackground: false, showBorder: true, xOffset: 30, yOffset: -20 });
    const initialCount = sent.length;
    expect(document.querySelector("[data-floating-width]").textContent).toBe("680 px");
    expect(document.querySelector("[data-floating-scale]").textContent).toBe("125%");
    const summary = document.querySelector("[data-embedded-appearance-summary]");
    expect(summary.closest("details").open).toBe(false);
    expect(summary.textContent).toBe("背景关闭 · 边框开启");
    const xOffset = document.querySelector('[data-setting="xOffset"][type="number"]');
    const yOffset = document.querySelector('[data-setting="yOffset"][type="number"]');
    expect(xOffset.closest("details")).toBeNull();
    expect(yOffset.closest("details")).toBeNull();
    expect(xOffset.value).toBe("30");
    expect(yOffset.value).toBe("-20");
    dom.window.settingsApp.receive({ version: 1, type: "lyricsLayoutPreview", payload: {
      floatingWindowWidth: 720, floatingLyricsLayoutScalePercent: 150
    } });
    expect(document.querySelector("[data-floating-width]").textContent).toBe("720 px");
    expect(document.querySelector('[data-setting="windowWidth"][type="number"]').value).toBe("420");
    receive({ useFloatingWindow: false, showBackground: true, showBorder: false, xOffset: -100, yOffset: 0 });
    expect(summary.textContent).toBe("背景开启 · 边框关闭");
    expect(xOffset.value).toBe("-100");
    expect(yOffset.value).toBe("0");
    expect(sent).toHaveLength(initialCount);
    dom.window.close();
  });

  it("keeps embedded size readouts independent through floating resize updates and mode changes", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    const initial = {
      useFloatingWindow: true, showCover: true, lyricsLayoutScalePercent: 100,
      floatingLyricsLayoutScalePercent: 160, fontSize: 14, coverSize: 34, coverGap: 8,
      coverCornerRadius: 6, effectiveFontSize: 14, effectiveCoverSize: 34, effectiveCoverGap: 8
    };
    const assertEmbeddedPreview = () => {
      expect(document.querySelector('[data-setting="lyricsLayoutScalePercent"][type="number"]').value).toBe("100");
      expect(document.querySelector("[data-effective-font-size]").textContent).toBe("14 px");
      expect(document.querySelector("[data-effective-cover-size]").textContent).toBe("34 px");
      expect(document.querySelector("[data-effective-cover-gap]").textContent).toBe("8 px");
      expect(document.querySelector("#layoutScalePreview").hidden).toBe(false);
    };
    const receiveState = settings => dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings } });
    receiveState(initial);
    const before = sent.length;
    for (const floatingScale of [210, 250]) {
      receiveState({ ...initial, floatingLyricsLayoutScalePercent: floatingScale });
      assertEmbeddedPreview();
      expect(document.querySelector("[data-floating-scale]").textContent).toBe(`${floatingScale}%`);
      dom.window.settingsApp.receive({ version: 1, type: "lyricsLayoutPreview", payload: {
        scalePercent: 100, floatingLyricsLayoutScalePercent: floatingScale,
        effectiveFontSize: 14, effectiveCoverSize: 34, effectiveCoverGap: 8
      } });
      assertEmbeddedPreview();
    }
    receiveState({ ...initial, useFloatingWindow: false, floatingLyricsLayoutScalePercent: 250 });
    assertEmbeddedPreview();
    expect(sent).toHaveLength(before);
    dom.window.close();
  });

  it.each(["embedded", "floating"])("resets only the requested %s profile without selecting its mode", async mode => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      useFloatingWindow: mode === "embedded", lyricsLayoutScalePercent: 80,
      floatingLyricsLayoutScalePercent: 160, showCover: true, enableControlPanel: false,
      enableFloatingControlPanel: true
    } } });
    const document = dom.window.document;
    const selected = document.querySelector("[data-window-mode]:checked").value;
    const button = document.querySelector(`[data-reset-window-mode="${mode}"]`);
    expect(button.closest("label")).toBeNull();
    expect(button.textContent).toBe("恢复此模式默认");
    expect(button.hasAttribute("title")).toBe(false);
    expect(button.getAttribute("aria-label")).toBe(`恢复此模式默认：${mode === "embedded" ? "任务栏嵌入" : "悬浮窗口"}`);
    expect(button.querySelector("svg").getAttribute("aria-hidden")).toBe("true");
    button.querySelector("svg path").dispatchEvent(new dom.window.MouseEvent("click", { bubbles: true }));
    expect(sent.at(-1)).toEqual({ version: 1, type: "resetWindowPresentation", payload: mode });
    expect(document.querySelector("[data-window-mode]:checked").value).toBe(selected);
    expect(document.querySelector("[data-floating-scale]").textContent).toBe("160%");
    expect(document.querySelector("#embeddedControlPanelToggle").checked).toBe(false);
    expect(document.querySelector("#floatingControlPanelToggle").checked).toBe(true);
    dom.window.close();
  });

  it.each([false, true])("groups mode-specific controls and preserves shared controls for floating=%s", async floating => {
    const { dom, script } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      useFloatingWindow: floating, showBackground: true, showCover: true,
      enableControlPanel: true, enableFloatingControlPanel: false
    } } });
    const document = dom.window.document;
    for (const key of ["windowWidth", "lyricsLayoutScalePercent", "xOffset", "yOffset", "showBackground", "backgroundOpacity", "showBorder"]) {
      const control = document.querySelector(`[data-setting="${key}"]`);
      expect(control.closest(".embedded-window-mode-settings")).not.toBeNull();
      expect(control.disabled).toBe(floating);
    }
    for (const key of ["embeddedPanelBlurAmount", "embeddedPanelBrightness", "embeddedPanelTintOpacity"]) {
      const control = document.querySelector(`[data-setting="${key}"]`);
      expect(control.closest(".embedded-window-mode-settings")).not.toBeNull();
      expect(control.disabled).toBe(floating);
    }
    for (const key of ["floatingPanelBlurAmount", "floatingPanelBrightness", "floatingPanelTintOpacity"]) {
      const control = document.querySelector(`[data-setting="${key}"]`);
      expect(control.closest(".floating-window-mode-settings")).not.toBeNull();
      expect(control.disabled).toBe(!floating);
    }
    for (const key of ["fontFamily", "lyricsTextAlignment"]) {
      expect(document.querySelector(`[data-setting="${key}"]`).closest(".window-mode-card")).toBeNull();
    }
    expect(document.querySelector("#displayMonitorList")).not.toBeNull();
    dom.window.close();
  });

  it.each([false, true])("keeps material sliders expanded inside the control panel group for floating=%s", async floating => {
    const { dom, script } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      useFloatingWindow: floating, enableControlPanel: false, enableFloatingControlPanel: false
    } } });
    const document = dom.window.document;
    const group = document.querySelector('[data-display-group="controls"]');
    expect(group.getAttribute("aria-label")).toBe("播放控制面板");
    for (const prefix of ["embedded", "floating"]) {
      for (const suffix of ["BlurAmount", "Brightness", "TintOpacity"]) {
        const slider = document.querySelector(`[data-setting="${prefix}Panel${suffix}"][type="range"]`);
        expect(slider.closest("details")).toBeNull();
        expect(slider.closest('[data-display-group="controls"]')).toBe(group);
        expect(slider.closest("[data-presentation-mode]").hidden).toBe((prefix === "floating") !== floating);
        // Embedded material requires its panel; floating material also belongs to the lyric card.
        expect(slider.disabled).toBe(prefix === "embedded" || !floating);
      }
    }
    expect(document.querySelector('[data-material-summary]')).toBeNull();
    expect(document.querySelector('[data-embedded-appearance-summary]').closest("details")).not.toBeNull();
    dom.window.close();
  });

  it.each([undefined, NaN])("uses profile material defaults for absent or invalid fields: %s", async value => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const defaults = {
      embeddedPanelBlurAmount: 24, embeddedPanelBrightness: 20, embeddedPanelTintOpacity: 65,
      floatingPanelBlurAmount: 32, floatingPanelBrightness: 25, floatingPanelTintOpacity: 55
    };
    const initialCount = sent.length;
    const settings = Object.fromEntries(Object.keys(defaults).map(key => [key, value]));
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings } });
    for (const [key, expected] of Object.entries(defaults)) {
      expect(dom.window.document.querySelector(`input[type="number"][data-setting="${key}"]`).value).toBe(String(expected));
    }
    const saved = { embeddedPanelBrightness: 0.45, embeddedPanelTintOpacity: 0.55, embeddedPanelBlurAmount: 24,
      floatingPanelBrightness: 0.45, floatingPanelTintOpacity: 0.55, floatingPanelBlurAmount: 24 };
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: saved } });
    for (const [key, savedValue] of Object.entries(saved)) {
      const expected = key.endsWith("BlurAmount") ? savedValue : Math.round(savedValue * 100);
      expect(dom.window.document.querySelector(`input[type="number"][data-setting="${key}"]`).value).toBe(String(expected));
    }
    expect(sent).toHaveLength(initialCount);
    dom.window.close();
  });

  it.each(["floatingPanelBrightness", "floatingPanelTintOpacity"])("previews and commits bounded floating appearance %s", async key => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      useFloatingWindow: true, enableControlPanel: true, [key]: 0.65
    } } });
    const slider = dom.window.document.querySelector(`input[type="range"][data-setting="${key}"]`);
    const number = dom.window.document.querySelector(`input[type="number"][data-setting="${key}"]`);
    expect(slider.disabled).toBe(false);
    expect(number.value).toBe("65");
    slider.value = "80";
    slider.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    expect(sent.at(-1)).toEqual({ version: 1, type: "previewUpdate", payload: { key, value: 0.8 } });
    expect(number.value).toBe("80");
    slider.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1).payload).toEqual({ key, value: 0.8 });
    number.value = "150";
    number.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(number.value).toBe("100");
    expect(sent.at(-1).payload).toEqual({ key, value: 1 });
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      useFloatingWindow: false, enableControlPanel: true, [key]: 0.3
    } } });
    expect(slider.disabled).toBe(true);
    expect(number.value).toBe("30");
    dom.window.close();
  });

  it.each([
    ["embeddedPanelBrightness", 65, 80, 0.8, 150, 100, 1],
    ["embeddedPanelTintOpacity", 65, 80, 0.8, 150, 100, 1],
    ["embeddedPanelBlurAmount", 24, 40, 40, 100, 64, 64]
  ])("previews and commits taskbar material independently: %s", async (key, initial, edited, value, overflow, normalized, bounded) => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const receive = settings => dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings } });
    receive({ useFloatingWindow: false, enableControlPanel: true });
    const slider = dom.window.document.querySelector(`input[type="range"][data-setting="${key}"]`);
    const number = dom.window.document.querySelector(`input[type="number"][data-setting="${key}"]`);
    expect(number.value).toBe(String(key === "embeddedPanelTintOpacity" ? 65 : key === "embeddedPanelBrightness" ? 20 : initial));
    expect(slider.disabled).toBe(false);
    slider.value = String(edited);
    slider.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    expect(number.value).toBe(String(edited));
    expect(sent.at(-1)).toEqual({ version: 1, type: "previewUpdate", payload: { key, value } });
    number.value = String(overflow);
    number.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(number.value).toBe(String(normalized));
    expect(sent.at(-1)).toEqual({ version: 1, type: "update", payload: { key, value: bounded } });
    expect(sent.some(message => message.payload?.key?.startsWith("floatingPanel"))).toBe(false);
    receive({ useFloatingWindow: true, enableControlPanel: true, [key]: value });
    expect(slider.disabled).toBe(true);
    expect(number.disabled).toBe(true);
    receive({ useFloatingWindow: false, enableControlPanel: false, [key]: value });
    expect(slider.disabled).toBe(true);
    dom.window.close();
  });
  it("keeps the floating hover switch independent without requiring a cover", async () => {
    const { dom, script, sent } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      useFloatingWindow: true, showCover: false, enableControlPanel: true
    } } });
    const toggle = dom.window.document.querySelector("#floatingControlPanelToggle");
    expect(toggle.disabled).toBe(false);
    expect(toggle.checked).toBe(true);
    expect(dom.window.document.querySelector("#embeddedControlPanelToggle").disabled).toBe(true);
    toggle.click();
    expect(sent.at(-1).payload).toEqual({ key: "enableFloatingControlPanel", value: false });
    expect(dom.window.document.querySelector("#embeddedControlPanelToggle").checked).toBe(true);
    dom.window.close();
  });
  it("keeps saved player card order and gear settings separate", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: {
        sourceRecognitionOrder: ["Spotify", "QQMusic", "Netease", "Kugou"],
        enableSpotify: true,
        enableQQMusic: true,
        playerLyricOffsets: {},
        defaultPlayerLyricOffsets: {},
        playerLyricProviders: {},
        mediaHotkeys: [],
        mediaHotkeyStatuses: {}
      }, fonts: [] }
    });

    expect(document.querySelector("#priorityList")).toBeNull();
    expect(document.querySelector('[data-page="sources"]').textContent).not.toContain("识别优先级");
    expect([...document.querySelectorAll("[data-player-settings]")].map(item => item.dataset.playerSettings))
      .toEqual(["spotify", "qqmusic", "netease", "kugou", "applemusic", "sodamusic", "foobar2000", "browser"]);

    document.querySelector('[data-player-settings="browser"]').click();
    const drawerBody = document.querySelector("#playerDrawerBody");
    expect([...drawerBody.children].filter(element => element.classList.contains("player-setting-section")).map(element => element.id))
      .toEqual(["playerCompatibilitySection", "playerDiscoverySection", "playerRecognitionSection", "playerCardInfoSection", "playerAdvancedSettings", "playerTrustSection"]);
    expect(document.querySelector("#playerAdvancedSettings").parentElement).toBe(drawerBody);
    expect(document.querySelector("#playerRemoveActions").parentElement).toBe(drawerBody);
    expect(document.querySelector("#playerAdvancedSettings").hidden).toBe(false);
    expect(document.querySelector("#playerRemoveActions").hidden).toBe(true);
    expect(document.querySelector("#playerRecognitionToggle").checked).toBe(false);
    expect(document.querySelector("#removePlayerSourceButton").hidden).toBe(true);
    document.querySelector("#playerRecognitionToggle").checked = true;
    document.querySelector("#playerRecognitionToggle").dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1).payload).toEqual({ key: "enableBrowser", value: true });

    document.querySelector('[data-player-settings="spotify"]').click();
    expect(document.querySelector("#removePlayerSourceButton").hidden).toBe(true);
    const toggle = document.querySelector("#playerRecognitionToggle");
    toggle.checked = false;
    toggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1).payload).toEqual({ key: "enableSpotify", value: false });
    expect(sent.some(message => message.type === "reorderSources")).toBe(false);
  });

  it("previews player card order, saves on drop, and restores canceled drags", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: {
        sourceRecognitionOrder: ["QQMusic", "Netease", "Kugou", "Spotify"],
        playerLyricOffsets: {},
        defaultPlayerLyricOffsets: {},
        playerLyricProviders: {},
        mediaHotkeys: [],
        mediaHotkeyStatuses: {}
      }, fonts: [] }
    });
    const grid = document.querySelector("#sourceGrid");
    const order = () => [...grid.children].map(card => card.dataset.playerCard);
    const drag = (target, type) => {
      const event = new dom.window.Event(type, { bubbles: true, cancelable: true });
      Object.defineProperty(event, "dataTransfer", { value: { effectAllowed: "", dropEffect: "", setData() {} } });
      target.dispatchEvent(event);
    };

    const initialMessages = sent.length;
    drag(document.querySelector('[data-player-drag-id="qqmusic"]'), "dragstart");
    drag(document.querySelector('[data-player-card="netease"]'), "dragover");
    expect(order()).toEqual(["netease", "qqmusic", "kugou", "spotify", "applemusic", "sodamusic", "foobar2000", "browser"]);
    expect(sent).toHaveLength(initialMessages);
    drag(grid, "drop");
    expect(sent.at(-1).type).toBe("reorderSources");
    expect(sent.at(-1).payload).toEqual(["Netease", "QQMusic", "Kugou", "Spotify", "AppleMusic", "SodaMusic", "Foobar2000", "Browser"]);
    expect(document.querySelector("#sourceOrderAnnouncement").textContent).toContain("第 2 位");

    const committedMessages = sent.length;
    const spotifyHandle = document.querySelector('[data-player-drag-id="spotify"]');
    drag(spotifyHandle, "dragstart");
    drag(document.querySelector('[data-player-card="netease"]'), "dragover");
    expect(order()).toEqual(["spotify", "netease", "qqmusic", "kugou", "applemusic", "sodamusic", "foobar2000", "browser"]);
    drag(spotifyHandle, "dragend");
    expect(order()).toEqual(["netease", "qqmusic", "kugou", "spotify", "applemusic", "sodamusic", "foobar2000", "browser"]);
    expect(sent).toHaveLength(committedMessages);

    document.querySelector('[data-player-drag-id="qqmusic"]')
      .dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "ArrowUp", altKey: true, bubbles: true }));
    expect(order()).toEqual(["qqmusic", "netease", "kugou", "spotify", "applemusic", "sodamusic", "foobar2000", "browser"]);
    expect(sent.at(-1).payload).toEqual(["QQMusic", "Netease", "Kugou", "Spotify", "AppleMusic", "SodaMusic", "Foobar2000", "Browser"]);
    expect(document.activeElement.dataset.playerDragId).toBe("qqmusic");
  });

  it("refreshes media sessions without clearing the drawer content while waiting", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      sourceRecognitionOrder: ["QQMusic", "Netease", "Kugou", "Spotify", "Browser"],
      playerLyricOffsets: {}, defaultPlayerLyricOffsets: {}, playerLyricProviders: {},
      mediaHotkeys: [], mediaHotkeyStatuses: {}
    }, fonts: [] } });
    document.querySelector("#addPlayerSourceButton").click();
    expect(document.querySelector("#playerDiscoveryStatus").textContent).toBe("正在检测媒体会话…");
    dom.window.settingsApp.receive({ version: 1, type: "playerSessions", payload: { status: "ready", sessions: [
      { sourceAppUserModelId: "Example.Player!Music", displayName: "Example Player", isPlaying: true, isBuiltIn: false }
    ] } });
    const list = document.querySelector("#availablePlayerSessions");
    const originalItem = list.firstElementChild;
    document.querySelector("#refreshPlayerSessionsButton").click();
    expect(sent.at(-1).type).toBe("discoverPlayerSessions");
    expect(document.querySelector("#playerSettingsDialog").open).toBe(true);
    expect(list.firstElementChild).toBe(originalItem);
    expect(list.getAttribute("aria-busy")).toBe("true");
    expect(document.querySelector("#playerDiscoveryStatus").textContent).toBe("");
    dom.window.settingsApp.receive({ version: 1, type: "playerSessions", payload: { status: "ready", sessions: [] } });
    const emptyState = document.querySelector("#playerDiscoveryStatus").firstElementChild;
    expect(list.hasAttribute("aria-busy")).toBe(false);
    document.querySelector("#refreshPlayerSessionsButton").click();
    expect(document.querySelector("#playerDiscoveryStatus").firstElementChild).toBe(emptyState);
    expect(document.querySelector("#playerSettingsDialog").open).toBe(true);
  });

  it("adds a detected media session and configures it as a player", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    const settings = {
      sourceRecognitionOrder: ["QQMusic", "Netease", "Kugou", "Spotify"],
      playerLyricOffsets: {}, defaultPlayerLyricOffsets: {}, playerLyricProviders: {},
      mediaHotkeys: [], mediaHotkeyStatuses: {}
    };
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings, fonts: [] } });
    document.querySelector("#addPlayerSourceButton").click();
    expect(document.querySelector("#playerSettingsDialog").open).toBe(true);
    expect(document.querySelector("#playerDiscoverySection").hidden).toBe(false);
    expect(document.querySelector("#playerRecognitionSection").hidden).toBe(false);
    expect(document.querySelector("#playerRecognitionSection .setting-label small").textContent)
      .toBe("关闭后不再监听此播放器，已有偏移设置会保留。");
    expect(document.querySelector("#playerTrustSection").hidden).toBe(false);
    expect(document.querySelector("#playerAdvancedSettings").hidden).toBe(true);
    expect(document.querySelector("#playerRemoveActions").hidden).toBe(true);
    expect(document.querySelector("#confirmAddPlayerSource").hidden).toBe(false);
    expect(document.querySelector("#savePlayerInfoButton").hidden).toBe(true);
    const style = document.createElement("style");
    style.textContent = await read("TaskbarLyrics.App/Web/Settings/settings.css");
    document.head.appendChild(style);
    expect(dom.window.getComputedStyle(document.querySelector("#savePlayerInfoButton")).display).toBe("none");
    expect(dom.window.getComputedStyle(document.querySelector("#playerRemoveActions")).display).toBe("none");
    expect(document.querySelector("#chooseCustomPlayerIconButton").disabled).toBe(true);
    expect(document.querySelector("#customPlayerName").disabled).toBe(true);
    expect(document.querySelector("#customPlayerIconPreview .default-player-icon").getAttribute("src"))
      .toBe("../../Assets/PlayerIcons/Presets/music-player.svg");
    expect(sent.at(-1).type).toBe("discoverPlayerSessions");
    const recognition = document.querySelector("#playerRecognitionToggle");
    recognition.checked = false;
    recognition.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    const addHandle = document.querySelector('[data-lyric-provider-drag="Kugou"]');
    addHandle.focus();
    addHandle.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "ArrowUp", altKey: true, bubbles: true }));
    expect(document.activeElement.dataset.lyricProviderDrag).toBe("Kugou");
    expect(document.querySelector("#lyricProviderOrderAnnouncement").textContent).toBe("酷狗音乐 已移至第 1 位，共 4 位");
    const neteaseToggle = document.querySelector('[data-lyric-provider-toggle="Netease"]');
    neteaseToggle.focus();
    neteaseToggle.checked = false;
    neteaseToggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(document.activeElement.dataset.lyricProviderToggle).toBe("Netease");
    expect(sent.at(-1).type).toBe("discoverPlayerSessions");
    dom.window.settingsApp.receive({ version: 1, type: "playerSessions", payload: { status: "ready", sessions: [] } });
    expect(document.querySelector("#playerDiscoveryStatus").classList.contains("empty")).toBe(true);
    expect(document.querySelector("#playerDiscoveryStatus strong").textContent).toBe("暂未检测到可添加的播放器");
    expect(document.querySelector("#playerDiscoveryStatus .default-player-icon")).not.toBeNull();
    dom.window.settingsApp.receive({ version: 1, type: "playerSessions", payload: { status: "ready", sessions: [
      { sourceAppUserModelId: "Example.Player!Music", displayName: "Example.Player!Music", trackTitle: "Song", isPlaying: true, isBuiltIn: false },
      { sourceAppUserModelId: "chrome.exe", displayName: "Chrome", trackTitle: "Browser song", isPlaying: true, isBuiltIn: false },
      { sourceAppUserModelId: "Spotify.exe", displayName: "Spotify", trackTitle: "", isPlaying: false, isBuiltIn: true }
    ] } });
    expect(document.querySelectorAll("[data-player-session-index]")).toHaveLength(2);
    expect(document.querySelectorAll('[data-player-session-index="0"] small')).toHaveLength(1);
    document.querySelector('[data-player-session-index="0"]').click();
    expect(document.querySelector("#chooseCustomPlayerIconButton").disabled).toBe(false);
    expect(document.querySelector("#customPlayerName").disabled).toBe(false);
    document.querySelector("#customPlayerName").value = "我的播放器";
    expect(document.querySelector("#confirmAddPlayerSource").disabled).toBe(false);
    const iconInput = document.querySelector("#customPlayerIcon");
    let pickerClicks = 0;
    iconInput.addEventListener("click", () => { pickerClicks++; });
    document.querySelector("#chooseCustomPlayerIconButton").click();
    expect(pickerClicks).toBe(1);
    iconInput.dispatchEvent(new dom.window.Event("cancel", { bubbles: true }));
    expect(document.querySelector("#playerSettingsDialog").open).toBe(true);
    expect(document.querySelector("#playerSettingsDialog").classList.contains("closing")).toBe(false);
    const iconFile = new dom.window.File([Uint8Array.from([137, 80, 78, 71, 13, 10, 26, 10])], "icon.png", { type: "image/png" });
    Object.defineProperty(iconInput, "files", { configurable: true, value: [iconFile] });
    iconInput.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(document.querySelector("#confirmAddPlayerSource").disabled).toBe(true);
    await new Promise(resolve => dom.window.setTimeout(resolve, 10));
    expect(document.querySelector("#customPlayerIconPreview img").getAttribute("src"))
      .toBe("data:image/png;base64,iVBORw0KGgo=");
    expect(document.querySelector("#clearCustomPlayerIconButton").hidden).toBe(false);
    document.querySelector("#clearCustomPlayerIconButton").click();
    expect(document.querySelector("#customPlayerIconPreview .default-player-icon")).not.toBeNull();
    expect(document.querySelector("#clearCustomPlayerIconButton").hidden).toBe(true);
    iconInput.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    await new Promise(resolve => dom.window.setTimeout(resolve, 10));
    document.querySelector("#confirmAddPlayerSource").click();
    expect(sent.at(-1).type).toBe("addPlayerSource");
    expect(sent.at(-1).payload).toEqual({ sourceAppUserModelId: "Example.Player!Music", displayName: "我的播放器", iconDataUrl: "data:image/png;base64,iVBORw0KGgo=", presetIconId: "", presetIconColor: "", enabled: false,
      lyricProviders: [
        { providerId: "Kugou", enabled: true }, { providerId: "QQMusic", enabled: true },
        { providerId: "Netease", enabled: false }, { providerId: "LRCLIB", enabled: true }
      ] });

    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      ...settings,
      sourceRecognitionOrder: [...settings.sourceRecognitionOrder, "Example.Player!Music", "Other.Player!Music"],
      customPlayerSources: [
        { sourceAppUserModelId: "Example.Player!Music", displayName: "我的播放器", enabled: false, iconDataUrl: "data:image/png;base64,iVBORw0KGgo=" },
        { sourceAppUserModelId: "Other.Player!Music", displayName: "其他播放器", enabled: true }
      ],
      playerLyricOffsets: { "Example.Player!Music": 0, "Other.Player!Music": 0 },
      playerLyricProviders: { "Example.Player!Music": [
        { providerId: "Kugou", enabled: true }, { providerId: "QQMusic", enabled: true },
        { providerId: "Netease", enabled: false }, { providerId: "LRCLIB", enabled: true }
      ] }
    }, fonts: [] } });
    expect(document.querySelector("#playerSettingsDialog").classList.contains("closing")).toBe(true);
    document.querySelector("#playerSettingsDialog").dispatchEvent(new dom.window.Event("animationend"));
    expect(document.querySelector("#playerSettingsDialog").open).toBe(false);
    expect(document.querySelector('[data-player-card="custom-Example.Player!Music"] .source-info strong').textContent).toBe("我的播放器");
    expect(document.querySelector('[data-player-card="custom-Example.Player!Music"] .source-logo img').getAttribute("src"))
      .toBe("data:image/png;base64,iVBORw0KGgo=");
    document.querySelector('[data-player-settings="custom-Example.Player!Music"]').click();
    expect(document.querySelector("#playerAdvancedSettings").hidden).toBe(false);
    expect(document.querySelector("#playerRemoveActions").hidden).toBe(false);
    expect(document.querySelector("#savePlayerInfoButton").hidden).toBe(false);
    expect(document.querySelector("#savePlayerInfoButton").textContent).toBe("保存播放器信息");
    expect(document.querySelector("#playerRecognitionToggle").checked).toBe(false);
    expect([...document.querySelectorAll("[data-lyric-provider-item]")].map(item => item.dataset.lyricProviderItem))
      .toEqual(["Kugou", "QQMusic", "Netease", "LRCLIB"]);
    expect(document.querySelector("#customPlayerName").value).toBe("我的播放器");
    expect(document.querySelector("#customPlayerIconPreview img")).not.toBeNull();
    expect(document.querySelector("#removePlayerSourceButton").hidden).toBe(false);
    document.querySelector("#customPlayerName").value = "改名后的播放器";
    document.querySelector("#customPlayerName").dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    document.querySelector("#clearCustomPlayerIconButton").click();
    document.querySelector("#savePlayerInfoButton").click();
    expect(sent.at(-1)).toMatchObject({ type: "updatePlayerSource", payload: {
      sourceAppUserModelId: "Example.Player!Music", displayName: "改名后的播放器",
      iconDataUrl: "", presetIconId: "", presetIconColor: ""
    } });
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      ...settings,
      customPlayerSources: [{ sourceAppUserModelId: "Example.Player!Music", displayName: "改名后的播放器", enabled: true }]
    }, fonts: [] } });
    expect(document.querySelector("#playerSettingsTitle").textContent).toBe("改名后的播放器");
    expect(document.querySelector('[data-player-card="custom-Example.Player!Music"] .source-info strong').textContent).toBe("改名后的播放器");
    const toggle = document.querySelector("#playerRecognitionToggle");
    toggle.checked = false;
    toggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1).payload).toEqual({ key: "customPlayerEnabled:Example.Player!Music", value: false });
    document.querySelector("#removePlayerSourceButton").click();
    document.querySelector("#confirmRemovePlayerSource").click();
    expect(sent.at(-1).type).toBe("removePlayerSource");
    expect(sent.at(-1).payload).toBe("Example.Player!Music");
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      ...settings,
      sourceRecognitionOrder: [...settings.sourceRecognitionOrder, "Other.Player!Music"],
      customPlayerSources: [{ sourceAppUserModelId: "Other.Player!Music", displayName: "其他播放器", enabled: true }]
    }, fonts: [] } });
    expect(document.querySelector("#playerSettingsDialog").open).toBe(false);
    expect(document.querySelector('[data-player-card="custom-Other.Player!Music"]')).not.toBeNull();
  });

  it("uses the default icon for new players and preserves an existing preset icon", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      sourceRecognitionOrder: ["QQMusic", "Netease", "Kugou", "Spotify", "Browser"],
      playerLyricOffsets: {}, defaultPlayerLyricOffsets: {}, playerLyricProviders: {},
      mediaHotkeys: [], mediaHotkeyStatuses: {}
    }, fonts: [] } });
    document.querySelector("#addPlayerSourceButton").click();
    dom.window.settingsApp.receive({ version: 1, type: "playerSessions", payload: { status: "ready", sessions: [
      { sourceAppUserModelId: "Example.Player!Music", displayName: "Example Player", isPlaying: true, isBuiltIn: false }
    ] } });
    document.querySelector('[data-player-session-index="0"]').click();
    expect(document.querySelector("#customPlayerIconPreview .default-player-icon")).not.toBeNull();
    document.querySelector("#confirmAddPlayerSource").click();
    expect(sent.at(-1).payload).toEqual({
      sourceAppUserModelId: "Example.Player!Music", displayName: "Example Player", iconDataUrl: "",
      presetIconId: "", presetIconColor: "", enabled: true,
      lyricProviders: [
        { providerId: "QQMusic", enabled: true }, { providerId: "Kugou", enabled: true },
        { providerId: "Netease", enabled: true }, { providerId: "LRCLIB", enabled: true }
      ]
    });
    dom.window.settingsApp.receive({ version: 1, type: "playerSourceActionResult", payload: { message: "添加失败" } });
    expect(document.querySelector("#confirmAddPlayerSource").disabled).toBe(false);
    expect(document.querySelector("#playerDiscoveryStatus").textContent).toBe("添加失败");
    document.querySelector("#playerSettingsDialog").close();
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      sourceRecognitionOrder: ["QQMusic", "Netease", "Kugou", "Spotify", "Browser", "Example.Player!Music"],
      customPlayerSources: [{ sourceAppUserModelId: "Example.Player!Music", displayName: "Example Player", enabled: true,
        presetIconId: "disc", presetIconColor: "#123ABC" }],
      playerLyricOffsets: {}, defaultPlayerLyricOffsets: {}, playerLyricProviders: {},
      mediaHotkeys: [], mediaHotkeyStatuses: {}
    }, fonts: [] } });
    document.querySelector('[data-player-settings="custom-Example.Player!Music"]').click();
    expect(document.querySelector("#customPlayerIconPreview .preset-icon-disc").style.color).toBe("rgb(18, 58, 188)");
    document.querySelector("#customPlayerName").value = "改名";
    document.querySelector("#customPlayerName").dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    document.querySelector("#savePlayerInfoButton").click();
    expect(sent.at(-1).payload).toMatchObject({ displayName: "改名", iconDataUrl: "", presetIconId: "disc", presetIconColor: "#123ABC" });
  });

  it("uses a discovered Windows app icon until the user uploads a replacement", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      sourceRecognitionOrder: ["QQMusic", "Netease", "Kugou", "Spotify", "Browser"],
      playerLyricOffsets: {}, defaultPlayerLyricOffsets: {}, playerLyricProviders: {},
      mediaHotkeys: [], mediaHotkeyStatuses: {}
    }, fonts: [] } });
    document.querySelector("#addPlayerSourceButton").click();
    const iconDataUrl = "data:image/png;base64,iVBORw0KGgo=";
    dom.window.settingsApp.receive({ version: 1, type: "playerSessions", payload: { status: "ready", sessions: [
      { sourceAppUserModelId: "Example.Player!Music", displayName: "Example Player", isPlaying: true,
        isBuiltIn: false, iconDataUrl }
    ] } });
    expect(document.querySelector(".available-player-session-icon img").getAttribute("src")).toBe(iconDataUrl);
    document.querySelector('[data-player-session-index="0"]').click();
    expect(document.querySelector("#customPlayerIconPreview img").getAttribute("src")).toBe(iconDataUrl);
    expect(document.querySelector("#clearCustomPlayerIconButton").hidden).toBe(true);
    document.querySelector("#confirmAddPlayerSource").click();
    expect(sent.at(-1).payload).toMatchObject({ iconDataUrl, presetIconId: "", presetIconColor: "" });
  });

  it.each([
    ["qqmusic", "QQMusic", ["QQMusic", "Kugou", "Netease", "LRCLIB"]],
    ["netease", "Netease", ["Netease", "QQMusic", "Kugou", "LRCLIB"]],
    ["kugou", "Kugou", ["Kugou", "QQMusic", "Netease", "LRCLIB"]],
    ["spotify", "Spotify", ["QQMusic", "Kugou", "Netease", "LRCLIB"]]
  ])("uses player defaults without overwriting stored lyric preferences: %s", async (id, adapter, expected) => {
    const { dom, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    const receive = providers => dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: {
        sourceRecognitionOrder: [adapter],
        playerLyricOffsets: {}, defaultPlayerLyricOffsets: {},
        playerLyricProviders: providers === undefined ? {} : { [adapter]: providers },
        mediaHotkeys: [], mediaHotkeyStatuses: {}
      }, fonts: [] }
    });
    for (const missing of [undefined, null, []]) {
      receive(missing);
      document.querySelector(`[data-player-settings="${id}"]`).click();
      expect([...document.querySelectorAll('[data-lyric-provider-item]')].map(item => item.dataset.lyricProviderItem))
        .toEqual(expected);
    }

    const stored = ["LRCLIB", "QQMusic", "Kugou", "Netease"]
      .map(providerId => ({ providerId, enabled: providerId !== adapter }));
    receive(stored);
    expect([...document.querySelectorAll('[data-lyric-provider-item]')].map(item => item.dataset.lyricProviderItem))
      .toEqual(stored.map(item => item.providerId));
    for (const provider of stored) {
      expect(document.querySelector(`[data-lyric-provider-toggle="${provider.providerId}"]`).checked)
        .toBe(provider.enabled);
    }
  });

  it("edits online lyric trust order and switches per player", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: {
        sourceRecognitionOrder: ["QQMusic", "Spotify"],
        playerLyricOffsets: {},
        defaultPlayerLyricOffsets: {},
        playerLyricProviders: {},
        mediaHotkeys: [],
        mediaHotkeyStatuses: {}
      }, fonts: [] }
    });

    document.querySelector('[data-player-settings="qqmusic"]').click();
    expect([...document.querySelectorAll('[data-lyric-provider-item]')].map(item => item.dataset.lyricProviderItem))
      .toEqual(["QQMusic", "Kugou", "Netease", "LRCLIB"]);
    const handle = document.querySelector('[data-lyric-provider-drag="Kugou"]');
    handle.focus();
    handle.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "ArrowUp", altKey: true, bubbles: true }));
    expect(document.activeElement.dataset.lyricProviderDrag).toBe("Kugou");
    expect(document.querySelector("#lyricProviderOrderAnnouncement").textContent).toBe("酷狗音乐 已移至第 1 位，共 4 位");
    expect(sent.at(-1).payload).toEqual({
      key: "playerLyricProviders:QQMusic",
      value: [
        { providerId: "Kugou", enabled: true },
        { providerId: "QQMusic", enabled: true },
        { providerId: "Netease", enabled: true },
        { providerId: "LRCLIB", enabled: true }
      ]
    });

    const toggle = document.querySelector('[data-lyric-provider-toggle="QQMusic"]');
    toggle.focus();
    toggle.checked = false;
    toggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(document.activeElement.dataset.lyricProviderToggle).toBe("QQMusic");
    expect(sent.at(-1).payload.value[1].enabled).toBe(false);

    document.querySelector('[data-player-settings="spotify"]').click();
    expect([...document.querySelectorAll('[data-lyric-provider-item]')].map(item => item.dataset.lyricProviderItem))
      .toEqual(["QQMusic", "Kugou", "Netease", "LRCLIB"]);
    expect(document.querySelector('[data-lyric-provider-toggle="QQMusic"]').checked).toBe(true);
  });

  it("previews lyric source positions while dragging and restores canceled drags", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: {
        sourceRecognitionOrder: ["QQMusic"],
        playerLyricOffsets: {},
        defaultPlayerLyricOffsets: {},
        playerLyricProviders: {},
        mediaHotkeys: [],
        mediaHotkeyStatuses: {}
      }, fonts: [] }
    });
    document.querySelector('[data-player-settings="qqmusic"]').click();
    const list = document.querySelector("#lyricProviderList");
    const order = () => [...list.children].map(item => item.dataset.lyricProviderItem);
    const drag = (target, type, clientY = 0) => {
      const event = new dom.window.Event(type, { bubbles: true, cancelable: true });
      Object.defineProperties(event, {
        clientY: { value: clientY },
        dataTransfer: { value: { effectAllowed: "", dropEffect: "", setData() {} } }
      });
      target.dispatchEvent(event);
    };

    const initialMessages = sent.length;
    drag(document.querySelector('[data-lyric-provider-drag="Kugou"]'), "dragstart");
    drag(document.querySelector('[data-lyric-provider-item="Netease"]'), "dragover", 1);
    expect(order()).toEqual(["QQMusic", "Netease", "Kugou", "LRCLIB"]);
    expect([...list.querySelectorAll(".priority-number")].map(item => item.textContent)).toEqual(["1", "2", "3", "4"]);
    expect(sent).toHaveLength(initialMessages);
    drag(list, "drop");
    expect(sent.at(-1).payload.value.map(item => item.providerId)).toEqual(order());
    expect(document.querySelector("#lyricProviderOrderAnnouncement").textContent).toBe("酷狗音乐 已移至第 3 位，共 4 位");

    const committedMessages = sent.length;
    const qqHandle = document.querySelector('[data-lyric-provider-drag="QQMusic"]');
    drag(qqHandle, "dragstart");
    drag(document.querySelector('[data-lyric-provider-item="LRCLIB"]'), "dragover", 1);
    expect(order()).toEqual(["Netease", "Kugou", "LRCLIB", "QQMusic"]);
    drag(qqHandle, "dragend");
    expect(order()).toEqual(["QQMusic", "Netease", "Kugou", "LRCLIB"]);
    expect(sent).toHaveLength(committedMessages);
  });

  it("keeps the public pages and setting fields in the document", async () => {
    const html = await read("TaskbarLyrics.App/Web/Settings/settings.html");
    const document = new JSDOM(html).window.document;
    const navigationPages = [...document.querySelectorAll("[data-nav]")].map(item => item.dataset.nav);
    const contentPages = [...document.querySelectorAll("[data-page]")].map(item => item.dataset.page);

    expect([...navigationPages].sort()).toEqual([...settingsPages].sort());
    expect([...contentPages].sort()).toEqual([...settingsPages].sort());
    for (const page of settingsPages) {
      expect(document.querySelector(`[data-nav="${page}"]`)).not.toBeNull();
      expect(document.querySelector(`[data-page="${page}"]`)).not.toBeNull();
    }

    const settingKeys = new Set([
      ...[...document.querySelectorAll("[data-setting]")].map(control => control.dataset.setting),
      ...[...document.querySelectorAll("[data-settings]")].flatMap(control => control.dataset.settings.split(" "))
    ]);
    for (const key of persistedSettings) {
      expect(settingKeys.has(key), `missing persisted setting control: ${key}`).toBe(true);
    }
  });

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
    for (const page of settingsPages) {
      dom.window.settingsApp.receive({ version: 1, type: "navigate", payload: { page, focusCurrentTrack: false } });

      expect(dom.window.document.querySelector(`[data-nav="${page}"]`).classList.contains("active")).toBe(true);
      expect(dom.window.document.querySelector(`[data-nav="${page}"]`).getAttribute("aria-current")).toBe("page");
      expect(dom.window.document.querySelector(`[data-page="${page}"]`).classList.contains("active")).toBe(true);
    }
  });

  it("echoes the word-scanning setting and posts V1 updates", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);

    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: { enableWordScanning: true }, fonts: [] }
    });

    const toggle = document.querySelector('input[data-setting="enableWordScanning"]');
    expect(toggle).not.toBeNull();
    expect(toggle.checked).toBe(true);

    toggle.checked = false;
    toggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "enableWordScanning", value: false }
    });

    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: { enableWordScanning: true }, fonts: [] }
    });
    expect(toggle.checked).toBe(true);
  });

  it("hydrates the auto-hide setting and posts V1 updates", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);

    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: { autoHideWhenNoPlayback: true }, fonts: [] }
    });

    const toggle = document.querySelector('input[data-setting="autoHideWhenNoPlayback"]');
    expect(toggle).not.toBeNull();
    expect(toggle.checked).toBe(true);

    toggle.checked = false;
    toggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "autoHideWhenNoPlayback", value: false }
    });

    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: { autoHideWhenNoPlayback: true }, fonts: [] }
    });
    expect(toggle.checked).toBe(true);
  });

  it("renders connected displays and posts mode and multi-selection updates", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: {
          lyricsDisplayMode: "Selected",
          selectedDisplayIds: ["display-b"],
          availableDisplays: [
            { id: "display-a", name: "显示器 1 · Internal", isPrimary: true, width: 1920, height: 1080 },
            { id: "display-b", name: "显示器 2 · External", isPrimary: false, width: 2560, height: 1440 }
          ]
        },
        fonts: []
      }
    });

    const mode = document.querySelector('[data-display-mode][value="Selected"]');
    const displayA = document.querySelector('[data-display-id="display-a"]');
    const displayB = document.querySelector('[data-display-id="display-b"]');
    expect(mode.checked).toBe(true);
    expect(displayA.checked).toBe(false);
    expect(displayB.checked).toBe(true);
    expect(document.querySelector("#displayMonitorHint").textContent).toBe("已选择 1/2 台显示器。");
    const displayGroup = document.querySelector("#displayMonitorList");
    expect(displayGroup.getAttribute("role")).toBe("group");
    expect(document.getElementById(displayGroup.getAttribute("aria-labelledby")).textContent).toBe("选择显示歌词的显示器");

    displayA.focus();
    displayA.checked = true;
    displayA.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "selectedDisplayIds", value: ["display-b", "display-a"] }
    });
    expect(document.activeElement.dataset.displayId).toBe("display-a");

    const allMode = document.querySelector('[data-display-mode][value="All"]');
    allMode.checked = true;
    allMode.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "lyricsDisplayMode", value: "All" }
    });
    expect(document.querySelector('input[data-display-id="display-a"]')).toBeNull();
    expect(document.querySelector('[data-display-id="display-a"] .is-enabled')).toBeNull();
    expect(document.querySelector("#displayMonitorHint").textContent).toBe("已连接 2 台显示器，歌词将在全部显示器显示。");
  });

  it("requires consent before changing an unauthorized spectrum mode", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: { spectrumDisplayMode: "Disabled", spectrumAudioAccessGranted: false },
        fonts: []
      }
    });

    const spectrumTrigger = document.querySelector('[data-setting="spectrumDisplayMode"]');
    const consentDialog = document.querySelector("#spectrumAudioConsentDialog");
    const showConsentDialog = consentDialog.showModal.bind(consentDialog);
    let focusWhenConsentOpened = null;
    consentDialog.showModal = function showModal() {
      focusWhenConsentOpened = document.activeElement;
      showConsentDialog();
    };
    const beforeSelection = sent.length;
    dom.window.settingsApp.receive({
      version: 1,
      type: "requestSpectrumDisplayMode",
      payload: { mode: "Always" }
    });
    expect(consentDialog.open).toBe(true);
    expect(sent.slice(beforeSelection)).toEqual([]);
    consentDialog.querySelector('[data-dialog-cancel="spectrumAudioConsentDialog"]').click();
    consentDialog.dispatchEvent(new dom.window.Event("animationend"));

    spectrumTrigger.click();
    focusWhenConsentOpened = null;
    document.querySelector('#selectListbox [data-option-index="1"]').click();

    expect(consentDialog.open).toBe(true);
    expect(focusWhenConsentOpened).toBe(spectrumTrigger);
    expect(sent.slice(beforeSelection)).toEqual([]);
    expect(spectrumTrigger.querySelector(".select-trigger-value").textContent).toContain("关闭");
    expect(document.querySelector("#spectrumTuningButton").disabled).toBe(true);
    expect(document.querySelector("#spectrumTuningDescription").textContent).toContain("“歌词”页");

    consentDialog.querySelector('[data-dialog-cancel="spectrumAudioConsentDialog"]').click();
    consentDialog.dispatchEvent(new dom.window.Event("animationend"));
    expect(sent.slice(beforeSelection)).toEqual([]);

    spectrumTrigger.click();
    document.querySelector('#selectListbox [data-option-index="1"]').click();
    document.querySelector("#confirmSpectrumAudioAccess").click();
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "confirmSpectrumAudioAccess",
      payload: "PureMusicOnly"
    });

    consentDialog.dispatchEvent(new dom.window.Event("animationend"));
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: { spectrumDisplayMode: "Always", spectrumAudioAccessGranted: true },
        fonts: []
      }
    });

    spectrumTrigger.click();
    document.querySelector('#selectListbox [data-option-index="1"]').click();
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "spectrumDisplayMode", value: "PureMusicOnly" }
    });
    expect(consentDialog.open).toBe(false);
    expect(document.querySelector("#revokeSpectrumAudioAccessButton").hidden).toBe(false);
    expect(document.querySelector("#spectrumTuningButton").disabled).toBe(false);
    expect(document.querySelector("#spectrumTuningDescription").textContent).toContain("实时调节频谱参数");
  });

  it("renders spectrum capture states and posts recovery commands", async () => {
    const { dom, sent, script } = await createSettingsDom();
    enableDialogDomSupport(dom);
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: { spectrumDisplayMode: "Always", spectrumAudioAccessGranted: true },
        fonts: []
      }
    });

    const status = document.querySelector("#spectrumAudioAccessStatus");
    const failureDialog = document.querySelector("#spectrumCaptureFailureDialog");
    dom.window.settingsApp.receive({
      version: 1,
      type: "spectrumCaptureState",
      payload: { state: "capturing", message: "capture ready" }
    });
    expect(status.dataset.state).toBe("capturing");
    expect(status.textContent).toBe("capture ready");

    dom.window.settingsApp.receive({
      version: 1,
      type: "requestSpectrumDisplayMode",
      payload: { mode: "PureMusicOnly" }
    });
    expect(failureDialog.open).toBe(false);
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "spectrumDisplayMode", value: "PureMusicOnly" }
    });

    dom.window.settingsApp.receive({
      version: 1,
      type: "spectrumCaptureState",
      payload: { state: "blocked", message: "capture blocked" }
    });
    expect(failureDialog.open).toBe(true);
    expect(document.querySelector("#spectrumCaptureFailureMessage").textContent).toBe("capture blocked");
    expect(status.dataset.state).toBe("blocked");
    expect(document.querySelector("#retrySpectrumAudioAccessButton").hidden).toBe(false);

    failureDialog.dispatchEvent(new dom.window.Event("cancel", { cancelable: true }));
    failureDialog.dispatchEvent(new dom.window.Event("animationend"));
    expect(failureDialog.open).toBe(false);
    expect(document.querySelector("#retrySpectrumAudioAccessButton").hidden).toBe(false);

    document.querySelector("#retrySpectrumAudioAccessButton").click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "retrySpectrumCapture", payload: {} });
    expect(status.dataset.state).toBe("waiting");
    expect(status.textContent).toBe("正在重试系统音频采集…");
    expect(document.querySelector("#retrySpectrumAudioAccessButton").hidden).toBe(true);

    dom.window.settingsApp.receive({
      version: 1,
      type: "spectrumCaptureState",
      payload: { state: "blocked", message: "capture blocked again" }
    });
    document.querySelector("#retrySpectrumCaptureButton").click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "retrySpectrumCapture", payload: {} });
    failureDialog.dispatchEvent(new dom.window.Event("animationend"));

    dom.window.settingsApp.receive({
      version: 1,
      type: "spectrumCaptureState",
      payload: { state: "blocked", message: "capture still blocked" }
    });
    document.querySelector("#disableSpectrumButton").click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "disableSpectrum", payload: {} });

    document.querySelector("#revokeSpectrumAudioAccessButton").click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "revokeSpectrumAudioAccess", payload: {} });
  });

  it("runs one lyric diagnostics request and exposes the running track", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);

    document.querySelector('[data-nav="lyricDiagnostics"]').click();
    expect(document.querySelector('[data-page="lyricDiagnostics"]').classList.contains("active")).toBe(true);

    document.querySelector("#runLyricDiagnosticsButton").click();
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "runLyricDiagnostics",
      payload: {}
    });
    expect(document.querySelector("#runLyricDiagnosticsButton").disabled).toBe(true);

    dom.window.settingsApp.receive({
      version: 1,
      type: "lyricDiagnosticsState",
      payload: {
        status: "running",
        track: { title: "Song", artist: "Artist", album: "Album", sourceApp: "QQMusic", durationSeconds: 201, songId: "song-1" }
      }
    });

    expect(document.querySelector("#lyricDiagnosticsStatus").dataset.state).toBe("running");
    expect(document.querySelector("#lyricDiagnosticsStatus").textContent).toContain("Song");
    expect(document.querySelector("#lyricDiagnosticsTrackPanel").hidden).toBe(false);
    expect(document.querySelector("#lyricDiagnosticsTrack").textContent).toContain("QQMusic");
    expect(document.querySelector("#lyricDiagnosticsTrackPanel .panel-head p").textContent)
      .toContain("清除歌词缓存时会一并删除");
    expect(document.querySelector("#clearDialogDescription").textContent)
      .toContain("“使用并记住”的指定记录");
  });

  it("renders diagnostic providers, rejected candidates, and the final selection", async () => {
    const { dom, script } = await createSettingsDom();
    const css = await read("TaskbarLyrics.App/Web/Settings/settings.css");
    const document = dom.window.document;
    dom.window.eval(script);

    dom.window.settingsApp.receive({
      version: 1,
      type: "lyricDiagnosticsState",
      payload: {
        status: "success",
        report: {
          capturedAtUtc: "2026-08-08T10:20:30Z",
          originalTrack: { title: "Original", artist: "Artist", album: "Album", sourceApp: "QQMusic", durationSeconds: 200, songId: "song-1" },
          effectiveTrack: { title: "Effective", artist: "Artist", album: "Album", sourceApp: "QQMusic", durationSeconds: 200, songId: "song-1" },
          preferredProvider: "QQMusic",
          searchVariants: [{ id: "strict", title: "Effective", artists: ["Artist"], album: "Album", durationSeconds: 200, relaxationReasons: [] }],
          providers: [
            {
              providerId: "QQMusic",
              state: "Succeeded",
              detail: "selected candidate",
              selected: true,
              candidates: [{ candidateId: "qq-1", title: "<unsafe>", artists: ["Artist with an intentionally long diagnostic display name"], album: "Album", durationSeconds: 198, queryVariantId: "strict", fetchMetadataKeys: ["tokenType", "metadata-key-with-an-intentionally-long-unbroken-value"], isAdmitted: false, score: 61, rejectionReasons: ["below-admission-threshold"] }]
            },
            { providerId: "Netease", state: "NoLyrics", detail: "not found", selected: false, candidates: [] }
          ],
          selection: { providerId: "QQMusic", candidateId: "qq-2-with-a-very-long-unbroken-diagnostic-identity", acquisition: "Remote", format: "Lrc", timingKind: "Timed", timingProvenance: "Provider", lineCount: 42, diagnostics: { elapsedMs: "84" } },
          error: null
        }
      }
    });

    expect(document.querySelector("#lyricDiagnosticsStatus").dataset.state).toBe("success");
    expect(document.querySelector("#lyricDiagnosticsProviderCount").textContent).toBe("2 个歌词源");
    expect(document.querySelector("#lyricDiagnosticsReportSummary").textContent).toContain("首选来源：");
    expect(document.querySelector(".diagnostics-candidate").textContent).toContain("below-admission-threshold");
    expect(document.querySelector(".diagnostics-candidate").textContent).toContain("61 分");
    const providerDetails = [...document.querySelectorAll(".diagnostics-provider")];
    expect(providerDetails).toHaveLength(2);
    expect(providerDetails.every(provider => provider.open)).toBe(true);
    expect(providerDetails[0].querySelector(".diagnostics-provider-toggle-meta").textContent).toContain("1 个候选");
    expect(providerDetails[1].querySelector(".diagnostics-provider-toggle-meta").textContent).toContain("0 个候选");
    providerDetails[0].querySelector("summary").click();
    expect(providerDetails[0].open).toBe(false);
    expect(document.querySelector(".diagnostics-candidate strong").textContent).toBe("<unsafe>");
    expect(document.querySelector(".diagnostics-candidate").innerHTML).not.toContain("<strong><unsafe>");
    expect(document.querySelector(".diagnostics-candidate-title small").getAttribute("title")).toContain("intentionally long");
    expect(document.querySelector(".diagnostics-selection-card").textContent).toContain("QQMusic");
    expect(document.querySelector("#lyricDiagnosticsVariantsPanel").hidden).toBe(false);
    expect(css).toMatch(/\.diagnostics-candidate-title small\s*\{[^}]*min-width:\s*0;[^}]*text-overflow:\s*ellipsis/s);
    expect(css).toMatch(/\.diagnostics-candidate\.is-selected\s*\{[^}]*var\(--success\)/s);
    expect(css).toMatch(/\.diagnostics-selection-meta (?:span|code)[\s\S]*overflow-wrap:\s*anywhere/s);
  });

  it("marks the selected candidate and reflects trust-priority selection", async () => {
    const { dom, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);

    dom.window.settingsApp.receive({
      version: 1,
      type: "lyricDiagnosticsState",
      payload: {
        status: "success",
        report: {
          capturedAtUtc: "2026-08-10T12:00:00Z",
          originalTrack: { title: "Priority Song", artist: "Artist", album: "Album", sourceApp: "QQMusic", durationSeconds: 200, songId: "song-1" },
          preferredProvider: null,
          searchVariants: [{ id: "exact", title: "Priority Song", artists: ["Artist"], album: "Album", durationSeconds: 200, relaxationReasons: [] }],
          providers: [
            {
              providerId: "QQMusic",
              state: "Succeeded",
              detail: null,
              selected: false,
              candidates: [{ candidateId: "qq-1", title: "Priority Songs", artists: ["Artist"], album: "Album", durationSeconds: 192, queryVariantId: "exact", fetchMetadataKeys: [], isAdmitted: true, isHighConfidence: false, score: 84, rejectionReasons: [] }]
            },
            {
              providerId: "Kugou",
              state: "Succeeded",
              detail: null,
              selected: true,
              candidates: [{ candidateId: "kugou-1", title: "Priority Song", artists: ["Artist"], album: "Album", durationSeconds: 200, queryVariantId: "exact", fetchMetadataKeys: [], isAdmitted: true, isHighConfidence: true, score: 96, rejectionReasons: [] }]
            },
            {
              providerId: "Netease",
              state: "Succeeded",
              detail: null,
              selected: false,
              candidates: [{ candidateId: "netease-1", title: "Priority Song", artists: ["Artist"], album: "Album", durationSeconds: 200, queryVariantId: "exact", fetchMetadataKeys: [], isAdmitted: true, isHighConfidence: true, score: 100, rejectionReasons: [] }]
            }
          ],
          selection: { providerId: "kugou", candidateId: "kugou-1", acquisition: "Remote", format: "Lrc", timingKind: "Timed", timingProvenance: "Provider", lineCount: 30, diagnostics: { identityScore: "96" } },
          error: null
        }
      }
    });

    const providers = [...document.querySelectorAll(".diagnostics-provider")];
    expect(providers).toHaveLength(3);

    const kugouProvider = providers.find(p => p.querySelector(".diagnostics-provider-title strong").textContent === "Kugou");
    expect(kugouProvider.querySelector('[data-state="selected"]').textContent).toBe("最终采用");
    expect(kugouProvider.querySelector(".diagnostics-provider-title").textContent).toContain("成功");
    expect(kugouProvider.querySelector(".diagnostics-candidate.is-selected")).not.toBeNull();
    expect(kugouProvider.querySelector(".diagnostics-candidate.is-selected [data-state=\"selected\"]").textContent).toBe("当前采用");

    const highConfidenceBadges = document.querySelectorAll('[data-state="high-confidence"]');
    expect(highConfidenceBadges).toHaveLength(2);
    expect([...highConfidenceBadges].every(badge => badge.textContent === "高置信")).toBe(true);

    const qqCandidate = providers.find(p => p.querySelector(".diagnostics-provider-title strong").textContent === "QQMusic").querySelector(".diagnostics-candidate-controls");
    expect(providers.find(p => p.querySelector(".diagnostics-provider-title strong").textContent === "QQMusic").querySelector(".diagnostics-provider-title").textContent).toContain("成功");
    expect(qqCandidate.querySelector(".diagnostics-score").textContent).toBe("84 分");
    const neteaseCandidate = providers.find(p => p.querySelector(".diagnostics-provider-title strong").textContent === "Netease").querySelector(".diagnostics-candidate-summary");
    expect(neteaseCandidate.textContent).toContain("已接纳");
    expect(providers[0].querySelector(".diagnostics-provider-toggle-meta").textContent).toContain("1 个候选");

    const selectionText = document.querySelector(".diagnostics-selection-card").textContent;
    expect(selectionText).toContain("kugou");
    expect(document.querySelector("#lyricDiagnosticsReportSummary").textContent).toContain("首选来源：未指定");
  });

  it("shows current and remembered apply actions for every candidate and reports async states", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);

    dom.window.settingsApp.receive({
      version: 1,
      type: "lyricDiagnosticsState",
      payload: {
        status: "success",
        report: {
          originalTrack: { title: "Song", artist: "Artist", sourceApp: "QQMusic" },
          providers: [{
            providerId: "QQMusic",
            state: "IdentityRejected",
            candidates: [
              { candidateId: "rejected-1", title: "Wrong Song", artists: ["Artist"], isAdmitted: false, score: 42, rejectionReasons: ["below-admission-threshold"] },
              { candidateId: "accepted-1", title: "Song", artists: ["Artist"], isAdmitted: true, score: 96, rejectionReasons: [] },
              { title: "Missing Id Song", artists: ["Artist"], isAdmitted: true, score: 90, rejectionReasons: [] }
            ]
          }]
        }
      }
    });

    const buttons = [...document.querySelectorAll("[data-lyric-diagnostics-apply]")];
    expect(buttons).toHaveLength(4);
    expect(buttons.every(button => button.dataset.providerId && button.dataset.candidateId)).toBe(true);
    expect(buttons.filter(button => button.dataset.applyMode === "current").every(button => button.textContent.includes("本次使用"))).toBe(true);
    expect(buttons.filter(button => button.dataset.applyMode === "remember").every(button => button.textContent.includes("使用并记住"))).toBe(true);
    expect(buttons.filter(button => button.dataset.applyMode === "current").every(button => button.classList.contains("secondary"))).toBe(true);
    expect(buttons.filter(button => button.dataset.applyMode === "remember").every(button => button.classList.contains("ghost"))).toBe(true);
    const acceptedControls = document.querySelector('[data-candidate-id="accepted-1"]').closest(".diagnostics-candidate-controls");
    const acceptedSummary = acceptedControls.querySelector(".diagnostics-candidate-summary");
    expect(acceptedSummary.textContent).toContain("已接纳");
    expect(acceptedSummary.textContent).toContain("96 分");
    expect(acceptedSummary.firstElementChild.classList.contains("diagnostics-candidate-badges")).toBe(true);
    expect(acceptedSummary.lastElementChild.classList.contains("diagnostics-score")).toBe(true);
    expect(acceptedControls.lastElementChild.classList.contains("diagnostics-candidate-actions")).toBe(true);

    const currentButton = document.querySelector('[data-candidate-id="rejected-1"][data-apply-mode="current"]');
    currentButton.click();
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "applyLyricDiagnosticCandidate",
      payload: { providerId: "QQMusic", candidateId: "rejected-1", mode: "current" }
    });
    const loadingButton = document.querySelector('[data-candidate-id="rejected-1"][data-apply-mode="current"]');
    expect(loadingButton.disabled).toBe(true);
    expect(loadingButton.getAttribute("aria-busy")).toBe("true");
    expect(document.querySelector('[data-candidate-id="rejected-1"][data-apply-mode="remember"]').disabled).toBe(true);
    expect(document.querySelector("#runLyricDiagnosticsButton").disabled).toBe(true);
    expect(document.querySelector("#runLyricDiagnosticsButton").getAttribute("aria-busy")).toBe("true");
    expect(document.querySelector("#lyricDiagnosticsStatus").textContent).toContain("正在获取并应用歌词");

    const sentCountWhileLoading = sent.length;
    loadingButton.dispatchEvent(new dom.window.MouseEvent("click", { bubbles: true }));
    expect(sent).toHaveLength(sentCountWhileLoading);

    dom.window.settingsApp.receive({
      version: 1,
      type: "lyricDiagnosticsState",
      payload: {
        status: "success",
        apply: { status: "success", providerId: "QQMusic", candidateId: "rejected-1", mode: "current", message: "已应用当前歌词。" }
      }
    });
    expect(document.querySelector("#lyricDiagnosticsStatus").dataset.state).toBe("success");
    expect(document.querySelector("#lyricDiagnosticsStatus").textContent).toBe("已应用当前歌词。");
    expect(document.querySelector('[data-candidate-id="rejected-1"][data-apply-mode="current"]').textContent).toContain("已应用");
    expect(document.querySelector("#runLyricDiagnosticsButton").disabled).toBe(false);

    const rememberButton = document.querySelector('[data-candidate-id="accepted-1"][data-apply-mode="remember"]');
    rememberButton.click();
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "applyLyricDiagnosticCandidate",
      payload: { providerId: "QQMusic", candidateId: "accepted-1", mode: "remember" }
    });
    expect(document.querySelector("#lyricDiagnosticsStatus").textContent).toContain("写入歌词缓存");

    dom.window.settingsApp.receive({
      version: 1,
      type: "lyricDiagnosticsState",
      payload: {
        status: "success",
        apply: { status: "success", providerId: "QQMusic", candidateId: "accepted-1", mode: "remember", message: "已应用并写入歌词缓存。" }
      }
    });
    expect(document.querySelector("#lyricDiagnosticsStatus").dataset.state).toBe("success");
    expect(document.querySelector("#lyricDiagnosticsStatus").textContent).toContain("已应用并写入歌词缓存");
    expect(document.querySelector('[data-candidate-id="accepted-1"][data-apply-mode="remember"]').textContent).toContain("已应用并记住");

    dom.window.settingsApp.receive({
      version: 1,
      type: "lyricDiagnosticsState",
      payload: {
        status: "success",
        apply: { status: "error", providerId: "QQMusic", candidateId: "accepted-1", mode: "remember", message: "当前歌曲已切换，请重新查找。" }
      }
    });
    expect(document.querySelector("#lyricDiagnosticsStatus").dataset.state).toBe("error");
    expect(document.querySelector("#lyricDiagnosticsStatus").textContent).toContain("当前歌曲已切换");
    expect(document.querySelector('[data-candidate-id="accepted-1"][data-apply-mode="remember"]').textContent).toContain("使用并记住");
  });

  it("shows empty and error diagnostic states without stale report content", async () => {
    const { dom, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);

    dom.window.settingsApp.receive({ version: 1, type: "lyricDiagnosticsState", payload: { status: "empty", message: "没有当前歌曲" } });
    expect(document.querySelector("#lyricDiagnosticsStatus").dataset.state).toBe("empty");
    expect(document.querySelector("#lyricDiagnosticsStatus").textContent).toBe("没有当前歌曲");
    expect(document.querySelector("#lyricDiagnosticsReportPanel").hidden).toBe(true);

    dom.window.settingsApp.receive({ version: 1, type: "lyricDiagnosticsState", payload: { status: "error", message: "runner failed" } });
    expect(document.querySelector("#lyricDiagnosticsStatus").dataset.state).toBe("error");
    expect(document.querySelector("#lyricDiagnosticsStatus").textContent).toBe("runner failed");
    expect(document.querySelector("#lyricDiagnosticsTrackPanel").hidden).toBe(true);
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

  it("keeps non-target pages inert during rapid transitions", async () => {
    const { dom, script } = await createSettingsDom();
    const document = dom.window.document;
    const pendingAnimation = new Promise(() => {});

    document.querySelectorAll("[data-page]").forEach(page => {
      page.animate = () => ({ cancel() {}, finished: pendingAnimation });
    });
    dom.window.eval(script);

    document.querySelector('[data-nav="lyrics"]').click();
    expect(document.querySelector('[data-page="sources"]').inert).toBe(true);
    expect(document.querySelector('[data-page="lyrics"]').inert).toBe(false);

    document.querySelector('[data-nav="shortcuts"]').click();
    expect(document.querySelector('[data-page="lyrics"]').inert).toBe(true);
    expect(document.querySelector('[data-page="shortcuts"]').inert).toBe(false);
    expect(document.querySelector('[data-page="shortcuts"]').hasAttribute("inert")).toBe(false);
  });

  it("finishes reduced-motion navigation with only the target page focusable", async () => {
    const { dom, script } = await createSettingsDom({ reducedMotion: true });
    const document = dom.window.document;
    dom.window.eval(script);

    document.querySelector('[data-nav="about"]').click();

    expect(document.querySelector('[data-page="about"]').classList.contains("active")).toBe(true);
    expect(document.querySelector('[data-page="about"]').inert).toBe(false);
    expect([...document.querySelectorAll("[data-page]")].filter(page => !page.inert)).toEqual([
      document.querySelector('[data-page="about"]')
    ]);
    expect(document.activeElement).toBe(document.querySelector("#aboutHeading"));
  });

  it("exposes color saturation and brightness as native ranges while retaining pointer picking", async () => {
    const { dom, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: {
        settings: {
          foregroundColorMode: "Custom",
          foregroundColor: "#FF336699"
        },
        fonts: []
      }
    });

    const colorArea = document.querySelector("#colorArea");
    const saturation = document.querySelector("#colorSaturationSlider");
    const brightness = document.querySelector("#colorBrightnessSlider");
    expect(colorArea.hasAttribute("tabindex")).toBe(false);
    expect(colorArea.getAttribute("aria-hidden")).toBe("true");
    expect(saturation.type).toBe("range");
    expect(brightness.type).toBe("range");

    document.querySelector("#colorPickerButton").click();
    expect(document.activeElement).toBe(saturation);
    expect(saturation.value).toBe("67");
    expect(brightness.value).toBe("60");

    saturation.value = "25";
    saturation.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    expect(document.querySelector("#colorCursor").style.left).toBe("25%");

    colorArea.getBoundingClientRect = () => ({ left: 0, top: 0, width: 100, height: 100 });
    colorArea.setPointerCapture = () => {};
    colorArea.releasePointerCapture = () => {};
    const pointerDown = new dom.window.Event("pointerdown", { bubbles: true });
    Object.defineProperties(pointerDown, {
      clientX: { value: 75 },
      clientY: { value: 25 },
      pointerId: { value: 1 }
    });
    colorArea.dispatchEvent(pointerDown);

    expect(saturation.value).toBe("75");
    expect(brightness.value).toBe("75");
  });

  it("gives destructive and reset dialogs accessible names and descriptions", async () => {
    const { dom } = await createSettingsDom();
    const document = dom.window.document;
    const dialogs = ["restoreDialog", "clearDialog", "deleteTrackOffsetDialog", "clearTrackOffsetsDialog"];

    dialogs.forEach(id => {
      const dialog = document.querySelector(`#${id}`);
      const title = document.getElementById(dialog.getAttribute("aria-labelledby"));
      const description = document.getElementById(dialog.getAttribute("aria-describedby"));
      expect(title?.textContent).toBeTruthy();
      expect(description?.textContent).toBeTruthy();
    });
  });

  it.each([false, true])("keeps window width when previewing layout scale in floating mode %s", async (useFloatingWindow) => {
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
          useFloatingWindow,
          windowWidth: 420,
          effectiveWindowWidth: 420,
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
    if (useFloatingWindow) {
      expect(slider.disabled).toBe(true);
      const before = sent.length;
      slider.value = "125";
      slider.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
      slider.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
      expect(sent).toHaveLength(before);
      dom.window.close();
      return;
    }
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
        effectiveCoverCornerRadius: 8,
        effectiveWindowWidth: 420
      }
    });

    expect(document.querySelector("[data-effective-font-size]").textContent).toBe("17.5 px");
    expect(document.querySelector("[data-effective-cover-size]").textContent).toBe("43 px");
    expect(document.querySelector("[data-effective-window-width]")).toBeNull();
    expect(document.querySelector('input[type="number"][data-setting="windowWidth"]').value).toBe("420");
    expect(sent.filter(message => message.payload?.key === "windowWidth")).toHaveLength(0);
    expect(document.querySelector("[data-reset-layout-scale]").closest(".mode-scale-footer").contains(document.querySelector("#layoutScalePreview"))).toBe(true);
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
          enableControlPanel: true,
          coverSize: 34,
          coverGap: 8,
          coverCornerRadius: 6
        },
        fonts: []
      }
    });

    const toggle = document.querySelector('input[data-cover-display][value="Hidden"]');
    toggle.click();

    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "showCover", value: false }
    });
    expect(document.querySelector('input[data-setting="coverSize"]').value).toBe("34");
    expect(document.querySelector('input[data-setting="enableControlPanel"]').checked).toBe(true);
    expect(document.querySelector("[data-effective-cover-size]").textContent).toBe("已隐藏");
    expect(document.querySelector("[data-effective-cover-gap-item]").hidden).toBe(true);
    document.querySelectorAll('[data-depends="showCover"]').forEach(row => {
      expect(row.classList.contains("is-disabled")).toBe(true);
      row.querySelectorAll("input, button").forEach(control => expect(control.disabled).toBe(true));
    });
  });

  it.each([false, true])("keeps the shared lyric layout outside the mode panel and editable for floating=%s", async floating => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    const layout = document.querySelector('[data-display-group="lyrics"]');
    const receive = useFloatingWindow => dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: {
      settings: { useFloatingWindow, enableControlPanel: false, enableFloatingControlPanel: false,
        singleLineLyrics: true, lyricsTextAlignment: "Center", showCover: true, coverPosition: "Right" }
    } });
    const count = sent.length;
    receive(!floating);
    receive(floating);
    expect(document.querySelectorAll('[data-display-group="lyrics"]')).toHaveLength(1);
    expect(layout.closest(".display-presentation")).toBeNull();
    expect(layout.parentElement).toBe(document.querySelector('[data-page="displayArea"] > .page-inner'));
    expect(layout.closest("[hidden]")).toBeNull();
    for (const control of layout.querySelectorAll("input")) expect(control.disabled).toBe(false);
    expect(layout.querySelector('[data-setting="singleLineLyrics"][value="true"]').checked).toBe(true);
    expect(layout.querySelector('[data-setting="lyricsTextAlignment"][value="Center"]').checked).toBe(true);
    expect(layout.querySelector('[data-cover-display][value="Right"]').checked).toBe(true);
    expect(sent).toHaveLength(count);
    layout.querySelector('[data-setting="singleLineLyrics"][value="false"]').click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "update", payload: { key: "singleLineLyrics", value: false } });
    dom.window.close();
  });

  it("defaults to double line and sends boolean values when switching lyric line count", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const receive = settings => dom.window.settingsApp.receive({ version: 1, type: "settingsState",
      payload: { settings: { sourceRecognitionOrder: [], playerLyricOffsets: {}, defaultPlayerLyricOffsets: {},
        mediaHotkeys: [], mediaHotkeyStatuses: {}, ...settings }, fonts: [] } });
    receive({});
    const controls = dom.window.document.querySelectorAll('[data-setting="singleLineLyrics"]');
    expect(controls[0].checked).toBe(true);
    for (const value of [true, false]) {
      const selected = [...controls].find(control => control.value === String(value));
      selected.checked = true;
      selected.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
      expect(sent.at(-1)).toEqual({ version: 1, type: "update", payload: { key: "singleLineLyrics", value } });
      receive({ singleLineLyrics: value });
      expect(selected.checked).toBe(true);
    }
  });

  it("offers single method selection and independent regions without permitting an empty selection", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    const receive = settings => dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings } });
    receive({ useFloatingWindow: false, showCover: true, enableControlPanel: true });
    const cover = document.querySelector('[data-setting="controlPanelTriggerCover"]');
    const lyrics = document.querySelector('[data-setting="controlPanelTriggerLyrics"]');
    const method = document.querySelector('[data-setting="controlPanelTriggerMethod"][value="Hover"]');
    expect(cover.checked).toBe(true);
    expect(lyrics.checked).toBe(false);
    expect(document.querySelector('[data-setting="controlPanelTriggerMethod"][value="Click"]').checked).toBe(true);
    const before = sent.length;
    cover.click();
    expect(cover.checked).toBe(true);
    expect(sent).toHaveLength(before);
    lyrics.click();
    expect(lyrics.checked && cover.checked).toBe(true);
    expect(sent.at(-1).payload).toEqual({ key: "controlPanelTriggerLyrics", value: true });
    cover.click();
    expect(cover.checked).toBe(false);
    expect(sent.at(-1).payload).toEqual({ key: "controlPanelTriggerCover", value: false });
    method.click();
    expect(sent.at(-1).payload).toEqual({ key: "controlPanelTriggerMethod", value: "Hover" });
    document.querySelector('#embeddedControlPanelToggle').click();
    expect(cover.disabled && lyrics.disabled && method.disabled).toBe(true);
    expect(lyrics.checked && method.checked).toBe(true);
    for (const control of [cover, lyrics, method]) {
      expect(control.closest("[hidden]")).toBeNull();
    }
    const disabledCount = sent.length;
    cover.click();
    lyrics.click();
    method.click();
    expect(sent).toHaveLength(disabledCount);
    expect(cover.checked).toBe(false);
    expect(lyrics.checked && method.checked).toBe(true);
    document.querySelector('#embeddedControlPanelToggle').click();
    expect(lyrics.disabled || method.disabled).toBe(false);
    expect(cover.checked).toBe(false);
    expect(lyrics.checked && method.checked).toBe(true);
    for (const control of [cover, lyrics, method]) {
      expect(control.closest("[hidden]")).toBeNull();
    }
    lyrics.click();
    expect(lyrics.checked).toBe(true);
    receive({ useFloatingWindow: true, showCover: true, enableControlPanel: true,
      controlPanelTriggerMethod: "Hover", controlPanelTriggerCover: false, controlPanelTriggerLyrics: true });
    expect(cover.disabled && lyrics.disabled && method.disabled).toBe(true);
    expect(method.checked && lyrics.checked).toBe(true);
    dom.window.close();
  });

  it("retains keyboard segmented navigation and enables lyrics when the cover is hidden", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: {
      useFloatingWindow: false, showCover: true, enableControlPanel: true
    } } });
    const click = document.querySelector('[data-setting="controlPanelTriggerMethod"][value="Click"]');
    click.focus();
    click.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true, cancelable: true }));
    expect(sent.at(-1).payload).toEqual({ key: "controlPanelTriggerMethod", value: "Hover" });
    document.querySelector('[data-cover-display][value="Hidden"]').click();
    expect(document.querySelector('[data-setting="controlPanelTriggerCover"]').disabled).toBe(true);
    expect(document.querySelector('[data-setting="controlPanelTriggerLyrics"]').checked).toBe(true);
    expect(document.querySelector('#embeddedControlPanelToggle').disabled).toBe(false);
    const count = sent.length;
    document.querySelector('[data-setting="controlPanelTriggerLyrics"]').click();
    expect(sent).toHaveLength(count);
    expect(document.querySelector('[data-setting="controlPanelTriggerLyrics"]').checked).toBe(true);
    dom.window.close();
  });

  it("loads and saves the cover control panel switch independently of cover visibility", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: {
      settings: { sourceRecognitionOrder: [], playerLyricOffsets: {}, showCover: true, enableControlPanel: true }
    } });
    const toggle = dom.window.document.querySelector('input[data-setting="enableControlPanel"]');
    expect(toggle.checked).toBe(true);
    toggle.checked = false;
    toggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1)).toEqual({ version: 1, type: "update", payload: { key: "enableControlPanel", value: false } });
    expect(dom.window.document.querySelector('input[data-cover-display][value="Hidden"]').checked).toBe(false);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: {
      settings: { sourceRecognitionOrder: [], playerLyricOffsets: {}, showCover: true, enableControlPanel: false }
    } });
    expect(toggle.checked).toBe(false);
  });

  it.each([
    [false, true, false],
    [false, false, false],
    [true, true, true],
    [true, false, true]
  ])("gates the embedded control panel switch for floating=%s, cover=%s", async (floating, showCover, disabled) => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const initialCount = sent.length;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: {
      settings: { useFloatingWindow: floating, showCover, enableControlPanel: true }
    } });
    const document = dom.window.document;
    const toggle = document.querySelector('input[data-setting="enableControlPanel"]');
    const row = document.querySelector("#embeddedControlPanelRow");
    expect(document.querySelectorAll('input[data-setting="enableControlPanel"]')).toHaveLength(1);
    expect(row.closest(".embedded-window-mode-settings")).not.toBeNull();
    expect(row.closest(".window-mode-choice")).toBeNull();
    expect(toggle.disabled).toBe(disabled);
    expect(toggle.checked).toBe(true);
    expect(row.classList.contains("is-disabled")).toBe(disabled);
    expect(sent).toHaveLength(initialCount);
    if (disabled) {
      row.querySelector('label[for="embeddedControlPanelToggle"]').click();
      toggle.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
      expect(sent).toHaveLength(initialCount);
    }
  });

  it.each([
    [{ forceAlwaysOnTop: true }, "Fullscreen"],
    [{ forceAlwaysOnTop: false }, "Window"],
    [{ forceAlwaysOnTop: true, floatingWindowLayer: "Desktop" }, "Desktop"],
    [{ forceAlwaysOnTop: false, floatingWindowLayer: "Unknown" }, "Window"]
  ])("restores floating display level from %o", async (settings, expected) => {
    const { dom, script } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: {
      settings: { useFloatingWindow: true, ...settings }
    } });
    expect(dom.window.document.querySelector('[data-setting="floatingWindowLayer"]:checked').value).toBe(expected);
  });

  it("supports all three floating levels by keyboard and preserves the choice across mode changes", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: {
      settings: { useFloatingWindow: true, floatingWindowLayer: "Fullscreen" }
    } });
    const document = dom.window.document;
    const options = [...document.querySelectorAll('[data-setting="floatingWindowLayer"]')];
    expect(options.map(option => option.value)).toEqual(["Desktop", "Window", "Fullscreen"]);
    expect(document.querySelector("#floatingTopmostRow").textContent).toContain("独占全屏可能不支持");
    options[2].dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Home", bubbles: true }));
    expect(options[0].checked).toBe(true);
    expect(document.activeElement).toBe(options[0]);
    expect(sent.at(-1).payload).toEqual({ key: "floatingWindowLayer", value: "Desktop" });
    options[0].dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true }));
    expect(options[1].checked).toBe(true);
    expect(sent.at(-1).payload).toEqual({ key: "floatingWindowLayer", value: "Window" });
    options[1].dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "End", bubbles: true }));
    expect(options[2].checked).toBe(true);
    expect(sent.at(-1).payload).toEqual({ key: "floatingWindowLayer", value: "Fullscreen" });
    options[0].click();
    document.querySelector('[data-window-mode="embedded"]').click();
    expect(options.every(option => option.disabled)).toBe(true);
    const count = sent.length;
    options[1].dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent).toHaveLength(count);
    document.querySelector('[data-window-mode="floating"]').click();
    expect(options[0].checked).toBe(true);
    expect(options.every(option => !option.disabled)).toBe(true);
  });

  it("preserves the panel preference while switching display modes and cover visibility", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: {
      settings: { useFloatingWindow: false, showCover: true, enableControlPanel: true, forceAlwaysOnTop: true }
    } });
    const document = dom.window.document;
    const toggle = document.querySelector("#embeddedControlPanelToggle");
    const floating = document.querySelector('[data-window-mode="floating"]');
    const embedded = document.querySelector('[data-window-mode="embedded"]');
    const topmost = document.querySelector('[data-setting="floatingWindowLayer"][value="Fullscreen"]');
    floating.click();
    expect(toggle.disabled).toBe(true);
    expect(toggle.checked).toBe(true);
    expect(topmost.disabled).toBe(false);
    embedded.click();
    expect(toggle.disabled).toBe(false);
    expect(topmost.disabled).toBe(true);
    document.querySelector('label[for="embeddedControlPanelToggle"]').click();
    expect(embedded.checked).toBe(true);
    expect(toggle.checked).toBe(false);
    expect(sent.at(-1).payload).toEqual({ key: "enableControlPanel", value: false });
    floating.click();
    embedded.click();
    expect(toggle.checked).toBe(false);
    toggle.click();
    const cover = document.querySelector('input[data-cover-display][value="Hidden"]');
    cover.click();
    expect(toggle.disabled).toBe(false);
    expect(toggle.checked).toBe(true);
    expect(document.querySelector('[data-setting="controlPanelTriggerCover"]').disabled).toBe(true);
    expect(document.querySelector('[data-setting="controlPanelTriggerLyrics"]').checked).toBe(true);
    cover.click();
    expect(toggle.disabled).toBe(false);
    expect(toggle.checked).toBe(true);
    expect(sent.filter(message => message.type === "update" && message.payload.key === "enableControlPanel").map(message => message.payload.value)).toEqual([false, true]);
  });

  it("keeps each offset slider synchronized with its editable value above", async () => {
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

    const xNumber = document.querySelector('input[type="number"][data-setting="xOffset"]');
    const yNumber = document.querySelector('input[type="number"][data-setting="yOffset"]');
    const xSlider = document.querySelector('input[type="range"][data-setting="xOffset"]');
    const ySlider = document.querySelector('input[type="range"][data-setting="yOffset"]');

    expect(xNumber.value).toBe("30");
    expect(yNumber.value).toBe("-20");

    xSlider.value = "120";
    xSlider.dispatchEvent(new dom.window.Event("input", { bubbles: true }));

    expect(xNumber.value).toBe("120");
    expect(document.querySelector('[data-setting="xOffset"][type="number"]').value).toBe("120");
    expect(document.querySelector('[data-setting="yOffset"][type="number"]').value).toBe("-20");
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
    expect(xNumber.value).toBe("120");
    expect(document.querySelector('[data-setting="xOffset"][type="number"]').value).toBe("120");
    expect(document.querySelector('[data-setting="yOffset"][type="number"]').value).toBe("-75");

    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "previewUpdate",
      payload: { key: "yOffset", value: -75 }
    });

    yNumber.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

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

  it.each([
    { name: "embedded center anchor", floating: false, anchor: "Center", minX: -500, maxX: 500, minY: -2, maxY: 2 },
    { name: "embedded right anchor", floating: false, anchor: "Right", minX: -1000, maxX: 0, minY: -2, maxY: 2 },
    { name: "floating window", floating: true, anchor: "Left", minX: -2000, maxX: 2000, minY: -2000, maxY: 2000 }
  ])("preserves zero offsets when loading and editing $name", async ({ floating, anchor, minX, maxX, minY, maxY }) => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);
    const initialCount = sent.length;
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: {
        useFloatingWindow: floating, horizontalAnchor: anchor, xOffset: 0, yOffset: 0,
        taskbarMinXOffset: minX, taskbarMaxXOffset: maxX,
        taskbarMinYOffset: minY, taskbarMaxYOffset: maxY
      }, fonts: [] }
    });
    expect(sent).toHaveLength(initialCount);

    for (const key of ["xOffset", "yOffset"]) {
      const number = document.querySelector(`input[type="number"][data-setting="${key}"]`);
      const slider = document.querySelector(`input[type="range"][data-setting="${key}"]`);
      expect(slider.value).toBe("0");
      expect(slider.min).toBe(number.min);
      expect(slider.max).toBe(number.max);
      expect(number.value).toBe("0");
      expect(Number(number.min)).toBe(key === "xOffset" ? minX : minY);
      expect(Number(number.max)).toBe(key === "xOffset" ? maxX : maxY);
      if (floating) {
        expect(number.disabled).toBe(true);
        expect(slider.disabled).toBe(true);
        number.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
        number.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
        expect(sent).toHaveLength(initialCount);
        continue;
      }
      number.value = "-1";
      number.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
      number.value = "0";
      number.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
      expect(sent.at(-1)).toEqual({ version: 1, type: "previewUpdate", payload: { key, value: 0 } });
      number.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
      expect(sent.at(-1)).toEqual({ version: 1, type: "update", payload: { key, value: 0 } });
      expect(number.value).toBe("0");
      expect(slider.value).toBe("0");
    }
  });

  it("coalesces repeated offset numeric input into one preview per animation frame", async () => {
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
    const slider = document.querySelector('input[type="number"][data-setting="xOffset"]');
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

  it("keeps a dragged slider responsive while layout previews arrive out of order", async () => {
    const { dom, script } = await createSettingsDom();
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
          useFloatingWindow: false,
          lyricsLayoutScalePercent: 100,
          fontSize: 14,
          coverSize: 34,
          coverGap: 8,
          coverCornerRadius: 6
        },
        fonts: []
      }
    });

    const slider = document.querySelector('input[type="range"][data-setting="lyricsLayoutScalePercent"]');
    const number = document.querySelector('input[type="number"][data-setting="lyricsLayoutScalePercent"]');
    const unrelatedNumber = document.querySelector('input[type="number"][data-setting="fontSize"]');
    unrelatedNumber.value = "37";
    slider.value = "125";
    slider.dispatchEvent(new dom.window.Event("input", { bubbles: true }));

    expect(number.value).toBe("125");
    expect(unrelatedNumber.value).toBe("37");

    const layoutPreview = (scalePercent, taskbarMaxScalePercent) => dom.window.settingsApp.receive({
      version: 1,
      type: "lyricsLayoutPreview",
      payload: {
        scalePercent, fontSize: 14, coverSize: 34, coverGap: 8, coverCornerRadius: 6,
        ...(taskbarMaxScalePercent === undefined ? {} : { taskbarMaxScalePercent })
      }
    });
    layoutPreview(100, 180);
    expect(slider.value).toBe("125");
    expect(number.value).toBe("125");

    slider.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    layoutPreview(100);
    expect(slider.value).toBe("125");
    expect(slider.max).toBe("180");
    layoutPreview(125);
    layoutPreview(130);
    expect(slider.value).toBe("130");
    expect(number.value).toBe("130");

    slider.value = "150";
    slider.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
    slider.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    layoutPreview(100, 100);
    expect(slider.value).toBe("100");
    expect(number.value).toBe("100");
  });

  it("pairs every settings slider with a quiet numeric input", async () => {
    const { dom } = await createSettingsDom();
    const document = dom.window.document;
    const sliders = Array.from(document.querySelectorAll('input[type="range"][data-setting]'));

    expect(sliders.length).toBe(13);
    sliders.forEach(slider => {
      const control = slider.closest(".slider-number-control");
      expect(control).not.toBeNull();
      expect(control.querySelector(`input[type="number"][data-setting="${slider.dataset.setting}"]`)).not.toBeNull();
      expect(control.querySelector(".compact-number-input")).not.toBeNull();
    });
    expect(document.querySelector("#hueSlider").closest(".slider-number-control").querySelector("#hueNumberInput")).not.toBeNull();
  });

  it("caps both compact-taskbar zoom controls and clamps an oversized numeric commit", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: {
      settings: { sourceRecognitionOrder: [], playerLyricOffsets: {}, defaultPlayerLyricOffsets: {}, mediaHotkeys: [],
        mediaHotkeyStatuses: {}, useFloatingWindow: false, lyricsLayoutScalePercent: 100, fontSize: 14,
        taskbarEmbeddingAvailable: true, taskbarMaxHeight: 32, taskbarMaxScalePercent: 72 }, fonts: []
    } });
    const controls = [...dom.window.document.querySelectorAll('[data-setting="lyricsLayoutScalePercent"]')];
    expect(controls.every(control => control.max === "72")).toBe(true);
    expect(controls.every(control => control.value === "72")).toBe(true);
    const numeric = controls.find(control => control.type === "number");
    numeric.value = "300";
    numeric.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(sent.at(-1).payload).toEqual({ key: "lyricsLayoutScalePercent", value: 72 });
    expect(controls.every(control => Number(control.value) <= 72)).toBe(true);
  });

  it("selects a window presentation card and mirrors embedded taskbar bounds", async () => {
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
          useFloatingWindow: false,
          forceAlwaysOnTop: true,
          taskbarEmbeddingAvailable: true,
          taskbarMaxWidth: 420,
          taskbarMaxHeight: 48,
          taskbarMaxScalePercent: 100,
          taskbarMaxFontSize: 14,
          taskbarMaxCoverSize: 34,
          taskbarMaxCoverGap: 8,
          taskbarMaxWindowWidth: 420,
          taskbarMinXOffset: 0,
          taskbarMaxXOffset: 0,
          taskbarMinYOffset: 0,
          taskbarMaxYOffset: 0
        },
        fonts: []
      }
    });

    const embeddedMode = document.querySelector('[data-window-mode="embedded"]');
    const floatingMode = document.querySelector('[data-window-mode="floating"]');
    const topmostRow = document.querySelector("#floatingTopmostRow");
    const topmostControl = topmostRow.querySelector('[data-setting="floatingWindowLayer"][value="Fullscreen"]');
    const embeddedNote = document.querySelector(".embedded-mode-note");
    const widthRow = document.querySelector("#embeddedWindowWidthRow");
    const widthControls = [...widthRow.querySelectorAll('[data-setting="windowWidth"]')];
    expect(widthRow.closest(".embedded-window-mode-settings")).not.toBeNull();
    expect(widthRow.closest(".advanced-size-panel")).toBeNull();
    expect(widthControls).toHaveLength(2);
    expect(widthControls.every(control => !control.disabled)).toBe(true);
    const dependentControls = [...document.querySelectorAll('[data-depends="useFloatingWindow"] input, [data-depends="useFloatingWindow"] button')];
    expect(embeddedNote.closest(".display-presentation")).not.toBeNull();
    expect(embeddedNote.closest(".window-mode-choice")).toBeNull();
    expect(embeddedNote.querySelector("#taskbarEmbeddingStatus")).not.toBeNull();
    expect(topmostRow.closest(".floating-window-mode-settings")).not.toBeNull();
    expect(topmostControl.closest(".window-mode-choice")).toBeNull();
    expect(topmostRow.closest("[data-presentation-mode]").hidden).toBe(true);
    expect(topmostControl.checked).toBe(true);
    expect(embeddedMode.checked).toBe(true);
    expect(embeddedMode.tabIndex).toBe(0);
    expect(floatingMode.checked).toBe(false);
    expect(floatingMode.tabIndex).toBe(-1);
    expect(dependentControls.every(control => control.disabled)).toBe(true);

    floatingMode.checked = true;
    floatingMode.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

    expect(floatingMode.checked).toBe(true);
    expect(floatingMode.tabIndex).toBe(0);
    expect(topmostRow.closest("[data-presentation-mode]").hidden).toBe(false);
    expect(dependentControls.every(control => !control.disabled)).toBe(true);
    expect(widthControls.every(control => control.disabled)).toBe(true);
    expect(widthRow.classList.contains("is-disabled")).toBe(true);
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "useFloatingWindow", value: true }
    });

    const windowLayer = topmostRow.querySelector('[value="Window"]');
    windowLayer.checked = true;
    const messagesBeforeDisabledWidth = sent.length;
    widthControls.forEach(control => {
      control.value = "350";
      control.dispatchEvent(new dom.window.Event("input", { bubbles: true }));
      control.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    });
    expect(sent).toHaveLength(messagesBeforeDisabledWidth);
    windowLayer.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(floatingMode.checked).toBe(true);
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "floatingWindowLayer", value: "Window" }
    });

    floatingMode.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Home", bubbles: true }));

    expect(document.activeElement).toBe(embeddedMode);
    expect(embeddedMode.checked).toBe(true);
    expect(embeddedMode.tabIndex).toBe(0);
    expect(topmostRow.closest("[data-presentation-mode]").hidden).toBe(true);
    expect(topmostControl.disabled).toBe(true);
    expect(topmostControl.checked).toBe(false);
    expect(dependentControls.every(control => control.disabled)).toBe(true);
    expect(widthControls.every(control => !control.disabled)).toBe(true);
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "useFloatingWindow", value: false }
    });

    const widthInput = document.querySelector('input[type="number"][data-setting="windowWidth"]');
    widthInput.value = "5000";
    widthInput.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

    expect(widthInput.value).toBe("420");
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "windowWidth", value: 420 }
    });

    const widthSlider = document.querySelector('input[type="range"][data-setting="windowWidth"]');
    expect(widthInput.min).toBe("100");
    expect(widthSlider.min).toBe("100");
    widthInput.value = "100";
    widthInput.dispatchEvent(new dom.window.Event("change", { bubbles: true }));
    expect(widthSlider.value).toBe("100");
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "windowWidth", value: 100 }
    });
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

  it("keeps the host-resolved color when the foreground follows the system", async () => {
    const { dom, script } = await createSettingsDom();
    const document = dom.window.document;
    const createSettings = (foregroundColorMode, foregroundColor) => ({
      sourceRecognitionOrder: [],
      playerLyricOffsets: {},
      defaultPlayerLyricOffsets: {},
      mediaHotkeys: [],
      mediaHotkeyStatuses: {},
      foregroundColorMode,
      foregroundColor,
      toolWindowTheme: "System"
    });
    dom.window.eval(script);

    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: createSettings("Custom", "#FF336699"), fonts: [] }
    });
    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: createSettings("System", "#FF111827"), fonts: [] }
    });

    expect(document.querySelector('[data-setting="foregroundColorMode"] .select-trigger-value').textContent).toBe("跟随系统");
    expect(document.querySelector("[data-mode-value]").textContent).toBe("#111827");
    expect(document.querySelector("[data-custom-color]").hidden).toBe(true);
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

  it("normalizes invalid lyrics text alignment to left and posts only supported values", async () => {
    const { dom, sent, script } = await createSettingsDom();
    const document = dom.window.document;
    dom.window.eval(script);
    const initialSentCount = sent.length;

    dom.window.settingsApp.receive({
      version: 1,
      type: "settingsState",
      payload: { settings: { lyricsTextAlignment: "Unsupported" }, fonts: [] }
    });

    const left = document.querySelector('input[data-setting="lyricsTextAlignment"][value="Left"]');
    expect(left.checked).toBe(true);
    expect(left.closest('[data-page]').dataset.page).toBe("displayArea");
    expect(document.querySelector('.select-trigger[data-setting="lyricsTextAlignment"]')).toBeNull();
    expect(sent).toHaveLength(initialSentCount);

    document.querySelector('input[data-setting="lyricsTextAlignment"][value="Right"]').click();

    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "lyricsTextAlignment", value: "Right" }
    });
  });
  it("combines cover visibility and position while preserving alignment and the hidden position", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    const document = dom.window.document;
    const receive = settings => dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings, fonts: [] } });
    receive({ showCover: true, coverPosition: "Unsupported", lyricsTextAlignment: "Center" });
    const cover = value => document.querySelector(`input[data-cover-display][value="${value}"]`);
    const alignment = value => document.querySelector(`input[data-setting="lyricsTextAlignment"][value="${value}"]`);
    expect(cover("Left").checked).toBe(true);
    expect(alignment("Center").checked).toBe(true);
    cover("Right").closest("label").click();
    expect(sent.at(-1)).toEqual({ version: 1, type: "update", payload: { key: "coverPosition", value: "Right" } });
    cover("Hidden").click();
    expect(cover("Hidden").checked).toBe(true);
    expect(cover("Right").disabled).toBe(false);
    expect(sent.at(-1).payload).toEqual({ key: "showCover", value: false });
    alignment("Right").click();
    expect(sent.at(-1).payload).toEqual({ key: "lyricsTextAlignment", value: "Right" });
    const count = sent.length;
    cover("Right").click();
    expect(sent.slice(count).map(message => message.payload)).toEqual([{ key: "showCover", value: true }]);
    cover("Hidden").click();
    const hiddenCount = sent.length;
    cover("Left").click();
    expect(sent.slice(hiddenCount).map(message => message.payload)).toEqual([
      { key: "coverPosition", value: "Left" }, { key: "showCover", value: true }
    ]);
    expect(cover("Left").checked).toBe(true);
    expect(alignment("Right").checked).toBe(true);
    const finalCount = sent.length;
    receive({ showCover: false, coverPosition: "Right", lyricsTextAlignment: "Center" });
    expect(cover("Hidden").checked).toBe(true);
    receive({ showCover: true, coverPosition: "Left", lyricsTextAlignment: "Center" });
    expect(cover("Left").checked).toBe(true);
    expect(alignment("Center").checked).toBe(true);
    expect(sent).toHaveLength(finalCount);
    dom.window.close();
  });

  it("navigates layout radio cards with arrows and Home/End without duplicate updates", async () => {
    const { dom, sent, script } = await createSettingsDom();
    dom.window.eval(script);
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: { showCover: true, coverPosition: "Left", lyricsTextAlignment: "Left", horizontalAnchor: "Center" }, fonts: [] } });
    const document = dom.window.document;
    const coverLeft = document.querySelector('input[data-cover-display][value="Left"]');
    const coverRight = document.querySelector('input[data-cover-display][value="Right"]');
    const key = (input, value) => input.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: value, bubbles: true, cancelable: true }));
    key(coverLeft, "ArrowRight");
    expect(coverRight.checked).toBe(true);
    expect(document.activeElement).toBe(coverRight);
    const count = sent.length;
    key(coverRight, "End");
    expect(sent).toHaveLength(count);
    key(coverRight, "Home");
    const hidden = document.querySelector('input[data-cover-display][value="Hidden"]');
    expect(hidden.checked).toBe(true);
    expect(document.activeElement).toBe(hidden);
    const hiddenCount = sent.length;
    key(hidden, "Home");
    expect(sent).toHaveLength(hiddenCount);
    key(hidden, "ArrowRight");
    expect(coverLeft.checked).toBe(true);
    expect(document.activeElement).toBe(coverLeft);
    const alignmentLeft = document.querySelector('input[data-setting="lyricsTextAlignment"][value="Left"]');
    key(alignmentLeft, "End");
    expect(sent.at(-1).payload).toEqual({ key: "lyricsTextAlignment", value: "Right" });
    expect(coverLeft.checked).toBe(true);
    expect(document.querySelector('[data-setting="horizontalAnchor"]')).toBeNull();
    expect(sent.filter(message => message.payload?.key === "horizontalAnchor")).toHaveLength(0);
    expect(coverLeft.checked).toBe(true);
    expect(document.querySelector('input[data-setting="lyricsTextAlignment"][value="Right"]').checked).toBe(true);
    const finalCount = sent.length;
    dom.window.settingsApp.receive({ version: 1, type: "settingsState", payload: { settings: { showCover: true, coverPosition: "Left", lyricsTextAlignment: "Right", horizontalAnchor: "Center" }, fonts: [] } });
    expect(document.querySelector('[data-setting="horizontalAnchor"]')).toBeNull();
    expect(sent).toHaveLength(finalCount);
  });

});
