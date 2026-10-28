import { describe, expect, it } from "vitest";
import { JSDOM, catalogs } from "./helpers/localized-dom.js";
import { readFileSync } from "node:fs";

const languages = ["Zh-CN", "EN", "Zh-Hant"];
const settingsRoot = new URL('../../TaskbarLyrics.App/Web/Settings/', import.meta.url);
const settingsScripts = ['bridge.js', 'hotkeys.js', 'state.js', 'track-offsets-page.js', 'display-presentation.js', 'player-compatibility.js', 'settings.js'];

function renderSettings(locale) {
  const dom = new JSDOM(readFileSync(new URL('settings.html', settingsRoot), 'utf8'), { locale });
  const w = dom.window;
  w.chrome = { webview: { postMessage: () => {} } };
  w.matchMedia = () => ({ matches: true, addEventListener() {}, removeEventListener() {} });
  w.requestAnimationFrame = () => 0;
  w.HTMLMediaElement.prototype.load = () => {};
  w.HTMLMediaElement.prototype.pause = () => {};
  w.HTMLElement.prototype.scrollIntoView = () => {};
  for (const file of settingsScripts) w.eval(readFileSync(new URL(file, settingsRoot), 'utf8'));
  w.settingsApp.receive({ version: 1, type: 'settingsState', payload: { settings: {}, fonts: [] } });
  return { dom, w };
}

describe("named localization resources", () => {
  it("renders English settings and sends the original setting identifiers", () => {
    const { dom, w } = renderSettings('EN');
    w.document.querySelector('[data-nav="general"]').click();
    expect(w.document.querySelector('#pageTitle').textContent).toBe('General');
    expect(w.document.querySelector('[data-nav="lyrics"]').textContent.trim()).toBe('Lyrics');
    expect(w.document.body.textContent).not.toMatch(/\{\{UI_/);
    expect(w.taskbarLyricsPlayerCompatibility.resolveSourceProfile('汽水音乐')).toBe('sodamusic');
    expect(w.document.documentElement.lang).toBe('en');
    expect(w.taskbarLyricsI18n.locale).toBe('en');
    dom.window.close();
  });

  it("renders generic traditional Chinese settings", () => {
    const { dom, w } = renderSettings('Zh-Hant');
    w.document.querySelector('[data-nav="general"]').click();
    expect(w.document.querySelector('#pageTitle').textContent).toBe('常規');
    expect(w.document.querySelector('[data-nav="lyrics"]').textContent.trim()).toBe('歌詞');
    expect(w.document.body.textContent).not.toMatch(/\{\{UI_/);
    expect(w.taskbarLyricsPlayerCompatibility.resolveSourceProfile('汽水音乐')).toBe('sodamusic');
    expect(w.document.documentElement.lang).toBe('zh-Hant');
    expect(w.taskbarLyricsI18n.locale).toBe('zh-Hant');
    dom.window.close();
  });

  it("keeps language keys and placeholders aligned", () => {
    const referenceKeys = Object.keys(catalogs['Zh-CN']).sort();
    for (const language of languages)
      expect(Object.keys(catalogs[language]).sort(), language).toEqual(referenceKeys);
    for (const language of languages)
      for (const [key, value] of Object.entries(catalogs[language])) {
        const label = `${language}/${key}`;
        expect(value.trim(), label).not.toBe("");
        expect(value.match(/\{\d+\}/g)?.sort() ?? [], label)
          .toEqual(catalogs['Zh-CN'][key].match(/\{\d+\}/g)?.sort() ?? []);
      }
  });

  it("translates resource references without translating media text", () => {
    const dom = new JSDOM('<p>{{UI_Pause}}</p><p id="track">暂停</p>', { locale: 'EN' });
    expect(dom.window.document.querySelector('p').textContent).toBe('Pause');
    expect(dom.window.document.querySelector('#track').textContent).toBe('暂停');
    expect(dom.window.taskbarLyricsI18n.t('Missing_Key')).toBe('Missing_Key');
    dom.window.close();
  });

  it("falls back to the resource key for missing traditional Chinese entries", () => {
    const dom = new JSDOM('<p>{{UI_Pause}}</p>', { locale: 'Zh-Hant' });
    expect(dom.window.document.querySelector('p').textContent).toBe('暫停');
    expect(dom.window.taskbarLyricsI18n.t('Missing_Key')).toBe('Missing_Key');
    dom.window.close();
  });
});
