import { readFile } from "node:fs/promises";
import { JSDOM } from "./helpers/localized-dom.js";
import { describe, expect, it } from "vitest";

const root = new URL("../../TaskbarLyrics.App/Web/Settings/", import.meta.url);
async function createDom() {
  const names = ["settings.html", "bridge.js", "hotkeys.js", "state.js", "display-presentation.js", "player-compatibility.js", "settings.js"];
  const [html, ...scripts] = await Promise.all(names.map(name => readFile(new URL(name, root), "utf8")));
  const dom = new JSDOM(html, { runScripts: "outside-only" });
  const stylesheet = dom.window.document.createElement("style");
  stylesheet.textContent = await readFile(new URL("settings.css", root), "utf8");
  dom.window.document.head.append(stylesheet);
  const markdownStyle = dom.window.document.createElement("style");
  markdownStyle.textContent = await readFile(new URL("github-markdown.css", root), "utf8");
  dom.window.document.head.append(markdownStyle);
  dom.window.sent = [];
  dom.window.chrome = { webview: { postMessage(message) { dom.window.sent.push(JSON.parse(message)); } } };
  dom.window.matchMedia = () => ({ matches: false, addEventListener() {}, removeEventListener() {} });
  dom.window.requestAnimationFrame = callback => { callback(0); return 0; };
  dom.window.HTMLMediaElement.prototype.load = () => {};
  dom.window.HTMLMediaElement.prototype.pause = () => {};
  dom.window.HTMLElement.prototype.scrollIntoView = () => {};
  scripts.forEach(script => dom.window.eval(script));
  return dom;
}

describe("update availability indicator", () => {
  it("retains availability across checking, failures, reading About and navigation collapse", async () => {
    const dom = await createDom();
    try {
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const about = dom.window.document.querySelector('[data-nav="about"]');
      receive({ state: "available", hasUpdate: true, version: "v2.0.0", sequence: 10 });
      receive({ state: "latest", hasUpdate: false, sequence: 9 });
      expect(about.classList.contains("has-update")).toBe(true);
      expect(about.getAttribute("aria-label")).toContain("更新");
      about.click();
      dom.window.document.documentElement.classList.add("sidebar-collapsed");
      receive({ state: "checking" });
      receive({ state: "error" });
      expect(about.classList.contains("has-update")).toBe(true);
      receive({ state: "latest", hasUpdate: false });
      expect(about.classList.contains("has-update")).toBe(false);
      expect(about.getAttribute("aria-label")).toBe("关于");
    } finally { dom.window.close(); }
  });
});

