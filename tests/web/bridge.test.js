import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../..", import.meta.url);
const read = relativePath => readFile(new URL(relativePath, root), "utf8");
const settingsPages = ["sources", "shortcuts", "lyrics", "trackOffsets", "displayArea", "general", "advanced", "lyricDiagnostics", "about"];
const persistedSettings = [
  "enableLocalLyrics", "localMusicFolders", "enableGlobalMediaHotkeys", "showLyricsOnStartup", "autoHideWhenNoPlayback",
  "showLyricTranslation", "enableWordScanning", "spectrumDisplayMode", "lyricsLayoutScalePercent", "fontSize", "showCover",
  "coverSize", "coverGap", "coverCornerRadius", "fontFamily", "fontWeight", "lyricsTextAlignment", "foregroundColorMode",
  "showTextShadow", "toolWindowTheme", "showBackground", "backgroundOpacity", "showBorder", "windowWidth", "horizontalAnchor",
  "xOffset", "yOffset", "forceAlwaysOnTop", "startWithWindows", "autoCheckUpdates"
];

async function createSettingsDom({ deferAnimationFrames = false, reducedMotion = false } = {}) {
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
      .toEqual(["spotify", "qqmusic", "netease", "kugou", "browser"]);

    document.querySelector('[data-player-settings="browser"]').click();
    const drawerBody = document.querySelector("#playerDrawerBody");
    expect([...drawerBody.children].filter(element => element.classList.contains("player-setting-section")).map(element => element.id))
      .toEqual(["playerDiscoverySection", "playerRecognitionSection", "playerCardInfoSection", "playerAdvancedSettings", "playerTrustSection"]);
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
    expect(order()).toEqual(["netease", "qqmusic", "kugou", "spotify", "browser"]);
    expect(sent).toHaveLength(initialMessages);
    drag(grid, "drop");
    expect(sent.at(-1).type).toBe("reorderSources");
    expect(sent.at(-1).payload).toEqual(["Netease", "QQMusic", "Kugou", "Spotify", "Browser"]);
    expect(document.querySelector("#sourceOrderAnnouncement").textContent).toContain("第 2 位");

    const committedMessages = sent.length;
    const spotifyHandle = document.querySelector('[data-player-drag-id="spotify"]');
    drag(spotifyHandle, "dragstart");
    drag(document.querySelector('[data-player-card="netease"]'), "dragover");
    expect(order()).toEqual(["spotify", "netease", "qqmusic", "kugou", "browser"]);
    drag(spotifyHandle, "dragend");
    expect(order()).toEqual(["netease", "qqmusic", "kugou", "spotify", "browser"]);
    expect(sent).toHaveLength(committedMessages);

    document.querySelector('[data-player-drag-id="qqmusic"]')
      .dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "ArrowUp", altKey: true, bubbles: true }));
    expect(order()).toEqual(["qqmusic", "netease", "kugou", "spotify", "browser"]);
    expect(sent.at(-1).payload).toEqual(["QQMusic", "Netease", "Kugou", "Spotify", "Browser"]);
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

    const settingKeys = new Set([...document.querySelectorAll("[data-setting]")].map(control => control.dataset.setting));
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
    expect(document.querySelector("#displayMonitorHint").textContent).toBe("已连接 2 台显示器，歌词将在全部任务栏显示。");
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
    const dependentControls = [...document.querySelectorAll('[data-depends="useFloatingWindow"] input, [data-depends="useFloatingWindow"] button')];
    expect(embeddedMode.checked).toBe(true);
    expect(embeddedMode.tabIndex).toBe(0);
    expect(floatingMode.checked).toBe(false);
    expect(floatingMode.tabIndex).toBe(-1);
    expect(dependentControls.every(control => control.disabled)).toBe(true);

    floatingMode.checked = true;
    floatingMode.dispatchEvent(new dom.window.Event("change", { bubbles: true }));

    expect(floatingMode.checked).toBe(true);
    expect(floatingMode.tabIndex).toBe(0);
    expect(dependentControls.every(control => !control.disabled)).toBe(true);
    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "useFloatingWindow", value: true }
    });

    floatingMode.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Home", bubbles: true }));

    expect(document.activeElement).toBe(embeddedMode);
    expect(embeddedMode.checked).toBe(true);
    expect(embeddedMode.tabIndex).toBe(0);
    expect(dependentControls.every(control => control.disabled)).toBe(true);
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

    const trigger = document.querySelector('[data-setting="lyricsTextAlignment"]');
    expect(trigger.querySelector(".select-trigger-value").textContent).toBe("左对齐");
    expect(sent).toHaveLength(initialSentCount);

    trigger.dispatchEvent(new dom.window.MouseEvent("click", { bubbles: true }));
    document.querySelector('[data-option-index="2"]').dispatchEvent(new dom.window.MouseEvent("click", { bubbles: true }));

    expect(sent.at(-1)).toEqual({
      version: 1,
      type: "update",
      payload: { key: "lyricsTextAlignment", value: "Right" }
    });
  });
});
