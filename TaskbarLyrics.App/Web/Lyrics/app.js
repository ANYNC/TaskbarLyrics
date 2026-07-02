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
const coverImageNextEl = document.getElementById("coverImageNext");
const coverFallbackEl = document.getElementById("coverFallback");
const root = document.documentElement;
const spectrumBarEls = Array.from(document.querySelectorAll(".spectrum span"));

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
let transitionGeneration = 0;
let transitionStartTime = 0;
let transitionBaseNextOpacity = 0.72;
let transitionBaseNextFontSize = 12;
let transitionTargetCurrentFontSize = 13;
let secondaryOpacity = 0.72;
let lastLineProgress = Number.NaN;
let lastCurrentLineIndex = -1;
let lastTrackId = "";
let metricsUpdatePending = false;
const transitionDurationMs = 560;
const trackSwitchSearchMinVisibleMs = 900;
const coverSwapDelayMs = 180;
const coverSwitchMinVisibleMs = 420;
const SEARCHING_TEXT = "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd...";
const LEGACY_SEARCHING_TEXT = "\u6b63\u5728\u5339\u914d\u6b4c\u8bcd...";
let trackSwitchSearchStartedAt = 0;
let delayedFrameTimer = 0;
let coverUpdateTimer = 0;
let coverStateTimer = 0;
let coverSwitchStartedAt = 0;
let activeCoverImageEl = coverImageEl;
let standbyCoverImageEl = coverImageNextEl;
let currentCoverUri = "";
let coverGeneration = 0;
let isSpectrumMode = false;
let hasAudioDrivenSpectrum = false;
let spectrumAnimationFrame = 0;
let lastSpectrumFrameTime = 0;
const spectrumTargets = spectrumBarEls.map(() => 0);
const spectrumVisuals = spectrumBarEls.map(() => 0);
const spectrumSilence = spectrumBarEls.map(() => 0);
const spectrumTuning = {
  rise: 0.56,
  fall: 0.24,
  minHeight: 5,
  heightRange: 17,
  opacity: 0.78
};

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
  const safe = toDisplayLine(line, SEARCHING_TEXT);
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
  // Follow the same direction as slide easing, but settle slightly earlier to reduce tail-end perceptual jumps.
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

function isSearchingLine(line) {
  return line === SEARCHING_TEXT || line === LEGACY_SEARCHING_TEXT;
}

function setDisplayMode(showSpectrum) {
  const shouldShowSpectrum = Boolean(showSpectrum);
  if (isSpectrumMode === shouldShowSpectrum) {
    return;
  }

  isSpectrumMode = shouldShowSpectrum;
  layoutEl.classList.toggle("spectrum-mode", shouldShowSpectrum);
}

function setSpectrumTargetValues(values) {
  const hasValues = Array.isArray(values) && values.length > 0;
  for (let i = 0; i < spectrumTargets.length; i++) {
    spectrumTargets[i] = hasValues ? clamp01(values[i] ?? 0) : 0;
  }
}

function startSpectrumRenderer() {
  if (spectrumAnimationFrame) {
    return;
  }

  lastSpectrumFrameTime = 0;
  spectrumAnimationFrame = window.requestAnimationFrame(renderSpectrumFrame);
}

function stopSpectrumRenderer() {
  if (!spectrumAnimationFrame) {
    return;
  }

  window.cancelAnimationFrame(spectrumAnimationFrame);
  spectrumAnimationFrame = 0;
  lastSpectrumFrameTime = 0;
}

