(() => {
  const rect = element => {
    if (!element) return null;
    const r = element.getBoundingClientRect();
    return { x: r.x, y: r.y, width: r.width, height: r.height, right: r.right, bottom: r.bottom };
  };
  const stateKey = "__taskbarLyricsLayoutDiagnostics";
  if (!window[stateKey]) {
    const state = { events: [], droppedEvents: 0 };
    window[stateKey] = state;
    for (const type of ["pointerdown", "click", "pointerover"]) {
      document.addEventListener(type, event => {
        const target = event.target instanceof Element ? event.target.closest(".cover, .lyrics-pane, .layout") : null;
        if (!target || (type === "pointerover" && event.relatedTarget instanceof Node && target.contains(event.relatedTarget))) return;
        if (state.events.length >= 24) { state.droppedEvents++; return; }
        state.events.push({
          type, time: Date.now(), target: target.classList.contains("cover") ? "cover" :
            target.classList.contains("lyrics-pane") ? "lyrics" : "layout",
          x: event.clientX, y: event.clientY, button: event.button, bounds: rect(target),
          viewportWidth: window.innerWidth, viewportHeight: window.innerHeight
        });
      }, true);
    }
  }
  const measure = selector => {
    const element = document.querySelector(selector);
    if (!element) return null;
    const style = getComputedStyle(element);
    const range = document.createRange();
    range.selectNodeContents(element);
    const ink = element.classList.contains("line-text") && typeof range.getBoundingClientRect === "function"
      ? range.getBoundingClientRect() : null;
    return {
      bounds: rect(element), clientWidth: element.clientWidth, clientHeight: element.clientHeight,
      textBounds: ink ? { x: ink.x, y: ink.y, width: ink.width, height: ink.height, bottom: ink.bottom } : null,
      scrollWidth: element.scrollWidth, scrollHeight: element.scrollHeight,
      fontFamily: style.fontFamily, fontSize: style.fontSize, lineHeight: style.lineHeight,
      paddingTop: style.paddingTop, paddingBottom: style.paddingBottom,
      overflowX: style.overflowX, overflowY: style.overflowY,
      display: style.display, visibility: style.visibility, opacity: style.opacity,
      transform: style.transform, top: style.top, boxSizing: style.boxSizing
    };
  };
  const root = document.documentElement;
  const rootStyle = getComputedStyle(root);
  const variables = Object.fromEntries([
    "--font-size", "--current-size", "--next-size", "--row-height", "--row-gap", "--line-pitch",
    "--cover-size", "--cover-gap", "--viewport-descender-buffer", "--lyrics-pane-padding-top",
    "--primary-offset-y", "--secondary-offset-y", "--line-text-padding-bottom"
  ].map(name => [name, rootStyle.getPropertyValue(name)]));
  const elements = Object.fromEntries([
    ".layout", ".shell", ".cover", ".lyrics-pane", ".lyrics-layer", ".viewport", ".track",
    ".current-line", ".current-line .line-text", ".next-line", ".next-line .line-text"
  ].map(selector => [selector, measure(selector)]));
  const state = window[stateKey];
  const snapshot = {
    readyState: document.readyState, fontsStatus: document.fonts?.status ?? null,
    fontAvailable: document.fonts?.check('14px "Source Han Sans SC"') ?? null,
    devicePixelRatio: window.devicePixelRatio, innerWidth: window.innerWidth, innerHeight: window.innerHeight,
    screen: { width: screen.width, height: screen.height, availWidth: screen.availWidth, availHeight: screen.availHeight },
    visualViewport: window.visualViewport ? { width: visualViewport.width, height: visualViewport.height, scale: visualViewport.scale } : null,
    rootClasses: root.className, layoutClasses: document.querySelector(".layout")?.className,
    variables, elements, pointerEvents: state.events.splice(0), droppedEvents: state.droppedEvents
  };
  state.droppedEvents = 0;
  return snapshot;
})()
