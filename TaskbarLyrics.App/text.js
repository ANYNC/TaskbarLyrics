(() => {
  const { language = "zh-CN", strings = {} } = window.taskbarLyricsLocale ?? {};
  // zh-Hant-HK/MO/TW all use the generic Traditional catalog, so the document locale is the catalog language.
  const locale = language === "EN" ? "en" : language === "Zh-Hant" ? "zh-Hant" : "zh-CN";
  const t = source => Object.hasOwn(strings, source) ? strings[source] : source;
  const interpolate = (source, values) => source.replace(/\{(\d+)\}/g,
    (match, index) => index < values.length ? String(values[index] ?? "") : match);
  const format = (key, ...values) => interpolate(t(key), values);
  const markup = source => source.replace(/\{\{([A-Za-z0-9_]+)\}\}/g, (_, key) => t(key));

  function translateDocument() {
    document.documentElement.lang = locale;
    const walker = document.createTreeWalker(document.documentElement, NodeFilter.SHOW_TEXT, {
      acceptNode: node => node.parentElement?.closest("script, style, [data-i18n-skip]")
        ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT
    });
    while (walker.nextNode()) {
      const node = walker.currentNode;
      const key = node.nodeValue.trim();
      if (key && /\{\{[A-Za-z0-9_]+\}\}/.test(key)) node.nodeValue = markup(node.nodeValue);
    }
    for (const element of document.querySelectorAll("*")) {
      for (const attribute of ["title", "placeholder", "aria-label", "data-tooltip", "data-help", "data-tip"]) {
        if (element.hasAttribute(attribute)) element.setAttribute(attribute, markup(element.getAttribute(attribute)));
      }
    }
  }

  window.taskbarLyricsI18n = Object.freeze({ language, locale, t, format, translateDocument });
})();
