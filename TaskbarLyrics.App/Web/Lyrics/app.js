const layoutEl = document.getElementById("layout");
const viewportEl = document.getElementById("viewport");
const trackEl = document.getElementById("track");
const currentLineEl = document.getElementById("currentLine");
const nextLineEl = document.getElementById("nextLine");
const incomingLineEl = document.getElementById("incomingLine");
const currentLineTextEl = document.getElementById("currentLineText");
const currentLineScanTextEl = document.getElementById("currentLineScanText");
const nextLineTextEl = document.getElementById("nextLineText");
const nextLineScanTextEl = document.getElementById("nextLineScanText");
const incomingLineTextEl = document.getElementById("incomingLineText");
const incomingTranslationPairEl = document.getElementById("incomingTranslationPair");
const incomingTranslationOriginalLineEl = document.getElementById("incomingTranslationOriginalLine");
const incomingTranslationOriginalTextEl = document.getElementById("incomingTranslationOriginalText");
const incomingTranslationOriginalScanTextEl = document.getElementById("incomingTranslationOriginalScanText");
const incomingTranslationLineEl = document.getElementById("incomingTranslationLine");
const incomingTranslationTextEl = document.getElementById("incomingTranslationText");
const coverEl = document.getElementById("cover");
const coverImageEl = document.getElementById("coverImage");
const coverImageNextEl = document.getElementById("coverImageNext");
const coverFallbackEl = document.getElementById("coverFallback");
const root = document.documentElement;
const spectrumEl = document.querySelector(".spectrum");
let spectrumBarEls = Array.from(document.querySelectorAll(".spectrum span"));

let displayedCurrent = currentLineTextEl?.textContent || "";
let displayedNext = nextLineTextEl?.textContent || "";
let requestedFontSize = 13;
let viewportDescenderBufferPx = 2;
let layoutScaleFactor = 1;
let requestedSpectrumBarWidthPx = 3;
let requestedSpectrumGapPx = 3;
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
let transitionPromotedLine = "";
let transitionPromotedLineIndex = -1;
let transitionWordScanProgress = null;
let transitionUsesTranslationPair = false;
let isPlaybackPlaying = false;
let isTranslationMode = false;
let secondaryOpacity = 0.72;
let lastLineProgress = Number.NaN;
let lastCurrentLineIndex = -1;
let lastTrackId = "";
let metricsUpdatePending = false;
const lineScrollElements = new WeakMap();
const horizontalScrollMetrics = new WeakMap();
const transitionDurationMs = 560;
const translationPairTransitionDurationMs = 760;
const currentLineRestOpacity = 0.98;
const leavingLineOpacity = 0.16;
const trackSwitchSearchMinVisibleMs = 900;
const coverSwapDelayMs = 180;
const coverSwitchMinVisibleMs = 420;
const horizontalScrollAnchorRatio = 0.65;
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
let spectrumTargets = spectrumBarEls.map(() => 0);
let spectrumVisuals = spectrumBarEls.map(() => 0);
let spectrumSilence = spectrumBarEls.map(() => 0);
const spectrumTuning = {
  rise: 0.56,
  fall: 0.24,
  minHeight: 5,
  heightRange: 17,
  opacity: 0.78
};

function clamp01(value) {
  const parsed = Number(value);
  if (Number.isNaN(parsed)) {
    return 0;
  }
  return Math.max(0, Math.min(1, parsed));
}

function prefersReducedMotion() {
  return window.matchMedia?.("(prefers-reduced-motion: reduce)").matches === true;
}

