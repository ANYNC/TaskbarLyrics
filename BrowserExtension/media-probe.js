(() => {
  const pageOrigin = location.origin === "null" ? window.origin : location.origin;
  if (typeof pageOrigin !== "string" || !/^https?:\/\//.test(pageOrigin)) return;
  if (globalThis.__taskbarLyricsMediaProbe) {
    window.postMessage({ channel: "taskbarlyrics-ready-v1" }, pageOrigin); return;
  }
  globalThis.__taskbarLyricsMediaProbe = true;
  let enabled = false;
  const ids = new WeakMap();
  const observedMedia = new WeakSet();
  const detachedMedia = new Set();
  const actions = new Map();
  let serial = 0;
  let active = null;
  let reportedMediaId = null;
  let diagnosticState = "";
  let sessionPosition = null;
  let positionRevision = 0;
  let trackKey = null;
  let trackVersion = 0;
  let associated = null;
  let associationKind = null;
  let associationCandidate = null;
  let associationAt = 0;
  let latest = null;
  let commandPending = false;
  const session = navigator.mediaSession;
  const sessionMediaId = "media-session";
  function metadataKey() {
    const metadata = session?.metadata;
    return JSON.stringify([metadata?.title || document.title, metadata?.artist || "", metadata?.album || ""]);
  }
  function refreshTrack() {
    const key = metadataKey();
    if (key !== trackKey) {
      trackKey = key; trackVersion++;
      associated = associationCandidate = null;
      associationKind = null;
      if (sessionPosition?.track !== key) sessionPosition = null;
    }
  }
  function updateSessionPosition() {
    if (!sessionPosition) return;
    if (sessionPosition.track !== metadataKey()) { sessionPosition = null; return; }
    const now = performance.now();
    if (sessionPosition.playing) sessionPosition.position += Math.max(0, now - sessionPosition.updatedAt) / 1000 * sessionPosition.rate;
    sessionPosition.position = Math.max(0, Math.min(sessionPosition.duration || 31536000, sessionPosition.position));
    sessionPosition.updatedAt = now;
    sessionPosition.playing = session.playbackState === "playing";
  }
  if (session) {
    const original = session.setActionHandler;
    session.setActionHandler = function(action, handler) {
      const result = Reflect.apply(original, this, [action, handler]);
      if (this === session) {
        if (handler) actions.set(action, handler); else actions.delete(action);
      }
      return result;
    };
    if (typeof session.setPositionState === "function") {
      const setPositionState = session.setPositionState;
      session.setPositionState = function(state) {
        const result = Reflect.apply(setPositionState, this, arguments);
        if (this === session) {
          refreshTrack();
          sessionPosition = state && Object.keys(state).length ? {
            position: Number(state.position ?? 0), duration: Number.isFinite(Number(state.duration)) ? Number(state.duration) : 0,
            rate: Number(state.playbackRate ?? 1), updatedAt: performance.now(), capturedAt: performance.now(),
            playing: session.playbackState === "playing", track: metadataKey()
          } : null;
          // Sites can publish position before metadata in the same task. Commit
          // that batch together, without guessing across later asynchronous work.
          const captured = sessionPosition;
          const revision = ++positionRevision;
          queueMicrotask(() => {
            if (!document || revision !== positionRevision) return;
            if (captured) { captured.track = metadataKey(); captured.playing = session.playbackState === "playing"; }
            sessionPosition = captured;
            report();
          });
        }
        return result;
      };
    }
    // Freeze the captured position at the actual pause transition, rather than
    // letting a one-second sampling interval continue advancing a paused song.
    let owner = session;
    let playbackState;
    while (owner && !playbackState) {
      playbackState = Object.getOwnPropertyDescriptor(owner, "playbackState");
      owner = Object.getPrototypeOf(owner);
    }
    if (playbackState?.get && playbackState.set) {
      try {
        const descriptor = playbackState;
        Object.defineProperty(session, "playbackState", {
          configurable: true, enumerable: descriptor.enumerable, get: descriptor.get,
          set(value) {
            if (this === session) updateSessionPosition();
            const result = Reflect.apply(descriptor.set, this, [value]);
            if (this === session) {
              if (sessionPosition) sessionPosition.playing = session.playbackState === "playing";
              report();
            }
            return result;
          }
        });
      } catch (error) {
        if (!(error instanceof TypeError)) throw error; // Non-configurable host properties remain sampled.
      }
    }
  }
  function rememberMedia(media) {
    if (observedMedia.has(media)) return;
    observedMedia.add(media);
    // A player can keep new Audio() off-DOM. Weak references avoid owning the
    // lifetime of discarded players while still observing their native timeline.
    for (const reference of detachedMedia) if (!reference.deref()) detachedMedia.delete(reference);
    if (detachedMedia.size >= 128) return;
    detachedMedia.add(new WeakRef(media));
    for (const name of ["play", "pause", "ended", "seeking", "seeked", "ratechange", "loadedmetadata", "durationchange", "emptied"])
      media.addEventListener(name, event => { if (event.type === "play") active = event.currentTarget; report(); });
  }
  const originalPlay = HTMLMediaElement.prototype.play;
  HTMLMediaElement.prototype.play = function() {
    const result = Reflect.apply(originalPlay, this, arguments);
    rememberMedia(this);
    report();
    Promise.resolve(result).then(report, report).catch(() => {});
    return result;
  };
  function elements() {
    const result = new Set(document.querySelectorAll("audio,video"));
    for (const reference of detachedMedia) {
      const media = reference.deref();
      if (media) result.add(media); else detachedMedia.delete(reference);
    }
    return [...result];
  }
  function pick() {
    const all = elements();
    // Prefer audible media, but do not declare every muted video ineligible: a
    // real player can be muted. Ambiguous equal candidates remain unselected.
    const audible = all.filter(media => !media.muted && media.volume > 0);
    const candidates = audible.length ? audible : all;
    const playing = candidates.filter(media => !media.paused && !media.ended);
    if (active && playing.includes(active)) return active;
    active = playing.length === 1 ? playing[0] : !playing.length && candidates.length === 1 ? candidates[0] : null;
    return active;
  }
  function idOf(media) {
    if (!ids.has(media)) ids.set(media, `media-${++serial}`);
    return ids.get(media);
  }
  function diagnose(source, playing, hasTitle, hasDuration) {
    const state = `${source}:${playing}:${hasTitle}:${hasDuration}`;
    if (state === diagnosticState) return;
    diagnosticState = state;
    console.debug(`[TaskbarLyrics] MediaProbe Source=${source} Playing=${playing} HasTitle=${hasTitle} HasDuration=${hasDuration}`);
  }
  function seekRanges(media) {
    const ranges = [];
    for (let i = 0; i < Math.min(media?.seekable?.length ?? 0, 8); i++) {
      const start = media.seekable.start(i), end = media.seekable.end(i);
      if (Number.isFinite(start) && Number.isFinite(end) && start >= 0 && end > start) ranges.push({ start, end });
    }
    return ranges;
  }
  function artwork(metadata) {
    return Array.from(metadata?.artwork ?? []).slice(0, 8).flatMap(item => {
      try {
        const url = new URL(item.src, document.baseURI);
        if (!["http:", "https:"].includes(url.protocol) || url.username || url.password || url.href.length > 4096) return [];
        return [{ src: url.href, sizes: String(item.sizes ?? "").slice(0, 80), type: String(item.type ?? "").slice(0, 80) }];
      } catch { return []; }
    });
  }
  function capture() {
    refreshTrack(); updateSessionPosition();
    const metadata = session?.metadata;
    const candidate = pick();
    const audio = elements().filter(media => media.tagName === "AUDIO" && Number.isFinite(media.duration) && media.duration > 0);
    // Metadata-only Media Sessions are common. A single audible audio player in
    // this document is a bounded fallback; videos and multiple audios do not
    // establish this association, nor does media in another document.
    const uniqueAudio = audio.length === 1 && !audio[0].muted && audio[0].volume > 0 ? audio[0] : null;
    const usesSession = Boolean(metadata?.title?.trim() && (sessionPosition ||
      ["playing", "paused"].includes(session?.playbackState) || !candidate || uniqueAudio));
    if (usesSession && !sessionPosition && session.playbackState === "none" && uniqueAudio) {
      associated = uniqueAudio; associationKind = "single-audio";
    }
    // Associate only after matching both duration and position in two spaced
    // observations. Never bind across documents or just by matching a title.
    if (usesSession && !associated && candidate && sessionPosition &&
        Number.isFinite(candidate.duration) && Math.abs(candidate.duration - sessionPosition.duration) < 1 &&
        Math.abs(candidate.currentTime - sessionPosition.position) < 1 && !candidate.seeking) {
      if (associationCandidate === candidate && performance.now() - associationAt >= 750) { associated = candidate; associationKind = "timeline-match"; }
      else if (associationCandidate !== candidate) { associationCandidate = candidate; associationAt = performance.now(); }
    } else if (!associated) associationCandidate = null;
    if (associated && (!elements().includes(associated) || (associationKind === "single-audio"
        ? uniqueAudio !== associated || Boolean(sessionPosition)
        : !sessionPosition || Math.abs(associated.duration - sessionPosition.duration) >= 1))) {
      associated = null; associationKind = null;
    }
    const media = usesSession ? associated : candidate;
    if (!usesSession && !media) return null;
    const state = media ? media.seeking ? "seeking" : media.ended ? "ended" : media.paused ? "paused" : "playing"
      : ["playing", "paused"].includes(session?.playbackState) ? session.playbackState : "unknown";
    const position = media ? Number.isFinite(media.currentTime) ? Math.max(0, media.currentTime) : null : sessionPosition?.position ?? null;
    const duration = media ? Number.isFinite(media.duration) && media.duration > 0 ? media.duration : null : sessionPosition?.duration || null;
    const ranges = media ? seekRanges(media) : [];
    const seekable = media ? ranges.length > 0 : Boolean(duration && actions.has("seekto"));
    const supported = usesSession ? [...actions.keys()] : [];
    if (media) {
      supported.push("play", "pause");
      if (seekable) supported.push("seekto", "seekbackward", "seekforward");
    }
    const details = { version: 1, trackVersion, source: media ? usesSession ? "associated-element" : "element" : "media-session",
      titleSource: usesSession ? "media-session" : "document", playbackState: state,
      positionSeconds: position, durationSeconds: duration, playbackRate: media ? media.playbackRate : sessionPosition?.rate ?? 1,
      sampleAgeSeconds: 0, anchorAgeSeconds: media ? 0 : sessionPosition ? Math.min(31536000, Math.max(0, (performance.now() - sessionPosition.capturedAt) / 1000)) : null,
      seekable: ranges, actions: [...new Set(supported)], artwork: usesSession ? artwork(metadata) : [] };
    return { media, snapshot: { mediaId: usesSession ? sessionMediaId : idOf(media), title: usesSession ? metadata.title : document.title,
      artist: usesSession ? metadata.artist || "" : "", album: usesSession ? metadata.album || "" : "",
      isPlaying: state === "playing", positionSeconds: position ?? 0, durationSeconds: duration ?? 0, canSeek: seekable, details } };
  }
  function report() {
    if (!enabled) return;
    latest = capture();
    reportedMediaId = latest?.snapshot.mediaId ?? null;
    if (!latest) { diagnose("none", false, false, false); return; }
    const snapshot = latest.snapshot;
    diagnose(snapshot.details.source, snapshot.isPlaying, Boolean(snapshot.title?.trim()), snapshot.durationSeconds > 0);
    window.postMessage({ channel: "taskbarlyrics-media-v1", sampledAt: performance.now(), payload: snapshot }, pageOrigin);
  }
  async function execute(payload) {
    const fail = reason => ({ ok: false, reason, status: "rejected" });
    if (!enabled || payload.mediaId !== reportedMediaId) return fail("stale-target");
    const current = capture();
    if (!current || payload.mediaId !== current.snapshot.mediaId ||
        payload.trackVersion !== undefined && payload.trackVersion !== current.snapshot.details.trackVersion) return fail("stale-track");
    const media = current.media;
    const before = current.snapshot;
    const paused = before.details.playbackState === "paused" || before.details.playbackState === "ended";
    if (payload.action === "TogglePlayPause" && before.details.playbackState === "unknown") return fail("unknown-state");
    const action = { TogglePlayPause: paused ? "play" : "pause", PreviousTrack: "previoustrack", NextTrack: "nexttrack",
      SeekBackward: "seekbackward", SeekForward: "seekforward", SeekTo: "seekto" }[payload.action];
    if (!action || !before.details.actions.includes(action)) return fail("unsupported");
    if (action === "seekto" && (!Number.isFinite(payload.seconds) || payload.seconds < 0)) return fail("invalid-position");
    if (before.details.titleSource === "media-session" && actions.has(action)) {
      let timeout;
      const expired = Symbol("handler-timeout");
      let result;
      try {
        result = await Promise.race([
          Promise.resolve(actions.get(action)({ action, seekTime: payload.seconds, seekOffset: 5, fastSeek: false })),
          new Promise(resolve => { timeout = setTimeout(() => resolve(expired), 750); })
        ]);
      } finally { clearTimeout(timeout); }
      if (result === expired) return { ok: false, status: "unconfirmed", reason: "handler-timeout" };
      if (result === false) return fail("handler-rejected");
    } else if (!media) return fail("unsupported");
    else if (action === "play") await media.play();
    else if (action === "pause") media.pause();
    else if (action.startsWith("seek")) {
      const target = action === "seekto" ? payload.seconds : media.currentTime + (action === "seekbackward" ? -5 : 5);
      const ranges = seekRanges(media);
      if (!ranges.some(range => target >= range.start && target <= range.end)) return fail("outside-seekable-range");
      media.currentTime = target;
    } else return fail("unsupported");
    report();
    // Invocation and observed effects are distinct. Poll briefly because website
    // callbacks often return before their player has actually changed state.
    for (let attempt = 0; attempt < 5; attempt++) {
      const after = enabled ? capture()?.snapshot : null;
      if (!after) return fail("target-unavailable");
      const changedTrack = after.details.trackVersion !== before.details.trackVersion;
      const trackAction = action === "nexttrack" || action === "previoustrack";
      if (after.mediaId !== before.mediaId || changedTrack && !trackAction) return fail("stale-track");
      const effect = trackAction ? changedTrack
        : action === "play" || action === "pause" ? after.details.playbackState === (action === "play" ? "playing" : "paused")
        : after.details.positionSeconds !== null && before.details.positionSeconds !== null &&
          (action === "seekto" ? Math.abs(after.positionSeconds - payload.seconds) < 2
            : (after.positionSeconds - before.positionSeconds) * (action === "seekforward" ? 1 : -1) > 2);
      if (effect) return { ok: true, status: "observed", reason: "", observed: after.details.playbackState };
      if (changedTrack) return fail("stale-track");
      await new Promise(resolve => setTimeout(resolve, 200));
    }
    return { ok: false, status: "unconfirmed", reason: "effect-not-observed" };
  }
  window.addEventListener("message", event => {
    if (event.source !== window || event.origin !== pageOrigin) return;
    if (event.data?.channel === "taskbarlyrics-config-v1") { enabled = event.data.enabled === true; if (!enabled) reportedMediaId = null; report(); }
    if (event.data?.channel === "taskbarlyrics-command-v1") {
      const payload = event.data.payload;
      if (!payload || typeof payload.requestId !== "string") return;
      if (commandPending) {
        window.postMessage({ channel: "taskbarlyrics-result-v1", requestId: payload.requestId, ok: false, status: "rejected", reason: "busy" }, pageOrigin); return;
      }
      commandPending = true;
      execute(payload).then(result => window.postMessage({ channel: "taskbarlyrics-result-v1", requestId: payload.requestId, ...result }, pageOrigin),
        () => window.postMessage({ channel: "taskbarlyrics-result-v1", requestId: payload.requestId, ok: false, status: "rejected", reason: "handler-error" }, pageOrigin))
        .catch(() => {}).finally(() => { commandPending = false; });
    }
  });
  for (const name of ["play", "pause", "ended", "seeking", "seeked", "ratechange", "loadedmetadata", "durationchange", "emptied"])
    document.addEventListener(name, event => { if (event.type === "play") active = event.target; report(); }, true);
  setInterval(report, 1000);
  window.postMessage({ channel: "taskbarlyrics-ready-v1" }, pageOrigin);
})();
