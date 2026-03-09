const layoutEl = document.getElementById("layout");
const viewportEl = document.getElementById("viewport");
const trackEl = document.getElementById("track");
const currentLineEl = document.getElementById("currentLine");
const nextLineEl = document.getElementById("nextLine");
const incomingLineEl = document.getElementById("incomingLine");
const currentLineTextEl = document.getElementById("currentLineText");
const nextLineTextEl = document.getElementById("nextLineText");
const incomingLineTextEl = document.getElementById("incomingLineText");
const coverEl = document.getElementById("cover");
const coverImageEl = document.getElementById("coverImage");
const coverFallbackEl = document.getElementById("coverFallback");
const root = document.documentElement;

let displayedCurrent = currentLineTextEl?.textContent || "";
let displayedNext = nextLineTextEl?.textContent || "";
let requestedFontSize = 13;
let rowHeightPx = 14;
let rowGapPx = 1;
let linePitchPx = 15;
let isTransitioning = false;
let queuedFrame = null;
let transitionFallbackTimer = 0;
let transitionOpacityAnimation = 0;
let transitionStartTime = 0;
let transitionBaseNextOpacity = 0.72;
let transitionBaseNextScale = 0.923;
let transitionTargetCurrentScale = 1;
let secondaryOpacity = 0.72;
let lastLineProgress = Number.NaN;
let lastCurrentLineIndex = -1;
let lastTrackId = "";
const transitionDurationMs = 560;

function normalizeWeight(weight) {
  const normalized = String(weight || "").trim().toLowerCase();
  switch (normalized) {
    case "light": return "300";
    case "medium": return "500";
    case "semibold": return "600";
    case "bold": return "700";
    default: return "500";
  }
}

function clamp01(value) {
  const parsed = Number(value);
  if (Number.isNaN(parsed)) {
    return 0;
  }
  return Math.max(0, Math.min(1, parsed));
}

function normalizeTrackId(trackId) {
  if (trackId === null || trackId === undefined) {
    return "";
  }

  return String(trackId);
}

function toDisplayLine(line, fallback = " ") {
  const text = (line ?? "").toString().trim();
  return text.length > 0 ? text : fallback;
}

function setTrackOffset(rowCount) {
  trackEl.style.transform = `translateY(${-linePitchPx * rowCount}px)`;
}

function setCurrentLine(line) {
  const safe = toDisplayLine(line, "Waiting for lyrics...");
  if (currentLineTextEl) {
    currentLineTextEl.textContent = safe;
  }
  displayedCurrent = safe;
}

function setSecondaryLine(line) {
  const safe = toDisplayLine(line, " ");
  if (nextLineTextEl) {
    nextLineTextEl.textContent = safe;
  }
  displayedNext = safe;
}

function setIncomingLine(line) {
  if (incomingLineTextEl) {
    incomingLineTextEl.textContent = toDisplayLine(line, " ");
  }
}

function updateSecondaryOpacity(progress) {
  const p = clamp01(progress);
  const target = 0.58 + ((1 - p) * 0.16);
  secondaryOpacity += (target - secondaryOpacity) * 0.28;
  nextLineEl.style.opacity = secondaryOpacity.toFixed(3);
}

function easeOutCubic(t) {
  const x = 1 - clamp01(t);
  return 1 - (x * x * x);
}

function getSizeEase(t) {
  return easeOutCubic(clamp01(t / 0.86));
}

function getFadeOutEase(t) {
  const normalized = clamp01(t / 0.74);
  if (normalized >= 0.97) {
    return 1;
  }

  return easeOutCubic(normalized);
}

function getFadeInEase(t) {
  const normalized = clamp01(t / 0.72);
  if (normalized >= 0.96) {
    return 1;
  }

  return easeOutCubic(normalized);
}

function stopTransitionOpacityAnimation() {
  if (transitionOpacityAnimation) {
    window.cancelAnimationFrame(transitionOpacityAnimation);
    transitionOpacityAnimation = 0;
  }
}

