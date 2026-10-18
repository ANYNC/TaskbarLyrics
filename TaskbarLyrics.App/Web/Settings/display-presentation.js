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

  function pause() {
    if (!videoMode) return;
    const playing = !video.paused;
    ++revision;
    video.pause();
    if (playing) status.textContent = '演示已暂停 · 可重播';
  }

  function play() {
    if (!active || !videoMode) return;
    const current = ++revision;
    video.pause();
    video.currentTime = 0;
    video.muted = true;
    status.textContent = `正在演示 · ${durations[videoMode]} 秒`;
    video.play().catch(() => {
      if (current === revision && active) status.textContent = '点击重播演示';
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
    page.querySelector('[data-panel-options]').hidden = state.enableControlPanel === false;
    if (animate) {
      const after = groups.map(group => group.getBoundingClientRect());
      const duration = parseFloat(getComputedStyle(document.documentElement).getPropertyValue('--duration-slow')) || 220;
      groups.forEach((group, index) => {
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
    page.querySelector('[data-presentation-name]').textContent = floating ? '悬浮窗口' : '任务栏嵌入';
    page.querySelector('[data-presentation-description]').textContent = floating
      ? '展开播放控制面板，并演示移动位置与调整大小。'
      : '歌词保留在任务栏，播放面板在附近独立出现。';
    video.setAttribute('aria-label', `${floating ? '悬浮窗口' : '任务栏嵌入'}模式演示`);
    video.poster = `media/${nextMode}-poster.png`;
    video.src = `media/${nextMode}.mp4`;
    video.load();
    status.textContent = motion.matches ? '已减少动态效果 · 可手动重播' : '固定示例';
    if (!motion.matches && !document.hidden) play();
  }

  page.querySelector('[data-replay-presentation]').addEventListener('click', play);
  video.addEventListener('ended', () => {
    if (active && video.ended && video.currentSrc === video.src) status.textContent = '演示结束';
  });
  video.addEventListener('error', () => {
    if (active && videoMode && video.currentSrc === video.src) status.textContent = '演示暂不可用，请重试';
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
