import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { JSDOM } from "./helpers/localized-dom.js";
import { normaliseMedia } from "../../BrowserExtension/rules.js";

const probe = readFileSync(new URL("../../BrowserExtension/media-probe.js", import.meta.url), "utf8");
function page(html, origin = "https://music.test/") {
  const dom = new JSDOM(html, {url:origin}), w = dom.window, packets = [];
  const session = {metadata:null,playbackState:"none",setActionHandler(){},setPositionState(){}};
  Object.defineProperty(w.navigator, "mediaSession", {value:session});
  w.postMessage = packet => packets.push(packet);
  w.eval(probe);
  const report = () => {
    const count = packets.length;
    w.dispatchEvent(new w.MessageEvent("message", {source:w, origin:w.location.origin,
      data:{channel:"taskbarlyrics-config-v1",enabled:true}}));
    return packets.slice(count).findLast(packet => packet.channel === "taskbarlyrics-media-v1")?.payload;
  };
  const media = (selector, duration=200, paused=false) => {
    const element = w.document.querySelector(selector);
    Object.defineProperties(element, {duration:{get:()=>duration},paused:{get:()=>paused},ended:{get:()=>false}});
    element.currentTime = 40;
    return element;
  };
  return {w,session,report,media,close:()=>w.close()};
}

describe("generic document metadata fallbacks",()=>{
  it.each(["https://music.test/","https://music.163.com/","https://open.spotify.com/"])("uses standard page metadata with the same rules on %s",origin=>{
    const p=page('<title>Website</title><meta name="title" content="Lower priority"><meta property="og:title" content="▶ Song, by Artist"><meta property="og:image" content="/cover.jpg"><audio></audio>',origin);
    try{
      p.media("audio");const snapshot=p.report();
      expect(snapshot).toMatchObject({title:"Song",artist:"Artist",positionSeconds:40,durationSeconds:200,
        details:{source:"element",titleSource:"document",artwork:[{src:new URL("/cover.jpg",origin).href}]}});
      expect(normaliseMedia(snapshot)).not.toBeNull();
    }finally{p.close();}
  });
  it("falls through empty meta fields and never carries a guessed artist to the next title",()=>{
    const p=page('<title>Website</title><meta property="og:title" content=" "><meta name="title" content="Song by: Artist"><audio></audio>');
    try{
      p.media("audio");const first=p.report();expect(first).toMatchObject({title:"Song",artist:"Artist"});
      p.w.document.querySelector('meta[name="title"]').setAttribute("content","Song - Another title");
      const next=p.report();expect(next).toMatchObject({title:"Song - Another title",artist:""});
      expect(next.details.trackVersion).toBeGreaterThan(first.details.trackVersion);
      p.w.document.querySelector('meta[name="title"]').remove();
      expect(p.report()).toMatchObject({title:"Website",artist:""});
    }finally{p.close();}
  });
  it("keeps declared media metadata and artwork ahead of page-wide fallbacks",()=>{
    const p=page('<meta property="og:title" content="Wrong, by Wrong"><meta property="og:image" content="/wrong.jpg"><audio></audio>');
    try{
      p.media("audio");p.session.metadata={title:"Real Song",artist:"Real Artist",album:"Real Album",artwork:[{src:"https://cdn.test/real.webp"}]};
      expect(p.report()).toMatchObject({title:"Real Song",artist:"Real Artist",album:"Real Album",details:{
        source:"associated-element",titleSource:"media-session",artwork:[{src:"https://cdn.test/real.webp"}]}});
    }finally{p.close();}
  });
  it("reads the selected video's own poster ahead of Open Graph imagery",()=>{
    const p=page('<title>Video</title><meta property="og:image" content="/page.jpg"><video poster="/poster.jpg"></video>');
    try{
      p.media("video");expect(p.report().details.artwork).toEqual([{src:"https://music.test/poster.jpg",sizes:"",type:""}]);
      p.w.document.querySelector("video").removeAttribute("poster");
      expect(p.report().details.artwork[0].src).toBe("https://music.test/page.jpg");
    }finally{p.close();}
  });
  it("resolves relative image paths against the document base URI",()=>{
    const p=page('<base href="https://images.test/album/"><title>Video</title><video poster="cover.jpg"></video>');
    try{p.media("video");expect(p.report().details.artwork[0].src).toBe("https://images.test/album/cover.jpg");}finally{p.close();}
  });
  it.each(["data:image/png;base64,AA","blob:https://music.test/id","https://user:password@cdn.test/image.jpg","javascript:alert(1)"])("filters unsupported fallback artwork (%s)",src=>{
    const p=page('<title>Video</title><video></video>');
    try{p.media("video").setAttribute("poster",src);expect(p.report().details.artwork).toEqual([]);}finally{p.close();}
  });
  it("does not assign page-wide artwork to an ambiguous multi-player document",()=>{
    const p=page('<title>Media</title><meta property="og:image" content="/page.jpg"><audio id="song"></audio><audio id="other"></audio>');
    try{
      p.media("#song");p.media("#other",180,true);
      expect(p.report()).toMatchObject({positionSeconds:40,details:{source:"element",artwork:[]}});
    }finally{p.close();}
  });
  it("keeps metadata-only sessions unknown instead of borrowing a background video's time or poster",()=>{
    const p=page('<meta property="og:image" content="/page.jpg"><video muted loop poster="/background.jpg"></video>');
    try{
      p.media("video",8).muted=true;p.session.metadata={title:"Actual Song",artist:"Artist"};
      expect(p.report()).toMatchObject({title:"Actual Song",artist:"Artist",canSeek:false,details:{
        source:"media-session",playbackState:"unknown",positionSeconds:null,durationSeconds:null,artwork:[]}});
    }finally{p.close();}
  });
  it("ignores unloaded elements and does not report page metadata alone as a playing track",()=>{
    const p=page('<meta property="og:title" content="Website"><audio id="empty"></audio><audio id="ready"></audio>');
    try{
      p.media("#empty",Number.NaN);expect(p.report()).toBeUndefined();
      p.media("#ready",200);expect(p.report()).toMatchObject({title:"Website",isPlaying:true,durationSeconds:200});
    }finally{p.close();}
  });
  it("preserves the open duration of live media without inventing an end or skip command",()=>{
    const p=page('<title>Live</title><audio></audio>');
    try{
      p.media("audio",Infinity);const snapshot=p.report();
      expect(snapshot).toMatchObject({isPlaying:true,positionSeconds:40,canSeek:false,details:{durationSeconds:null}});
      expect(snapshot.details.actions).not.toContain("nexttrack");
      expect(snapshot.details.actions).not.toContain("previoustrack");
    }finally{p.close();}
  });
});
