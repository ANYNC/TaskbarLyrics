import { readFileSync } from "node:fs";
import { describe, expect, it } from "vitest";
import { JSDOM } from "./helpers/localized-dom.js";

describe("browser relay lifetime",()=>{
  it("stops an invalidated extension context without throwing or repeatedly reporting",async()=>{
    const dom=new JSDOM("",{url:"https://music.test/"});
    try{
      const w=dom.window,packets=[];let calls=0;
      w.postMessage=packet=>packets.push(packet);
      w.chrome={runtime:{onMessage:{addListener(){}},sendMessage(){
        calls++;if(calls>1)throw new Error("Extension context invalidated.");
        return Promise.resolve({configuration:{enabled:true,sites:[{enabled:true,origin:"https://music.test"}]}});
      }}};
      w.eval(readFileSync(new URL("../../BrowserExtension/relay.js",import.meta.url),"utf8"));
      await Promise.resolve();
      const report=()=>w.dispatchEvent(new w.MessageEvent("message",{source:w,origin:w.location.origin,
        data:{channel:"taskbarlyrics-media-v1",payload:{mediaId:"media-1"}}}));
      report();report();await Promise.resolve();
      expect(calls).toBe(2);
      expect(packets.at(-1)).toMatchObject({channel:"taskbarlyrics-config-v1",enabled:false});
    }finally{dom.window.close();}
  });
});