function renderSpectrumFrame(now) {
  if (!lastSpectrumFrameTime) {
    lastSpectrumFrameTime = now;
  }

  const elapsedFrames = Math.max(0.5, Math.min(2.4, (now - lastSpectrumFrameTime) / 16.67));
  lastSpectrumFrameTime = now;
  let isSettled = true;

  for (let i = 0; i < spectrumBarEls.length; i++) {
    const target = spectrumTargets[i] ?? 0;
    const current = spectrumVisuals[i] ?? 0;
    const baseRate = target > current ? spectrumTuning.rise : spectrumTuning.fall;
    const rate = 1 - Math.pow(1 - baseRate, elapsedFrames);
    const next = current + ((target - current) * rate);
    spectrumVisuals[i] = Math.abs(next - target) < 0.002 ? target : next;

    if (Math.abs(spectrumVisuals[i] - target) >= 0.002) {
      isSettled = false;
    }

    const level = spectrumVisuals[i];
    const height = spectrumTuning.minHeight + (level * spectrumTuning.heightRange);
    const bar = spectrumBarEls[i];
    bar.style.height = `${height.toFixed(2)}px`;
    bar.style.transform = "scaleY(1)";
    bar.style.opacity = spectrumTuning.opacity.toFixed(3);
  }

  if (hasAudioDrivenSpectrum || !isSettled) {
    spectrumAnimationFrame = window.requestAnimationFrame(renderSpectrumFrame);
  } else {
    spectrumAnimationFrame = 0;
    lastSpectrumFrameTime = 0;
  }
}

function setAudioDrivenSpectrum(values) {
  if (!Array.isArray(values) || values.length === 0) {
    hasAudioDrivenSpectrum = false;
    layoutEl.classList.remove("spectrum-audio-driven");
    setSpectrumTargetValues([]);
    startSpectrumRenderer();
    return;
  }

  hasAudioDrivenSpectrum = true;
  layoutEl.classList.add("spectrum-audio-driven");
  setSpectrumTargetValues(values);
  startSpectrumRenderer();
}

function clearSpectrumBars() {
  hasAudioDrivenSpectrum = false;
  setSpectrumTargetValues([]);
  stopSpectrumRenderer();
  layoutEl.classList.remove("spectrum-audio-driven");
  for (let i = 0; i < spectrumBarEls.length; i++) {
    spectrumVisuals[i] = 0;
    const bar = spectrumBarEls[i];
    bar.style.height = "";
    bar.style.transform = "";
    bar.style.opacity = "";
  }
}

function setCoverLoadingState(isLoading) {
  if (!coverEl) {
    return;
  }

  if (coverStateTimer) {
    window.clearTimeout(coverStateTimer);
    coverStateTimer = 0;
  }

  if (isLoading) {
    coverSwitchStartedAt = window.performance.now();
    coverEl.classList.add("switching");
    return;
  }

  const elapsed = coverSwitchStartedAt > 0
    ? window.performance.now() - coverSwitchStartedAt
    : coverSwitchMinVisibleMs;
  const delay = Math.max(0, coverSwitchMinVisibleMs - elapsed);
  coverStateTimer = window.setTimeout(() => {
    coverStateTimer = 0;
    coverSwitchStartedAt = 0;
    coverEl.classList.remove("switching");
  }, delay);
}

function clearCoverUpdateTimer() {
  if (coverUpdateTimer) {
    window.clearTimeout(coverUpdateTimer);
    coverUpdateTimer = 0;
  }
}

function swapCoverImageLayers() {
  const previous = activeCoverImageEl;
  activeCoverImageEl = standbyCoverImageEl;
  standbyCoverImageEl = previous;
}

function clearImageElement(imageEl) {
  if (!imageEl) {
    return;
  }

  imageEl.onload = null;
  imageEl.onerror = null;
  imageEl.style.opacity = "0";
  imageEl.removeAttribute("src");
}

function crossfadeToCoverImage(uri, generation, onDone) {
  if (!activeCoverImageEl || !standbyCoverImageEl) {
    if (typeof onDone === "function") {
      onDone();
    }
    return;
  }

  const incoming = standbyCoverImageEl;
  const outgoing = activeCoverImageEl;
  incoming.onload = null;
  incoming.onerror = null;
  incoming.style.opacity = "0";
  incoming.src = uri;

  window.requestAnimationFrame(() => {
    if (generation !== coverGeneration) {
      return;
    }

    incoming.style.opacity = "1";
    outgoing.style.opacity = "0";
    if (coverFallbackEl) {
      coverFallbackEl.style.opacity = "0";
    }
  });

  coverUpdateTimer = window.setTimeout(() => {
    coverUpdateTimer = 0;
    if (generation !== coverGeneration) {
      return;
    }

    clearImageElement(outgoing);
    swapCoverImageLayers();
    currentCoverUri = uri;
    if (coverFallbackEl) {
      coverFallbackEl.style.display = "none";
      coverFallbackEl.style.opacity = "1";
    }
    if (typeof onDone === "function") {
      onDone();
    }
  }, 460);
}

