import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { JSDOM } from "./helpers/localized-dom.js";

const probe = readFileSync(new URL("../../BrowserExtension/media-probe.js", import.meta.url), "utf8");
function page() {
  const dom = new JSDOM("", {url:"https://music.test/"}), w = dom.window, packets = [];
  const playResult = Promise.resolve();
  const loadResult = {};
  w.HTMLMediaElement.prototype.play = function() { this.dispatchEvent(new w.Event("play")); return playResult; };
  w.HTMLMediaElement.prototype.load = function() {
    if (!(this instanceof w.HTMLMediaElement)) throw new w.TypeError("Illegal invocation");
    return loadResult;
  };
  const savedPlay = w.HTMLMediaElement.prototype.play, savedCreate = w.document.createElement.bind(w.document);
  const savedAudio = w.Audio, savedPrototype = w.Audio.prototype;
  Object.defineProperty(w.navigator,"mediaSession",{value:{metadata:{title:"Song",artist:"Artist",artwork:[{src:"/cover.jpg"}]},
    playbackState:"none",setActionHandler(){}}});
  w.postMessage = packet => packets.push(packet);
  w.eval(probe);
  const enable = () => w.dispatchEvent(new w.MessageEvent("message",{source:w,origin:w.location.origin,
    data:{channel:"taskbarlyrics-config-v1",enabled:true}}));
  const current = () => packets.findLast(packet=>packet.channel==="taskbarlyrics-media-v1")?.payload;
  return {w,savedPlay,savedCreate,savedAudio,savedPrototype,playResult,loadResult,enable,current,close:()=>w.close()};
}
function ready(media) {
  Object.defineProperties(media,{duration:{get:()=>200},paused:{get:()=>false},ended:{get:()=>false}});
  media.currentTime=35;
}

describe("generic off-DOM media discovery",()=>{
  it.each(["constructor","call","createElement","createElementNS"])("observes %s media even when play bypasses the installed prototype hook",async creation=>{
    const p=page();try{
      p.enable();
      const media=creation==="constructor" ? new p.w.Audio() : creation==="call" ? p.w.Audio()
        : creation==="createElement" ? p.w.document.createElement("audio")
          : p.w.document.createElementNS("http://www.w3.org/1999/xhtml","audio");
      ready(media);
      expect(p.savedPlay.call(media)).toBe(p.playResult);await p.playResult;
      expect(p.w.document.querySelectorAll("audio,video")).toHaveLength(0);
      expect(p.current()).toMatchObject({title:"Song",artist:"Artist",isPlaying:true,positionSeconds:35,durationSeconds:200,
        details:{source:"associated-element",artwork:[{src:"https://music.test/cover.jpg"}]}});
    }finally{p.close();}
  });
  it("observes an older off-DOM instance on load without changing the native result or invocation errors",()=>{
    const p=page();try{
      const media=p.savedCreate("audio");ready(media);p.enable();
      expect(p.current().details.positionSeconds).toBeNull();
      expect(media.load()).toBe(p.loadResult);
      expect(p.current()).toMatchObject({isPlaying:true,positionSeconds:35,durationSeconds:200});
      expect(()=>p.w.HTMLMediaElement.prototype.load.call({})).toThrow(p.w.TypeError);
    }finally{p.close();}
  });
  it("preserves Audio prototypes, subclass construction and unrelated document creation",()=>{
    const p=page();try{
      expect(p.w.Audio.prototype).toBe(p.savedPrototype);
      class AudioPlayer extends p.w.Audio {}
      class NativeAudioPlayer extends p.savedAudio {}
      const media=new AudioPlayer();
      const nativeMedia=new NativeAudioPlayer();
      // Audio is an exotic factory in some hosts (including jsdom). Preserve
      // that host's actual subclass behavior rather than inventing a new one.
      expect(media instanceof AudioPlayer).toBe(nativeMedia instanceof NativeAudioPlayer);
      expect(Object.getPrototypeOf(media)).toBe(Object.getPrototypeOf(nativeMedia));
      expect(media).toBeInstanceOf(p.savedAudio);expect(media).toBeInstanceOf(p.w.HTMLAudioElement);
      expect(p.w.document.createElement("div")).toBeInstanceOf(p.w.HTMLDivElement);
      expect(()=>p.w.document.createElement("invalid name")).toThrow();
      expect(()=>p.w.Document.prototype.createElement.call({},"audio")).toThrow(p.w.TypeError);
    }finally{p.close();}
  });
});