function resetForTrackSwitch(safeCurrent, safeNext, progress, currentLineIndex, trackId) {
  stopTransitionOpacityAnimation();
  if (transitionFallbackTimer) {
    window.clearTimeout(transitionFallbackTimer);
    transitionFallbackTimer = 0;
  }
  queuedFrame = null;
  isTransitioning = false;

  trackEl.classList.add("no-anim");
  trackEl.classList.remove("animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setTrackOffset(0);
  setCurrentLine(safeCurrent);
  setSecondaryLine(safeNext);
  setIncomingLine("");
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  nextLineEl.style.transform = "";
  incomingLineEl.style.opacity = "";
  updateSecondaryOpacity(progress);
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");

  lastLineProgress = clamp01(progress);
  lastCurrentLineIndex = Number.isInteger(currentLineIndex) ? currentLineIndex : -1;
  lastTrackId = trackId;
}

function runTransitionOpacityAnimation(now) {
  if (!isTransitioning) {
    return;
  }

  const elapsed = Math.max(0, now - transitionStartTime);
  const t = clamp01(elapsed / transitionDurationMs);
  const sizeE = getSizeEase(t);
  const fadeOutE = getFadeOutEase(t);
  const fadeInE = getFadeInEase(t);

  currentLineEl.style.opacity = String(0.98 + ((0.16 - 0.98) * fadeOutE));
  nextLineEl.style.opacity = String(transitionBaseNextOpacity + ((0.98 - transitionBaseNextOpacity) * fadeInE));
  incomingLineEl.style.opacity = secondaryOpacity.toFixed(3);
  
  const currentScale = transitionBaseNextScale + ((transitionTargetCurrentScale - transitionBaseNextScale) * sizeE);
  nextLineEl.style.transform = `translateY(var(--primary-offset-y)) scale(${currentScale.toFixed(4)})`;

  if (t < 1) {
    transitionOpacityAnimation = window.requestAnimationFrame(runTransitionOpacityAnimation);
  } else {
    transitionOpacityAnimation = 0;
  }
}

function applyFrame(safeCurrent, safeNext, progress, currentLineIndex) {
  const p = clamp01(progress);
  const hasLineIndex = Number.isInteger(currentLineIndex) && currentLineIndex >= 0;

  if (hasLineIndex) {
    if (!Number.isInteger(lastCurrentLineIndex) || lastCurrentLineIndex < 0) {
      setCurrentLine(safeCurrent);
      setSecondaryLine(safeNext);
      updateSecondaryOpacity(p);
      lastCurrentLineIndex = currentLineIndex;
      lastLineProgress = p;
      return;
    }

    if (currentLineIndex !== lastCurrentLineIndex) {
      startTransition(safeCurrent, safeNext, p, currentLineIndex);
    } else {
      setSecondaryLine(safeNext);
      updateSecondaryOpacity(p);
    }

    lastLineProgress = p;
    return;
  }

  const isRepeatedPromotionCandidate =
    safeCurrent === displayedCurrent &&
    displayedNext === displayedCurrent &&
    safeNext !== displayedNext;
  const isUnchangedTextFrame =
    safeCurrent === displayedCurrent &&
    safeNext === displayedNext;
  const wrappedProgressForSameText =
    isUnchangedTextFrame &&
    Number.isFinite(lastLineProgress) &&
    (lastLineProgress - p) > 0.16 &&
    lastLineProgress > 0.62;

  if (safeCurrent !== displayedCurrent || isRepeatedPromotionCandidate || wrappedProgressForSameText) {
    startTransition(safeCurrent, safeNext, p, -1);
  } else {
    setSecondaryLine(safeNext);
    updateSecondaryOpacity(p);
  }

  lastLineProgress = p;
}

function updateMetrics() {
  const viewportDescenderBufferPx = 2;
  const measuredViewportHeight = viewportEl.clientHeight || 30;
  const hostHeight = Math.max(26, measuredViewportHeight - viewportDescenderBufferPx);
  rowHeightPx = Math.max(13, Math.floor(hostHeight / 2));
  rowGapPx = Math.max(0, hostHeight - (rowHeightPx * 2));
  linePitchPx = rowHeightPx + rowGapPx;
  
  // Provide baseline compensation for descenders
  // In Fluent Design, placing baseline consistently is crucial.
  const baselineOffsetPx = Math.max(0, Math.floor(rowHeightPx * 0.1));
  
  const currentSizeMax = Math.max(11.2, rowHeightPx * 0.88);
  const currentSize = Math.min(requestedFontSize, currentSizeMax);
  const nextSize = Math.max(9, currentSize * 0.92);
  
  const currentScale = 1;
  const nextScale = nextSize / currentSize;
  
  root.style.setProperty("--row-height", `${rowHeightPx}px`);
  root.style.setProperty("--row-gap", `${rowGapPx}px`);
  root.style.setProperty("--line-pitch", `${linePitchPx}px`);
  root.style.setProperty("--font-size", `${currentSize.toFixed(2)}px`);
  root.style.setProperty("--current-scale", `${currentScale.toFixed(4)}`);
  root.style.setProperty("--next-scale", `${nextScale.toFixed(4)}`);
  root.style.setProperty("--baseline-offset", `${baselineOffsetPx}px`);
  setTrackOffset(0);
}

function finalizeTransition(promotedCurrent, upcomingNext, progress, promotedLineIndex = -1) {
  const incomingEndOpacity = Number.parseFloat(window.getComputedStyle(incomingLineEl).opacity || "0.72");

  trackEl.classList.add("no-anim");
  stopTransitionOpacityAnimation();
  setCurrentLine(promotedCurrent);
  setSecondaryLine(upcomingNext);
  setIncomingLine("");
  trackEl.classList.remove("animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setTrackOffset(0);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  secondaryOpacity = Number.isFinite(incomingEndOpacity) ? incomingEndOpacity : 0.72;
  incomingLineEl.style.opacity = "";
  nextLineEl.style.transform = "";
  updateSecondaryOpacity(progress);
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");
  isTransitioning = false;
  lastLineProgress = clamp01(progress);
  if (Number.isInteger(promotedLineIndex) && promotedLineIndex >= 0) {
    lastCurrentLineIndex = promotedLineIndex;
  }

  if (queuedFrame) {
    const frame = queuedFrame;
    queuedFrame = null;
    applyFrame(frame.current, frame.next, frame.progress, frame.currentLineIndex);
  }
}

function startTransition(newCurrent, newNext, progress, currentLineIndex = -1) {
  if (isTransitioning) {
    queuedFrame = { current: newCurrent, next: newNext, progress, currentLineIndex };
    return;
  }

  isTransitioning = true;
  const promoted = toDisplayLine(newCurrent, "Waiting for lyrics...");
  const upcoming = toDisplayLine(newNext, " ");
  transitionBaseNextOpacity = secondaryOpacity;
  
  const nextScaleFallback = Number.parseFloat(window.getComputedStyle(root).getPropertyValue("--next-scale") || "0.923");
  transitionBaseNextScale = nextScaleFallback;
  transitionTargetCurrentScale = 1;
  
  transitionStartTime = 0;
  stopTransitionOpacityAnimation();

  trackEl.classList.add("no-anim");
  trackEl.classList.remove("animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setTrackOffset(0);
  if (nextLineTextEl) {
    nextLineTextEl.textContent = promoted;
  }
  setIncomingLine(upcoming);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  nextLineEl.style.transform = `translateY(var(--primary-offset-y)) scale(${transitionBaseNextScale})`;
  incomingLineEl.style.opacity = secondaryOpacity.toFixed(3);
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");

  const onTransitionEnd = (event) => {
    if (!event || event.target !== trackEl || event.propertyName !== "transform") {
      return;
    }

    trackEl.removeEventListener("transitionend", onTransitionEnd);
    if (transitionFallbackTimer) {
      window.clearTimeout(transitionFallbackTimer);
      transitionFallbackTimer = 0;
    }
    finalizeTransition(promoted, upcoming, progress, currentLineIndex);
  };

  trackEl.addEventListener("transitionend", onTransitionEnd);
  window.requestAnimationFrame(() => {
    transitionStartTime = window.performance.now();
    transitionOpacityAnimation = window.requestAnimationFrame(runTransitionOpacityAnimation);
    currentLineEl.classList.add("leaving");
    nextLineEl.classList.add("promoting");
    trackEl.classList.add("animating");
    window.requestAnimationFrame(() => setTrackOffset(1));
  });
  transitionFallbackTimer = window.setTimeout(() => {
    trackEl.removeEventListener("transitionend", onTransitionEnd);
    finalizeTransition(promoted, upcoming, progress, currentLineIndex);
  }, transitionDurationMs + 120);
}

updateMetrics();
setCurrentLine(displayedCurrent);
setSecondaryLine(displayedNext);
setIncomingLine("");
updateSecondaryOpacity(0);

if (typeof ResizeObserver !== "undefined") {
  new ResizeObserver(updateMetrics).observe(layoutEl);
} else {
  window.addEventListener("resize", updateMetrics);
}

window.taskbarLyrics = {
  setLyrics(current, next, progress, currentLineIndex, trackId) {
    const safeCurrent = toDisplayLine(current, "Waiting for lyrics...");
    const safeNext = toDisplayLine(next, " ");
    const p = clamp01(progress);
    const lineIndex = Number(currentLineIndex);
    const normalizedTrackId = normalizeTrackId(trackId);
    if (normalizedTrackId.length > 0 && normalizedTrackId !== lastTrackId) {
      resetForTrackSwitch(safeCurrent, safeNext, p, lineIndex, normalizedTrackId);
      return;
    }

    if (normalizedTrackId.length > 0) {
      lastTrackId = normalizedTrackId;
    }

    applyFrame(safeCurrent, safeNext, p, lineIndex);
  },

  setCover(dataUri, fallbackText, fallbackColor) {
    const uri = (dataUri ?? "").toString().trim();
    const text = toDisplayLine(fallbackText, "N").slice(0, 1).toUpperCase();
    if (coverFallbackEl) {
      coverFallbackEl.textContent = text;
    }

    if (coverEl && fallbackColor && CSS.supports("color", fallbackColor)) {
      coverEl.style.background = fallbackColor;
    }

    if (coverImageEl) {
      if (uri.length > 0) {
        if (coverImageEl.src !== uri) {
          coverImageEl.classList.remove("loaded");
          coverImageEl.onload = () => coverImageEl.classList.add("loaded");
          coverImageEl.src = uri;
        } else {
          coverImageEl.classList.add("loaded");
        }
        
        if (coverFallbackEl) {
          coverFallbackEl.style.display = "none";
        }
      } else {
        coverImageEl.classList.remove("loaded");
        coverImageEl.removeAttribute("src");
        if (coverFallbackEl) {
          coverFallbackEl.style.display = "flex";
        }
      }
    }
  },

  applyStyle(payload) {
    if (!payload || typeof payload !== "object") {
      return;
    }

    root.style.setProperty("--font-family", payload.fontFamily || "\"SF Pro Display\", \"Segoe UI Variable Display\", \"Segoe UI Variable Text\", \"Microsoft YaHei UI\", sans-serif");
    requestedFontSize = Number(payload.fontSize) || 13;
    updateMetrics();
    root.style.setProperty("--font-weight", normalizeWeight(payload.fontWeight));

    if (payload.primaryColor && CSS.supports("color", payload.primaryColor)) {
      root.style.setProperty("--primary", payload.primaryColor);
    }

    if (payload.secondaryColor && CSS.supports("color", payload.secondaryColor)) {
      root.style.setProperty("--secondary", payload.secondaryColor);
    }
  }
};