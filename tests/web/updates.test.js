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
  it("offers same-version reinstall and an explicit restart after verification", async () => {
    const dom = await createDom();
    try {
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const document = dom.window.document;
      receive({ state: "latest", hasUpdate: false, canReinstall: true });
      const action = document.querySelector("#updateActionButton");
      expect(action.disabled).toBe(false);
      expect(action.textContent).toBe("强制重装");
      action.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "reinstallUpdate", payload: {} });
      receive({ state: "downloading", operationKind: "reinstall", progressPercent: 50 });
      expect(document.querySelector("#updateProgress").value).toBe(50);
      expect(action.textContent).toBe("取消");
      expect(action.disabled).toBe(false);
      expect(document.querySelector("#checkUpdateButton").disabled).toBe(true);
      receive({ state: "verifying", operationKind: "reinstall", progressPercent: NaN });
      expect(document.querySelector("#updateProgress").hasAttribute("value")).toBe(false);
      receive({ state: "readyToRestart", operationKind: "reinstall", canApply: true });
      expect(action.disabled).toBe(false);
      expect(action.textContent).toBe("重启并重装");
      action.click();
      expect(dom.window.sent.at(-1)).toEqual({ version: 1, type: "applyUpdate", payload: {} });
      expect(document.querySelector('[data-nav="about"]').classList.contains("has-update")).toBe(false);
    } finally { dom.window.close(); }
  });
  it("keeps the reinstall retry available after cancel or failure", async () => {
    const dom = await createDom();
    try {
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const action = dom.window.document.querySelector("#updateActionButton");
      receive({ state: "latest", hasUpdate: false, canReinstall: true });
      receive({ state: "canceled", hasUpdate: false, canReinstall: true });
      expect(action.disabled).toBe(false);
      expect(action.textContent).toBe("重试");
      receive({ state: "error", hasUpdate: false, canReinstall: true, reasonCode: "DownloadFailed", url: "https://github.com/ANYNC/TaskbarLyrics/releases/latest" });
      expect(action.disabled).toBe(false);
      expect(action.textContent).toBe("手动下载");
      const check = dom.window.document.querySelector("#checkUpdateButton");
      expect(check.textContent).toBe("重试");
      check.click();
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
  it("keeps two action slots when current, without duplicate status, size or developer messaging", async () => {
    const dom = await createDom();
    try {
      const document = dom.window.document;
      dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload: {
        state: "latest", version: "1.7.6", hasUpdate: false, canReinstall: true,
        message: "当前已是最新版本：1.7.6。", downloadSizeBytes: 112197632,
        url: "https://github.com/ANYNC/TaskbarLyrics/releases/latest"
      } });
      const shown = [...document.querySelectorAll(".update-actions button")].filter(button => dom.window.getComputedStyle(button).display !== "none");
      expect(shown.map(button => button.id)).toEqual(["checkUpdateButton", "updateActionButton"]);
      expect(document.querySelector("#updateStatusDetail").textContent).toBe("");
      expect(dom.window.getComputedStyle(document.querySelector(".update-extra")).display).toBe("none");
      expect(document.querySelector("#updateActionButton").disabled).toBe(false);
    } finally { dom.window.close(); }
  });
  it("keeps two action slots across checking, available, failed and latest states", async () => {
    const dom = await createDom();
    try {
      const document = dom.window.document;
      const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
      const visibleActions = () => [...document.querySelectorAll(".update-actions button")].filter(button => dom.window.getComputedStyle(button).display !== "none");
      expect(visibleActions()).toHaveLength(2);
      receive({ state: "checking" });
      expect(visibleActions()).toHaveLength(2);
      receive({ state: "available", hasUpdate: true, canDownload: true, url: "https://github.com/ANYNC/TaskbarLyrics/releases/latest" });
      expect(visibleActions()).toHaveLength(2);
      expect(document.querySelector("#updateActionButton").textContent).toBe("更新");
      receive({ state: "error", hasUpdate: true, canDownload: true, reasonCode: "DownloadFailed", url: "https://github.com/ANYNC/TaskbarLyrics/releases/latest" });
      expect(visibleActions()).toHaveLength(2);
      expect(document.querySelector("#checkUpdateButton").textContent).toBe("重试");
      expect(document.querySelector("#updateActionButton").textContent).toBe("手动下载");
      expect(document.querySelector("#updateReason").hidden).toBe(false);
      receive({ state: "latest", hasUpdate: false, reasonCode: "Unsupported", downloadSizeBytes: 112197632, url: "https://github.com/ANYNC/TaskbarLyrics/releases/latest" });
      expect(visibleActions().map(button => button.id)).toEqual(["checkUpdateButton", "updateActionButton"]);
      expect(document.querySelector("#updateActionButton").disabled).toBe(false);
      expect(document.querySelector("#updateActionButton").textContent).toBe("打开发布页");
      expect(document.querySelector("#updateReason").textContent).toBe("");
      expect(document.querySelector(".update-extra").hidden).toBe(true);
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

it("defaults notes to collapsed when current and expanded for a new version, preserving manual toggles for that version", async () => {
  const dom = await createDom();
  try {
    const receive = payload => dom.window.settingsApp.receive({ version: 1, type: "updateStatus", payload });
    const panel = dom.window.document.querySelector("#releaseNotesPanel");
    receive({ state: "latest", version: "1.7.6", hasUpdate: false, releaseNotesHtml: "<p>notes</p>" });
    expect(panel.tagName).toBe("DETAILS");
    expect(panel.open).toBe(false);
    panel.open = true;
    receive({ state: "latest", version: "1.7.6", hasUpdate: false });
    expect(panel.open).toBe(true);
    receive({ state: "available", version: "1.8.0", hasUpdate: true });
    expect(panel.open).toBe(true);
    panel.open = false;
    receive({ state: "downloading", version: "1.8.0", hasUpdate: true, progressPercent: 20 });
    expect(panel.open).toBe(false);
    receive({ state: "available", version: "1.9.0", hasUpdate: true });
    expect(panel.open).toBe(true);
    receive({ state: "latest", version: "1.9.0", hasUpdate: false });
    expect(panel.open).toBe(false);
  } finally { dom.window.close(); }
});
