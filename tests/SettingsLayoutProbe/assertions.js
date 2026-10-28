// Runs against the production HTML, CSS and JS in a real WebView2 renderer.
(async () => {
  const failures = [];
  const wait = () => new Promise(resolve => setTimeout(resolve, 260));
  const $ = selector => document.querySelector(selector);
  const visible = (element, label, container = null) => {
    const rect = element.getBoundingClientRect();
    const clip = container?.getBoundingClientRect() || { left: 0, top: 0, right: innerWidth, bottom: innerHeight };
    if (rect.width <= 0 || rect.height <= 0 || rect.left < clip.left - 1 || rect.top < clip.top - 1 ||
        rect.right > clip.right + 1 || rect.bottom > clip.bottom + 1) failures.push(label);
    return rect;
  };
  try {
    await document.fonts.ready;
    await wait();
    visible($('#restoreButton'), 'restore button clipped');
    visible($('#sidebarToggle'), 'sidebar toggle clipped');
    const about = $('[data-nav="about"]');
    about.focus();
    await wait();
    visible(about, 'last keyboard-focused navigation item clipped', $('.nav'));
    for (const button of document.querySelectorAll('[data-nav]')) {
      button.click();
      await wait();
      const page = $('.page.active');
      page.scrollTop = page.scrollHeight;
      await wait();
      visible(page, `page ${button.dataset.nav} outside viewport`);
      if (page.scrollWidth > page.clientWidth + 1) failures.push(`horizontal overflow: ${button.dataset.nav}`);
      const last = page.querySelector('.page-inner')?.lastElementChild;
      if (last && last.getBoundingClientRect().bottom > page.getBoundingClientRect().bottom + 1)
        failures.push(`page end unreachable: ${button.dataset.nav}`);
    }
    $('#restoreButton').click();
    await wait();
    visible($('#confirmRestore'), 'restore confirmation clipped');
    $('#restoreDialog').close();
    $('#spectrumAudioConsentDialog').showModal();
    await wait();
    visible($('#confirmSpectrumAudioAccess'), 'audio consent action clipped');
    $('#spectrumAudioConsentDialog').close();
    $('[data-nav="sources"]').click();
    await wait();
    $('[data-player-settings]').click();
    await wait();
    const body = $('#playerDrawerBody');
    body.scrollTop = body.scrollHeight;
    visible(body, 'player drawer body outside viewport');
    const last = [...body.children].findLast(element => element.getBoundingClientRect().height > 0);
    if (last && last.getBoundingClientRect().bottom > body.getBoundingClientRect().bottom + 1)
      failures.push('player drawer end unreachable');
    $('#playerSettingsDialog').close();
  } catch (error) { failures.push(String(error.stack || error)); }
  window.chrome.webview.postMessage({ layoutProbe: { viewport: [innerWidth, innerHeight], dpr: devicePixelRatio, failures } });
})();
