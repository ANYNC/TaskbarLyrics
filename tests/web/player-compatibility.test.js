import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";
import { describe, expect, it } from "vitest";

const root = new URL("../../TaskbarLyrics.App/Web/Settings/", import.meta.url);

async function createCompatibility() {
  const script = await readFile(new URL("player-compatibility.js", root), "utf8");
  const dom = new JSDOM('<section id="compatibility" hidden></section>', { runScripts: "outside-only" });
  dom.window.eval(script);
  return { dom, container: dom.window.document.querySelector("#compatibility"), render: dom.window.taskbarLyricsPlayerCompatibility.render };
}

describe("shared player compatibility", () => {
  it("explains onboarding setup requirements while preserving compact drawer metrics", async () => {
    const { dom, container, render } = await createCompatibility();
    render(container, "netease", undefined, true);
    expect(container.querySelector("h2").textContent).toBe("网易云音乐");
    expect(container.querySelector("img").getAttribute("src")).toBe("../../Assets/PlayerIcons/网易云音乐.png");
    expect(container.querySelector("[data-compatibility-link]")).toBeNull();
    expect([...container.querySelectorAll("dt")].map(item => item.textContent)).toEqual(["歌曲识别", "歌词同步", "使用准备"]);
    expect([...container.querySelectorAll("dd")].map(item => item.textContent)).toEqual(["支持", "支持，需配置插件", "安装 inflink-rs"]);
    expect(container.querySelector(".compatibility-reference-note").textContent).toBe("兼容性参考，非实时检测结果。");
    const instructions = container.querySelector("ol").outerHTML;
    render(container, "netease");
    expect(container.querySelector("h2, img")).toBeNull();
    expect(container.querySelector("[data-compatibility-link]")).not.toBeNull();
    expect([...container.querySelectorAll("dt")].map(item => item.textContent)).toEqual(["歌曲信息", "时间轴信息", "使用准备"]);
    expect([...container.querySelectorAll("dd")].map(item => item.textContent)).toEqual(["支持", "支持", "inflink-rs"]);
    expect(container.querySelector(".compatibility-reference-note")).toBeNull();
    expect(container.querySelector("ol").outerHTML).toBe(instructions);
    dom.window.close();
  });

  it.each([
    ["qqmusic", "supported", ["支持", "支持", "无需配置"]],
    ["spotify", "supported", ["支持", "支持", "无需配置"]],
    ["applemusic", "supported", ["支持", "不精准", "无需配置"]],
    ["sodamusic", "supported", ["支持", "支持", "无需配置"]],
    ["foobar2000", "setup", ["支持", "支持", "foo_mediacontrol"]],
    ["netease", "setup", ["支持", "支持", "inflink-rs"]],
    ["kugou", "limited", ["支持", "不支持", "无需配置"]],
    ["browser", "conditional", ["视网站而定", "视网站而定", "系统媒体会话"]],
    ["other", "unknown", ["待确认", "待确认", "系统媒体会话"]]
  ])("renders qualitative reference metrics for %s", async (player, level, values) => {
    const { dom, container, render } = await createCompatibility();
    render(container, player);
    expect(container.hidden).toBe(false);
    expect(container.querySelector(".player-compatibility").dataset.supportLevel).toBe(level);
    expect([...container.querySelectorAll("dt")].map(item => item.textContent)).toEqual(["歌曲信息", "时间轴信息", "使用准备"]);
    expect([...container.querySelectorAll("dd")].map(item => item.textContent)).toEqual(values);
    expect(container.getAttribute("aria-label")).toContain("非实时检测");
    expect(container.textContent).not.toContain("%");
    expect(container.textContent).not.toMatch(/已安装|未安装|已连接/);
    expect(container.querySelector("details, summary")).toBeNull();
    dom.window.close();
  });

  it.each(["qqmusic", "spotify", "sodamusic"])("keeps supported %s to a short summary without extra instructions", async player => {
    const { dom, container, render } = await createCompatibility();
    render(container, player);
    expect(container.querySelector(".compatibility-summary").textContent).toBe("完美支持，直接播放即可。");
    expect(container.querySelector("details, ol, button")).toBeNull();
    dom.window.close();
  });

  it.each([
    ["netease", "https://github.com/apoint123/inflink-rs", "配置步骤"],
    ["kugou", "https://github.com/MoeKoeMusic/MoeKoeMusic", "无法同步时"],
    ["foobar2000", "https://github.com/ungive/foo_mediacontrol", "配置步骤"]
  ])("shows %s instructions under a plain heading and delegates external links to the host", async (player, url, heading) => {
    const { dom, container, render } = await createCompatibility();
    const opened = [];
    render(container, player, link => opened.push(link));
    expect(container.querySelector(".compatibility-details h3").textContent).toBe(heading);
    expect(container.querySelector("details, summary, [hidden]")).toBeNull();
    expect(opened).toEqual([]);
    container.querySelector("[data-compatibility-link]").click();
    expect(opened).toEqual([url]);
    expect(container.querySelector("a, iframe")).toBeNull();
    dom.window.close();
  });

  it.each(["custom-Example.Player", "__proto__", "constructor", '<img src=x onerror="alert(1)">', null])(
    "treats an unrecognized source %s as unknown without inserting its identity", async player => {
      const { dom, container, render } = await createCompatibility();
      render(container, player);
      expect(container.querySelector(".player-compatibility").dataset.supportLevel).toBe("unknown");
      expect(container.querySelector("img, script, [data-compatibility-link]")).toBeNull();
      dom.window.close();
    }
  );

  it("shows a direct third-party player suggestion for Kugou instead of numbered monitor instructions", async () => {
    const { dom, container, render } = await createCompatibility();
    render(container, "kugou");
    expect(container.querySelector(".compatibility-resource").textContent).toBe("使用第三方酷狗播放器【MoeKoeMusic】");
    expect(container.querySelector("ol, li, details, summary")).toBeNull();
    expect(container.textContent).not.toContain("SMTC 时间轴监视器");
    dom.window.close();
  });

  it("preserves visible instructions and keyboard focus on refresh while updating the link callback", async () => {
    const { dom, container, render } = await createCompatibility();
    const opened = [];
    render(container, "netease", link => opened.push(`old:${link}`));
    const instructions = container.querySelector(".compatibility-details");
    const button = container.querySelector("button");
    button.focus();
    render(container, "netease", link => opened.push(link));
    expect(container.querySelector(".compatibility-details")).toBe(instructions);
    expect(dom.window.document.activeElement).toBe(button);
    button.click();
    expect(opened).toEqual(["https://github.com/apoint123/inflink-rs"]);
    render(container, "qqmusic");
    expect(container.querySelector("details, button")).toBeNull();
    expect(container.querySelector(".player-compatibility").dataset.supportLevel).toBe("supported");
    dom.window.close();
  });

  it("loads the shared script and styles locally before the settings entry point", async () => {
    const html = await readFile(new URL("settings.html", root), "utf8");
    const dom = new JSDOM(html);
    const scripts = [...dom.window.document.querySelectorAll("script[src]")].map(item => item.getAttribute("src"));
    expect(scripts.indexOf("player-compatibility.js")).toBeGreaterThanOrEqual(0);
    expect(scripts.indexOf("player-compatibility.js")).toBeLessThan(scripts.indexOf("settings.js"));
    expect(dom.window.document.querySelector('link[href="player-compatibility.css"]')).not.toBeNull();
    dom.window.close();
  });

  it.each([
    ["AppleInc.AppleMusicWin_nzyj5cx40ttqa!App", "applemusic"],
    [" APPLEMUSIC ", "applemusic"],
    ["汽水音乐", "sodamusic"],
    ["COM.SODA.MUSIC", "sodamusic"],
    ["foobar2000", "foobar2000"],
    ["FOOBAR2000.EXE", "foobar2000"],
    ["Unknown.AppleMusic.Player", "other"],
    ["https://example.com/foobar2000.exe", "other"],
    ["__proto__", "other"],
    [null, "other"]
  ])("resolves the exact custom source identity %s without fuzzy name matching", async (sourceId, expected) => {
    const { dom } = await createCompatibility();
    expect(dom.window.taskbarLyricsPlayerCompatibility.resolveSourceProfile(sourceId)).toBe(expected);
    dom.window.close();
  });
});
