(() => {
  const profiles = {
    qqmusic: {
      level: "supported", label: "支持", information: "支持", progress: "支持", preparation: "无需配置",
      summary: "完美支持，直接播放即可。"
    },
    spotify: {
      level: "supported", label: "支持", information: "支持", progress: "支持", preparation: "无需配置",
      summary: "完美支持，直接播放即可。"
    },
    applemusic: {
      level: "supported", label: "支持", information: "支持", progress: "不精准", preparation: "无需配置",
      summary: "支持播放，时间轴信息不精准。"
    },
    sodamusic: {
      level: "supported", label: "支持", information: "支持", progress: "支持", preparation: "无需配置",
      summary: "完美支持，直接播放即可。"
    },
    foobar2000: {
      level: "setup", label: "需配置", information: "支持", progress: "支持", preparation: "foo_mediacontrol",
      summary: "安装 foo_mediacontrol 插件补全时间轴信息。",
      detailsLabel: "配置步骤",
      steps: ["从 foo_mediacontrol 项目获取适合当前 foobar2000 版本的插件。", "按照项目说明安装插件并重启 foobar2000。"],
      link: "https://github.com/ungive/foo_mediacontrol", linkLabel: "官方插件说明",
      note: "第三方插件的版本要求以项目说明为准，本软件不会自动安装。"
    },
    netease: {
      level: "setup", label: "需配置", information: "支持", progress: "支持", preparation: "inflink-rs",
      summary: "通过 inflink-rs 提供歌曲信息和时间轴信息。",
      detailsLabel: "配置步骤",
      steps: ["按 BetterNCM 官方说明确认版本兼容并安装。", "在插件商店安装 inflink-rs，重启网易云音乐。"],
      link: "https://github.com/apoint123/inflink-rs", linkLabel: "官方安装说明",
      note: "第三方插件的版本要求以官方说明为准，本软件不会自动安装。"
    },
    kugou: {
      level: "limited", label: "有限支持", information: "支持", progress: "不支持", preparation: "无需配置",
      summary: "缺少时间轴信息，歌词无法稳定同步。",
      detailsLabel: "无法同步时",
      link: "https://github.com/MoeKoeMusic/MoeKoeMusic", linkPrefix: "使用第三方酷狗播放器", linkLabel: "【MoeKoeMusic】"
    },
    browser: {
      level: "conditional", label: "视网站而定", information: "视网站而定", progress: "视网站而定", preparation: "系统媒体会话",
      summary: "需要网站提供完整媒体信息和进度。"
    },
    other: {
      level: "unknown", label: "待确认", information: "待确认", progress: "待确认", preparation: "系统媒体会话",
      summary: "提供完整系统媒体信息和进度的播放器才可同步。",
      detailsLabel: "如何确认",
      steps: ["播放歌曲，在“播放源 → 添加播放器”中选择媒体会话。", "在“高级 → SMTC 时间轴监视器”检查歌曲信息和进度。"]
    }
  };

  const sourceProfiles = new Map([
    ["applemusic", "applemusic"],
    ["appleinc.applemusicwin_nzyj5cx40ttqa!app", "applemusic"],
    ["汽水音乐", "sodamusic"],
    ["com.soda.music", "sodamusic"],
    ["foobar2000", "foobar2000"],
    ["foobar2000.exe", "foobar2000"]
  ]);

  function resolveSourceProfile(sourceAppUserModelId) {
    return typeof sourceAppUserModelId === "string"
      ? sourceProfiles.get(sourceAppUserModelId.trim().toLowerCase()) ?? "other" : "other";
  }

  const rendered = new WeakMap();

  function render(container, playerId, onOpenLink) {
    const profile = Object.hasOwn(profiles, playerId) ? profiles[playerId] : profiles.other;
    const previous = rendered.get(container);
    if (previous?.profile === profile && container.firstElementChild) {
      previous.onOpenLink = onOpenLink;
      container.hidden = false;
      return;
    }
    const current = { profile, onOpenLink };
    rendered.set(container, current);
    const mark = profile.level === "supported" ? "✓" : profile.level === "unknown" ? "?" : "!";
    container.innerHTML = `<div class="player-compatibility" data-support-level="${profile.level}">
      <div class="compatibility-heading"><strong>兼容性参考</strong><span class="compatibility-level"><span aria-hidden="true">${mark}</span> ${profile.label}</span></div>
      <dl class="compatibility-metrics">
        <div><dt>歌曲信息</dt><dd>${profile.information}</dd></div>
        <div><dt>时间轴信息</dt><dd>${profile.progress}</dd></div>
        <div><dt>使用准备</dt><dd>${profile.preparation}</dd></div>
      </dl>
      <p class="compatibility-summary">${profile.summary}</p>
      ${profile.detailsLabel ? `<div class="compatibility-details"><h3 class="compatibility-details-title">${profile.detailsLabel}</h3>
        ${profile.steps ? `<ol>${profile.steps.map(step => `<li>${step}</li>`).join("")}</ol>` : ""}
        ${profile.link ? `<div class="compatibility-resource">${profile.linkPrefix ?? ""}<button class="${profile.linkPrefix ? "compatibility-link" : "btn secondary small"}" type="button" data-compatibility-link>${profile.linkLabel}</button></div>` : ""}
        ${profile.note ? `<p class="compatibility-note">${profile.note}</p>` : ""}</div>` : ""}
    </div>`;
    container.setAttribute("aria-label", "播放器兼容性参考，非实时检测结果");
    container.hidden = false;
    container.querySelector("[data-compatibility-link]")?.addEventListener("click", () => current.onOpenLink?.(profile.link));
  }

  window.taskbarLyricsPlayerCompatibility = { render, resolveSourceProfile };
})();
