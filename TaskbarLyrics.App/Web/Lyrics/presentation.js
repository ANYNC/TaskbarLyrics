(() => {
  const SCENES = Object.freeze({
    SEARCHING: "searching",
    LYRICS: "lyrics",
    SPECTRUM: "spectrum",
    MESSAGE: "message"
  });

  const LAYOUTS = Object.freeze({
    SINGLE: "single",
    TRANSLATION_PAIR: "translationPair"
  });

  const TRANSITIONS = Object.freeze({
    PROGRESS_PATCH: "progressPatch",
    REPLACE_IN_PLACE: "replaceInPlace",
    SINGLE_ROLL: "singleRoll",
    TRANSLATION_PAIR_ROLL: "translationPairRoll",
    SEARCHING_TO_SPECTRUM_ROLL: "searchingToSpectrumRoll",
    LAYER_SWITCH: "layerSwitch",
    IMMEDIATE: "immediate"
  });

  const DEFAULT_DURATION_MS = Object.freeze({
    singleRoll: 560,
    translationPairRoll: 760,
    searchingToSpectrumRoll: 520,
    layerSwitch: 300
  });

  function normalizeScene(scene, frame) {
    if (Object.values(SCENES).includes(scene)) {
      return scene;
    }

    if (frame?.isPureMusic === true) {
      return SCENES.SPECTRUM;
    }

    if (frame?.isSearching === true ||
        frame?.current === "\u6b63\u5728\u68c0\u7d22\u6b4c\u8bcd..." ||
        frame?.current === "\u6b63\u5728\u5339\u914d\u6b4c\u8bcd...") {
      return SCENES.SEARCHING;
    }

    if (Number.isInteger(Number(frame?.currentLineIndex)) && Number(frame.currentLineIndex) >= 0) {
      return SCENES.LYRICS;
    }

    return SCENES.MESSAGE;
  }

  function normalizeLayout(layout, frame) {
    if (layout === LAYOUTS.TRANSLATION_PAIR || frame?.translationMode === true) {
      return LAYOUTS.TRANSLATION_PAIR;
    }

    return LAYOUTS.SINGLE;
  }

  function normalizeFrame(frame = {}) {
    const source = frame && typeof frame === "object" ? frame : {};
    const currentLineIndex = Number(source.currentLineIndex);
    const layout = normalizeLayout(source.layout, source);
    const normalized = {
      scene: normalizeScene(source.scene, source),
      layout,
      translationMode: layout === LAYOUTS.TRANSLATION_PAIR,
      current: String(source.current ?? ""),
      next: String(source.next ?? ""),
      currentTranslation: String(source.currentTranslation ?? ""),
      nextTranslation: String(source.nextTranslation ?? ""),
      currentLineIndex: Number.isInteger(currentLineIndex) ? currentLineIndex : -1,
      progress: clamp01(source.progress),
      wordScanProgress: normalizeOptionalProgress(source.wordScanProgress),
      trackId: String(source.trackId ?? ""),
      isPlaying: source.isPlaying !== false,
      isPureMusic: source.isPureMusic === true
    };

    return Object.freeze(normalized);
  }

  function normalizeOptionalProgress(value) {
    if (value === null || value === undefined || value === "") {
      return null;
    }

    const parsed = Number(value);
    return Number.isFinite(parsed) ? clamp01(parsed) : null;
  }

  function clamp01(value) {
    const parsed = Number(value);
    if (!Number.isFinite(parsed)) {
      return 0;
    }

    return Math.max(0, Math.min(1, parsed));
  }

  function sameLyricsIdentity(current, target) {
    return current.scene === SCENES.LYRICS &&
      target.scene === SCENES.LYRICS &&
      current.currentLineIndex >= 0 &&
      current.currentLineIndex === target.currentLineIndex &&
      current.trackId === target.trackId;
  }

  class PresentationPlanner {
    plan(currentFrame, targetFrame, options = {}) {
      const current = normalizeFrame(currentFrame);
      const target = normalizeFrame(targetFrame);
      const immediate = options.forceImmediate === true ||
        options.animateTransition === false ||
        options.reducedMotion === true;

      if (immediate) {
        return makePlan(TRANSITIONS.IMMEDIATE, current, target, 0);
      }

      if (target.scene === SCENES.SPECTRUM) {
        if (current.scene === SCENES.SEARCHING) {
          return makePlan(
            TRANSITIONS.SEARCHING_TO_SPECTRUM_ROLL,
            current,
            target,
            DEFAULT_DURATION_MS.searchingToSpectrumRoll);
        }

        return makePlan(TRANSITIONS.LAYER_SWITCH, current, target, DEFAULT_DURATION_MS.layerSwitch);
      }

      if (current.scene === SCENES.SPECTRUM) {
        return makePlan(TRANSITIONS.LAYER_SWITCH, current, target, DEFAULT_DURATION_MS.layerSwitch);
      }

      if (target.scene === SCENES.SEARCHING) {
        if (current.scene === SCENES.SEARCHING) {
          const textChanged = current.current !== target.current ||
            current.next !== target.next ||
            current.currentTranslation !== target.currentTranslation ||
            current.nextTranslation !== target.nextTranslation;
          return makePlan(
            textChanged ? TRANSITIONS.REPLACE_IN_PLACE : TRANSITIONS.PROGRESS_PATCH,
            current,
            target,
            0);
        }

        return makePlan(
          TRANSITIONS.SINGLE_ROLL,
          current,
          target,
          DEFAULT_DURATION_MS.singleRoll);
      }

      if (sameLyricsIdentity(current, target)) {
        const textChanged = current.current !== target.current ||
          current.next !== target.next ||
          current.currentTranslation !== target.currentTranslation ||
          current.nextTranslation !== target.nextTranslation ||
          current.layout !== target.layout;
        return makePlan(
          textChanged ? TRANSITIONS.REPLACE_IN_PLACE : TRANSITIONS.PROGRESS_PATCH,
          current,
          target,
          0);
      }

      if (target.scene === SCENES.LYRICS && current.scene === SCENES.LYRICS) {
        const isTranslationPair = target.layout === LAYOUTS.TRANSLATION_PAIR;
        return makePlan(
          isTranslationPair
            ? TRANSITIONS.TRANSLATION_PAIR_ROLL
            : TRANSITIONS.SINGLE_ROLL,
          current,
          target,
          isTranslationPair
            ? DEFAULT_DURATION_MS.translationPairRoll
            : DEFAULT_DURATION_MS.singleRoll);
      }

      if (target.scene === SCENES.LYRICS && current.scene === SCENES.SEARCHING) {
        const isTranslationPair = target.layout === LAYOUTS.TRANSLATION_PAIR;
        return makePlan(
          isTranslationPair
            ? TRANSITIONS.TRANSLATION_PAIR_ROLL
            : TRANSITIONS.SINGLE_ROLL,
          current,
          target,
          isTranslationPair
            ? DEFAULT_DURATION_MS.translationPairRoll
            : DEFAULT_DURATION_MS.singleRoll);
      }

      if (target.scene === SCENES.MESSAGE) {
        const textChanged = current.scene !== SCENES.MESSAGE ||
          current.current !== target.current ||
          current.next !== target.next ||
          current.currentTranslation !== target.currentTranslation ||
          current.nextTranslation !== target.nextTranslation;
        return makePlan(
          textChanged ? TRANSITIONS.SINGLE_ROLL : TRANSITIONS.PROGRESS_PATCH,
          current,
          target,
          textChanged ? DEFAULT_DURATION_MS.singleRoll : 0);
      }

      return makePlan(TRANSITIONS.REPLACE_IN_PLACE, current, target, 0);
    }
  }

  function makePlan(kind, from, to, durationMs) {
    return Object.freeze({
      kind,
      from,
      to,
      durationMs
    });
  }

  class PresentationCoordinator {
    constructor(planner = new PresentationPlanner()) {
      this.planner = planner;
      this.currentFrame = normalizeFrame();
      this.activeTransition = null;
      this.pendingFrame = null;
      this.generation = 0;
      this.fallbackTimer = 0;
      this.animationFrameIds = new Set();
      this.listenerCleanups = new Set();
    }

    get isTransitioning() {
      return this.activeTransition !== null;
    }

    get activeGeneration() {
      return this.activeTransition?.generation ?? this.generation;
    }

    plan(targetFrame, options = {}) {
      return this.planner.plan(this.currentFrame, targetFrame, options);
    }

    setCurrentFrame(frame) {
      this.currentFrame = normalizeFrame(frame);
      return this.currentFrame;
    }

    queueLatest(frame, options = {}) {
      this.pendingFrame = { frame: normalizeFrame(frame), options };
      return this.pendingFrame;
    }

    takeLatest() {
      const pending = this.pendingFrame;
      this.pendingFrame = null;
      return pending;
    }

    beginTransition(plan, callbacks = {}) {
      this.cancelTransition();
      const generation = ++this.generation;
      const transition = {
        generation,
        plan,
        callbacks,
        finished: false
      };
      this.activeTransition = transition;

      const context = {
        generation,
        plan,
        isCurrent: () => this.activeTransition?.generation === generation,
        requestFrame: callback => this.requestFrame(generation, callback),
        scheduleFallback: (callback, delayMs) => this.scheduleFallback(generation, callback, delayMs),
        listenTransitionEnd: (target, callback) => this.listenTransitionEnd(generation, target, callback),
        finish: value => this.finishTransition(generation, value),
        cancel: () => this.cancelTransition(generation)
      };

      if (typeof callbacks.start === "function") {
        callbacks.start(context);
      }

      return context;
    }

    finishTransition(generation, value) {
      const transition = this.activeTransition;
      if (!transition || transition.generation !== generation || transition.finished) {
        return false;
      }

      transition.finished = true;
      this.clearTransitionResources();
      this.activeTransition = null;
      if (typeof transition.callbacks.complete === "function") {
        transition.callbacks.complete(value);
      }
      return true;
    }

    cancelTransition(expectedGeneration = null) {
      const transition = this.activeTransition;
      if (expectedGeneration !== null && (!transition || transition.generation !== expectedGeneration)) {
        return false;
      }

      if (transition && typeof transition.callbacks.cancel === "function") {
        transition.callbacks.cancel();
      }

      this.generation++;
      this.clearTransitionResources();
      this.activeTransition = null;
      return transition !== null;
    }

    requestFrame(generation, callback) {
      const frameId = window.requestAnimationFrame(timestamp => {
        this.animationFrameIds.delete(frameId);
        if (this.activeTransition?.generation !== generation) {
          return;
        }

        callback(timestamp);
      });
      this.animationFrameIds.add(frameId);
      return frameId;
    }

    scheduleFallback(generation, callback, delayMs) {
      this.clearFallbackTimer();
      this.fallbackTimer = window.setTimeout(() => {
        this.fallbackTimer = 0;
        if (this.activeTransition?.generation !== generation) {
          return;
        }

        callback();
      }, Math.max(0, Number(delayMs) || 0));
      return this.fallbackTimer;
    }

    listenTransitionEnd(generation, target, callback) {
      if (!target || typeof target.addEventListener !== "function") {
        return () => {};
      }

      const listener = event => {
        if (this.activeTransition?.generation !== generation) {
          return;
        }

        callback(event);
      };
      target.addEventListener("transitionend", listener);
      const cleanup = () => target.removeEventListener("transitionend", listener);
      this.listenerCleanups.add(cleanup);
      return cleanup;
    }

    clearTransitionResources() {
      this.clearFallbackTimer();
      this.clearAnimationFrames();
      this.clearTransitionListeners();
    }

    clearAnimationFrames() {
      this.animationFrameIds.forEach(frameId => window.cancelAnimationFrame(frameId));
      this.animationFrameIds.clear();
    }

    clearTransitionListeners() {
      this.listenerCleanups.forEach(cleanup => cleanup());
      this.listenerCleanups.clear();
    }

    clearFallbackTimer() {
      if (!this.fallbackTimer) {
        return;
      }

      window.clearTimeout(this.fallbackTimer);
      this.fallbackTimer = 0;
    }
  }

  window.taskbarLyricsPresentation = {
    SCENES,
    LAYOUTS,
    TRANSITIONS,
    DEFAULT_DURATION_MS,
    clamp01,
    normalizeFrame,
    PresentationPlanner,
    PresentationCoordinator
  };
})();
