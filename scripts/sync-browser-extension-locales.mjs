import { readFileSync, mkdirSync, writeFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import path from "node:path";

const root = fileURLToPath(new URL("../", import.meta.url));
const catalogs = new Map();
let current;
for (const line of readFileSync(path.join(root, "TaskbarLyrics.App/text.yaml"), "utf8").split(/\r?\n/)) {
  const language = /^([\w-]+):$/.exec(line);
  if (language) { current = {}; catalogs.set(language[1], current); }
  const entry = /^  (EXT_\w+): (".*")$/.exec(line);
  if (entry && current) current[entry[1]] = { message: JSON.parse(entry[2]) };
}
for (const [language, messages] of catalogs) {
  // Browser locale names differ from the desktop catalog's culture names.
  const locale = language === "Zh-Hant" ? "zh_TW" : language.replace(/-/g, "_").replace(/^EN$/, "en").replace(/^Zh_/, "zh_");
  if (!Object.keys(messages).length) continue;
  const directory = path.join(root, "BrowserExtension/_locales", locale);
  mkdirSync(directory, { recursive: true });
  writeFileSync(path.join(directory, "messages.json"), JSON.stringify(messages, null, 2) + "\n");
}
