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
  const audioContexts = new Set();
  const audioContextTimes = new WeakMap();
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
  function documentMetadata() {
    const content = selector => Array.from(document.querySelectorAll(selector))
      .map(node => node.getAttribute("content")?.trim()).find(Boolean) || "";
    const raw = content('meta[property="og:title"]') || content('meta[name="title"]') || document.title;
    // Only explicit author separators are interpreted. A site name or a dash in
    // a title is not evidence of an artist, and must not pollute lyric matching.
    const clean = raw.replace(/[\u25A0\u25B6\u25BA\u25B7\u25FC\u2758-\u275A\u25AE]/g, " ").replace(/\s+/g, " ").trim();
    const match = clean.match(/^(.+?)(?:,\s+by\s+|\s+by:\s*)(.+)$/i);
    return { title: match ? match[1].trim() : clean, artist: match ? match[2].trim() : "",
      artwork: content('meta[property="og:image"]') };
  }
  function metadataKey() {
    const metadata = session?.metadata;
    const fallback = metadata?.title?.trim() ? null : documentMetadata();
    return JSON.stringify([metadata?.title?.trim() || fallback.title, metadata?.title?.trim() ? metadata.artist || "" : fallback.artist, metadata?.album || ""]);
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
    // A player can keep new Audio() off-DOM. Weak references avoid owning the
    // lifetime of discarded players while still observing their native timeline.
    for (const reference of detachedMedia) if (!reference.deref()) detachedMedia.delete(reference);
    if (detachedMedia.size >= 128) return;
    observedMedia.add(media);
    detachedMedia.add(new WeakRef(media));
    for (const name of ["play", "pause", "ended", "seeking", "seeked", "ratechange", "loadedmetadata", "durationchange", "emptied"])
      media.addEventListener(name, event => { if (event.type === "play") active = event.currentTarget; report(); });
  }
  function observeCreated(media) {
    if (media instanceof HTMLMediaElement) rememberMedia(media);
    return media;
  }
  function rememberAudioContext(context) {
    if (context && typeof context === "object") audioContexts.add(context);
    return context;
  }
  // A muted video can still be the page's only media element while the real
  // audio runs through Web Audio. A running context is playback evidence only;
  // it never supplies a song position or duration.
  const audioConstructors = new Set();
  for (const name of ["AudioContext", "webkitAudioContext"]) {
    const constructor = window[name];
    if (typeof constructor !== "function" || audioConstructors.has(constructor)) continue;
    audioConstructors.add(constructor);
    try {
      window[name] = new Proxy(constructor, {
        apply(target, receiver, args) { return rememberAudioContext(Reflect.apply(target, receiver, args)); },
        construct(target, args, newTarget) { return rememberAudioContext(Reflect.construct(target, args, newTarget)); }
      });
    } catch { /* A non-configurable constructor remains unobserved. */ }
  }
  function audioContextPlaying() {
    let playing = false;
    for (const context of audioContexts) {
      if (context.state === "closed") {
        audioContexts.delete(context);
        audioContextTimes.delete(context);
        continue;
      }
      const current = Number(context.currentTime);
      if (context.state === "running" && Number.isFinite(current) && current > (audioContextTimes.get(context) ?? 0)) playing = true;
      audioContextTimes.set(context, current);
    }
    return playing;
  }
  // Observing play alone misses off-DOM players that invoke a previously saved
  // native play method. Register media at creation/load too, keeping the native
  // objects, prototypes, return values and exceptions intact.
  if (typeof window.Audio === "function") window.Audio = new Proxy(window.Audio, {
    apply(target, receiver, args) { return observeCreated(Reflect.apply(target, receiver, args)); },
    construct(target, args, newTarget) { return observeCreated(Reflect.construct(target, args, newTarget)); }
  });
  for (const name of ["createElement", "createElementNS"]) {
    const original = Document.prototype[name];
    Document.prototype[name] = function() { return observeCreated(Reflect.apply(original, this, arguments)); };
  }
  const originalLoad = HTMLMediaElement.prototype.load;
  HTMLMediaElement.prototype.load = function() {
    const result = Reflect.apply(originalLoad, this, arguments);
    rememberMedia(this);
    report();
    return result;
  };
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
    // Empty/preloaded players do not establish a current media candidate. Live
    // media can have infinite duration; its timeline remains explicitly open.
    const all = elements().filter(media => media.duration > 0);
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
  function mediaFallback() {
    // VIDEO can carry audio too. Use native playback/audibility evidence rather
    // than treating a tag name as proof of which object is the song player.
    const players = elements().filter(media => media.duration > 0);
    const playing = players.filter(media => !media.paused && !media.ended && !media.muted && media.volume > 0);
    let media = playing.length === 1 ? playing[0] : null;
    // Paused preloads are not competing playback. Preserve an established
    // player's identity through pause/mute, but not simultaneous audible media.
    if (!playing.length) {
      if (associationKind === "media-state" && players.includes(associated) && !associated.ended) media = associated;
      else if (players.length === 1 && !players[0].muted && players[0].volume > 0) media = players[0];
    }
    // Playback evidence and timeline identity are separate: overlapping media
    // can prove that this document is playing without identifying its timeline.
    const state = playing.length ? "playing" : players.length && players.every(item => item.paused || item.ended) ? "paused" : "unknown";
    return {media, state};
  }
  function diagnose(source, playing, hasTitle, hasDuration, titleSource = "none", hasArtwork = false, selectedTag = "none") {
    const association = source === "associated-element" ? associationKind : "none";
    const observed = elements(), eligible = observed.filter(media => media.duration > 0).length;
    const audio = observed.filter(media => media.tagName === "AUDIO" && Number.isFinite(media.duration) && media.duration > 0);
    const audioPlaying = audio.filter(media => !media.paused && !media.ended);
    const audiblePlaying = audioPlaying.filter(media => !media.muted && media.volume > 0).length;
    const videoCount = observed.filter(media => media.tagName === "VIDEO").length;
    const audioTagCount = observed.filter(media => media.tagName === "AUDIO").length;
    const openDurationCount = observed.filter(media => media.duration === Infinity).length;
    const mediaPlaying = observed.filter(media => media.duration > 0 && !media.paused && !media.ended);
    const mediaAudiblePlaying = mediaPlaying.filter(media => !media.muted && media.volume > 0).length;
    const declaredState = session?.playbackState ?? "unavailable";
    const state = `${source}:${playing}:${hasTitle}:${hasDuration}:${titleSource}:${hasArtwork}:${association}:${observed.length}:${eligible}:${declaredState}:${actions.size}:${audio.length}:${audioPlaying.length}:${audiblePlaying}:${videoCount}:${audioTagCount}:${openDurationCount}:${mediaPlaying.length}:${mediaAudiblePlaying}:${selectedTag}`;
    if (state === diagnosticState) return;
    diagnosticState = state;
    console.debug(`[TaskbarLyrics] MediaProbe Source=${source} Playing=${playing} HasTitle=${hasTitle} HasDuration=${hasDuration} TitleSource=${titleSource} HasArtwork=${hasArtwork} Association=${association} MediaCount=${observed.length} EligibleCount=${eligible} DeclaredState=${declaredState} ActionCount=${actions.size} AudioCount=${audio.length} AudioPlaying=${audioPlaying.length} AudioAudiblePlaying=${audiblePlaying} AudioTagCount=${audioTagCount} VideoCount=${videoCount} OpenDurationCount=${openDurationCount} MediaPlaying=${mediaPlaying.length} MediaAudiblePlaying=${mediaAudiblePlaying} SelectedTag=${selectedTag}`);
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
  function coverCandidates(metadata, media) {
    const standard = artwork(metadata);
    if (standard.length) return standard;
    // A poster belongs to this specific video. Page-wide Open Graph imagery is
    // a weaker fallback and is used only with one eligible media in this document.
    const poster = media?.tagName === "VIDEO" ? media.getAttribute("poster") : null;
    const eligible = elements().filter(item => item.duration > 0);
    const pageCover = media && eligible.length === 1 && eligible[0] === media ? documentMetadata().artwork : "";
    return artwork({ artwork: [poster, pageCover].filter(Boolean).map(src => ({src})) }).slice(0, 1);
  }
  function capture() {
    refreshTrack(); updateSessionPosition();
    const metadata = session?.metadata;
    const candidate = pick();
    const fallback = mediaFallback();
    const audioPlaying = audioContextPlaying();
    // Metadata is still useful when its timeline cannot be identified. Retain
    // it with unknown time rather than attaching it to an arbitrary player.
    const usesSession = Boolean(metadata?.title?.trim());
    if (usesSession && !sessionPosition && session.playbackState === "none" && fallback.media) {
      associated = fallback.media; associationKind = "media-state";
    }
    // Associate only after matching both duration and position in two spaced
    // observations. Never bind across documents or just by matching a title.
    if (usesSession && !associated && candidate && sessionPosition &&
        Number.isFinite(candidate.duration) && Math.abs(candidate.duration - sessionPosition.duration) < 1 &&
        Math.abs(candidate.currentTime - sessionPosition.position) < 1 && !candidate.seeking) {
      if (associationCandidate === candidate && performance.now() - associationAt >= 750) { associated = candidate; associationKind = "timeline-match"; }
      else if (associationCandidate !== candidate) { associationCandidate = candidate; associationAt = performance.now(); }
    } else if (!associated) associationCandidate = null;
    if (associated && (!elements().includes(associated) || (associationKind === "media-state"
        ? fallback.media !== associated || Boolean(sessionPosition)
        : !sessionPosition || Math.abs(associated.duration - sessionPosition.duration) >= 1))) {
      associated = null; associationKind = null;
    }
    const media = usesSession ? associated : candidate;
    if (!usesSession && !media) return null;
    const state = media ? media.seeking ? "seeking" : media.ended ? "ended" : media.paused ? "paused" : "playing"
      : ["playing", "paused"].includes(session?.playbackState) ? session.playbackState
        : usesSession ? (audioPlaying ? "playing" : fallback.state) : "unknown";
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
      seekable: ranges, actions: [...new Set(supported)], artwork: coverCandidates(usesSession ? metadata : null, media) };
    const pageMetadata = usesSession ? null : documentMetadata();
    return { media, snapshot: { mediaId: usesSession ? sessionMediaId : idOf(media), title: usesSession ? metadata.title : pageMetadata.title,
      artist: usesSession ? metadata.artist || "" : pageMetadata.artist, album: usesSession ? metadata.album || "" : "",
      isPlaying: state === "playing", positionSeconds: position ?? 0, durationSeconds: duration ?? 0, canSeek: seekable, details } };
  }
  function report() {
    if (!enabled) return;
    latest = capture();
    reportedMediaId = latest?.snapshot.mediaId ?? null;
    if (!latest) { diagnose("none", false, false, false); return; }
    const snapshot = latest.snapshot;
    diagnose(snapshot.details.source, snapshot.isPlaying, Boolean(snapshot.title?.trim()), snapshot.durationSeconds > 0,
      snapshot.details.titleSource, snapshot.details.artwork.length > 0, latest.media?.tagName ?? "none");
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
