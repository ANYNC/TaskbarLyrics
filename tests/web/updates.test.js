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
  it("shows the update notice without taking focus and follows the confirmed dismissal snapshot", async () => {
    const dom = await createDom();
    try {
      const document = dom.window.document;
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const available = { state: "available", hasUpdate: true, canDownload: true, version: "v2.0.0" };
      const notice = document.querySelector("#updateNotice");
      const dismiss = document.querySelector("#updateNoticeDismiss");
      const about = document.querySelector('[data-nav="about"]');
      about.focus();
      expect(notice.hidden).toBe(true);
      receive(available);
      expect(notice.hidden).toBe(false);
      expect(document.activeElement).toBe(about);
      expect(document.querySelector("#updateNoticeTitle").textContent).toContain("v2.0.0");
      expect(document.querySelector("#updateNoticePrimary").textContent).toBe("下载并重启");
      dismiss.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "dismissUpdateNotice", payload: "v2.0.0" });

      receive({ ...available, noticeDismissed: true });
      expect(notice.hidden).toBe(true);
      expect(about.classList.contains("has-update")).toBe(true);
      receive({ ...available, version: "v2.0.1" });
      expect(notice.hidden).toBe(false);
    } finally { dom.window.close(); }
  });

  it("handles the keyboard menu and hides ignored notices while keeping About downloads", async () => {
    const dom = await createDom();
    try {
      const document = dom.window.document;
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const available = { state: "available", hasUpdate: true, canDownload: true, version: "v2.0.0" };
      const more = document.querySelector("#updateNoticeMore");
      const ignore = document.querySelector("#updateNoticeIgnore");
      receive(available);

      more.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "ArrowDown", bubbles: true }));
      expect(more.getAttribute("aria-expanded")).toBe("true");
      expect(document.activeElement).toBe(ignore);
      ignore.dispatchEvent(new dom.window.KeyboardEvent("keydown", { key: "Escape", bubbles: true }));
      expect(more.getAttribute("aria-expanded")).toBe("false");
      expect(document.activeElement).toBe(more);
      more.click();
      ignore.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "ignoreUpdateVersion", payload: "v2.0.0" });

      receive({ ...available, noticeIgnored: true });
      expect(document.querySelector("#updateNotice").hidden).toBe(true);
      expect(document.querySelector('[data-nav="about"]').classList.contains("has-update")).toBe(false);
      expect(document.querySelector("#updateDirectButton").disabled).toBe(false);
      receive({ ...available, version: "v2.0.1" });
      expect(document.querySelector("#updateNotice").hidden).toBe(false);
    } finally { dom.window.close(); }
  });

  it("opens the release page when needed and starts a version-bound update from About otherwise", async () => {
    const dom = await createDom();
    try {
      const document = dom.window.document;
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const url = "https://github.com/ANYNC/TaskbarLyrics/releases/tag/v2.0.0";
      const available = { state: "available", hasUpdate: true, version: "v2.0.0", url };
      const action = document.querySelector("#updateNoticePrimary");
      receive(available);
      expect(action.textContent).toBe("打开发布页");
      expect(document.querySelector("#updateNoticeRestartHint").hidden).toBe(true);
      action.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "openExternalLink", payload: url });

      receive({ ...available, canDownload: true });
      action.click();
      expect(document.querySelector('[data-nav="about"]').getAttribute("aria-current")).toBe("page");
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "downloadUpdate", payload: "v2.0.0" });
      receive({ ...available, state: "downloading" });
      expect(document.querySelector("#updateNotice").hidden).toBe(true);
      expect(document.querySelector("#updateInlineOperation").hidden).toBe(false);
    } finally { dom.window.close(); }
  });

  it("offers no same-version download even with a legacy reinstall capability", async () => {
    const dom = await createDom();
    try {
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const document = dom.window.document;
      receive({ state: "latest", hasUpdate: false, canReinstall: true });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("当前已是最新版本");
      expect(document.querySelector("#updateReinstallButton")).toBeNull();
      expect(document.querySelector("#updateDirectButton").hidden).toBe(true);
      expect(document.querySelector("#updateActionButton").hidden).toBe(true);
      expect(document.querySelector("#checkUpdateButton").disabled).toBe(false);
    } finally { dom.window.close(); }
  });

  it("reports download, verification and restart states in the inline operation row without duplicate status text", async () => {
    const dom = await createDom();
    try {
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const document = dom.window.document;
      receive({ state: "downloading", progressPercent: 50, downloadSizeBytes: 112197632, canCancel: true });
      const operation = document.querySelector("#updateInlineOperation");
      expect(operation.hidden).toBe(false);
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("正在下载更新");
      expect(document.querySelector("#updateInlineStatus")).toBeNull();
      expect(document.querySelector("#updateInlineProgress").value).toBe(50);
      expect(document.querySelector("#updateInlinePercent").hidden).toBe(false);
      expect(document.querySelector("#updateInlinePercent").textContent).toBe("50%");
      expect(document.querySelector("#updateInlineSize").hidden).toBe(false);
      expect(document.querySelector("#updateInlineSize").textContent).toContain("MB");
      const cancel = document.querySelector("#updateInlineCancel");
      expect(cancel.hidden).toBe(false);
      expect(document.querySelector("#checkUpdateButton").disabled).toBe(true);
      cancel.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "cancelUpdateDownload", payload: {} });

      receive({ state: "verifying" });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("正在验证更新包");
      expect(document.querySelector("#updateInlineProgress").hasAttribute("value")).toBe(false);
      expect(document.querySelector("#updateInlinePercent").hidden).toBe(true);

      receive({ state: "readyToRestart", canApply: true });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("验证完成，正在自动重启并应用更新");
      expect(document.querySelector("#updateInlinePercent").hidden).toBe(true);
      expect(document.querySelector("#updateInlineSize").hidden).toBe(true);
      expect(document.querySelector("#updateInlineCancel").hidden).toBe(true);
      expect(document.querySelector("#checkUpdateButton").disabled).toBe(true);
      // Verification completes into an automatic restart; the page never posts applyUpdate itself.
      expect(dom.window.sent.some(message => message.type === "applyUpdate")).toBe(false);
    } finally { dom.window.close(); }
  });

  it("offers inline and dialog retry plus manual fallback after a canceled or failed download", async () => {
    const dom = await createDom();
    try {
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const document = dom.window.document;
      const retry = document.querySelector("#updateActionButton");
      const inlineRetry = document.querySelector("#updateDirectButton");
      const inlineManual = document.querySelector("#updateManualButton");
      const check = document.querySelector("#checkUpdateButton");
      receive({ state: "canceled", hasUpdate: true, canDownload: true });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("已取消下载");
      expect(retry.disabled).toBe(false);
      expect(inlineRetry.hidden).toBe(false);
      expect(inlineRetry.textContent).toBe("重试");
      expect(inlineManual.hidden).toBe(false);
      expect(inlineManual.classList.contains("ghost")).toBe(true);
      expect(check.disabled).toBe(false);
      expect(check.classList.contains("secondary")).toBe(true);
      expect(check.textContent).toBe("检查更新");

      receive({ state: "error", hasUpdate: true, canDownload: true, reasonCode: "DownloadFailed" });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("更新失败");
      expect(document.querySelector("#updateStatusDetail").hidden).toBe(false);
      expect(document.querySelector("#updateStatusDetail").textContent).toContain("下载或校验失败");
      expect(inlineRetry.hidden).toBe(false);
      expect(inlineRetry.textContent).toBe("重试");
      expect(retry.disabled).toBe(false);
      expect(retry.hidden).toBe(false);
      expect(document.querySelector("#updateReason").hidden).toBe(false);
      expect(document.querySelector("#updateReason").textContent).toContain("下载或校验失败");
      inlineRetry.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "downloadUpdate", payload: {} });
      retry.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "downloadUpdate", payload: {} });
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
        state: "latest", version: "1.7.6", hasUpdate: false,
        message: "当前已是最新版本：1.7.6。", downloadSizeBytes: 112197632,
        url: "https://github.com/ANYNC/TaskbarLyrics/releases/latest"
      } });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("当前已是最新版本");
      expect(document.querySelector("#updateStatusDetail").hidden).toBe(true);
      expect(document.querySelector("#updateStatusDetail").textContent).toBe("");
      expect(document.querySelector("#updateInlineOperation").hidden).toBe(true);
      expect(document.querySelector("#updateDialogStatusDetail").textContent).toBe("");
      expect(document.querySelector("#updateDialogStatusDetail").hidden).toBe(true);
      expect(document.querySelector("#updateSize").hidden).toBe(true);
      expect(document.querySelector("#updateInlineSize").hidden).toBe(true);
      expect(document.querySelector(".update-extra").hidden).toBe(true);
      expect(document.querySelector("#updateDirectButton").hidden).toBe(true);
      expect(document.querySelector("#updateManualButton").hidden).toBe(true);
      expect(document.querySelector("#checkUpdateButton").disabled).toBe(false);
      expect(document.querySelector("#checkUpdateButton").classList.contains("secondary")).toBe(false);
    } finally { dom.window.close(); }
  });
  it("keeps a single primary action across checking, available, manual-only, failed and latest states", async () => {
    const dom = await createDom();
    try {
      const document = dom.window.document;
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const notes = document.querySelector("#updateNotesButton");
      const direct = document.querySelector("#updateDirectButton");
      const manual = document.querySelector("#updateManualButton");
      const check = document.querySelector("#checkUpdateButton");
      expect([notes.hidden, manual.hidden, direct.hidden, check.hidden]).toEqual([false, true, true, false]);
      expect([notes.disabled, direct.disabled, check.disabled]).toEqual([true, true, false]);
      expect(check.classList.contains("secondary")).toBe(false);
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("尚未检查更新");

      receive({ state: "checking" });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("正在检查更新…");
      // 检查中的动作槽只置灰、不隐藏，避免状态切换时按钮闪烁。
      expect([notes.hidden, check.hidden]).toEqual([false, false]);
      expect([notes.disabled, check.disabled]).toEqual([true, true]);
      expect(document.querySelector("#updateInlineOperation").hidden).toBe(true);

      receive({ state: "available", hasUpdate: true, canDownload: true, version: "1.8.0" });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("已检测到更新");
      expect(document.querySelector("#updateStatusDetail").hidden).toBe(true);
      expect([notes.hidden, manual.hidden, direct.hidden, check.hidden]).toEqual([false, true, false, false]);
      expect([notes.disabled, direct.disabled, check.disabled]).toEqual([true, false, false]);
      expect(direct.textContent).toBe("直接更新");
      expect(check.classList.contains("secondary")).toBe(true);

      const releaseUrl = "https://github.com/ANYNC/TaskbarLyrics/releases/tag/v1.8.0";
      receive({ state: "available", hasUpdate: true, canDownload: false, reasonCode: "Unsupported", version: "1.8.0", url: releaseUrl });
      expect(document.querySelector("#updateStatusDetail").hidden).toBe(false);
      expect(document.querySelector("#updateStatusDetail").textContent).toContain("手动更新");
      expect([manual.hidden, direct.hidden]).toEqual([false, true]);
      expect(manual.classList.contains("ghost")).toBe(false);
      expect(check.classList.contains("secondary")).toBe(true);
      manual.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "openExternalLink", payload: releaseUrl });

      receive({ state: "error", hasUpdate: true, canDownload: true, reasonCode: "DownloadFailed" });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("更新失败");
      expect(document.querySelector("#updateStatusDetail").hidden).toBe(false);
      expect(document.querySelector("#updateStatusDetail").textContent).toBe("下载或校验失败，请重试或从发布页手动更新。");
      expect([notes.hidden, manual.hidden, direct.hidden, check.hidden]).toEqual([false, false, false, false]);
      expect(manual.classList.contains("ghost")).toBe(true);
      expect(direct.textContent).toBe("重试");
      expect(check.classList.contains("secondary")).toBe(true);
      expect(document.querySelector("#updateReason").hidden).toBe(false);
      expect(document.querySelector("#updateReason").textContent).toBe("下载或校验失败，请重试或从发布页手动更新。");

      receive({
        state: "latest", hasUpdate: false, reasonCode: "Unsupported", downloadSizeBytes: 112197632,
        url: "https://github.com/ANYNC/TaskbarLyrics/releases/latest"
      });
      expect(document.querySelector("#updateStatusTitle").textContent).toBe("当前已是最新版本");
      expect(document.querySelector("#updateStatusDetail").hidden).toBe(true);
      expect([notes.hidden, manual.hidden, direct.hidden, check.hidden]).toEqual([false, true, true, false]);
      expect([notes.disabled, direct.disabled, check.disabled]).toEqual([true, true, false]);
      expect(check.classList.contains("secondary")).toBe(false);
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

it("unifies the About page hierarchy, labels release notes, and restores focus when direct update starts", async () => {
  const dom = await createDom();
  try {
    const document = dom.window.document;
    const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
    const aboutPage = document.querySelector('[data-page="about"]');
    expect(aboutPage.querySelector(".page-inner.about-layout")).not.toBeNull();
    expect(aboutPage.querySelectorAll("#repositoryButton, [data-repository-link]")).toHaveLength(1);
    expect(aboutPage.querySelector(".about-meta-grid").tagName).toBe("DL");
    const notesPanel = document.querySelector("#releaseNotesPanel");
    const headingId = notesPanel.getAttribute("aria-labelledby");
    expect(headingId).toBe("releaseNotesHeading");
    expect(document.getElementById(headingId)?.textContent.trim()).not.toBe("");

    receive({ state: "available", hasUpdate: true, canDownload: true, version: "2.0.0" });
    const direct = document.querySelector("#updateDirectButton");
    direct.focus();
    expect(document.activeElement).toBe(direct);
    receive({ state: "downloading", progressPercent: 12, downloadSizeBytes: 104857600, canCancel: true });
    expect(document.activeElement).toBe(document.querySelector("#updateInlineCancel"));
  } finally { dom.window.close(); }
});
