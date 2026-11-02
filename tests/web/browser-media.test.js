import { readFileSync } from "node:fs";
import { describe, expect, it, vi } from "vitest";
import { JSDOM } from "./helpers/localized-dom.js";
import { normaliseMedia } from "../../BrowserExtension/rules.js";

const source = readFileSync(new URL("../../BrowserExtension/media-probe.js", import.meta.url), "utf8");
function page(html = "", origin = "https://music.test/") {
  const dom = new JSDOM(html, {url:origin});
  const w = dom.window, packets = [];
  let now = 0;
  Object.defineProperty(w.performance, "now", {value:()=>now});
  class Session {
    metadata = {title:"Song A", artist:"Artist", album:"Album", artwork:[]};
    state = "playing";
    get playbackState() { return this.state; }
    set playbackState(value) { this.state = value; }
    setActionHandler() {}
    setPositionState() {}
  }
  const session = new Session();
  Object.defineProperty(w.navigator, "mediaSession", {value:session});
  w.postMessage = value => packets.push(value);
  w.eval(source);
  const emit = data => w.dispatchEvent(new w.MessageEvent("message", {source:w,origin:w.location.origin,data}));
  const report = () => { emit({channel:"taskbarlyrics-config-v1", enabled:true}); return packets.filter(p=>p.channel==="taskbarlyrics-media-v1").at(-1)?.payload; };
  const command = (action, extra={}) => emit({channel:"taskbarlyrics-command-v1",payload:{requestId:Math.random().toString(),mediaId:report().mediaId,action,...extra}});
  const result = () => packets.filter(p=>p.channel==="taskbarlyrics-result-v1").at(-1);
  return {w,session,report,command,result,packets,advance:ms=>now+=ms,close:()=>w.close()};
}
function media(element, duration, initial=10) {
  let paused = false, seeking = false;
  Object.defineProperties(element, {duration:{get:()=>duration},paused:{get:()=>paused},ended:{get:()=>false},seeking:{get:()=>seeking},
    seekable:{get:()=>({length:1,start:()=>0,end:()=>duration})}});
  element.currentTime = initial;
  element.pause = () => { paused=true; };
  return {pause:()=>paused=true,seek:value=>{seeking=true;element.currentTime=value;},finish:()=>seeking=false};
}
const flush = async () => {for(let i=0;i<8;i++) await Promise.resolve();};

