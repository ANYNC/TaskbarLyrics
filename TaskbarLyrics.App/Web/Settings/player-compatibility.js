(() => {
  const profiles = {
    netease: {
      name: "网易云音乐", icon: "网易云音乐.png", search: "netease cloudmusic wangyiyun",
      level: "setup", label: "需配置", information: "支持", progress: "支持", preparation: "inflink-rs",
      summary: "安装 inflink-rs 后，即可同步歌词。",
      detailsLabel: "配置步骤",
      steps: ["按 BetterNCM 官方说明确认版本兼容并安装。", "在插件商店安装 inflink-rs，重启网易云音乐。"],
      link: "https://github.com/apoint123/inflink-rs", linkLabel: "官方安装说明",
      note: "第三方插件的版本要求以官方说明为准，本软件不会自动安装。"
    },
    qqmusic: {
      name: "QQ 音乐", icon: "QQ音乐.png", search: "qqmusic",
      level: "supported", label: "支持", information: "支持", progress: "支持", preparation: "无需配置",
      summary: "完美支持，直接播放即可。"
    },
    kugou: {
      name: "酷狗音乐", icon: "酷狗音乐.png", search: "kugou",
      level: "limited", label: "有限支持", information: "支持", progress: "不支持", preparation: "无需配置",
      summary: "缺少时间轴信息，歌词无法稳定同步。",
      detailsLabel: "无法同步时",
      link: "https://github.com/MoeKoeMusic/MoeKoeMusic", linkPrefix: "使用第三方酷狗播放器", linkLabel: "【MoeKoeMusic】"
    },
    spotify: {
      name: "Spotify", icon: "spotify.png",
      level: "supported", label: "支持", information: "支持", progress: "支持", preparation: "无需配置",
      summary: "完美支持，直接播放即可。"
    },
    applemusic: {
      name: "Apple Music", icon: "Apple Music.png.png", search: "applemusic 苹果音乐",
      level: "supported", label: "支持", information: "支持", progress: "不精准", preparation: "无需配置",
      summary: "支持播放，时间轴信息不精准。"
    },
    sodamusic: {
      name: "汽水音乐", icon: "Soda Music.png", search: "soda music qishui",
      level: "supported", label: "支持", information: "支持", progress: "支持", preparation: "无需配置",
      summary: "完美支持，直接播放即可。"
    },
    foobar2000: {
      name: "foobar2000", icon: "Foobar2000.png", search: "foobar foobra2000",
      level: "setup", label: "需配置", information: "支持", progress: "支持", preparation: "foo_mediacontrol",
      summary: "安装 foo_mediacontrol 插件补全时间轴信息。",
      detailsLabel: "配置步骤",
      steps: ["从 foo_mediacontrol 项目获取适合当前 foobar2000 版本的插件。", "按照项目说明安装插件并重启 foobar2000。"],
      link: "https://github.com/ungive/foo_mediacontrol", linkLabel: "官方插件说明",
      note: "第三方插件的版本要求以项目说明为准，本软件不会自动安装。"
    },
    browser: {
      name: "浏览器", icon: "Browser.png", search: "browser chrome edge firefox",
      level: "conditional", label: "视网站而定", information: "视网站而定", progress: "视网站而定", preparation: "系统媒体会话",
      summary: "需要网站提供完整媒体信息和进度。"
    },
    other: {
      name: "其他播放器", icon: "Presets/music-player.svg", requiresAdding: true,
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
    ["sodamusic", "sodamusic"],
    ["com.soda.music", "sodamusic"],
    ["foobar2000", "foobar2000"],
    ["foobar2000.exe", "foobar2000"]
  ]);

  function resolveSourceProfile(sourceAppUserModelId) {
    return typeof sourceAppUserModelId === "string"
      ? sourceProfiles.get(sourceAppUserModelId.trim().toLowerCase()) ?? "other" : "other";
  }

  const rendered = new WeakMap();

  function getProfile(playerId) {
    return Object.hasOwn(profiles, playerId) ? profiles[playerId] : profiles.other;
  }

  function getPlayers() {
    return Object.entries(profiles).map(([id, profile]) => ({ id, ...profile }));
  }

  function render(container, playerId, onOpenLink, onboarding = false) {
    const profile = getProfile(playerId);
    const previous = rendered.get(container);
    if (previous?.profile === profile && previous.onboarding === onboarding && container.firstElementChild) {
      previous.onOpenLink = onOpenLink;
      container.hidden = false;
      return;
    }
    const current = { profile, onOpenLink, onboarding };
    rendered.set(container, current);
    const mark = profile.level === "supported" ? "✓" : profile.level === "unknown" ? "?" : "!";
    const summary = `<p class="compatibility-summary">${profile.summary}</p>`;
    const explainSetup = onboarding && profile.level === "setup";
    container.innerHTML = `<div class="player-compatibility${onboarding ? " is-onboarding" : ""}" data-support-level="${profile.level}">
      <div class="compatibility-heading">${onboarding
        ? `<div class="compatibility-player"><img class="${playerId === "other" ? "default-player-icon" : ""}" src="../../Assets/PlayerIcons/${profile.icon}" alt="" draggable="false"><h2>${profile.name}</h2></div>`
        : "<strong>兼容性参考</strong>"}<span class="compatibility-level"><span aria-hidden="true">${mark}</span> ${onboarding && profile.level === "setup" ? "需配置插件" : profile.label}</span></div>
      ${onboarding ? summary : ""}
      <div class="compatibility-overview">
        <dl class="compatibility-metrics">
          <div><dt>${onboarding ? "歌曲识别" : "歌曲信息"}</dt><dd>${profile.information}</dd></div>
          <div><dt>${onboarding ? "歌词同步" : "时间轴信息"}</dt><dd>${profile.progress}${explainSetup ? "，需配置插件" : ""}</dd></div>
          <div><dt>使用准备</dt><dd>${explainSetup ? "安装 " : ""}${profile.preparation}</dd></div>
        </dl>
        ${onboarding ? '<p class="compatibility-reference-note">兼容性参考，非实时检测结果。</p>' : ""}
      </div>
      ${onboarding ? "" : summary}
      ${profile.detailsLabel ? `<div class="compatibility-details"><h3 class="compatibility-details-title">${profile.detailsLabel}</h3>
        ${profile.steps ? `<ol>${profile.steps.map(step => `<li>${step}</li>`).join("")}</ol>` : ""}
        ${profile.link && !(onboarding && profile.level === "setup") ? `<div class="compatibility-resource">${profile.linkPrefix ?? ""}<button class="${profile.linkPrefix ? "compatibility-link" : "btn secondary small"}" type="button" data-compatibility-link>${profile.linkLabel}</button></div>` : ""}
        ${profile.note ? `<p class="compatibility-note">${profile.note}</p>` : ""}</div>` : ""}
    </div>`;
    container.setAttribute("aria-label", "播放器兼容性参考，非实时检测结果");
    container.hidden = false;
    container.querySelector("[data-compatibility-link]")?.addEventListener("click", () => current.onOpenLink?.(profile.link));
  }

  window.taskbarLyricsPlayerCompatibility = { render, resolveSourceProfile, getPlayers, getProfile };
})();
