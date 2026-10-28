import { JSDOM as Base } from "jsdom";
import { readFileSync } from "node:fs";
const root = new URL("../../../TaskbarLyrics.App/", import.meta.url);
const runtime = readFileSync(new URL("text.js", root), "utf8");
const catalogs = {}; let language;
for (const line of readFileSync(new URL("text.yaml", root), "utf8").split(/\r?\n/)) {
  const section = line.match(/^(Zh-CN|EN|Zh-Hant):$/);
  if (section) { language = section[1]; catalogs[language] = {}; }
  const item = line.match(/^  ([A-Za-z0-9_]+): (.*)$/);
  if (item) catalogs[language][item[1]] = JSON.parse(item[2]);
}
export class JSDOM extends Base {
  constructor(html, options = {}) {
    const { locale = "Zh-CN", beforeParse, ...rest } = options;
    super(html, { ...rest, runScripts: rest.runScripts ?? "outside-only", beforeParse(window) {
      window.taskbarLyricsLocale = { language: locale, strings: catalogs[locale] };
      window.eval(runtime);
      beforeParse?.(window);
    }});
    this.window.taskbarLyricsI18n.translateDocument();
  }
}
export { catalogs };