function applyFallbackCover(text, fallbackColor) {
  if (coverFallbackEl) {
    coverFallbackEl.textContent = text;
  }

  if (coverEl && fallbackColor && CSS.supports("color", fallbackColor)) {
    coverEl.style.backgroundColor = fallbackColor;
  }
}

function scheduleFallbackCoverUpdate(text, fallbackColor, onApplied) {
  clearCoverUpdateTimer();
  coverUpdateTimer = window.setTimeout(() => {
    coverUpdateTimer = 0;
    applyFallbackCover(text, fallbackColor);
    if (typeof onApplied === "function") {
      onApplied();
    }
  }, coverSwapDelayMs);
}

function clearDelayedFrameTimer() {
  if (delayedFrameTimer) {
    window.clearTimeout(delayedFrameTimer);
    delayedFrameTimer = 0;
  }
}

function shouldHoldAfterSearch(frame) {
  return trackSwitchSearchStartedAt > 0 &&
    isSearchingLine(displayedCurrent) &&
    Number.isInteger(frame.currentLineIndex) &&
    frame.currentLineIndex >= 0 &&
    !isSearchingLine(frame.current);
}

function applyFrameAfterSearchDwell(frame) {
  clearDelayedFrameTimer();
  if (!shouldHoldAfterSearch(frame)) {
    applyFrame(frame.current, frame.next, frame.progress, frame.currentLineIndex);
    return;
  }

  const elapsed = window.performance.now() - trackSwitchSearchStartedAt;
  const delay = Math.max(0, trackSwitchSearchMinVisibleMs - elapsed);
  delayedFrameTimer = window.setTimeout(() => {
    delayedFrameTimer = 0;
    trackSwitchSearchStartedAt = 0;
    applyFrame(frame.current, frame.next, frame.progress, frame.currentLineIndex);
  }, delay);
}

