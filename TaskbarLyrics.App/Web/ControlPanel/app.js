const root = document.documentElement;
const artwork = document.getElementById("artworkImage");
const title = document.getElementById("title");
const artist = document.getElementById("artist");
const source = document.getElementById("source");
const sourceRow = document.getElementById("sourceRow");
const sourceIcon = document.getElementById("sourceIcon");
const sourceFallback = document.getElementById("sourceFallback");
const previous = document.getElementById("previous");
const playPause = document.getElementById("playPause");
const next = document.getElementById("next");
const playIcon = document.getElementById("playIcon");
const pauseIcon = document.getElementById("pauseIcon");
const seek = document.getElementById("seek");
const elapsed = document.getElementById("elapsed");
const duration = document.getElementById("duration");
const translation = document.getElementById("translation");
const volume = document.getElementById("volume");
const mute = document.getElementById("mute");
let durationMs = 0;
let positionMs = 0;
let isPlaying = false;
let positionUpdatedAt = performance.now();
let seeking = false;
let changingVolume = false;
let volumeCommitTimer;

function post(type, payload = {}) {
  window.chrome?.webview?.postMessage(JSON.stringify({ version: 1, type, payload }));
}

function formatTime(milliseconds) {
  const seconds = Math.max(0, Math.floor(milliseconds / 1000));
  const minutes = Math.floor(seconds / 60);
  const hours = Math.floor(minutes / 60);
  return hours ? `${hours}:${String(minutes % 60).padStart(2, "0")}:${String(seconds % 60).padStart(2, "0")}`
    : `${minutes}:${String(seconds % 60).padStart(2, "0")}`;
}

function paintRange(input) {
  input.style.setProperty("--value", `${Number(input.value) / Number(input.max) * 100}%`);
}

function paintTimeline() {
  if (seeking) return;
  const estimated = isPlaying ? positionMs + performance.now() - positionUpdatedAt : positionMs;
  const current = Math.min(Math.max(0, estimated), durationMs);
  seek.value = durationMs > 0 ? String(Math.round(current / durationMs * 1000)) : "0";
  paintRange(seek);
  elapsed.textContent = formatTime(current);
  duration.textContent = formatTime(durationMs);
}

function paintPlaybackState(playing) {
  isPlaying = playing === true;
  playIcon.toggleAttribute("hidden", isPlaying);
  pauseIcon.toggleAttribute("hidden", !isPlaying);
  playPause.setAttribute("aria-label", isPlaying ? "暂停" : "播放");
}

previous.addEventListener("click", () => post("mediaAction", { action: "previous" }));
playPause.addEventListener("click", () => post("mediaAction", { action: "toggle" }));
next.addEventListener("click", () => post("mediaAction", { action: "next" }));
document.getElementById("settings").addEventListener("click", () => post("openSettings"));
translation.addEventListener("click", () => post("toggleTranslation"));
seek.addEventListener("input", () => {
  seeking = true;
  paintRange(seek);
  elapsed.textContent = formatTime(Number(seek.value) / 1000 * durationMs);
});
seek.addEventListener("change", () => {
  const targetMs = Math.round(Number(seek.value) / 1000 * durationMs);
  post("seek", { positionMs: targetMs });
  positionMs = targetMs;
  positionUpdatedAt = performance.now();
  seeking = false;
});
for (const eventName of ["pointercancel", "blur"]) {
  seek.addEventListener(eventName, () => {
    seeking = false;
    paintTimeline();
  });
}
volume.addEventListener("input", () => {
  changingVolume = true;
  paintRange(volume);
  clearTimeout(volumeCommitTimer);
  volumeCommitTimer = setTimeout(() => post("setVolume", { level: Number(volume.value) }), 50);
});
volume.addEventListener("change", () => {
  clearTimeout(volumeCommitTimer);
  post("setVolume", { level: Number(volume.value) });
  changingVolume = false;
});
volume.addEventListener("blur", () => { changingVolume = false; });
mute.addEventListener("click", () => post("toggleMute"));
document.addEventListener("keydown", event => {
  if (event.key === "Escape") {
    event.preventDefault();
    post("dismiss");
  }
});

window.controlPanel = {
  receive(message) {
    if (message?.version !== 1) return;
    if (message.type === "timeline") {
      const data = message.payload;
      if (!data || typeof data !== "object") return;
      positionMs = Number.isFinite(data.positionMs) ? Math.max(0, data.positionMs) : 0;
      durationMs = Number.isFinite(data.durationMs) ? Math.max(0, data.durationMs) : 0;
      isPlaying = data.isPlaying === true;
      positionUpdatedAt = performance.now();
      seek.disabled = data.canSeek !== true || durationMs <= 0;
      paintTimeline();
    } else if (message.type === "volume") {
      const data = message.payload;
      if (!data || typeof data !== "object") return;
      volume.disabled = !Number.isFinite(data.level);
      if (!changingVolume && !volume.disabled) volume.value = String(Math.max(0, Math.min(100, data.level)));
      paintRange(volume);
      mute.setAttribute("aria-label", data.muted === true ? "取消静音" : "静音");
      mute.setAttribute("aria-pressed", String(data.muted === true));
    } else if (message.type === "playback") {
      if (message.payload?.isPlaying !== true && message.payload?.isPlaying !== false) return;
      paintPlaybackState(message.payload.isPlaying);
      paintTimeline();
    } else if (message.type === "snapshot") {
      const data = message.payload;
      if (!data || typeof data !== "object") return;
      const hasTrack = data.hasTrack === true;
      title.textContent = hasTrack ? data.title || "未知歌曲" : "暂无播放";
      artist.textContent = hasTrack ? data.artist || "未知歌手" : "当前没有播放内容";
      const sourceName = hasTrack ? data.source || "" : "";
      source.textContent = sourceName;
      sourceRow.hidden = !source.textContent;
      const sourceIconUri = data.sourceIconDataUri || "";
      const hasSourceIcon = /^data:image\/(?:png|jpeg|webp|svg\+xml)[;,]/.test(sourceIconUri);
      sourceRow.classList.toggle("default-icon", hasSourceIcon && data.sourceUsesDefaultIcon === true);
      sourceIcon.hidden = !hasSourceIcon;
      sourceFallback.toggleAttribute("hidden", hasSourceIcon);
      if (hasSourceIcon && sourceIcon.src !== sourceIconUri) sourceIcon.src = sourceIconUri;
      else if (!hasSourceIcon) sourceIcon.removeAttribute("src");
      sourceIcon.onerror = () => {
        sourceIcon.hidden = true;
        sourceFallback.removeAttribute("hidden");
        sourceRow.classList.remove("default-icon");
      };
      title.title = title.textContent;
      artist.title = artist.textContent;
      const coverUri = data.coverDataUri || data.fallbackIconDataUri || "";
      artwork.classList.toggle("visible", Boolean(coverUri));
      if (coverUri && artwork.src !== coverUri) artwork.src = coverUri;
      else if (!coverUri) artwork.removeAttribute("src");
      artwork.onerror = () => artwork.classList.remove("visible");
      for (const button of [previous, playPause, next]) button.disabled = !hasTrack;
      paintPlaybackState(data.isPlaying);
      translation.setAttribute("aria-pressed", String(data.translationEnabled === true));
      root.classList.toggle("light", data.light === true);
    }
  }
};

setInterval(paintTimeline, 250);
