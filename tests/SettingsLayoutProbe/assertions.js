// Runs against the production HTML, CSS and JS in a real WebView2 renderer.
(async () => {
  const failures = [];
  const wait = () => new Promise(resolve => setTimeout(resolve, 260));
  const $ = selector => document.querySelector(selector);
  const selectPage = async button => {
    button.click();
    const page = $(`[data-page="${button.dataset.nav}"]`);
    await Promise.allSettled(page.getAnimations().map(animation => animation.finished));
    await wait();
    if (!page.classList.contains('active')) throw new Error(`page did not activate: ${button.dataset.nav}`);
    return page;
  };
  const visible = (element, label, container = null) => {
    const rect = element.getBoundingClientRect();
    const clip = container?.getBoundingClientRect() || { left: 0, top: 0, right: innerWidth, bottom: innerHeight };
    if (rect.width <= 0 || rect.height <= 0 || rect.left < clip.left - 1 || rect.top < clip.top - 1 ||
        rect.right > clip.right + 1 || rect.bottom > clip.bottom + 1)
      failures.push(`${label}: bounds=${JSON.stringify(rect.toJSON())}, clip=${JSON.stringify(clip)}`);
    return rect;
  };
  const checkNoticeAlignment = (page, label) => {
    const content = page.firstElementChild.getBoundingClientRect();
    const notice = $('#updateNotice').getBoundingClientRect();
    if (Math.abs(content.left - notice.left) > 1 || Math.abs(content.right - notice.right) > 1)
      failures.push(`update notice and ${label} edges differ: notice [${notice.left}, ${notice.right}], content [${content.left}, ${content.right}]`);
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
    window.settingsApp.receive({ version: 1, type: 'updateStatus', payload: {
      state: 'available', hasUpdate: true, canDownload: true, version: 'v2.0.0'
    } });
    const notice = visible($('#updateNotice'), 'update notice clipped');
    visible($('#updateNoticePrimary'), 'update notice action clipped');
    visible($('#updateNoticeDismiss'), 'update notice dismiss clipped');
    if (notice.bottom > $('.page-stack').getBoundingClientRect().top + 1)
      failures.push('update notice overlaps page content');
    const more = $('#updateNoticeMore');
    more.click();
    const ignore = $('#updateNoticeIgnore');
    const menuItem = visible(ignore, 'update notice menu item clipped');
    const hit = document.elementFromPoint(menuItem.x + menuItem.width / 2, menuItem.y + menuItem.height / 2);
    if (!ignore.contains(hit) || document.activeElement !== ignore)
      failures.push('update notice menu item unreachable');
    more.click();
    $('#sidebarToggle').click();
    await wait();
    checkNoticeAlignment($('.page.active'), 'collapsed sidebar content');
    $('#sidebarToggle').click();
    await wait();
    for (const button of document.querySelectorAll('[data-nav]')) {
      const page = await selectPage(button);
      checkNoticeAlignment(page, button.dataset.nav);
      page.scrollTop = page.scrollHeight;
      await wait();
      visible(page, `page ${button.dataset.nav} outside viewport`);
      if (page.scrollWidth > page.clientWidth + 1) {
        const rightEdge = page.getBoundingClientRect().left + page.clientWidth - parseFloat(getComputedStyle(page).paddingRight);
        const overflow = [...page.querySelectorAll('*')].filter(element => {
          const rect = element.getBoundingClientRect();
          return rect.width && rect.right > rightEdge + 1;
        }).slice(0, 6).map(element => element.id || element.className || element.tagName);
        failures.push(`horizontal overflow: ${button.dataset.nav} (${page.scrollWidth}/${page.clientWidth}, ${overflow.join(', ')})`);
      }
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
    await selectPage($('[data-nav="sources"]'));
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
