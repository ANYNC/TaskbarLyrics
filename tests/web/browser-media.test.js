import { readFileSync } from "node:fs";
import { describe, expect, it, vi } from "vitest";
import { JSDOM } from "./helpers/localized-dom.js";
import { normaliseMedia } from "../../BrowserExtension/rules.js";

const source = readFileSync(new URL("../../BrowserExtension/media-probe.js", import.meta.url), "utf8");
function page(html = "", origin = "https://music.test/", beforeEval) {
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
  if (beforeEval) beforeEval(w);
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
  return {pause:()=>paused=true,resume:()=>paused=false,seek:value=>{seeking=true;element.currentTime=value;},finish:()=>seeking=false};
}
const flush = async () => {for(let i=0;i<8;i++) await Promise.resolve();};

describe("generic browser media", () => {
  it("uses a running Web Audio context as playback evidence without inventing a timeline",()=>{
    const p=page("", "https://music.test/", w => {
      w.AudioContext = class { constructor(){ this.state="running"; this.currentTime=0; } };
    });
    try{
      p.session.playbackState="none";
      const context=new p.w.AudioContext();
      context.currentTime=2;
      expect(p.report()).toMatchObject({title:"Song A",isPlaying:true,positionSeconds:0,durationSeconds:0,
        details:{source:"media-session",playbackState:"playing",positionSeconds:null,durationSeconds:null}});
      context.state="suspended";
      const paused=p.report();
      expect(paused.isPlaying).toBe(false);
      expect(paused.details).toMatchObject({source:"media-session",playbackState:"unknown",positionSeconds:null,durationSeconds:null});
    }finally{p.close();}
  });
  it("associates an off-DOM VIDEO player while excluding a muted decorative video",()=>{
    const p=page('<video id="background"></video>');
    try{
      p.session.playbackState="none";
      const background=p.w.document.querySelector("video");media(background,6.92,3);background.muted=true;
      const player=p.w.document.createElement("video");media(player,299.7,40);
      p.session.metadata.artwork=[{src:"/song.jpg"}];
      expect(p.w.document.querySelectorAll("video")).toHaveLength(1);
      const snapshot=p.report();
      expect(snapshot).toMatchObject({title:"Song A",artist:"Artist",isPlaying:true,positionSeconds:40,durationSeconds:299.7,
        details:{source:"associated-element",titleSource:"media-session",artwork:[{src:"https://music.test/song.jpg"}]}});
      player.currentTime=120;
      expect(p.report().positionSeconds).toBe(120);
      player.currentTime=15;
      expect(p.report().positionSeconds).toBe(15);
    }finally{p.close();}
  });
  it("handles a playing VIDEO with a paused VIDEO preload and reports pause without changing the target",()=>{
    const p=page('<video id="player"></video><video id="preload"></video>');
    try{
      p.session.playbackState="none";
      const controls=media(p.w.document.querySelector("#player"),200,30);
      media(p.w.document.querySelector("#preload"),180,0).pause();
      expect(p.report()).toMatchObject({isPlaying:true,positionSeconds:30,durationSeconds:200});
      controls.pause();
      expect(p.report()).toMatchObject({isPlaying:false,positionSeconds:30,details:{source:"associated-element",playbackState:"paused"}});
    }finally{p.close();}
  });
  it("keeps the timeline unknown when an AUDIO and a VIDEO are both audibly playing",()=>{
    const p=page('<audio></audio><video></video>');
    try{
      p.session.playbackState="none";
      media(p.w.document.querySelector("audio"),200,40);media(p.w.document.querySelector("video"),10,3);
      expect(p.report()).toMatchObject({isPlaying:true,canSeek:false,details:{source:"media-session",positionSeconds:null,durationSeconds:null}});
    }finally{p.close();}
  });
  it("does not bind a lone muted background VIDEO to metadata-only music",()=>{
    const p=page('<video></video>');
    try{
      p.session.playbackState="none";
      const video=p.w.document.querySelector("video");media(video,8,3);video.muted=true;
      expect(p.report().details).toMatchObject({source:"media-session",playbackState:"unknown",positionSeconds:null,durationSeconds:null});
    }finally{p.close();}
  });
  it.each(["audio","video"])("preserves an unknown total duration for metadata-only live %s",tag=>{
    const p=page(`<${tag}></${tag}>`);
    try{
      p.session.playbackState="none";media(p.w.document.querySelector(tag),Infinity,40);
      expect(p.report()).toMatchObject({isPlaying:true,positionSeconds:40,canSeek:false,details:{durationSeconds:null}});
    }finally{p.close();}
  });
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
  it("uses the only playing audio while another audio is paused for preloading", () => {
    const p=page('<audio></audio><audio></audio>');
    try {
      p.session.playbackState="none";p.w.document.title="Fallback title";
      const audios=p.w.document.querySelectorAll("audio");media(audios[0],225,42);media(audios[1],100,5).pause();
      expect(p.report()).toMatchObject({title:"Song A",artist:"Artist",isPlaying:true,details:{source:"associated-element",titleSource:"media-session",positionSeconds:42,durationSeconds:225}});
    } finally {p.close();}
  });
  it("retains the selected audio through pause and mute despite paused preloads",()=>{
    const p=page('<audio id="current"></audio><audio id="preload"></audio>');
    try{
      p.session.playbackState="none";
      const current=p.w.document.querySelector("#current"), controls=media(current,225,42);
      media(p.w.document.querySelector("#preload"),100,0).pause();
      expect(p.report().isPlaying).toBe(true);
      controls.pause();expect(p.report()).toMatchObject({isPlaying:false,positionSeconds:42,details:{playbackState:"paused"}});
      current.muted=true;controls.resume();
      expect(p.report()).toMatchObject({isPlaying:true,positionSeconds:42,details:{source:"associated-element"}});
    }finally{p.close();}
  });
  it("retains playback evidence but clears the timeline during overlapping audio and rebinds after it ends",()=>{
    const p=page('<audio id="current"></audio><audio id="next"></audio>');
    try{
      p.session.playbackState="none";
      const current=media(p.w.document.querySelector("#current"),225,42);
      const next=media(p.w.document.querySelector("#next"),180,0);next.pause();
      expect(p.report().details.source).toBe("associated-element");
      next.resume();
      expect(p.report()).toMatchObject({isPlaying:true,canSeek:false,details:{source:"media-session",playbackState:"playing",positionSeconds:null,durationSeconds:null,actions:[]}});
      current.pause();p.session.metadata={title:"Song B",artist:"Artist"};
      expect(p.report()).toMatchObject({title:"Song B",isPlaying:true,positionSeconds:0,durationSeconds:180,details:{source:"associated-element"}});
    }finally{p.close();}
  });
  it("does not select between multiple paused audios without an established player",()=>{
    const p=page('<audio></audio><audio></audio>');
    try{
      p.session.playbackState="none";
      for(const audio of p.w.document.querySelectorAll("audio"))media(audio,200,20).pause();
      expect(p.report()).toMatchObject({isPlaying:false,details:{playbackState:"paused",positionSeconds:null,durationSeconds:null}});
    }finally{p.close();}
  });
  it("does not bind a paused preload when another unknown muted audio is running",()=>{
    const p=page('<audio id="muted"></audio><audio id="preload"></audio>');
    try{
      p.session.playbackState="none";
      const muted=p.w.document.querySelector("#muted");media(muted,200,20);muted.muted=true;
      media(p.w.document.querySelector("#preload"),180,0).pause();
      expect(p.report().details).toMatchObject({playbackState:"unknown",positionSeconds:null,durationSeconds:null});
    }finally{p.close();}
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
