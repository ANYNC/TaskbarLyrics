import { readFileSync } from "node:fs";
import { describe, expect, it, vi } from "vitest";
import * as rules from "../../BrowserExtension/rules.js";

const source=readFileSync(new URL("../../BrowserExtension/worker.js",import.meta.url),"utf8").replace(/^import .*?;\s*/,"");
const flush=async()=>{for(let i=0;i<30;i++)await Promise.resolve();};
async function worker(mediaDetailsVersion) {
  const handlers={},packets=[],commands=[];
  const event=name=>({addListener:callback=>handlers[name]=callback});
  const port={postMessage:packet=>packets.push(packet),onMessage:event("native"),onDisconnect:event("disconnect")};
  const chrome={storage:{local:{get:async()=>({clientId:"profile"}),set:async()=>{}}},
    runtime:{connectNative:()=>port,onMessage:event("runtime"),onStartup:event("startup"),onInstalled:event("installed")},
    permissions:{contains:async()=>true,onRemoved:event("removed"),onAdded:event("added")},
    scripting:{getRegisteredContentScripts:async()=>[],registerContentScripts:async()=>{},executeScript:async()=>{}},
    tabs:{query:async()=>[],sendMessage:async(tabId,message,options)=>{commands.push({tabId,...options,payload:message.payload});return {ok:true,status:"observed",observed:"paused"};},
      onRemoved:event("tabRemoved"),onUpdated:event("updated")},alarms:{create:async()=>{},onAlarm:event("alarm")}};
  new Function("chrome",...Object.keys(rules),source)(chrome,...Object.values(rules));
  await flush();
  handlers.native({version:1,type:"configuration",payload:{revision:1,enabled:true,mediaDetailsVersion,sites:[{id:"music",origin:"https://music.test",enabled:true}]}});
  await flush();
  const media={mediaId:"media-session",title:"Song",artist:"Artist",album:"",isPlaying:true,positionSeconds:10,durationSeconds:100,canSeek:false,
    details:{version:1,trackVersion:3,source:"media-session",titleSource:"media-session",playbackState:"playing",positionSeconds:10,durationSeconds:100,
      playbackRate:1,sampleAgeSeconds:0,seekable:[],actions:["pause"],artwork:[]}};
  const send=(id=1,sentAt=Date.now())=>handlers.runtime({type:"media",payload:media,sentAt},
    {tab:{id},documentId:"doc-"+id,frameId:0,url:"https://music.test/"},()=>{});
  return {handlers,packets,commands,send};
}
describe("browser worker media contract",()=>{
  it.each([undefined,1])("negotiates optional details with desktop capability %s",async version=>{
    vi.useFakeTimers();try{
      const w=await worker(version);w.send();
      const packet=w.packets.at(-1);
      expect(packet.type).toBe("session");expect(packet.payload.title).toBe("Song");
      if(version===1)expect(packet.payload.details.trackVersion).toBe(3);else expect(packet.payload.details).toBeUndefined();
      const count=w.packets.length;w.send(1,Date.now()-7000);expect(w.packets).toHaveLength(count);
    }finally{vi.clearAllTimers();vi.useRealTimers();}
  });
  it("routes a control to its document and fails after removal without redirecting",async()=>{
    vi.useFakeTimers();try{
      const w=await worker(1);w.send(1);w.send(2);
      const payload={requestId:"control",documentId:"doc-2",mediaId:"media-session",trackVersion:3,action:"TogglePlayPause"};
      w.handlers.native({version:1,type:"command",payload});await flush();
      expect(w.commands).toEqual([{tabId:2,documentId:"doc-2",payload}]);
      expect(w.packets.at(-1)).toMatchObject({type:"result",payload:{requestId:"control",ok:true,status:"observed",observed:"paused"}});
      w.handlers.tabRemoved(2);w.handlers.native({version:1,type:"command",payload:{...payload,requestId:"closed"}});await flush();
      expect(w.commands).toHaveLength(1);
      expect(w.packets.at(-1)).toMatchObject({type:"result",payload:{requestId:"closed",ok:false,reason:"target-unavailable"}});
    }finally{vi.clearAllTimers();vi.useRealTimers();}
  });
});
