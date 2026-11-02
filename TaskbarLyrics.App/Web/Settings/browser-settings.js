(() => {
  const t = key => window.taskbarLyricsI18n.t(key);
  const element = (tag, text, className) => {
    const node = document.createElement(tag);
    if (text) node.textContent = text;
    if (className) node.className = className;
    return node;
  };
  window.taskbarLyricsBrowserSettings = {
    create({ post, updateMode }) {
      const get = id => document.getElementById(id);
      let sites = [];
      let dragged = null;
      let connectionFingerprint = "";
      function reorder(id, target) {
        const order = sites.map(site => site.id);
        const from = order.indexOf(id);
        if (from < 0 || target < 0 || target >= order.length || target === from) return;
        order.splice(target, 0, order.splice(from, 1)[0]);
        post({ type: "browserSitesReorder", value: { ids: order } });
      }
      get("browserWebsiteToggle").addEventListener("change", event => updateMode(event.target.checked));
      get("browserSiteAdd").addEventListener("click", () => {
        const input = get("browserSiteAddress");
        if (!input.value.trim()) { input.focus(); return; }
        post({ type: "browserSiteAdd", value: { address: input.value.trim(), name: get("browserSiteName").value.trim() } });
      });
      get("browserSiteAddress").addEventListener("keydown", event => {
        if (event.key === "Enter") { event.preventDefault(); get("browserSiteAdd").click(); }
      });
      function renderConnections(state) {
        const connections = state.browserConnections ?? [];
        const fingerprint = JSON.stringify([state.enableBrowser, state.browserWebsiteRecognitionEnabled, connections]);
        if (connectionFingerprint === fingerprint) return;
        connectionFingerprint = fingerprint;
        const focused = document.activeElement?.dataset.browserInstall;
        const rows = ["Chrome", "Edge"].map(browser => {
          const connection = connections.find(item => item.browser === browser);
          const row = element("div", "", "browser-connection");
          const key = !state.enableBrowser ? "UI_BrowserRecognitionOff" : !state.browserWebsiteRecognitionEnabled
            ? "UI_BrowserLegacyMode" : connection?.state === "website" ? "UI_BrowserConnected" : "UI_BrowserFallback";
          row.append(element("span", `${browser} · ${t(key)}`));
          const button = element("button", t("UI_BrowserInstallExtension"), "btn ghost small");
          button.type = "button"; button.dataset.browserInstall = browser;
          button.addEventListener("click", () => post({ type: "installBrowserExtension", value: browser }));
          row.append(button);
          return row;
        });
        get("browserConnections").replaceChildren(...rows);
        if (focused) get("browserConnections").querySelector(`[data-browser-install="${focused}"]`)?.focus({ preventScroll: true });
      }
      function render(state) {
        renderConnections(state);
        get("browserWebsiteToggle").checked = state.browserWebsiteRecognitionEnabled === true;
        sites = state.browserSites ?? [];
        const list = get("browserSiteList");
        const focused = list.contains(document.activeElement) ? document.activeElement : null;
        const focusId = focused?.closest("[data-browser-site]")?.dataset.browserSite;
        const focusAction = focused?.dataset.siteAction;
        const rows = sites.map((site, index) => {
          const row = element("article", "", "browser-site-row"); row.dataset.browserSite = site.id;
          const handle = element("button", "↕", "icon-button"); handle.type = "button"; handle.draggable = true;
          handle.dataset.siteAction = "move";
          handle.setAttribute("aria-label", `${t("UI_Drag")} ${site.name}`);
          handle.setAttribute("aria-keyshortcuts", "Alt+ArrowUp Alt+ArrowDown");
          handle.title = t("UI_DragToChangePlayerPriorityOrPressAltArrow");
          handle.addEventListener("dragstart", event => { dragged = site.id; event.dataTransfer?.setData("text/plain", site.id); });
          handle.addEventListener("dragend", () => { dragged = null; });
          row.addEventListener("dragover", event => { if (dragged) event.preventDefault(); });
          row.addEventListener("drop", event => { event.preventDefault(); if (dragged) reorder(dragged, index); dragged = null; });
          handle.addEventListener("keydown", event => {
            if (!event.altKey || !["ArrowUp", "ArrowDown"].includes(event.key)) return;
            event.preventDefault(); reorder(site.id, index + (event.key === "ArrowUp" ? -1 : 1));
          });
          const label = element("div", "", "browser-site-label");
          label.append(element("strong", site.name), element("small", site.origin));
          const toggle = element("label", "", "switch");
          const checkbox = element("input"); checkbox.type = "checkbox"; checkbox.checked = site.enabled;
          checkbox.dataset.siteAction = "toggle"; checkbox.setAttribute("aria-label", `${t("UI_EnablePlayerRecognition")} ${site.name}`);
          checkbox.addEventListener("change", () => post({ type: "browserSiteToggle", value: { id: site.id, enabled: checkbox.checked } }));
          toggle.append(checkbox, element("span", "", "switch-track"));
          const remove = element("button", t("UI_Remove"), "btn ghost small"); remove.type = "button";
          remove.dataset.siteAction = "remove"; remove.setAttribute("aria-label", `${t("UI_Remove")} ${site.name}`);
          remove.addEventListener("click", () => post({ type: "browserSiteRemove", value: { id: site.id } }));
          row.append(handle, label, toggle, remove); return row;
        });
        list.replaceChildren(...rows);
        get("browserSiteEmpty").hidden = sites.length > 0;
        if (focusId && focusAction) [...list.children].find(row => row.dataset.browserSite === focusId)
          ?.querySelector(`[data-site-action="${focusAction}"]`)?.focus({ preventScroll: true });
      }
      return { render, renderConnections };
    }
  };
})();
