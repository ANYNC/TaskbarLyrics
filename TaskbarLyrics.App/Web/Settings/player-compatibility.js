(() => {
  const profiles = {
    netease: {
      name: window.taskbarLyricsI18n.t("UI_NetEaseCloudMusic"), icon: "网易云音乐.png", search: "netease cloudmusic wangyiyun",
      level: "setup", label: window.taskbarLyricsI18n.t("UI_SetupRequired"), information: window.taskbarLyricsI18n.t("UI_Supported"), progress: window.taskbarLyricsI18n.t("UI_Supported"), preparation: "inflink-rs",
      summary: window.taskbarLyricsI18n.t("UI_InstallInflinkRsToEnableLyricSynchronization"),
      detailsLabel: window.taskbarLyricsI18n.t("UI_SetupSteps"),
      steps: [window.taskbarLyricsI18n.t("UI_CheckVersionCompatibilityAndInstallBetterNCMUsingItsOfficial"), window.taskbarLyricsI18n.t("UI_InstallInflinkRsFromThePluginStoreThenRestart")],
      link: "https://github.com/apoint123/inflink-rs", linkLabel: window.taskbarLyricsI18n.t("UI_OfficialSetupGuide"),
      note: window.taskbarLyricsI18n.t("UI_CheckTheOfficialGuideForThirdPartyPluginRequirements")
    },
    qqmusic: {
      name: window.taskbarLyricsI18n.t("UI_QQMusic"), icon: "QQ音乐.png", search: "qqmusic",
      level: "supported", label: window.taskbarLyricsI18n.t("UI_Supported"), information: window.taskbarLyricsI18n.t("UI_Supported"), progress: window.taskbarLyricsI18n.t("UI_Supported"), preparation: window.taskbarLyricsI18n.t("UI_NoSetupNeeded"),
      summary: window.taskbarLyricsI18n.t("UI_FullySupportedJustStartPlaying")
    },
    kugou: {
      name: window.taskbarLyricsI18n.t("UI_KugouMusic"), icon: "酷狗音乐.png", search: "kugou",
      level: "limited", label: window.taskbarLyricsI18n.t("UI_LimitedSupport"), information: window.taskbarLyricsI18n.t("UI_Supported"), progress: window.taskbarLyricsI18n.t("UI_Unsupported"), preparation: window.taskbarLyricsI18n.t("UI_NoSetupNeeded"),
      summary: window.taskbarLyricsI18n.t("UI_TimelineDataIsMissingSoLyricsCannotSynchronizeReliably"),
      detailsLabel: window.taskbarLyricsI18n.t("UI_IfLyricsDoNotSynchronize"),
      link: "https://github.com/MoeKoeMusic/MoeKoeMusic", linkPrefix: window.taskbarLyricsI18n.t("UI_TryTheThirdPartyKugouPlayer"), linkLabel: "【MoeKoeMusic】"
    },
    spotify: {
      name: "Spotify", icon: "spotify.png",
      level: "supported", label: window.taskbarLyricsI18n.t("UI_Supported"), information: window.taskbarLyricsI18n.t("UI_Supported"), progress: window.taskbarLyricsI18n.t("UI_Supported"), preparation: window.taskbarLyricsI18n.t("UI_NoSetupNeeded"),
      summary: window.taskbarLyricsI18n.t("UI_FullySupportedJustStartPlaying")
    },
    applemusic: {
      name: "Apple Music", icon: "Apple Music.png.png", search: "applemusic 苹果音乐",
      level: "supported", label: window.taskbarLyricsI18n.t("UI_Supported"), information: window.taskbarLyricsI18n.t("UI_Supported"), progress: window.taskbarLyricsI18n.t("UI_Imprecise"), preparation: window.taskbarLyricsI18n.t("UI_NoSetupNeeded"),
      summary: window.taskbarLyricsI18n.t("UI_PlaybackIsSupportedButTimelineDataIsImprecise")
    },
    sodamusic: {
      name: window.taskbarLyricsI18n.t("UI_SodaMusic"), icon: "Soda Music.png", search: "soda music qishui",
      level: "supported", label: window.taskbarLyricsI18n.t("UI_Supported"), information: window.taskbarLyricsI18n.t("UI_Supported"), progress: window.taskbarLyricsI18n.t("UI_Supported"), preparation: window.taskbarLyricsI18n.t("UI_NoSetupNeeded"),
      summary: window.taskbarLyricsI18n.t("UI_FullySupportedJustStartPlaying")
    },
    foobar2000: {
      name: "foobar2000", icon: "Foobar2000.png", search: "foobar foobra2000",
      level: "setup", label: window.taskbarLyricsI18n.t("UI_SetupRequired"), information: window.taskbarLyricsI18n.t("UI_Supported"), progress: window.taskbarLyricsI18n.t("UI_Supported"), preparation: "foo_mediacontrol",
      summary: window.taskbarLyricsI18n.t("UI_InstallFooMediacontrolToProvideTimelineData"),
      detailsLabel: window.taskbarLyricsI18n.t("UI_SetupSteps"),
      steps: [window.taskbarLyricsI18n.t("UI_GetTheFooMediacontrolPluginMatchingYourFoobar2000Version"), window.taskbarLyricsI18n.t("UI_FollowTheProjectInstructionsToInstallThePluginAnd")],
      link: "https://github.com/ungive/foo_mediacontrol", linkLabel: window.taskbarLyricsI18n.t("UI_OfficialPluginGuide"),
      note: window.taskbarLyricsI18n.t("UI_CheckTheProjectGuideForThirdPartyPluginRequirements")
    },
    browser: {
      name: window.taskbarLyricsI18n.t("UI_Browser"), icon: "Browser.png", search: "browser chrome edge firefox",
      level: "conditional", label: window.taskbarLyricsI18n.t("UI_DependsOnTheWebsite"), information: window.taskbarLyricsI18n.t("UI_DependsOnTheWebsite"), progress: window.taskbarLyricsI18n.t("UI_DependsOnTheWebsite"), preparation: window.taskbarLyricsI18n.t("UI_SystemMediaSession"),
      summary: window.taskbarLyricsI18n.t("UI_TheWebsiteMustProvideCompleteMediaInformationAndPlayback")
    },
    other: {
      name: window.taskbarLyricsI18n.t("UI_OtherPlayer"), icon: "Presets/music-player.svg", requiresAdding: true,
      level: "unknown", label: window.taskbarLyricsI18n.t("UI_Unverified"), information: window.taskbarLyricsI18n.t("UI_Unverified"), progress: window.taskbarLyricsI18n.t("UI_Unverified"), preparation: window.taskbarLyricsI18n.t("UI_SystemMediaSession"),
      summary: window.taskbarLyricsI18n.t("UI_SynchronizationRequiresAPlayerThatProvidesCompleteSystemMedia"),
      detailsLabel: window.taskbarLyricsI18n.t("UI_HowToCheck"),
      steps: [window.taskbarLyricsI18n.t("UI_PlayASongThenSelectItsMediaSessionIn"), window.taskbarLyricsI18n.t("UI_CheckTrackInformationAndProgressInAdvancedSMTCTimeline")]
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
        : `<strong>${window.taskbarLyricsI18n.t("UI_CompatibilityReference")}</strong>`}<span class="compatibility-level"><span aria-hidden="true">${mark}</span> ${onboarding && profile.level === "setup" ? window.taskbarLyricsI18n.t("UI_PluginRequired") : profile.label}</span></div>
      ${onboarding ? summary : ""}
      <div class="compatibility-overview">
        <dl class="compatibility-metrics">
          <div><dt>${onboarding ? window.taskbarLyricsI18n.t("UI_TrackRecognition") : window.taskbarLyricsI18n.t("UI_TrackInformation")}</dt><dd>${profile.information}</dd></div>
          <div><dt>${onboarding ? window.taskbarLyricsI18n.t("UI_LyricSynchronization") : window.taskbarLyricsI18n.t("UI_TimelineInformation")}</dt><dd>${profile.progress}${explainSetup ? window.taskbarLyricsI18n.t("UI_PluginRequired_1") : ""}</dd></div>
          <div><dt>${window.taskbarLyricsI18n.t("UI_Preparation")}</dt><dd>${explainSetup ? window.taskbarLyricsI18n.t("UI_Install") : ""}${profile.preparation}</dd></div>
        </dl>
        ${onboarding ? `<p class="compatibility-reference-note">${window.taskbarLyricsI18n.t("UI_CompatibilityReferenceNotALiveTest")}</p>` : ""}
      </div>
      ${onboarding ? "" : summary}
      ${profile.detailsLabel ? `<div class="compatibility-details"><h3 class="compatibility-details-title">${profile.detailsLabel}</h3>
        ${profile.steps ? `<ol>${profile.steps.map(step => `<li>${step}</li>`).join("")}</ol>` : ""}
        ${profile.link && !(onboarding && profile.level === "setup") ? `<div class="compatibility-resource">${profile.linkPrefix ?? ""}<button class="${profile.linkPrefix ? "compatibility-link" : "btn secondary small"}" type="button" data-compatibility-link>${profile.linkLabel}</button></div>` : ""}
        ${profile.note ? `<p class="compatibility-note">${profile.note}</p>` : ""}</div>` : ""}
    </div>`;
    container.setAttribute("aria-label", window.taskbarLyricsI18n.t("UI_PlayerCompatibilityReferenceNotALiveTest"));
    container.hidden = false;
    container.querySelector("[data-compatibility-link]")?.addEventListener("click", () => current.onOpenLink?.(profile.link));
  }

  window.taskbarLyricsPlayerCompatibility = { render, resolveSourceProfile, getPlayers, getProfile };
})();