function cancelActiveTransition() {
  transitionGeneration++;
  if (transitionFallbackTimer) {
    window.clearTimeout(transitionFallbackTimer);
    transitionFallbackTimer = 0;
  }
  clearDelayedFrameTimer();
  stopTransitionOpacityAnimation();
  isTransitioning = false;
  queuedFrame = null;
  trackEl.classList.add("no-anim");
  trackEl.classList.remove("animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setTrackOffset(0);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  nextLineEl.style.fontSize = "";
  incomingLineEl.style.opacity = secondaryOpacity.toFixed(3);
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");
}

function resetForTrackSwitch(safeCurrent, safeNext, progress, currentLineIndex, trackId) {
  cancelActiveTransition();
  lastTrackId = trackId;
  lastCurrentLineIndex = -1;
  lastLineProgress = 0;
  trackSwitchSearchStartedAt = window.performance.now();
  setCoverLoadingState(true);

  const hasLyricFrame = Number.isInteger(currentLineIndex) && currentLineIndex >= 0 && !isSearchingLine(safeCurrent);

  if (!isSearchingLine(displayedCurrent)) {
    startTransition(SEARCHING_TEXT, " ", 0, -1);
    if (hasLyricFrame) {
      queuedFrame = { current: safeCurrent, next: safeNext, progress, currentLineIndex };
    }
  } else {
    setSecondaryLine(" ");
    updateSecondaryOpacity(0);
    if (hasLyricFrame) {
      applyFrameAfterSearchDwell({ current: safeCurrent, next: safeNext, progress, currentLineIndex });
    }
  }
}


function runTransitionOpacityAnimation(now) {
  if (!isTransitioning) {
    return;
  }

  const elapsed = Math.max(0, now - transitionStartTime);
  const t = clamp01(elapsed / transitionDurationMs);
  const e = easeOutCubic(t);
  const sizeE = getSizeEase(t);
  const fadeOutE = getFadeOutEase(t);
  const fadeInE = getFadeInEase(t);

  currentLineEl.style.opacity = String(0.98 + ((0.16 - 0.98) * fadeOutE));
  nextLineEl.style.opacity = String(transitionBaseNextOpacity + ((0.98 - transitionBaseNextOpacity) * fadeInE));
  incomingLineEl.style.opacity = secondaryOpacity.toFixed(3);
  nextLineEl.style.fontSize = `${(transitionBaseNextFontSize + ((transitionTargetCurrentFontSize - transitionBaseNextFontSize) * sizeE)).toFixed(3)}px`;

  if (t < 1) {
    transitionOpacityAnimation = window.requestAnimationFrame(runTransitionOpacityAnimation);
  } else {
    transitionOpacityAnimation = 0;
  }
}

function applyFrame(safeCurrent, safeNext, progress, currentLineIndex) {
  if (isTransitioning) {
    queuedFrame = { current: safeCurrent, next: safeNext, progress, currentLineIndex };
    return;
  }

  const p = clamp01(progress);
  const hasLineIndex = Number.isInteger(currentLineIndex) && currentLineIndex >= 0;

  if (hasLineIndex) {
    if (!Number.isInteger(lastCurrentLineIndex) || lastCurrentLineIndex < 0) {
      // If we were in a non-lyric state, slide into the first line smoothly.
      if (isSearchingLine(displayedCurrent)) {
        startTransition(safeCurrent, safeNext, p, currentLineIndex);
      } else {
        setCurrentLine(safeCurrent);
        setSecondaryLine(safeNext);
        updateSecondaryOpacity(p);
      }
      lastCurrentLineIndex = currentLineIndex;
      lastLineProgress = p;
      return;
    }

    if (currentLineIndex !== lastCurrentLineIndex) {
      startTransition(safeCurrent, safeNext, p, currentLineIndex);
    } else {
      if (safeCurrent !== displayedCurrent) {
        setCurrentLine(safeCurrent);
      }
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
  if (isTransitioning) {
    metricsUpdatePending = true;
    return;
  }

  metricsUpdatePending = false;
  // WPF host extends the WebView 2px downward for descender safety; exclude that buffer from row metrics.
  const viewportDescenderBufferPx = 2;
  const measuredViewportHeight = viewportEl.clientHeight || 30;
  const hostHeight = Math.max(26, measuredViewportHeight - viewportDescenderBufferPx);
  rowHeightPx = Math.max(13, Math.floor(hostHeight / 2));
  rowGapPx = Math.max(0, hostHeight - (rowHeightPx * 2));
  linePitchPx = rowHeightPx + rowGapPx;
  const currentSizeMax = Math.max(11.2, rowHeightPx * 0.92);
  currentSize = Math.min(requestedFontSize, currentSizeMax);
  const nextSize = Math.max(9, currentSize * 0.92);
  root.style.setProperty("--row-height", `${rowHeightPx}px`);
  root.style.setProperty("--row-gap", `${rowGapPx}px`);
  root.style.setProperty("--line-pitch", `${linePitchPx}px`);
  root.style.setProperty("--current-size", `${currentSize.toFixed(2)}px`);
  root.style.setProperty("--next-size", `${nextSize.toFixed(2)}px`);
  setTrackOffset(0);
}

function finalizeTransition(promotedCurrent, upcomingNext, progress, promotedLineIndex = -1) {
  const incomingEndOpacity = Number.parseFloat(window.getComputedStyle(incomingLineEl).opacity || "0.72");

  // Freeze transitions while swapping layers to avoid visible "grow then shrink" rebound.
  trackEl.classList.add("no-anim");
  stopTransitionOpacityAnimation();
  setCurrentLine(promotedCurrent);
  setSecondaryLine(upcomingNext);
  setIncomingLine("");
  trackEl.classList.remove("animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setTrackOffset(0);
  // Reset inline opacity channels while transitions are disabled; otherwise a brief flash can appear.
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  secondaryOpacity = Number.isFinite(incomingEndOpacity) ? incomingEndOpacity : 0.72;
  incomingLineEl.style.opacity = "";
  nextLineEl.style.fontSize = "";
  updateSecondaryOpacity(progress);
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");
  isTransitioning = false;
  lastLineProgress = clamp01(progress);
  if (Number.isInteger(promotedLineIndex) && promotedLineIndex >= 0) {
    lastCurrentLineIndex = promotedLineIndex;
  }
  if (metricsUpdatePending) {
    updateMetrics();
  }

  if (queuedFrame) {
    const frame = queuedFrame;
    queuedFrame = null;
    applyFrameAfterSearchDwell(frame);
  }
}

function startTransition(newCurrent, newNext, progress, currentLineIndex = -1) {
  if (isTransitioning) {
    queuedFrame = { current: newCurrent, next: newNext, progress, currentLineIndex };
    return;
  }

  isTransitioning = true;
  const generation = ++transitionGeneration;
  const promoted = toDisplayLine(newCurrent, SEARCHING_TEXT);
  const upcoming = toDisplayLine(newNext, " ");
  transitionBaseNextOpacity = secondaryOpacity;
  transitionBaseNextFontSize = Number.parseFloat(window.getComputedStyle(nextLineEl).fontSize || "12");
  transitionTargetCurrentFontSize = Number.parseFloat(window.getComputedStyle(currentLineEl).fontSize || "13");
  transitionStartTime = 0;
  stopTransitionOpacityAnimation();

  // Start from baseline state first so promoting font-size always animates from second-line size.
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
  nextLineEl.style.fontSize = `${transitionBaseNextFontSize.toFixed(3)}px`;
  incomingLineEl.style.opacity = secondaryOpacity.toFixed(3);
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");

  const onTransitionEnd = (event) => {
    if (!event || event.target !== trackEl || event.propertyName !== "transform") {
      return;
    }

    trackEl.removeEventListener("transitionend", onTransitionEnd);
    if (generation !== transitionGeneration) {
      return;
    }

    if (transitionFallbackTimer) {
      window.clearTimeout(transitionFallbackTimer);
      transitionFallbackTimer = 0;
    }
    finalizeTransition(promoted, upcoming, progress, currentLineIndex);
  };

  trackEl.addEventListener("transitionend", onTransitionEnd);
  window.requestAnimationFrame(() => {
    if (generation !== transitionGeneration) {
      return;
    }

    transitionStartTime = window.performance.now();
    transitionOpacityAnimation = window.requestAnimationFrame(runTransitionOpacityAnimation);
    currentLineEl.classList.add("leaving");
    nextLineEl.classList.add("promoting");
    trackEl.classList.add("animating");
    window.requestAnimationFrame(() => {
      if (generation === transitionGeneration) {
        setTrackOffset(1);
      }
    });
  });
  transitionFallbackTimer = window.setTimeout(() => {
    trackEl.removeEventListener("transitionend", onTransitionEnd);
    if (generation !== transitionGeneration) {
      return;
    }

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
  setLyrics(current, next, progress, currentLineIndex, trackId, isPureMusic, isPlaying) {
    const safeCurrent = toDisplayLine(current, SEARCHING_TEXT);
    const safeNext = toDisplayLine(next, " ");
    const p = clamp01(progress);
    const lineIndex = Number(currentLineIndex);
    const normalizedTrackId = normalizeTrackId(trackId);
    const shouldShowSpectrum = Boolean(isPureMusic);
    const shouldAnimateSpectrum = isPlaying !== false;

    if (shouldShowSpectrum) {
      if (normalizedTrackId.length > 0) {
        lastTrackId = normalizedTrackId;
      }

      cancelActiveTransition();
      trackSwitchSearchStartedAt = 0;
      setCurrentLine(safeCurrent);
      setSecondaryLine(" ");
      setIncomingLine("");
      lastCurrentLineIndex = Number.isInteger(lineIndex) ? lineIndex : -1;
      lastLineProgress = p;
      setDisplayMode(true);
      if (!shouldAnimateSpectrum) {
        setAudioDrivenSpectrum(spectrumSilence);
      }
      return;
    }

    setDisplayMode(false);
    clearSpectrumBars();

    if (normalizedTrackId.length > 0 && normalizedTrackId !== lastTrackId) {
      resetForTrackSwitch(safeCurrent, safeNext, p, lineIndex, normalizedTrackId);
      return;
    }

    if (normalizedTrackId.length > 0) {
      lastTrackId = normalizedTrackId;
    }

    applyFrame(safeCurrent, safeNext, p, lineIndex);
  },

  setSpectrum(values) {
    if (!isSpectrumMode) {
      return;
    }

    setAudioDrivenSpectrum(values);
  },

  setSpectrumTuning(payload) {
    if (!payload || typeof payload !== "object") {
      return;
    }

    spectrumTuning.rise = Math.max(0.02, Math.min(1, Number(payload.rise) || spectrumTuning.rise));
    spectrumTuning.fall = Math.max(0.02, Math.min(1, Number(payload.fall) || spectrumTuning.fall));
    spectrumTuning.minHeight = Math.max(1, Math.min(18, Number(payload.minHeight) || spectrumTuning.minHeight));
    spectrumTuning.heightRange = Math.max(2, Math.min(40, Number(payload.heightRange) || spectrumTuning.heightRange));
    spectrumTuning.opacity = Math.max(0.2, Math.min(1, Number(payload.opacity) || spectrumTuning.opacity));
    if (isSpectrumMode) {
      startSpectrumRenderer();
    }
  },

  setCover(dataUri, fallbackText, fallbackColor) {
    const uri = (dataUri ?? "").toString().trim();
    const text = toDisplayLine(fallbackText, "N").slice(0, 1).toUpperCase();
    const generation = ++coverGeneration;
    clearCoverUpdateTimer();

    if (uri.length > 0 && uri === currentCoverUri) {
      setCoverLoadingState(false);
      return;
    }

    setCoverLoadingState(true);

    if (uri.length > 0) {
      const preloader = new Image();
      preloader.onload = () => {
        if (generation !== coverGeneration) {
          return;
        }

        crossfadeToCoverImage(uri, generation, () => setCoverLoadingState(false));
      };
      preloader.onerror = () => {
        if (generation !== coverGeneration) {
          return;
        }

        scheduleFallbackCoverUpdate(text, fallbackColor, () => {
          if (coverFallbackEl) {
            coverFallbackEl.style.display = "flex";
            coverFallbackEl.style.opacity = "1";
          }
          clearImageElement(activeCoverImageEl);
          clearImageElement(standbyCoverImageEl);
          currentCoverUri = "";
          setCoverLoadingState(false);
        });
      };
      window.setTimeout(() => {
        if (generation !== coverGeneration) {
          return;
        }

        preloader.src = uri;
      }, coverSwapDelayMs);
      return;
    }

    scheduleFallbackCoverUpdate(text, fallbackColor, () => {
      if (coverFallbackEl) {
        coverFallbackEl.style.display = "flex";
        coverFallbackEl.style.opacity = "1";
      }
      clearImageElement(activeCoverImageEl);
      clearImageElement(standbyCoverImageEl);
      currentCoverUri = "";
      setCoverLoadingState(false);
    });
  },

  applyStyle(payload) {
    if (!payload || typeof payload !== "object") {
      return;
    }

    root.style.setProperty("--font-family", payload.fontFamily || "\"SF Pro Display\", \"Segoe UI Variable Display\", \"Segoe UI Variable Text\", \"Microsoft YaHei UI\", sans-serif");
    requestedFontSize = Number(payload.fontSize) || 13;
    root.style.setProperty("--font-size", `${requestedFontSize}px`);
    updateMetrics();
    root.style.setProperty("--font-weight", normalizeWeight(payload.fontWeight));

    if (payload.primaryColor && CSS.supports("color", payload.primaryColor)) {
      root.style.setProperty("--primary", payload.primaryColor);
    }

    if (payload.secondaryColor && CSS.supports("color", payload.secondaryColor)) {
      root.style.setProperty("--secondary", payload.secondaryColor);
    }

    if (payload.surfaceColor && CSS.supports("background-color", payload.surfaceColor)) {
      root.style.setProperty("--surface-color", payload.surfaceColor);
    }

    if (payload.surfaceShadow && CSS.supports("box-shadow", payload.surfaceShadow)) {
      root.style.setProperty("--surface-shadow", payload.surfaceShadow);
    }

    if (payload.textShadow && CSS.supports("text-shadow", payload.textShadow)) {
      root.style.setProperty("--text-shadow", payload.textShadow);
    }

    if (payload.showCover === false) {
      document.body.classList.add("hide-cover");
    } else {
      document.body.classList.remove("hide-cover");
    }
  }
};