describe("update actions and release notes", () => {
  it("reinstalls the current version from the settings action row", async () => {
    const dom = await createDom();
    try {
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const document = dom.window.document;
      receive({ state: "latest", hasUpdate: false, canReinstall: true });
      const reinstall = document.querySelector("#updateReinstallButton");
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("当前已是最新版本");
      expect(reinstall.hidden).toBe(false);
      expect(reinstall.disabled).toBe(false);
      expect(reinstall.textContent).toBe("强制重装");
      expect(document.querySelector("#updateDirectButton").hidden).toBe(true);
      expect(document.querySelector("#checkUpdateButton").disabled).toBe(false);
      reinstall.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "reinstallUpdate", payload: {} });
    } finally { dom.window.close(); }
  });

  it("reports download, verification and restart states in the inline operation row", async () => {
    const dom = await createDom();
    try {
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const document = dom.window.document;
      receive({ state: "downloading", operationKind: "reinstall", progressPercent: 50, downloadSizeBytes: 112197632, canCancel: true });
      const operation = document.querySelector("#updateInlineOperation");
      expect(operation.hidden).toBe(false);
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("正在下载更新");
      expect(document.querySelector("#updateInlineStatus").textContent).toBe("正在下载更新");
      expect(document.querySelector("#updateInlineProgress").value).toBe(50);
      expect(document.querySelector("#updateInlineSize").hidden).toBe(false);
      expect(document.querySelector("#updateInlineSize").textContent).toContain("MB");
      const cancel = document.querySelector("#updateInlineCancel");
      expect(cancel.hidden).toBe(false);
      expect(document.querySelector("#updateReinstallButton").disabled).toBe(true);
      expect(document.querySelector("#checkUpdateButton").disabled).toBe(true);
      cancel.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "cancelUpdateDownload", payload: {} });

      receive({ state: "verifying", operationKind: "reinstall" });
      expect(document.querySelector("#updateInlineStatus").textContent).toBe("正在验证更新包");
      expect(document.querySelector("#updateInlineProgress").hasAttribute("value")).toBe(false);

      receive({ state: "readyToRestart", operationKind: "reinstall", canApply: true });
      expect(document.querySelector("#updateInlineStatus").textContent).toBe("验证完成，正在自动重启并应用更新");
      expect(document.querySelector("#updateInlineSize").hidden).toBe(true);
      expect(document.querySelector("#updateInlineCancel").hidden).toBe(true);
      expect(document.querySelector("#updateReinstallButton").disabled).toBe(true);
      expect(document.querySelector("#checkUpdateButton").disabled).toBe(true);
      // Verification completes into an automatic restart; the page never posts applyUpdate itself.
      expect(dom.window.sent.some(message => message.type === "applyUpdate")).toBe(false);
    } finally { dom.window.close(); }
  });

  it("keeps the reinstall action available after a canceled or failed download", async () => {
    const dom = await createDom();
    try {
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const document = dom.window.document;
      const reinstall = document.querySelector("#updateReinstallButton");
      const check = document.querySelector("#checkUpdateButton");
      receive({ state: "canceled", hasUpdate: false, canReinstall: true });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("已取消下载");
      expect(reinstall.disabled).toBe(false);
      expect(check.disabled).toBe(false);
      expect(check.textContent).toBe("检查更新");
      receive({ state: "error", hasUpdate: false, canReinstall: true, reasonCode: "DownloadFailed" });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("检查更新失败");
      expect(reinstall.disabled).toBe(false);
      expect(reinstall.hidden).toBe(false);
      expect(document.querySelector("#updateReason").hidden).toBe(false);
      expect(document.querySelector("#updateReason").textContent).toContain("下载或校验失败");
      reinstall.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "reinstallUpdate", payload: {} });
    } finally { dom.window.close(); }
  });

  it("preserves formatting while removing executable content and unsafe links", async () => {
    const dom = await createDom();
    try {
      dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload: {
        state: "available", hasUpdate: true, canDownload: true, notesComplete: false,
        releaseNotesHtml: '<h2 onclick="evil()">2.0</h2><ul><li><strong>Fix</strong></li></ul><script>evil()</script><img src="https://evil.test/pixel"><iframe src="https://evil.test"></iframe><a href="javascript:evil()">bad</a><a href="https://github.com/ANYNC/TaskbarLyrics/releases/latest" onclick="evil()">release</a><table><tr><td>value</td></tr></table>'
      } });
      const document = dom.window.document;
      const notes = document.querySelector("#releaseNotesContent");
      expect(notes.querySelector("strong").textContent).toBe("Fix");
      expect(notes.querySelector("td").textContent).toBe("value");
      expect(notes.querySelector("script,iframe,[onclick]")).toBeNull();
      expect(notes.querySelector("img").getAttribute("src")).toBe("https://evil.test/pixel");
      expect(notes.querySelector('a[href^="javascript:"]')).toBeNull();
      expect(document.querySelector("#releaseNotesIncomplete").hidden).toBe(false);
      notes.querySelector("a[data-release-url]").click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "openExternalLink", payload: "https://github.com/ANYNC/TaskbarLyrics/releases/latest" });
      expect(document.querySelector('[data-nav="about"]').classList.contains("has-update")).toBe(true);
    } finally { dom.window.close(); }
  });
});
describe("release update presentation", () => {
  it("shows the current-version row without duplicate detail, size or technical messaging", async () => {
    const dom = await createDom();
    try {
      const document = dom.window.document;
      dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload: {
        state: "latest", version: "1.7.6", hasUpdate: false, canReinstall: true,
        message: "当前已是最新版本：1.7.6。", downloadSizeBytes: 112197632,
        url: "https://github.com/ANYNC/TaskbarLyrics/releases/latest"
      } });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("当前已是最新版本");
      expect(document.querySelector("#updateStatusDetail")).toBeNull();
      expect(document.querySelector("#updateInlineOperation").hidden).toBe(true);
      expect(document.querySelector("#updateDialogStatusDetail").textContent).toBe("");
      expect(document.querySelector("#updateDialogStatusDetail").hidden).toBe(true);
      expect(document.querySelector("#updateSize").hidden).toBe(true);
      expect(document.querySelector("#updateInlineSize").hidden).toBe(true);
      expect(document.querySelector(".update-extra").hidden).toBe(true);
      expect(document.querySelector("#updateDirectButton").hidden).toBe(true);
      expect(document.querySelector("#updateReinstallButton").hidden).toBe(false);
      expect(document.querySelector("#updateReinstallButton").disabled).toBe(false);
      expect(document.querySelector("#checkUpdateButton").disabled).toBe(false);
    } finally { dom.window.close(); }
  });
  it("keeps the external action set stable across checking, available, failed and latest states", async () => {
    const dom = await createDom();
    try {
      const document = dom.window.document;
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const notes = document.querySelector("#updateNotesButton");
      const direct = document.querySelector("#updateDirectButton");
      const reinstall = document.querySelector("#updateReinstallButton");
      const check = document.querySelector("#checkUpdateButton");
      expect([notes.hidden, direct.hidden, reinstall.hidden, check.hidden]).toEqual([false, true, false, false]);
      expect([notes.disabled, direct.disabled, reinstall.disabled, check.disabled]).toEqual([true, true, false, false]);
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("尚未检查更新");

      receive({ state: "checking" });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("正在检查更新…");
      // 检查中的动作槽只置灰、不隐藏，避免状态切换时按钮闪烁。
      expect([notes.hidden, reinstall.hidden, check.hidden]).toEqual([false, false, false]);
      expect([notes.disabled, reinstall.disabled, check.disabled]).toEqual([true, true, true]);
      expect(document.querySelector("#updateInlineOperation").hidden).toBe(true);

      receive({ state: "available", hasUpdate: true, canDownload: true, version: "1.8.0" });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("已检测到更新");
      expect([notes.hidden, direct.hidden, reinstall.hidden, check.hidden]).toEqual([false, false, false, false]);
      expect([notes.disabled, direct.disabled, reinstall.disabled, check.disabled]).toEqual([true, false, false, false]);

      receive({ state: "error", hasUpdate: true, canDownload: true, reasonCode: "DownloadFailed" });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("检查更新失败");
      expect([notes.hidden, direct.hidden, reinstall.hidden, check.hidden]).toEqual([false, true, false, false]);
      // 直接更新 is hidden outside the available state, so only the visible actions are asserted.
      expect([notes.disabled, reinstall.disabled, check.disabled]).toEqual([true, false, false]);
      expect(document.querySelector("#updateReason").hidden).toBe(false);
      expect(document.querySelector("#updateReason").textContent).toBe("下载或校验失败，请重试或从发布页手动更新。");

      receive({
        state: "latest", hasUpdate: false, reasonCode: "Unsupported", downloadSizeBytes: 112197632,
        url: "https://github.com/ANYNC/TaskbarLyrics/releases/latest"
      });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("当前已是最新版本");
      expect([notes.hidden, direct.hidden, reinstall.hidden, check.hidden]).toEqual([false, true, false, false]);
      expect([notes.disabled, direct.disabled, reinstall.disabled, check.disabled]).toEqual([true, true, false, false]);
      expect(document.querySelector("#updateReason").hidden).toBe(true);
      expect(document.querySelector("#updateReason").textContent).toBe("");
      expect(document.querySelector(".update-extra").hidden).toBe(true);
      expect(document.querySelector("#updateReleaseButton").hidden).toBe(false);
    } finally { dom.window.close(); }
  });
});
describe("GitHub release-note images", () => {
  it("renders image-only notes, uses stable canonical sources, preserves dimensions and strips active attributes", async () => {
    const dom = await createDom();
    try {
      dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload: {
        state: "latest", releaseNotesHtml: '<img src="https://private-user-images.githubusercontent.com/example.png?jwt=expired" data-canonical-src="https://github.com/user-attachments/assets/example" alt="界面预览" width="1280" height="720" onerror="evil()" style="position:fixed" srcset="https://evil.test/2x 2x"><img src="javascript:evil()"><img src="file:///C:/secret"><img src="data:image/svg+xml,evil">', notesComplete: true
      } });
      const document = dom.window.document;
      const content = document.querySelector("#releaseNotesContent");
      const images = [...content.querySelectorAll("img")];
      expect(images).toHaveLength(1);
      expect(images[0].src).toBe("https://github.com/user-attachments/assets/example");
      expect(images[0].alt).toBe("界面预览");
      expect(images[0].getAttribute("width")).toBe("1280");
      expect(images[0].getAttribute("height")).toBe("720");
      expect(images[0].getAttribute("loading")).toBe("lazy");
      expect(images[0].getAttribute("referrerpolicy")).toBe("no-referrer");
      expect(images[0].hasAttribute("onerror")).toBe(false);
      expect(images[0].hasAttribute("style")).toBe(false);
      expect(images[0].hasAttribute("srcset")).toBe(false);
      expect(document.querySelector("#releaseNotesPanel").hidden).toBe(false);
      expect(content.classList.contains("markdown-body")).toBe(true);
      expect(dom.window.getComputedStyle(images[0]).maxWidth).toBe("100%");
      expect(dom.window.getComputedStyle(images[0]).height).toBe("auto");
    } finally { dom.window.close(); }
  });
});
it("uses the original attachment address for expired GitHub HTML image and link URLs", async () => {
  const dom = await createDom();
  try {
    const id = "a0eccbd5-4684-4120-8028-1b4831f7ce44";
    const original = `https://github.com/user-attachments/assets/${id}`;
    const signed = `https://private-user-images.githubusercontent.com/72554009/690299523-${id}.png?jwt=expired`;
    dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload: {
      state: "latest", hasUpdate: false, version: "1.7.6",
      releaseNotes: `![preview](${original})`,
      releaseNotesHtml: `<a href="${signed}"><img src="${signed}" alt="preview"></a>`
    } });
    expect(dom.window.document.querySelector("#releaseNotesContent img").src).toBe(original);
    expect(dom.window.document.querySelector("#releaseNotesContent a").href).toBe(original);
  } finally { dom.window.close(); }
});

it("keeps cumulative notes expanded in the update dialog and hides an empty panel", async () => {
  const dom = await createDom();
  try {
    const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
    const document = dom.window.document;
    const panel = document.querySelector("#releaseNotesPanel");
    const notesButton = document.querySelector("#updateNotesButton");
    expect(panel.tagName).toBe("SECTION");
    expect(panel.querySelector("details, summary")).toBeNull();
    expect(notesButton.disabled).toBe(true);
    receive({ state: "latest", version: "1.7.6", hasUpdate: false, releaseNotesHtml: "<p>notes</p>", notesComplete: false });
    expect(panel.hidden).toBe(false);
    expect(panel.hasAttribute("open")).toBe(false);
    expect(document.querySelector("#releaseNotesIncomplete").hidden).toBe(false);
    expect(document.querySelector("#releaseNotesContent").textContent).toBe("notes");
    expect(notesButton.disabled).toBe(false);
    receive({ state: "latest", version: "1.7.6", hasUpdate: false, releaseNotesHtml: "", notesComplete: true });
    expect(panel.hidden).toBe(true);
    expect(document.querySelector("#releaseNotesIncomplete").hidden).toBe(true);
    expect(notesButton.disabled).toBe(true);
  } finally { dom.window.close(); }
});