describe("generic browser media", () => {
  it("keeps metadata and artwork for a metadata-only session with one audible audio, ignoring a decorative video", () => {
    const p=page('<audio></audio><video></video>');
    try {
      p.session.playbackState="none";
      p.session.metadata.artwork=[{src:"https://cdn.test/cover.webp",sizes:"256x256",type:"image/webp"}];
      media(p.w.document.querySelector("audio"),225,42);
      const video=p.w.document.querySelector("video");media(video,7.8,2);video.muted=true;
      const snapshot=p.report();
      expect(snapshot).toMatchObject({title:"Song A",artist:"Artist",isPlaying:true,positionSeconds:42,durationSeconds:225,
        details:{source:"associated-element",titleSource:"media-session",artwork:[{src:"https://cdn.test/cover.webp"}]}});
    } finally {p.close();}
  });
  it("does not attach metadata-only session information when multiple audio players are present", () => {
    const p=page('<audio></audio><audio></audio>');
    try {
      p.session.playbackState="none";p.w.document.title="Fallback title";
      const audios=p.w.document.querySelectorAll("audio");media(audios[0],225,42);media(audios[1],100,5).pause();
      expect(p.report()).toMatchObject({title:"Fallback title",artist:"",details:{source:"element",titleSource:"document",artwork:[]}});
    } finally {p.close();}
  });
  it.each(["https://music.test/", "https://different-player.test/"])("does not mix decorative video with a Media Session on %s", origin => {
    const p = page('<video muted loop></video>',origin);
    try {
      media(p.w.document.querySelector("video"),7.882,2);
      p.session.setPositionState({position:10,duration:146.402});
      expect(p.report()).toMatchObject({positionSeconds:10,durationSeconds:146.402,details:{source:"media-session"}});
      p.session.setPositionState({position:90,duration:146.402});
      expect(p.report().positionSeconds).toBe(90);
      p.w.document.querySelector("video").pause();
      expect(p.report().positionSeconds).toBe(90);
    } finally {p.close();}
  });
  it("binds a matching timeline then immediately follows a forward and backward website seek", () => {
    const p=page('<audio></audio>');
    try {
      const audio=p.w.document.querySelector("audio"), controls=media(audio,200,10);
      p.session.setPositionState({position:10,duration:200});
      expect(p.report().details.source).toBe("media-session");
      p.advance(1000); audio.currentTime=11;
      expect(p.report().details.source).toBe("associated-element");
      controls.seek(90);
      expect(p.report()).toMatchObject({positionSeconds:90,isPlaying:false,details:{playbackState:"seeking"}});
      controls.finish();
      expect(p.report()).toMatchObject({positionSeconds:90,isPlaying:true});
      controls.seek(20);controls.finish();
      expect(p.report().positionSeconds).toBe(20);
    } finally {p.close();}
  });
  it("discards the previous association and unknown timeline on a new track", () => {
    const p=page('<audio></audio>');
    try {
      const audio=p.w.document.querySelector("audio");media(audio,200,10);
      p.session.setPositionState({position:10,duration:200}); const first=p.report();
      p.advance(1000);audio.currentTime=11;p.report();
      p.session.metadata={title:"Song B"};
      const next=p.report();
      expect(next.details).toMatchObject({source:"media-session",positionSeconds:null,durationSeconds:null});
      expect(next.details.trackVersion).toBeGreaterThan(first.details.trackVersion);
      expect(next.canSeek).toBe(false);
    } finally {p.close();}
  });
  it("coalesces a site's position-before-metadata update in the same task", async () => {
    const p=page();
    try {
      p.session.setPositionState({position:10,duration:200});await flush();const before=p.report();
      p.session.setPositionState({position:0,duration:180});
      p.session.metadata={title:"Song B"};p.session.playbackState="paused";await flush();
      expect(p.report()).toMatchObject({title:"Song B",positionSeconds:0,durationSeconds:180,isPlaying:false,details:{positionSeconds:0}});
      expect(p.report().details.trackVersion).toBeGreaterThan(before.details.trackVersion);
    } finally {p.close();}
  });
  it("retains explicit unknowns and only standard HTTP artwork from the current metadata", () => {
    const p=page();
    try {
      p.session.playbackState="none";
      p.session.metadata.artwork=[{src:"/cover.webp?token=necessary",sizes:"256x256",type:"image/webp"},{src:"data:image/png;base64,AA"},{src:"https://user:secret@other.test/cover"}];
      const snapshot=p.report();
      expect(snapshot.details).toMatchObject({playbackState:"unknown",positionSeconds:null,durationSeconds:null,
        artwork:[{src:"https://music.test/cover.webp?token=necessary",sizes:"256x256",type:"image/webp"}]});
      expect(normaliseMedia(snapshot).details).toEqual(snapshot.details);
      expect(normaliseMedia({...snapshot,details:{...snapshot.details,sampleAgeSeconds:7}})).toBeNull();
      expect(normaliseMedia({...snapshot,details:{...snapshot.details,version:2}})).toBeNull();
    } finally {p.close();}
  });
  it("reports only registered controls and rejects a stale track command", async () => {
    const p=page();
    try {
      const next=vi.fn();p.session.setActionHandler("nexttrack",next);
      const before=p.report();expect(before.details.actions).toEqual(["nexttrack"]);
      p.session.metadata={title:"Song B"};p.report();
      p.command("NextTrack",{trackVersion:before.details.trackVersion});await flush();
      expect(next).not.toHaveBeenCalled();expect(p.result()).toMatchObject({ok:false,reason:"stale-track"});
    } finally {p.close();}
  });
  it("distinguishes observed effects, handler rejection and a no-op handler", async () => {
    vi.useFakeTimers();const p=page();
    try {
      p.session.setActionHandler("pause",()=>{p.session.playbackState="paused";});
      p.command("TogglePlayPause");await flush();
      expect(p.result()).toMatchObject({ok:true,status:"observed",observed:"paused"});
      p.session.setActionHandler("play",()=>false);
      p.command("TogglePlayPause");await flush();
      expect(p.result()).toMatchObject({ok:false,reason:"handler-rejected"});
      p.session.setActionHandler("play",()=>{});
      p.command("TogglePlayPause");await vi.advanceTimersByTimeAsync(1100);await flush();
      expect(p.result()).toMatchObject({ok:false,status:"unconfirmed",reason:"effect-not-observed"});
    } finally {p.close();vi.useRealTimers();}
  });
  it("confirms an actual next track change without calling a different document", async () => {
    const p=page();
    try {
      p.session.setActionHandler("nexttrack",()=>{p.session.metadata={title:"Song B"};});
      p.command("NextTrack");await flush();
      expect(p.result()).toMatchObject({ok:true,status:"observed"});
    } finally {p.close();}
  });
  it("bounds a handler that never settles and releases the command slot", async () => {
    vi.useFakeTimers();const p=page();
    try {
      p.session.setActionHandler("pause",()=>new Promise(()=>{}));
      p.command("TogglePlayPause");await vi.advanceTimersByTimeAsync(800);await flush();
      expect(p.result()).toMatchObject({ok:false,status:"unconfirmed",reason:"handler-timeout"});
      p.session.setActionHandler("pause",()=>{p.session.playbackState="paused";});
      p.command("TogglePlayPause");await flush();
      expect(p.result()).toMatchObject({ok:true,status:"observed"});
    } finally {p.close();vi.useRealTimers();}
  });
});