function normalizeWordScanProgress(value) {
  if (value === null || value === undefined || value === "") {
    return null;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? clamp01(parsed) : null;
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

function resolveTranslationDisplay(line) {
  const text = (line ?? "").toString().trim();
  return {
    text: text.length > 0 ? text : "…",
    isPlaceholder: text.length === 0
  };
}

function setTrackOffset(rowCount) {
  const offset = snapToPhysicalPixel(-linePitchPx * rowCount);
  if (offset === 0) {
    trackEl.style.removeProperty("transform");
    return;
  }

  trackEl.style.transform = `translateY(${offset}px)`;
}

function snapToPhysicalPixel(value) {
  const devicePixelRatio = Number(window.devicePixelRatio) || 1;
  return Math.round(value * devicePixelRatio) / devicePixelRatio;
}

function getLineScrollElements(lineElement) {
  if (!lineElement) {
    return null;
  }

  const cached = lineScrollElements.get(lineElement);
  if (cached) {
    return cached;
  }

  const textViewport = lineElement.querySelector(".line-text-viewport");
  const textStack = lineElement.querySelector(".line-text-stack");
  const baseText = lineElement.querySelector(".line-text-base");
  if (!textViewport || !textStack || !baseText) {
    return null;
  }

  const elements = { textViewport, textStack, baseText };
  lineScrollElements.set(lineElement, elements);
  return elements;
}

function clearLineHorizontalScroll(lineElement, discardMetrics = true) {
  if (!lineElement) {
    return;
  }

  if (discardMetrics) {
    horizontalScrollMetrics.delete(lineElement);
  }
  lineElement.classList.remove("horizontal-scrolling");
  getLineScrollElements(lineElement)?.textStack.style.removeProperty("--line-scroll-offset");
}

function measureLineHorizontalScroll(lineElement) {
  const cached = horizontalScrollMetrics.get(lineElement);
  if (cached) {
    return cached;
  }

  const elements = getLineScrollElements(lineElement);
  if (!elements) {
    return null;
  }

  const viewportWidth = elements.textViewport.clientWidth;
  const contentWidth = elements.baseText.scrollWidth;
  const metrics = {
    textStack: elements.textStack,
    viewportWidth,
    contentWidth,
    overflowWidth: Math.max(0, contentWidth - viewportWidth)
  };
  if (viewportWidth > 0 && contentWidth > 0) {
    horizontalScrollMetrics.set(lineElement, metrics);
  }
  return metrics;
}

function updateLineHorizontalScroll(lineElement, normalizedProgress) {
  if (normalizedProgress === null) {
    clearLineHorizontalScroll(lineElement);
    return;
  }

  const metrics = measureLineHorizontalScroll(lineElement);
  if (!metrics || metrics.viewportWidth <= 0 || metrics.overflowWidth < 0.5) {
    clearLineHorizontalScroll(lineElement, false);
    return;
  }

  const scanHeadPosition = normalizedProgress * metrics.contentWidth;
  const anchorPosition = metrics.viewportWidth * horizontalScrollAnchorRatio;
  const rawOffset = Math.max(0, Math.min(metrics.overflowWidth, scanHeadPosition - anchorPosition));
  const offset = snapToPhysicalPixel(rawOffset);
  lineElement.classList.add("horizontal-scrolling");
  metrics.textStack.style.setProperty("--line-scroll-offset", `${offset === 0 ? 0 : -offset}px`);
}

function refreshLineHorizontalScroll(lineElement) {
  horizontalScrollMetrics.delete(lineElement);
  const progress = Number.parseFloat(
    lineElement.style.getPropertyValue("--word-scan-progress")) / 100;
  updateLineHorizontalScroll(lineElement, Number.isFinite(progress) ? clamp01(progress) : null);
}

function setLineText(lineElement, baseTextElement, scanTextElement, text) {
  const isChanged = baseTextElement?.textContent !== text ||
    (scanTextElement && scanTextElement.textContent !== text);
  if (!isChanged) {
    return;
  }

  clearLineHorizontalScroll(lineElement);
  if (baseTextElement) {
    baseTextElement.textContent = text;
  }
  if (scanTextElement) {
    scanTextElement.textContent = text;
  }
}

function setCurrentLine(line) {
  const safe = toDisplayLine(line, SEARCHING_TEXT);
  setLineText(currentLineEl, currentLineTextEl, currentLineScanTextEl, safe);
  displayedCurrent = safe;
}

function setLineWordScanProgress(lineElement, progress, allowSmoothing = true) {
  const normalized = normalizeWordScanProgress(progress);
  const isScanning = normalized !== null;
  const previousProgress = Number.parseFloat(
    lineElement.style.getPropertyValue("--word-scan-progress")) / 100;
  const isContinuousProgress = Number.isFinite(previousProgress) &&
    normalized !== null &&
    normalized >= previousProgress - 0.02 &&
    normalized - previousProgress <= 0.35;
  lineElement.classList.toggle("word-scanning", isScanning);
  lineElement.classList.toggle(
    "word-scan-smoothing",
    isScanning &&
      normalized > 0 &&
      normalized < 1 &&
      isPlaybackPlaying &&
      !prefersReducedMotion() &&
      allowSmoothing &&
      isContinuousProgress);
  if (normalized === null) {
    lineElement.style.removeProperty("--word-scan-progress");
    updateLineHorizontalScroll(lineElement, null);
    return;
  }

  lineElement.style.setProperty("--word-scan-progress", `${(normalized * 100).toFixed(3)}%`);
  updateLineHorizontalScroll(lineElement, normalized);
}

function setWordScanProgress(progress, allowSmoothing = true) {
  setLineWordScanProgress(currentLineEl, progress, allowSmoothing);
}

function updateTransitionWordScanProgress(progress, allowSmoothing = true) {
  transitionWordScanProgress = progress;
  const targetLine = transitionUsesTranslationPair
    ? incomingTranslationOriginalLineEl
    : nextLineEl;
  setLineWordScanProgress(targetLine, progress, allowSmoothing);
  trackEl.classList.toggle(
    "word-scan-transition",
    !transitionUsesTranslationPair && normalizeWordScanProgress(progress) !== null);
}

function setSecondaryLine(line) {
  const translationDisplay = isTranslationMode
    ? resolveTranslationDisplay(line)
    : null;
  const safe = translationDisplay?.text ?? toDisplayLine(line, " ");
  nextLineEl.classList.toggle(
    "translation-placeholder",
    translationDisplay?.isPlaceholder === true);
  setLineText(nextLineEl, nextLineTextEl, nextLineScanTextEl, safe);
  displayedNext = safe;
}

function setIncomingLine(line) {
  setLineText(incomingLineEl, incomingLineTextEl, null, toDisplayLine(line, " "));
}

function setTranslationMode(enabled) {
  isTranslationMode = Boolean(enabled);
  layoutEl.classList.toggle("translation-mode", isTranslationMode);
  if (isTranslationMode) {
    setLineWordScanProgress(nextLineEl, null);
    nextLineEl.style.opacity = "";
  } else {
    clearIncomingTranslationPair();
  }
}

function setIncomingTranslationPair(original, translation, wordScanProgress) {
  const translationDisplay = resolveTranslationDisplay(translation);
  setLineText(
    incomingTranslationOriginalLineEl,
    incomingTranslationOriginalTextEl,
    incomingTranslationOriginalScanTextEl,
    toDisplayLine(original, SEARCHING_TEXT));
  setLineText(
    incomingTranslationLineEl,
    incomingTranslationTextEl,
    null,
    translationDisplay.text);
  incomingTranslationLineEl.classList.toggle(
    "translation-placeholder",
    translationDisplay.isPlaceholder);
  setLineWordScanProgress(incomingTranslationOriginalLineEl, wordScanProgress, false);
}

function clearIncomingTranslationPair() {
  incomingTranslationPairEl.classList.remove("preparing", "entering", "no-anim");
  incomingTranslationPairEl.style.opacity = "";
  incomingTranslationPairEl.style.transform = "";
  setLineWordScanProgress(incomingTranslationOriginalLineEl, null);
  setLineText(incomingTranslationOriginalLineEl, incomingTranslationOriginalTextEl, incomingTranslationOriginalScanTextEl, " ");
  setLineText(incomingTranslationLineEl, incomingTranslationTextEl, null, " ");
  incomingTranslationLineEl.classList.remove("translation-placeholder");
}

function updateSecondaryOpacity(progress) {
  if (isTranslationMode) {
    nextLineEl.style.opacity = "";
    return;
  }

  const p = clamp01(progress);
  const target = 0.58 + ((1 - p) * 0.16);
  secondaryOpacity += (target - secondaryOpacity) * 0.28;
  nextLineEl.style.opacity = secondaryOpacity.toFixed(3);
}

function easeOutCubic(t) {
  const x = 1 - clamp01(t);
  return 1 - (x * x * x);
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

function ensureSpectrumBarCount(value) {
  const count = Math.max(8, Math.min(32, Math.round(Number(value) || spectrumBarEls.length || 21)));
  if (!spectrumEl || spectrumBarEls.length === count) {
    return;
  }

  stopSpectrumRenderer();
  const fragment = document.createDocumentFragment();
  for (let index = 0; index < count; index++) {
    const bar = document.createElement("span");
    bar.style.setProperty("--i", index.toString());
    fragment.appendChild(bar);
  }

  spectrumEl.replaceChildren(fragment);
  spectrumBarEls = Array.from(spectrumEl.querySelectorAll("span"));
  spectrumTargets = spectrumBarEls.map(() => 0);
  spectrumVisuals = spectrumBarEls.map(() => 0);
  spectrumSilence = spectrumBarEls.map(() => 0);
  updateSpectrumGeometry();
  if (isSpectrumMode) {
    startSpectrumRenderer();
  }
}

function alignDownToPhysicalPixel(value) {
  const pixelsPerDip = Number(window.devicePixelRatio) || 1;
  return Math.floor(Math.max(0, value) * pixelsPerDip) / pixelsPerDip;
}

function updateSpectrumGeometry() {
  if (!spectrumEl || spectrumBarEls.length === 0) {
    return;
  }

  const availableWidth = spectrumEl.clientWidth;
  if (!Number.isFinite(availableWidth) || availableWidth <= 0) {
    return;
  }

  const barCount = spectrumBarEls.length;
  const gapCount = Math.max(0, barCount - 1);
  const desiredWidth = (requestedSpectrumBarWidthPx * barCount) + (requestedSpectrumGapPx * gapCount);
  let fittedBarWidth = requestedSpectrumBarWidthPx;
  let fittedGap = requestedSpectrumGapPx;

  if (desiredWidth > availableWidth) {
    const fitRatio = availableWidth / desiredWidth;
    const minimumBarWidth = 1 / (Number(window.devicePixelRatio) || 1);
    fittedBarWidth = Math.max(
      minimumBarWidth,
      alignDownToPhysicalPixel(requestedSpectrumBarWidthPx * fitRatio));
    const remainingWidth = Math.max(0, availableWidth - (fittedBarWidth * barCount));
    fittedGap = gapCount > 0
      ? alignDownToPhysicalPixel(Math.min(requestedSpectrumGapPx, remainingWidth / gapCount))
      : 0;
  }

  spectrumEl.style.setProperty("--spectrum-fitted-bar-width", `${fittedBarWidth}px`);
  spectrumEl.style.setProperty("--spectrum-fitted-gap", `${fittedGap}px`);
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
    const height = Math.max(1, Math.round((spectrumTuning.minHeight + (level * spectrumTuning.heightRange)) * layoutScaleFactor));
    const bar = spectrumBarEls[i];
    bar.style.height = `${height}px`;
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
    applyFrame(
      frame.current,
      frame.next,
      frame.progress,
      frame.currentLineIndex,
      frame.wordScanProgress,
      frame.currentTranslation,
      frame.nextTranslation,
      frame.translationMode);
    return;
  }

  const elapsed = window.performance.now() - trackSwitchSearchStartedAt;
  const delay = Math.max(0, trackSwitchSearchMinVisibleMs - elapsed);
  delayedFrameTimer = window.setTimeout(() => {
    delayedFrameTimer = 0;
    trackSwitchSearchStartedAt = 0;
    applyFrame(
      frame.current,
      frame.next,
      frame.progress,
      frame.currentLineIndex,
      frame.wordScanProgress,
      frame.currentTranslation,
      frame.nextTranslation,
      frame.translationMode);
  }, delay);
}

function applyFrameWithoutTransition(
  safeCurrent,
  safeNext,
  progress,
  currentLineIndex,
  wordScanProgress,
  currentTranslation = "",
  nextTranslation = "",
  translationMode = false) {
  const useTranslationPair = Boolean(translationMode);
  const visibleSecondary = useTranslationPair
    ? toDisplayLine(currentTranslation, " ")
    : safeNext;
  const p = clamp01(progress);

  cancelActiveTransition();
  trackSwitchSearchStartedAt = 0;
  setTranslationMode(useTranslationPair);
  setIncomingLine("");
  setCurrentLine(safeCurrent);
  setWordScanProgress(wordScanProgress, false);
  setSecondaryLine(visibleSecondary);
  updateSecondaryOpacity(p);
  lastCurrentLineIndex = Number.isInteger(currentLineIndex) ? currentLineIndex : -1;
  lastLineProgress = p;
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
  trackEl.classList.remove("translation-pair-animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setLineWordScanProgress(nextLineEl, null);
  trackEl.classList.remove("word-scan-transition");
  transitionPromotedLine = "";
  transitionPromotedLineIndex = -1;
  transitionWordScanProgress = null;
  transitionUsesTranslationPair = false;
  setTrackOffset(0);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  nextLineEl.style.fontSize = "";
  nextLineEl.style.removeProperty("transform");
  nextLineEl.style.removeProperty("--promotion-scale");
  incomingLineEl.style.opacity = secondaryOpacity.toFixed(3);
  clearIncomingTranslationPair();
  void trackEl.offsetHeight;
  trackEl.classList.remove("no-anim");
}

function resetForTrackSwitch(
  safeCurrent,
  safeNext,
  progress,
  currentLineIndex,
  trackId,
  wordScanProgress,
  currentTranslation,
  nextTranslation,
  translationMode) {
  cancelActiveTransition();
  setTranslationMode(false);
  lastTrackId = trackId;
  lastCurrentLineIndex = -1;
  lastLineProgress = 0;
  trackSwitchSearchStartedAt = window.performance.now();
  setCoverLoadingState(true);

  const hasLyricFrame = Number.isInteger(currentLineIndex) && currentLineIndex >= 0 && !isSearchingLine(safeCurrent);

  if (!isSearchingLine(displayedCurrent)) {
    startTransition(SEARCHING_TEXT, " ", 0, -1, null);
    if (hasLyricFrame) {
      queuedFrame = {
        current: safeCurrent,
        next: safeNext,
        progress,
        currentLineIndex,
        wordScanProgress,
        currentTranslation,
        nextTranslation,
        translationMode
      };
    }
  } else {
    setSecondaryLine(" ");
    updateSecondaryOpacity(0);
    if (hasLyricFrame) {
      applyFrameAfterSearchDwell({
        current: safeCurrent,
        next: safeNext,
        progress,
        currentLineIndex,
        wordScanProgress,
        currentTranslation,
        nextTranslation,
        translationMode
      });
    }
  }
}


function runTransitionOpacityAnimation(now) {
  if (!isTransitioning) {
    return;
  }

  const elapsed = Math.max(0, now - transitionStartTime);
  const t = clamp01(elapsed / transitionDurationMs);
  const fadeOutE = getFadeOutEase(t);
  const fadeInE = getFadeInEase(t);

  currentLineEl.style.opacity = String(currentLineRestOpacity + ((leavingLineOpacity - currentLineRestOpacity) * fadeOutE));
  nextLineEl.style.opacity = String(transitionBaseNextOpacity + ((currentLineRestOpacity - transitionBaseNextOpacity) * fadeInE));
  incomingLineEl.style.opacity = secondaryOpacity.toFixed(3);

  if (t < 1) {
    transitionOpacityAnimation = window.requestAnimationFrame(runTransitionOpacityAnimation);
  } else {
    transitionOpacityAnimation = 0;
  }
}

function applyFrame(
  safeCurrent,
  safeNext,
  progress,
  currentLineIndex,
  wordScanProgress,
  currentTranslation = "",
  nextTranslation = "",
  translationMode = false) {
  const useTranslationPair = Boolean(translationMode);
  const visibleSecondary = useTranslationPair
    ? toDisplayLine(currentTranslation, " ")
    : safeNext;
  const p = clamp01(progress);

  if (useTranslationPair !== isTranslationMode) {
    const shouldAnimateSearchResult =
      useTranslationPair &&
      isSearchingLine(displayedCurrent) &&
      !isSearchingLine(safeCurrent);
    cancelActiveTransition();
    setTranslationMode(useTranslationPair);
    if (shouldAnimateSearchResult) {
      startTransition(
        safeCurrent,
        safeNext,
        p,
        currentLineIndex,
        wordScanProgress,
        currentTranslation,
        nextTranslation,
        useTranslationPair);
      return;
    }

    setCurrentLine(safeCurrent);
    setWordScanProgress(wordScanProgress, false);
    setSecondaryLine(visibleSecondary);
    updateSecondaryOpacity(p);
    lastCurrentLineIndex = Number.isInteger(currentLineIndex) ? currentLineIndex : -1;
    lastLineProgress = p;
    return;
  }

  if (isTransitioning) {
    queuedFrame = {
      current: safeCurrent,
      next: safeNext,
      progress,
      currentLineIndex,
      wordScanProgress,
      currentTranslation,
      nextTranslation,
      translationMode: useTranslationPair
    };
    const updatesPromotedLine = transitionPromotedLineIndex >= 0
      ? currentLineIndex === transitionPromotedLineIndex
      : safeCurrent === transitionPromotedLine;
    if (updatesPromotedLine) {
      updateTransitionWordScanProgress(wordScanProgress);
    }
    return;
  }

  const hasLineIndex = Number.isInteger(currentLineIndex) && currentLineIndex >= 0;

  if (hasLineIndex) {
    if (!Number.isInteger(lastCurrentLineIndex) || lastCurrentLineIndex < 0) {
      // If we were in a non-lyric state, slide into the first line smoothly.
      if (isSearchingLine(displayedCurrent)) {
        startTransition(
          safeCurrent,
          safeNext,
          p,
          currentLineIndex,
          wordScanProgress,
          currentTranslation,
          nextTranslation,
          useTranslationPair);
      } else {
        setCurrentLine(safeCurrent);
        setWordScanProgress(wordScanProgress, false);
        setSecondaryLine(visibleSecondary);
        updateSecondaryOpacity(p);
      }
      lastCurrentLineIndex = currentLineIndex;
      lastLineProgress = p;
      return;
    }

    if (currentLineIndex !== lastCurrentLineIndex) {
      startTransition(
        safeCurrent,
        safeNext,
        p,
        currentLineIndex,
        wordScanProgress,
        currentTranslation,
        nextTranslation,
        useTranslationPair);
    } else {
      if (safeCurrent !== displayedCurrent) {
        setCurrentLine(safeCurrent);
      }
      setWordScanProgress(wordScanProgress);
      setSecondaryLine(visibleSecondary);
      updateSecondaryOpacity(p);
    }

    lastLineProgress = p;
    return;
  }

  const isRepeatedPromotionCandidate =
    safeCurrent === displayedCurrent &&
    displayedNext === displayedCurrent &&
    visibleSecondary !== displayedNext;
  const isUnchangedTextFrame =
    safeCurrent === displayedCurrent &&
    visibleSecondary === displayedNext;
  const wrappedProgressForSameText =
    isUnchangedTextFrame &&
    Number.isFinite(lastLineProgress) &&
    (lastLineProgress - p) > 0.16 &&
    lastLineProgress > 0.62;

  if (safeCurrent !== displayedCurrent || isRepeatedPromotionCandidate || wrappedProgressForSameText) {
    startTransition(
      safeCurrent,
      safeNext,
      p,
      -1,
      wordScanProgress,
      currentTranslation,
      nextTranslation,
      useTranslationPair);
  } else {
    setWordScanProgress(wordScanProgress);
    setSecondaryLine(visibleSecondary);
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
  // Exclude the host-provided descender buffer from row metrics.
  const measuredViewportHeight = viewportEl.clientHeight || 30;
  const minimumHostHeight = Math.max(2, Math.round(26 * layoutScaleFactor));
  const hostHeight = Math.max(minimumHostHeight, measuredViewportHeight - viewportDescenderBufferPx);
  rowHeightPx = Math.max(1, Math.floor(hostHeight / 2));
  rowGapPx = Math.max(0, hostHeight - (rowHeightPx * 2));
  linePitchPx = rowHeightPx + rowGapPx;
  const currentSizeMax = Math.max(11.2 * layoutScaleFactor, rowHeightPx * 0.92);
  currentSize = Math.min(requestedFontSize, currentSizeMax);
  const nextSize = Math.max(9 * layoutScaleFactor, currentSize * 0.92);
  root.style.setProperty("--row-height", `${rowHeightPx}px`);
  root.style.setProperty("--row-gap", `${rowGapPx}px`);
  root.style.setProperty("--line-pitch", `${linePitchPx}px`);
  root.style.setProperty("--current-size", `${currentSize.toFixed(2)}px`);
  root.style.setProperty("--next-size", `${nextSize.toFixed(2)}px`);
  setTrackOffset(0);
  refreshLineHorizontalScroll(currentLineEl);
}

function finalizeTransition(promotedCurrent, upcomingNext, progress, promotedLineIndex = -1, wordScanProgress = null) {
  const incomingEndOpacity = Number.parseFloat(window.getComputedStyle(incomingLineEl).opacity || "0.72");

  // Freeze transitions while swapping layers to avoid visible "grow then shrink" rebound.
  trackEl.classList.add("no-anim");
  stopTransitionOpacityAnimation();
  currentLineEl.style.opacity = String(leavingLineOpacity);
  nextLineEl.style.opacity = String(currentLineRestOpacity);
  void trackEl.offsetHeight;
  setCurrentLine(promotedCurrent);
  setWordScanProgress(wordScanProgress, false);
  setSecondaryLine(upcomingNext);
  setLineWordScanProgress(nextLineEl, null);
  setIncomingLine("");
  trackEl.classList.remove("animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  trackEl.classList.remove("word-scan-transition");
  transitionPromotedLine = "";
  transitionPromotedLineIndex = -1;
  transitionWordScanProgress = null;
  transitionUsesTranslationPair = false;
  setTrackOffset(0);
  // Reset inline opacity channels while transitions are disabled; otherwise a brief flash can appear.
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  secondaryOpacity = Number.isFinite(incomingEndOpacity) ? incomingEndOpacity : 0.72;
  incomingLineEl.style.opacity = "";
  nextLineEl.style.fontSize = "";
  nextLineEl.style.removeProperty("transform");
  nextLineEl.style.removeProperty("--promotion-scale");
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

function startTransition(
  newCurrent,
  newNext,
  progress,
  currentLineIndex = -1,
  wordScanProgress = null,
  currentTranslation = "",
  nextTranslation = "",
  translationMode = false) {
  if (isTransitioning) {
    queuedFrame = {
      current: newCurrent,
      next: newNext,
      progress,
      currentLineIndex,
      wordScanProgress,
      currentTranslation,
      nextTranslation,
      translationMode
    };
    return;
  }

  if (translationMode) {
    startTranslationPairTransition(
      newCurrent,
      currentTranslation,
      progress,
      currentLineIndex,
      wordScanProgress);
    return;
  }

  startStandardTransition(newCurrent, newNext, progress, currentLineIndex, wordScanProgress);
}

function finalizeTranslationPairTransition(
  promotedCurrent,
  promotedTranslation,
  progress,
  promotedLineIndex,
  wordScanProgress) {
  trackEl.classList.add("no-anim");
  incomingTranslationPairEl.classList.add("no-anim");
  stopTransitionOpacityAnimation();
  setCurrentLine(promotedCurrent);
  setWordScanProgress(wordScanProgress, false);
  setSecondaryLine(promotedTranslation);
  setLineWordScanProgress(nextLineEl, null);
  trackEl.classList.remove("translation-pair-animating", "animating", "word-scan-transition");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  transitionPromotedLine = "";
  transitionPromotedLineIndex = -1;
  transitionWordScanProgress = null;
  transitionUsesTranslationPair = false;
  setTrackOffset(0);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  clearIncomingTranslationPair();
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

function startTranslationPairTransition(
  newCurrent,
  currentTranslation,
  progress,
  currentLineIndex,
  wordScanProgress) {
  isTransitioning = true;
  transitionUsesTranslationPair = true;
  const generation = ++transitionGeneration;
  const promoted = toDisplayLine(newCurrent, SEARCHING_TEXT);
  const promotedTranslation = toDisplayLine(currentTranslation, " ");
  transitionPromotedLine = promoted;
  transitionPromotedLineIndex = currentLineIndex;
  transitionWordScanProgress = wordScanProgress;
  stopTransitionOpacityAnimation();

  trackEl.classList.add("no-anim");
  trackEl.classList.remove("animating", "translation-pair-animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setTrackOffset(0);
  clearIncomingTranslationPair();
  incomingTranslationPairEl.classList.add("preparing", "no-anim");
  setIncomingTranslationPair(promoted, promotedTranslation, wordScanProgress);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  void incomingTranslationPairEl.offsetHeight;
  trackEl.classList.remove("no-anim");
  incomingTranslationPairEl.classList.remove("no-anim");

  let hasFinished = false;
  const finish = () => {
    if (hasFinished) {
      return;
    }

    hasFinished = true;
    trackEl.removeEventListener("transitionend", onTransitionEnd);
    if (generation !== transitionGeneration) {
      return;
    }

    if (transitionFallbackTimer) {
      window.clearTimeout(transitionFallbackTimer);
      transitionFallbackTimer = 0;
    }
    finalizeTranslationPairTransition(
      promoted,
      promotedTranslation,
      progress,
      currentLineIndex,
      transitionWordScanProgress);
  };
  const onTransitionEnd = (event) => {
    if (!event || event.target !== trackEl || event.propertyName !== "transform") {
      return;
    }

    finish();
  };

  trackEl.addEventListener("transitionend", onTransitionEnd);
  window.requestAnimationFrame(() => {
    if (generation !== transitionGeneration) {
      return;
    }

    incomingTranslationPairEl.classList.remove("preparing");
    incomingTranslationPairEl.classList.add("entering");
    trackEl.classList.add("translation-pair-animating", "animating");
    window.requestAnimationFrame(() => {
      if (generation !== transitionGeneration) {
        return;
      }

      setTrackOffset(2);
      if (prefersReducedMotion()) {
        window.requestAnimationFrame(finish);
      }
    });
  });
  transitionFallbackTimer = window.setTimeout(finish, translationPairTransitionDurationMs + 120);
}

function startStandardTransition(newCurrent, newNext, progress, currentLineIndex = -1, wordScanProgress = null) {
  transitionUsesTranslationPair = false;

  isTransitioning = true;
  const generation = ++transitionGeneration;
  const promoted = toDisplayLine(newCurrent, SEARCHING_TEXT);
  const upcoming = toDisplayLine(newNext, " ");
  transitionPromotedLine = promoted;
  transitionPromotedLineIndex = currentLineIndex;
  transitionWordScanProgress = wordScanProgress;
  transitionBaseNextOpacity = secondaryOpacity;
  const nextFontSize = Number.parseFloat(window.getComputedStyle(nextLineEl).fontSize || "12");
  const currentFontSize = Number.parseFloat(window.getComputedStyle(currentLineEl).fontSize || "13");
  const promotionStartScale = currentFontSize > 0
    ? clamp01(nextFontSize / currentFontSize)
    : 1;
  transitionStartTime = 0;
  stopTransitionOpacityAnimation();

  // Render with final font metrics from the start, then animate only the visual scale.
  trackEl.classList.add("no-anim");
  trackEl.classList.remove("animating");
  currentLineEl.classList.remove("leaving");
  nextLineEl.classList.remove("promoting");
  setTrackOffset(0);
  setLineText(nextLineEl, nextLineTextEl, nextLineScanTextEl, promoted);
  setIncomingLine(upcoming);
  currentLineEl.style.opacity = "";
  nextLineEl.style.opacity = "";
  nextLineEl.style.fontSize = `${currentFontSize.toFixed(3)}px`;
  nextLineEl.style.setProperty("--promotion-scale", promotionStartScale.toFixed(6));
  nextLineEl.style.transform = "translateY(0px) scale(var(--promotion-scale))";
  updateTransitionWordScanProgress(wordScanProgress, false);
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
    finalizeTransition(promoted, upcoming, progress, currentLineIndex, transitionWordScanProgress);
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
    nextLineEl.style.setProperty("--promotion-scale", "1");
    nextLineEl.style.removeProperty("transform");
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

    finalizeTransition(promoted, upcoming, progress, currentLineIndex, transitionWordScanProgress);
  }, transitionDurationMs + 120);
}

function updateResponsiveLayout() {
  updateMetrics();
  updateSpectrumGeometry();
}

updateResponsiveLayout();
setTranslationMode(false);
setCurrentLine(displayedCurrent);
setWordScanProgress(null);
setSecondaryLine(displayedNext);
setIncomingLine("");
updateSecondaryOpacity(0);

if (typeof ResizeObserver !== "undefined") {
  new ResizeObserver(updateResponsiveLayout).observe(layoutEl);
} else {
  window.addEventListener("resize", updateResponsiveLayout);
}

if (document.fonts?.ready) {
  document.fonts.ready.then(updateResponsiveLayout).catch(() => {});
}

const lyricsApi = {
  setLyrics(
    current,
    next,
    progress,
    currentLineIndex,
    trackId,
    isPureMusic,
    isPlaying,
    wordScanProgress,
    currentTranslation,
    nextTranslation,
    translationMode,
    animateTransition = true) {
    isPlaybackPlaying = Boolean(isPlaying);
    const safeCurrent = toDisplayLine(current, SEARCHING_TEXT);
    const safeNext = toDisplayLine(next, " ");
    const safeCurrentTranslation = toDisplayLine(currentTranslation, " ");
    const safeNextTranslation = toDisplayLine(nextTranslation, " ");
    const useTranslationPair = Boolean(translationMode);
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
      setTranslationMode(false);
      trackSwitchSearchStartedAt = 0;
      setCurrentLine(safeCurrent);
      setWordScanProgress(null);
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

    if (animateTransition === false) {
      applyFrameWithoutTransition(
        safeCurrent,
        safeNext,
        p,
        lineIndex,
        wordScanProgress,
        safeCurrentTranslation,
        safeNextTranslation,
        useTranslationPair);
      return;
    }

    if (normalizedTrackId.length > 0 && normalizedTrackId !== lastTrackId) {
      resetForTrackSwitch(
        safeCurrent,
        safeNext,
        p,
        lineIndex,
        normalizedTrackId,
        wordScanProgress,
        safeCurrentTranslation,
        safeNextTranslation,
        useTranslationPair);
      return;
    }

    if (normalizedTrackId.length > 0) {
      lastTrackId = normalizedTrackId;
    }

    applyFrame(
      safeCurrent,
      safeNext,
      p,
      lineIndex,
      wordScanProgress,
      safeCurrentTranslation,
      safeNextTranslation,
      useTranslationPair);
  },

  setSpectrum(values) {
    if (!isSpectrumMode) {
      return;
    }

    if (Array.isArray(values) && values.length > 0) {
      ensureSpectrumBarCount(values.length);
    }
    setAudioDrivenSpectrum(values);
  },

  setSpectrumTuning(payload) {
    if (!payload || typeof payload !== "object") {
      return;
    }

    ensureSpectrumBarCount(payload.barCount);
    spectrumTuning.rise = Math.max(0.02, Math.min(1, Number(payload.rise) || spectrumTuning.rise));
    spectrumTuning.fall = Math.max(0.02, Math.min(1, Number(payload.fall) || spectrumTuning.fall));
    spectrumTuning.minHeight = Math.max(1, Math.min(18, Number(payload.minHeight) || spectrumTuning.minHeight));
    spectrumTuning.heightRange = Math.max(2, Math.min(40, Number(payload.heightRange) || spectrumTuning.heightRange));
    spectrumTuning.opacity = Math.max(0.2, Math.min(1, Number(payload.opacity) || spectrumTuning.opacity));
    if (isSpectrumMode) {
      startSpectrumRenderer();
    }
  },

  setCover(dataUri, fallbackText, fallbackColor, diagnosticTrackId) {
    const uri = (dataUri ?? "").toString().trim();
    const text = toDisplayLine(fallbackText, "N").slice(0, 1).toUpperCase();
    const trackId = (diagnosticTrackId ?? "").toString();
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

        const mimeSeparatorIndex = uri.indexOf(";");
        const mime = uri.startsWith("data:") && mimeSeparatorIndex > 5
          ? uri.slice(5, mimeSeparatorIndex)
          : "";
        try {
          window.taskbarLyricsBridge.post("coverDecodeError", {
            trackId,
            mime,
            uriLength: uri.length,
            generation
          });
        } catch {
          // Diagnostics must not interrupt the fallback transition.
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
    const layoutScalePercent = Number(payload.layoutScalePercent);
    if (Number.isFinite(layoutScalePercent) && layoutScalePercent > 0) {
      layoutScaleFactor = layoutScalePercent / 100;
    }
    requestedFontSize = Number(payload.fontSize) || 13;
    root.style.setProperty("--font-size", `${requestedFontSize}px`);
    root.style.setProperty("--font-weight", window.taskbarLyricsState.normalizeWeight(payload.fontWeight));
    root.classList.toggle("cover-hidden", payload.showCover === false);

    const coverSize = Number(payload.coverSize);
    if (Number.isFinite(coverSize) && coverSize > 0) {
      root.style.setProperty("--cover-size", `${coverSize}px`);
    }
    const coverGap = Number(payload.coverGap);
    if (Number.isFinite(coverGap) && coverGap >= 0) {
      root.style.setProperty("--cover-gap", `${coverGap}px`);
    }
    const coverCornerRadius = Number(payload.coverCornerRadius);
    if (Number.isFinite(coverCornerRadius) && coverCornerRadius >= 0) {
      root.style.setProperty("--cover-radius", `${coverCornerRadius}px`);
    }

    const descenderBuffer = Number(payload.viewportDescenderBuffer);
    if (Number.isFinite(descenderBuffer) && descenderBuffer >= 0) {
      viewportDescenderBufferPx = descenderBuffer;
    }

    const pixelMetrics = {
      layoutHorizontalPadding: "--layout-padding-inline",
      lyricsPaneTopPadding: "--lyrics-pane-padding-top",
      lyricsPaneRightPadding: "--lyrics-pane-padding-right",
      lyricsPaneLeftPadding: "--lyrics-pane-padding-left",
      primaryOffsetY: "--primary-offset-y",
      secondaryOffsetY: "--secondary-offset-y",
      lineTextBottomPadding: "--line-text-padding-bottom",
      surfaceRadius: "--surface-radius",
      layerTransitionOffset: "--layer-transition-offset",
      coverFallbackFontSize: "--cover-fallback-font-size",
      spectrumWidth: "--spectrum-width",
      spectrumHeight: "--spectrum-height",
      spectrumGap: "--spectrum-gap",
      spectrumBarWidth: "--spectrum-bar-width",
      spectrumBarHeight: "--spectrum-bar-height",
      spectrumLowHeight: "--spectrum-low-height",
      spectrumHighHeight: "--spectrum-high-height",
      spectrumMiddleHeight: "--spectrum-middle-height"
    };
    Object.entries(pixelMetrics).forEach(([payloadKey, cssVariable]) => {
      const value = Number(payload[payloadKey]);
      if (Number.isFinite(value) && value >= 0) {
        root.style.setProperty(cssVariable, `${value}px`);
      }
    });
    const spectrumBarWidth = Number(payload.spectrumBarWidth);
    if (Number.isFinite(spectrumBarWidth) && spectrumBarWidth > 0) {
      requestedSpectrumBarWidthPx = spectrumBarWidth;
    }
    const spectrumGap = Number(payload.spectrumGap);
    if (Number.isFinite(spectrumGap) && spectrumGap >= 0) {
      requestedSpectrumGapPx = spectrumGap;
    }
    updateResponsiveLayout();

    if (payload.primaryColor && CSS.supports("color", payload.primaryColor)) {
      root.style.setProperty("--primary", payload.primaryColor);
    }

    if (payload.secondaryColor && CSS.supports("color", payload.secondaryColor)) {
      root.style.setProperty("--secondary", payload.secondaryColor);
    }

    if (payload.translationColor && CSS.supports("color", payload.translationColor)) {
      root.style.setProperty("--translation", payload.translationColor);
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
  }
};

window.taskbarLyrics = {
  receive(message) {
    if (message?.version !== 1 || !message.type) return;
    const payload = message.payload;
    switch (message.type) {
      case "lyrics":
        lyricsApi.setLyrics(
          payload?.current,
          payload?.next,
          payload?.progress,
          payload?.currentLineIndex,
          payload?.trackId,
          payload?.isPureMusic,
          payload?.isPlaying,
          payload?.wordScanProgress,
          payload?.currentTranslation,
          payload?.nextTranslation,
          payload?.translationMode,
          payload?.animateTransition);
        break;
      case "cover":
        lyricsApi.setCover(payload?.dataUri, payload?.fallbackText, payload?.fallbackColor, payload?.trackId);
        break;
      case "spectrum":
        lyricsApi.setSpectrum(payload);
        break;
      case "spectrumTuning":
        lyricsApi.setSpectrumTuning(payload);
        break;
      case "style":
        lyricsApi.applyStyle(payload);
        break;
    }
  }
};
