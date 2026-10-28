(() => {
  const page = document.querySelector('[data-page="displayArea"]');
  const video = page.querySelector('[data-presentation-video]');
  const status = page.querySelector('[data-presentation-status]');
  const groups = Array.from(page.querySelectorAll('.display-setting-group'));
  const motion = window.matchMedia('(prefers-reduced-motion: reduce)');
  let mode = null;
  let videoMode = null;
  let active = false;
  let revision = 0;
  let animations = [];
  const durations = { embedded: 5, floating: 8 };
  const mediaVersion = 'floating-transparent-hq-v4';

  function pause() {
    if (!videoMode) return;
    const playing = !video.paused;
    ++revision;
    video.pause();
    if (playing) status.textContent = window.taskbarLyricsI18n.t("UI_DemoPausedReplayAvailable");
  }

  function play() {
    if (!active || !videoMode) return;
    const current = ++revision;
    video.pause();
    video.currentTime = 0;
    video.muted = true;
    status.textContent = window.taskbarLyricsI18n.format("UI_PlayingDemoS", durations[videoMode]);
    video.play().catch(() => {
      if (current === revision && active) status.textContent = window.taskbarLyricsI18n.t("UI_ClickToReplayTheDemo");
    });
  }

  function sync(state) {
    if (!state) return;
    const nextMode = state.useFloatingWindow === true ? 'floating' : 'embedded';
    const changed = mode !== nextMode;
    const animate = changed && mode !== null && state.page === 'displayArea' && !motion.matches;
    const before = animate ? groups.map(group => group.getBoundingClientRect()) : [];
    if (changed) {
      animations.forEach(animation => animation.cancel());
      animations = [];
      page.querySelectorAll('[data-presentation-mode]').forEach(element => {
        element.hidden = element.dataset.presentationMode !== nextMode;
      });
      mode = nextMode;
    }
    if (animate) {
      const after = groups.map(group => group.getBoundingClientRect());
      const duration = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--duration-slow')) || 220;
      groups.forEach((group, index) => {
        // Hidden groups have no layout box and cannot provide a meaningful animation endpoint.
        if (before[index].width <= 0 || before[index].height <= 0 ||
            after[index].width <= 0 || after[index].height <= 0) return;
        const distance = before[index].top - after[index].top;
        if (Math.abs(distance) < 0.5 || typeof group.animate !== 'function') return;
        animations.push(group.animate(
          [{ transform: `translateY(${distance}px)` }, { transform: 'translateY(0)' }],
          { duration, easing: 'cubic-bezier(.2,.75,.25,1)' }
        ));
      });
    }
    active = state.page === 'displayArea';
    if (!active) { pause(); return; }
    if (videoMode === nextMode) return;
    ++revision;
    video.pause();
    videoMode = nextMode;
    const floating = nextMode === 'floating';
    page.querySelector('[data-presentation-name]').textContent = floating ? window.taskbarLyricsI18n.t("UI_FloatingWindow") : window.taskbarLyricsI18n.t("UI_TaskbarEmbedded");
    page.querySelector('[data-presentation-description]').textContent = floating
      ? window.taskbarLyricsI18n.t("UI_ExpandPlaybackControlsAndDemonstrateMovingAndResizing")
      : window.taskbarLyricsI18n.t("UI_LyricsStayInTheTaskbarWithPlaybackControlsNearby");
    video.setAttribute('aria-label', window.taskbarLyricsI18n.format("UI_Demo", floating ? window.taskbarLyricsI18n.t("UI_FloatingWindow") : window.taskbarLyricsI18n.t("UI_TaskbarEmbedded")));
    video.poster = `media/${nextMode}-poster.png?v=${mediaVersion}`;
    video.src = `media/${nextMode}.mp4?v=${mediaVersion}`;
    video.load();
    status.textContent = motion.matches ? window.taskbarLyricsI18n.t("UI_ReducedMotionReplayManually") : window.taskbarLyricsI18n.t("UI_FixedExample");
    if (!motion.matches && !document.hidden) play();
  }

  page.querySelector('[data-replay-presentation]').addEventListener('click', play);
  video.addEventListener('ended', () => {
    if (active && video.ended && video.currentSrc === video.src) status.textContent = window.taskbarLyricsI18n.t("UI_DemoFinished");
  });
  video.addEventListener('error', () => {
    if (active && videoMode && video.currentSrc === video.src) status.textContent = window.taskbarLyricsI18n.t("UI_DemoUnavailablePleaseRetry");
  });
  document.addEventListener('visibilitychange', () => { if (document.hidden) pause(); });
  motion.addEventListener('change', () => {
    if (!motion.matches) return;
    animations.forEach(animation => animation.cancel());
    animations = [];
    pause();
  });
  window.addEventListener('pagehide', () => {
    animations.forEach(animation => animation.cancel());
    animations = [];
    active = false;
    pause();
  }, { once: true });

  window.taskbarLyricsDisplayPresentation = { sync };
})();
