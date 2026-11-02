export const HOST_NAME = "com.taskbarlyrics.browser";
export const PROTOCOL_VERSION = 1;
export function originOf(address) {
  try {
    const url = new URL(address);
    return ["http:", "https:"].includes(url.protocol) && !url.username && !url.password ? url.origin : null;
  } catch { return null; }
}
export function senderOrigin(sender) {
  const actual = originOf(sender.origin);
  const address = originOf(sender.url);
  if (sender.origin !== undefined && !actual) return null;
  if (address) return sender.origin === undefined || actual === address ? address : null;
  // Only the browser-supplied origin may authorize an inherited-origin frame.
  // Never infer it from the top-level tab or from a page message's payload.
  return /^about:(?:blank|srcdoc)(?:[?#]|$)|^blob:/.test(sender.url ?? "") ? actual : null;
}
export function permissionPattern(origin) {
  const url = new URL(origin);
  // Chrome host permissions cover all ports; exact origin filtering remains separate.
  return `${url.protocol}//${url.hostname}/*`;
}
export function allowedOrigin(configuration, address) {
  const origin = originOf(address);
  return Boolean(configuration.enabled && origin && configuration.sites?.some(site => site.enabled && site.origin === origin));
}
export function mediaText(value, maximum = 512) {
  if (typeof value !== "string") return "";
  // Do not split a UTF-16 pair at the native field limit or forward control characters.
  return value.toWellFormed().replace(/\p{Cc}/gu, " ").trim().slice(0, maximum).replace(/[\uD800-\uDBFF]$/, "");
}
export function normaliseMedia(raw) {
  if (!raw || typeof raw !== "object" || typeof raw.isPlaying !== "boolean" ||
      !Number.isFinite(raw.positionSeconds) || raw.positionSeconds < 0 || raw.positionSeconds > 31536000 ||
      !Number.isFinite(raw.durationSeconds) || raw.durationSeconds < 0 || raw.durationSeconds > 31536000 ||
      typeof raw.mediaId !== "string" || !/^[a-zA-Z0-9-]{1,64}$/.test(raw.mediaId)) return null;
  const title = mediaText(raw.title);
  const details = raw.details === undefined ? undefined : normaliseDetails(raw.details);
  if (raw.details !== undefined && !details) return null;
  return title ? { mediaId: raw.mediaId, title, artist: mediaText(raw.artist), album: mediaText(raw.album),
    isPlaying: raw.isPlaying, positionSeconds: raw.positionSeconds, durationSeconds: raw.durationSeconds,
    canSeek: raw.canSeek === true, ...(details ? { details } : {}) } : null;
}
export function normaliseDetails(raw) {
  const seconds = value => value === null || Number.isFinite(value) && value >= 0 && value <= 31536000;
  if (!raw || raw.version !== 1 || !Number.isSafeInteger(raw.trackVersion) || raw.trackVersion < 0 ||
      !["element", "media-session", "associated-element"].includes(raw.source) ||
      !["document", "media-session"].includes(raw.titleSource) ||
      !["playing", "paused", "ended", "seeking", "unknown"].includes(raw.playbackState) ||
      !seconds(raw.positionSeconds) || !seconds(raw.durationSeconds) ||
      !Number.isFinite(raw.playbackRate) || Math.abs(raw.playbackRate) > 16 ||
      !Number.isFinite(raw.sampleAgeSeconds) || raw.sampleAgeSeconds < 0 || raw.sampleAgeSeconds > 6 ||
      !Array.isArray(raw.actions) || raw.actions.length > 7 ||
      !Array.isArray(raw.artwork) || raw.artwork.length > 8 ||
      !Array.isArray(raw.seekable) || raw.seekable.length > 8) return null;
  const actions = [...new Set(raw.actions.filter(action => ["play", "pause", "previoustrack", "nexttrack", "seekbackward", "seekforward", "seekto"].includes(action)))];
  const seekable = raw.seekable.filter(range => range && Number.isFinite(range.start) && Number.isFinite(range.end) &&
    range.start >= 0 && range.end > range.start && range.end <= 31536000).map(({start, end}) => ({start, end}));
  const artwork = raw.artwork.flatMap(item => {
    try {
      if (!item || typeof item.src !== "string" || item.src.length > 4096) return [];
      const url = new URL(item.src);
      if (!["http:", "https:"].includes(url.protocol) || url.username || url.password) return [];
      return [{src:url.href, sizes:mediaText(item.sizes,80), type:mediaText(item.type,80)}];
    } catch { return []; }
  });
  return { version:1, trackVersion:raw.trackVersion, source:raw.source, titleSource:raw.titleSource, playbackState:raw.playbackState,
    positionSeconds:raw.positionSeconds, durationSeconds:raw.durationSeconds, playbackRate:raw.playbackRate,
    sampleAgeSeconds:raw.sampleAgeSeconds, anchorAgeSeconds:seconds(raw.anchorAgeSeconds) ? raw.anchorAgeSeconds : null, actions, seekable, artwork };
}
