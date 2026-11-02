import { originOf, permissionPattern } from "./rules.js";
const t = key => chrome.i18n.getMessage(key);
for (const node of document.querySelectorAll("[data-i18n]")) node.textContent = t(node.dataset.i18n);
const status = document.querySelector("#status");
const stored = await chrome.storage.local.get(["connected", "configuration"]);
const configuration = stored.configuration || { sites: [] };
status.textContent = stored.connected ? t("EXT_Connected") : t("EXT_Disconnected");
const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
const origin = originOf(tab?.url);
const add = document.querySelector("#add");
add.disabled = !origin || !stored.connected;
async function withWebsiteAccess(address, action) {
  try {
    if (await chrome.permissions.request({ origins: [permissionPattern(address)] })) await action();
  } catch { status.textContent = t("EXT_Retry"); }
}
add.addEventListener("click", () => {
  void withWebsiteAccess(origin, async () => {
    const response = await chrome.runtime.sendMessage({ type: "suggestSite", url: origin });
    status.textContent = response?.ok ? t("EXT_Sent") : t("EXT_Retry");
  });
});
for (const site of configuration.sites.filter(site => site.enabled)) {
  const row = document.createElement("div"); row.className = "site";
  const text = document.createElement("span"); text.textContent = site.name;
  const button = document.createElement("button");
  const granted = await chrome.permissions.contains({ origins: [permissionPattern(site.origin)] });
  button.textContent = granted ? t("EXT_Granted") : t("EXT_Grant");
  button.setAttribute("aria-label", `${button.textContent} ${site.name || site.origin}`);
  button.disabled = granted;
  button.addEventListener("click", () => {
    void withWebsiteAccess(site.origin, async () => {
      await chrome.runtime.sendMessage({ type: "permissionsChanged" });
      button.textContent = t("EXT_Granted"); button.disabled = true;
      button.setAttribute("aria-label", `${button.textContent} ${site.name || site.origin}`);
    });
  });
  row.append(text, button); document.querySelector("#sites").append(row);
}
