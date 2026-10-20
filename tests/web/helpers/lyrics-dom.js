import { readFile } from "node:fs/promises";
import { JSDOM } from "jsdom";

const root = new URL("../../../TaskbarLyrics.App/Web/Lyrics/", import.meta.url);
let assets;

export function loadLyricsAssets() {
  assets ??= Promise.all([
    ["html", "index.html"], ["bridge", "bridge.js"], ["state", "state.js"],
    ["presentation", "presentation.js"], ["script", "app.js"], ["style", "style.css"]
  ].map(async ([key, file]) => [key, await readFile(new URL(file, root), "utf8")]))
    .then(entries => Object.freeze(Object.fromEntries(entries)));
  return assets;
}

export function createLyricsDom(html) {
  const dom = new JSDOM(html.replace("{{STYLE_CSS}}", "").replace("{{APP_JS}}", ""), {
    runScripts: "outside-only"
  });
  dom.window.CSS = { supports: () => true };
  return dom;
}

// Call after the test has installed its own clock, frame queue and native mocks.
export function runLyricsScripts(dom, { bridge, state, presentation, script }) {
  for (const source of [bridge, state, presentation, script]) dom.window.eval(source);
}
