# QQ 音乐歌词获取文档

本文只讲三件事：**怎么拿 songid → 怎么取歌词 → 怎么生成歌词文件**。文末附完整可运行代码。

整体流程：

```
歌名/歌手  ──①搜索接口──▶  songid(数字)  ──②歌词接口──▶  加密歌词(hex)
                                                              │
                                                  ③解码: 3DES + inflate
                                                              ▼
                                                     QRC/LRC 文本  ──▶  .lrc 文件
```

---

## 1. 接口一：搜索（获取 songid）

用于把「歌名 + 歌手」换成 QQ 音乐的数字 **songid**。

| 项 | 值 |
|---|---|
| URL | `https://c.y.qq.com/soso/fcgi-bin/search_for_qq_cp` |
| Method | `GET` |
| Header | `User-Agent: <浏览器UA>`、`Referer: https://y.qq.com/` |

**查询参数**

| 参数 | 值 | 说明 |
|---|---|---|
| `w` | `歌名 歌手` | 关键词（URL 编码） |
| `format` | `json` | 返回 JSON |
| `n` | `10` | 返回条数 |
| `p` | `1` | 页码 |
| `t` | `0` | 搜索类型 |
| `remoteplace` | `txt.yqq.song` | 搜索位置 |
| `platform` | `yqq.json` | 平台标识 |

**取结果**：`data.song.list[].songid`（**数字**）。

> ⚠️ 一定要用 **数字 songid**（如 `295618479`），**不是**字母 songmid（如 `003WkhSf2FOUwq`）。
> 传字母 songmid 时，歌词接口会返回空内容。

curl 示例：

```bash
curl -s 'https://c.y.qq.com/soso/fcgi-bin/search_for_qq_cp?w=%E5%98%98%E6%9C%88%20%E3%83%A8%E3%83%AB%E3%82%B7%E3%82%AB&format=json&n=10&p=1&t=0&remoteplace=txt.yqq.song&platform=yqq.json' \
  -H 'User-Agent: Mozilla/5.0' -H 'Referer: https://y.qq.com/'
```

---

## 2. 接口二：获取歌词

| 项 | 值 |
|---|---|
| URL | `https://c.y.qq.com/qqmusic/fcgi-bin/lyric_download.fcg` |
| Method | `POST` |
| Header | `User-Agent`、`Referer: https://y.qq.com/`、`Content-Type: application/x-www-form-urlencoded` |

**表单参数**

| 参数 | 值 | 说明 |
|---|---|---|
| `version` | `15` | 接口版本 |
| `miniversion` | `100` | 最低版本 |
| `lrctype` | `4` | 歌词类型（4 = QRC 逐字） |
| `musicid` | `<数字 songid>` | 歌曲 ID |

curl 示例：

```bash
curl -s 'https://c.y.qq.com/qqmusic/fcgi-bin/lyric_download.fcg' \
  -H 'User-Agent: Mozilla/5.0' -H 'Referer: https://y.qq.com/' \
  -H 'Content-Type: application/x-www-form-urlencoded' \
  --data 'version=15&miniversion=100&lrctype=4&musicid=295618479'
```

**响应**：一段被 XML 注释包裹的伪 XML，里面有 3 个 CDATA 字段：

| 字段 | 内容 | 是否加密 |
|---|---|---|
| `content` | 主歌词（QRC 逐字） | ✅ 加密（hex） |
| `contentts` | 翻译歌词 | ❌ 明文 LRC |
| `contentroma` | 罗马音歌词 | ✅ 加密（hex） |

```
<!--
<lyric musicid="295618479" encode="1" ...>
  <content ...><![CDATA[<hex 密文>]]></content>
  <contentts ...><![CDATA[[ti:...] [00:00.00]明文 LRC]]></contentts>
  <contentroma ...><![CDATA[<hex 密文>]]></contentroma>
</lyric>
-->
```

> 判断规则：**内容是纯 hex 才解密，否则原样返回**。所以 `contentts`（明文）不会被误处理。

---

## 3. 解码（把密文变成歌词文本）

```
hex 字符串
  → ① hex → 字节数组
  → ② 3DES 解密（DES-EDE3 / ECB，每 8 字节一块，无 IV）
  → ③ raw deflate 解压
  → UTF-8 文本（QRC XML 或 LRC）
```

- **密钥**：`!@#)(*$%123ZXC!@!@#)(NHL`（24 字节）
- **注意**：这里的 3DES 是 QQ 专用实现，**与标准库的 3DES 不通用**（用标准库解出来不是合法数据），所以必须用本文内嵌的自实现。

### QRC 文本格式

| 语法 | 含义 |
|---|---|
| `[ti:标题]` `[ar:歌手]` `[al:专辑]` `[by:]` `[offset:0]` | 元数据 |
| `[起始ms,时长ms]` | 行标签 |
| `文本(起始ms,时长ms)` | 字标签（逐字时间轴） |

示例：

```
[10140,3720]去(10140,120)到(10260,300)每(10560,150)一(10800,180)...
```

---

## 4. 生成歌词文件（.lrc）

把解码出的行时间轴写成标准 LRC：

```
[ti:标题]
[ar:歌手]
[al:专辑]
[00:00.69]雨が降った 花が散った
[00:03.77]ただ染まった 頬を想った
```

下面的完整脚本会自动完成「搜索 → 取歌词 → 解码 → 写出 `.lrc`」。

---

## 5. 完整代码（内嵌，可直接运行）

保存为 `qq_lyric.js`，依赖仅 Node.js 内置模块。

```js
#!/usr/bin/env node
/**
 * QQ音乐歌词获取 + 解码 + 生成 .lrc
 * 用法:
 *   node qq_lyric.js "歌名" "歌手"        // 搜索并下载
 *   node qq_lyric.js --id 295618479      // 直接按数字 songid 下载
 *   node qq_lyric.js --json "歌名" "歌手" // 只输出 JSON
 * 输出: 当前目录下 <歌手> - <歌名>.lrc
 */
'use strict';
const https = require('https');
const fs = require('fs');
const zlib = require('zlib');
const path = require('path');

/* ========== 1. QQ 专用 3DES (DES-EDE3 / ECB) ========== */
const box1=[14,4,13,1,2,15,11,8,3,10,6,12,5,9,0,7,0,15,7,4,14,2,13,1,10,6,12,11,9,5,3,8,4,1,14,8,13,6,2,11,15,12,9,7,3,10,5,0,15,12,8,2,4,9,1,7,5,11,3,14,10,0,6,13];
const box2=[15,1,8,14,6,11,3,4,9,7,2,13,12,0,5,10,3,13,4,7,15,2,8,15,12,0,1,10,6,9,11,5,0,14,7,11,10,4,13,1,5,8,12,6,9,3,2,15,13,8,10,1,3,15,4,2,11,6,7,12,0,5,14,9];
const box3=[10,0,9,14,6,3,15,5,1,13,12,7,11,4,2,8,13,7,0,9,3,4,6,10,2,8,5,14,12,11,15,1,13,6,4,9,8,15,3,0,11,1,2,12,5,10,14,7,1,10,13,0,6,9,8,7,4,15,14,3,11,5,2,12];
const box4=[7,13,14,3,0,6,9,10,1,2,8,5,11,12,4,15,13,8,11,5,6,15,0,3,4,7,2,12,1,10,14,9,10,6,9,0,12,11,7,13,15,1,3,14,5,2,8,4,3,15,0,6,10,10,13,8,9,4,5,11,12,7,2,14];
const box5=[2,12,4,1,7,10,11,6,8,5,3,15,13,0,14,9,14,11,2,12,4,7,13,1,5,0,15,10,3,9,8,6,4,2,1,11,10,13,7,8,15,9,12,5,6,3,0,14,11,8,12,7,1,14,2,13,6,15,0,9,10,4,5,3];
const box6=[12,1,10,15,9,2,6,8,0,13,3,4,14,7,5,11,10,15,4,2,7,12,9,5,6,1,13,14,0,11,3,8,9,14,15,5,2,8,12,3,7,0,4,10,1,13,11,6,4,3,2,12,9,5,15,10,11,14,1,7,6,0,8,13];
const box7=[4,11,2,14,15,0,8,13,3,12,9,7,5,10,6,1,13,0,11,7,4,9,1,10,14,3,5,12,2,15,8,6,1,4,11,13,12,3,7,14,10,15,6,8,0,5,9,2,6,11,13,8,1,4,10,7,9,5,0,15,14,2,3,12];
const box8=[13,2,8,4,6,15,11,1,10,9,3,14,5,0,12,7,1,15,13,8,10,3,7,4,12,5,6,11,0,14,9,2,7,11,4,1,9,12,14,2,0,6,10,13,15,3,5,8,2,1,14,7,4,10,8,13,15,12,9,0,3,5,6,11];
const KEY_RND_SHIFT=[1,1,2,2,2,2,2,2,1,2,2,2,2,2,2,1];
const KEY_PERM_C=[56,48,40,32,24,16,8,0,57,49,41,33,25,17,9,1,58,50,42,34,26,18,10,2,59,51,43,35];
const KEY_PERM_D=[62,54,46,38,30,22,14,6,61,53,45,37,29,21,13,5,60,52,44,36,28,20,12,4,27,19,11,3];
const KEY_COMPRESSION=[13,16,10,23,0,4,2,27,14,5,20,9,22,18,11,3,25,7,15,6,26,19,12,1,40,51,30,36,46,54,29,39,50,44,32,47,43,48,38,55,33,52,45,41,49,35,28,31];
const ENCRYPT=1, DECRYPT=0;

function gba(a,b,c){return((a[(Math.floor(b/32)*4+3-Math.floor((b%32)/8))]&0xFF)>>>(7-(b%8))&1)<<c;}
function gbR(a,b,c){return((a>>>(31-b))&1)<<c;}
function gbL(a,b,c){return(((a<<b)&0x80000000)>>>c)|0;}
function fsb(a){return(a&0x20)|((a&0x1f)>>>1)|((a&0x01)<<4);}
function keySchedule(key,offset,schedule,mode){
  let c=0,d=0;
  for(let i=0;i<28;i++){c|=gba(key,KEY_PERM_C[i]+offset*8,31-i);d|=gba(key,KEY_PERM_D[i]+offset*8,31-i);}
  for(let i=0;i<16;i++){
    c=((c<<KEY_RND_SHIFT[i])|(c>>>(28-KEY_RND_SHIFT[i])))&-0x10;
    d=((d<<KEY_RND_SHIFT[i])|(d>>>(28-KEY_RND_SHIFT[i])))&-0x10;
    const toGen=(mode===DECRYPT)?15-i:i;
    for(let k=0;k<6;k++)schedule[toGen][k]=0;
    for(let k=0;k<24;k++)schedule[toGen][Math.floor(k/8)]=(schedule[toGen][Math.floor(k/8)]|gbR(c,KEY_COMPRESSION[k],7-(k%8)))&0xFF;
    for(let k=24;k<48;k++)schedule[toGen][Math.floor(k/8)]=(schedule[toGen][Math.floor(k/8)]|gbR(d,KEY_COMPRESSION[k]-27,7-(k%8)))&0xFF;
  }
}
function tripleDESKeySetup(key,schedule,mode){
  if(mode===ENCRYPT){keySchedule(key,0,schedule[0],ENCRYPT);keySchedule(key,8,schedule[1],DECRYPT);keySchedule(key,16,schedule[2],ENCRYPT);}
  else{keySchedule(key,0,schedule[2],DECRYPT);keySchedule(key,8,schedule[1],ENCRYPT);keySchedule(key,16,schedule[0],DECRYPT);}
}
function initialPermutation(state,input){
  state[0]=gba(input,57,31)|gba(input,49,30)|gba(input,41,29)|gba(input,33,28)|gba(input,25,27)|gba(input,17,26)|gba(input,9,25)|gba(input,1,24)|gba(input,59,23)|gba(input,51,22)|gba(input,43,21)|gba(input,35,20)|gba(input,27,19)|gba(input,19,18)|gba(input,11,17)|gba(input,3,16)|gba(input,61,15)|gba(input,53,14)|gba(input,45,13)|gba(input,37,12)|gba(input,29,11)|gba(input,21,10)|gba(input,13,9)|gba(input,5,8)|gba(input,63,7)|gba(input,55,6)|gba(input,47,5)|gba(input,39,4)|gba(input,31,3)|gba(input,23,2)|gba(input,15,1)|gba(input,7,0);
  state[1]=gba(input,56,31)|gba(input,48,30)|gba(input,40,29)|gba(input,32,28)|gba(input,24,27)|gba(input,16,26)|gba(input,8,25)|gba(input,0,24)|gba(input,58,23)|gba(input,50,22)|gba(input,42,21)|gba(input,34,20)|gba(input,26,19)|gba(input,18,18)|gba(input,10,17)|gba(input,2,16)|gba(input,60,15)|gba(input,52,14)|gba(input,44,13)|gba(input,36,12)|gba(input,28,11)|gba(input,20,10)|gba(input,12,9)|gba(input,4,8)|gba(input,62,7)|gba(input,54,6)|gba(input,46,5)|gba(input,38,4)|gba(input,30,3)|gba(input,22,2)|gba(input,14,1)|gba(input,6,0);
}
function inverseInitialPermutation(state,output){
  const oi=[3,2,1,0,7,6,5,4],bo=[7,6,5,4,3,2,1,0];
  for(let i=0;i<8;i++)output[oi[i]]=(gbR(state[1],bo[i],7)|gbR(state[0],bo[i],6)|gbR(state[1],bo[i]+8,5)|gbR(state[0],bo[i]+8,4)|gbR(state[1],bo[i]+16,3)|gbR(state[0],bo[i]+16,2)|gbR(state[1],bo[i]+24,1)|gbR(state[0],bo[i]+24,0))&0xFF;
}
function f(stateIn,key){
  const t1=gbL(stateIn,31,0)|((stateIn&-0x10000000)>>>1)|gbL(stateIn,4,5)|gbL(stateIn,3,6)|((stateIn&0x0f000000)>>>3)|gbL(stateIn,8,11)|gbL(stateIn,7,12)|((stateIn&0x00f00000)>>>5)|gbL(stateIn,12,17)|gbL(stateIn,11,18)|((stateIn&0x000f0000)>>>7)|gbL(stateIn,16,23);
  const t2=gbL(stateIn,15,0)|((stateIn&0x0000f000)<<15)|gbL(stateIn,20,5)|gbL(stateIn,19,6)|((stateIn&0x00000f00)<<13)|gbL(stateIn,24,11)|gbL(stateIn,23,12)|((stateIn&0x000000f0)<<11)|gbL(stateIn,28,17)|gbL(stateIn,27,18)|((stateIn&0x0000000f)<<9)|gbL(stateIn,0,23);
  const x0=((t1>>>24)&0xFF)^(key[0]&0xFF),x1=((t1>>>16)&0xFF)^(key[1]&0xFF),x2=((t1>>>8)&0xFF)^(key[2]&0xFF);
  const x3=((t2>>>24)&0xFF)^(key[3]&0xFF),x4=((t2>>>16)&0xFF)^(key[4]&0xFF),x5=((t2>>>8)&0xFF)^(key[5]&0xFF);
  const s=(box1[fsb(x0>>>2)]<<28)|(box2[fsb(((x0&3)<<4)|(x1>>>4))]<<24)|(box3[fsb(((x1&0x0f)<<2)|(x2>>>6))]<<20)|(box4[fsb(x2&0x3f)]<<16)|(box5[fsb(x3>>>2)]<<12)|(box6[fsb(((x3&3)<<4)|(x4>>>4))]<<8)|(box7[fsb(((x4&0x0f)<<2)|(x5>>>6))]<<4)|box8[fsb(x5&0x3f)];
  return gbL(s,15,0)|gbL(s,6,1)|gbL(s,19,2)|gbL(s,20,3)|gbL(s,28,4)|gbL(s,11,5)|gbL(s,27,6)|gbL(s,16,7)|gbL(s,0,8)|gbL(s,14,9)|gbL(s,22,10)|gbL(s,25,11)|gbL(s,4,12)|gbL(s,17,13)|gbL(s,30,14)|gbL(s,9,15)|gbL(s,1,16)|gbL(s,7,17)|gbL(s,23,18)|gbL(s,13,19)|gbL(s,31,20)|gbL(s,26,21)|gbL(s,2,22)|gbL(s,8,23)|gbL(s,18,24)|gbL(s,12,25)|gbL(s,29,26)|gbL(s,5,27)|gbL(s,21,28)|gbL(s,10,29)|gbL(s,3,30)|gbL(s,24,31);
}
function crypt(input,output,key){
  const state=[0,0];initialPermutation(state,input);
  for(let i=0;i<15;i++){const t=state[1];state[1]=(f(state[1],key[i])^state[0])|0;state[0]=t;}
  state[0]=(f(state[1],key[15])^state[0])|0;
  inverseInitialPermutation(state,output);
}
function tripleDESCrypt(input,output,key){crypt(input,output,key[0]);crypt(output,output,key[1]);crypt(output,output,key[2]);}

/* ========== 2. 解码 ========== */
const QQ_KEY = Buffer.from('!@#)(*$%123ZXC!@!@#)(NHL', 'ascii');
const SCHEDULE = Array.from({ length: 3 }, () => Array.from({ length: 16 }, () => new Uint8Array(6)));
tripleDESKeySetup(QQ_KEY, SCHEDULE, DECRYPT);

const isHex = s => s.length > 0 && s.length % 2 === 0 && /^[0-9a-fA-F]+$/.test(s);

/** hex -> 3DES -> inflate -> utf8；非 hex 明文原样返回 */
function decrypt(input) {
  if (input == null) return null;
  const s = input.trim();
  if (s === '') return null;
  if (!isHex(s)) return s;
  const enc = Buffer.from(s, 'hex');
  const out = Buffer.alloc(enc.length);
  const inp = new Uint8Array(8), tmp = new Uint8Array(8);
  for (let i = 0; i < enc.length; i += 8) {
    const bs = Math.min(8, enc.length - i);
    inp.fill(0);
    for (let j = 0; j < bs; j++) inp[j] = enc[i + j];
    tripleDESCrypt(inp, tmp, SCHEDULE);
    for (let j = 0; j < bs; j++) out[i + j] = tmp[j];
  }
  return zlib.inflateSync(out).toString('utf8');
}

/** 取 XML 里某个 tag 的 CDATA 内容 */
function cdata(xml, tag) {
  const m = xml.match(new RegExp('<' + tag + '[^>]*>[\\s\\S]*?<!\\[CDATA\\[([\\s\\S]*?)\\]\\]>'));
  return m ? m[1].trim() : null;
}

/* ========== 3. 网络请求 ========== */
const UA = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36';
function httpGet(url) {
  return new Promise((res, rej) => {
    const r = https.request(url, { headers: { 'User-Agent': UA, 'Referer': 'https://y.qq.com/' } }, s => {
      const ch = []; s.on('data', c => ch.push(c));
      s.on('end', () => res({ status: s.statusCode, body: Buffer.concat(ch).toString('utf8') }));
    });
    r.on('error', rej); r.setTimeout(15000, () => r.destroy(new Error('timeout'))); r.end();
  });
}
function httpPost(musicid) {
  return new Promise((res, rej) => {
    const body = 'version=15&miniversion=100&lrctype=4&musicid=' + musicid;
    const r = https.request({ host: 'c.y.qq.com', path: '/qqmusic/fcgi-bin/lyric_download.fcg', method: 'POST', headers: { 'User-Agent': UA, 'Referer': 'https://y.qq.com/', 'Content-Type': 'application/x-www-form-urlencoded', 'Content-Length': Buffer.byteLength(body) } }, s => {
      const ch = []; s.on('data', c => ch.push(c));
      s.on('end', () => res({ status: s.statusCode, body: Buffer.concat(ch).toString('utf8') }));
    });
    r.on('error', rej); r.setTimeout(15000, () => r.destroy(new Error('timeout'))); r.write(body); r.end();
  });
}

/** 搜索 -> 返回候选歌曲 [{songid,name,singer,album}] */
async function search(keyword) {
  const url = 'https://c.y.qq.com/soso/fcgi-bin/search_for_qq_cp?w=' + encodeURIComponent(keyword) +
    '&format=json&n=10&p=1&t=0&remoteplace=txt.yqq.song&platform=yqq.json';
  const r = await httpGet(url);
  const list = JSON.parse(r.body).data.song.list || [];
  return list.map(s => ({
    songid: String(s.songid),
    name: s.songname,
    singer: (s.singer || []).map(x => x.name).join('/'),
    album: s.albumname || '',
  }));
}

/** 按 songid 取歌词，返回 {raw, meta, lines, translation, roma} */
async function getLyrics(songid) {
  const xml = (await httpPost(songid)).body;
  const raw = decrypt(cdata(xml, 'content'));
  const meta = {}, lines = [];
  if (raw) {
    for (const m of raw.matchAll(/\[(\w+):([^\]]*)\]/g)) meta[m[1]] = m[2].trim();
    const hits = [...raw.matchAll(/\[(\d+),(\d+)\]/g)];
    for (let i = 0; i < hits.length; i++) {
      const begin = +hits[i][1], dur = +hits[i][2];
      const end = i + 1 < hits.length ? hits[i + 1].index : raw.lastIndexOf(']');
      const body = raw.slice(hits[i].index + hits[i][0].length, end);
      const words = [...body.matchAll(/([^()\n\r]*)\((\d+),(\d+)\)/g)].map(w => ({ text: w[1], begin: +w[2], duration: +w[3] }));
      const text = words.length ? words.map(w => w.text).join('') : body.replace(/\(\d+,\d+\)/g, '').trim();
      lines.push({ begin, duration: dur, end: begin + dur, text, words });
    }
    lines.sort((a, b) => a.begin - b.begin);
  }
  return { raw, meta, lines, translation: decrypt(cdata(xml, 'contentts')), roma: decrypt(cdata(xml, 'contentroma')) };
}

/** 转成标准 LRC 文本 */
function toLrc(meta, lines) {
  const fmt = ms => {
    const m = Math.floor(ms / 60000), s = Math.floor((ms % 60000) / 1000), cs = Math.floor((ms % 1000) / 10);
    return `${String(m).padStart(2, '0')}:${String(s).padStart(2, '0')}.${String(cs).padStart(2, '0')}`;
  };
  const head = [];
  ['ti', 'ar', 'al', 'by'].forEach(k => { if (meta[k]) head.push(`[${k}:${meta[k]}]`); });
  head.push(`[offset:${meta.offset || 0}]`);
  return head.join('\n') + '\n' + lines.map(l => `[${fmt(l.begin)}]${l.text}`).join('\n') + '\n';
}

/* ========== 4. 命令行 ========== */
(async () => {
  const args = process.argv.slice(2);
  const asJson = args.includes('--json');
  let songid, name, singer = '';
  const idIdx = args.indexOf('--id');
  if (idIdx >= 0) {
    songid = args[idIdx + 1];
  } else {
    const kw = args.filter(a => a !== '--json').join(' ');
    if (!kw) { console.error('用法: node qq_lyric.js "歌名" "歌手"  |  node qq_lyric.js --id <songid>'); process.exit(1); }
    const cands = await search(kw);
    if (!cands.length) { console.error('未找到:', kw); process.exit(1); }
    ({ songid, name, singer } = cands[0]);
    console.log(`匹配: ${name} / ${singer}  (songid=${songid})`);
  }
  const r = await getLyrics(songid);
  if (!r.lines.length) { console.error('无歌词'); process.exit(2); }
  if (asJson) { console.log(JSON.stringify(r, null, 2)); return; }
  name = name || r.meta.ti || songid;
  singer = singer || r.meta.ar || '';
  const file = `${singer ? singer + ' - ' : ''}${name}.lrc`.replace(/[\\/:*?"<>|]/g, '_');
  fs.writeFileSync(path.join(process.cwd(), file), toLrc(r.meta, r.lines), 'utf8');
  console.log(`共 ${r.lines.length} 行 → 已写出 ${file}`);
})().catch(e => { console.error('ERROR:', e.message); process.exit(1); });
```

**运行**

```bash
node qq_lyric.js "嘘月" "ヨルシカ"      # 搜索并生成「ヨルシカ - 嘘月.lrc」
node qq_lyric.js --id 295618479        # 已知 songid 直接下载
node qq_lyric.js --json "左右盲" "ヨルシカ"  # 看完整 JSON（含翻译/罗马音）
```

**输出示例**（`ヨルシカ - 嘘月.lrc`）：

```
[ti:嘘月]
[ar:ヨルシカ (Yorushika)]
[al:創作]
[offset:0]
[00:00.69]雨が降った 花が散った
[00:03.77]ただ染まった 頬を想った
```

---

## 6. 注意事项

- `musicid` 必须是**数字 songid**（不是字母 songmid），否则歌词为空。
- `content` / `contentroma` 是加密的；`contentts`（翻译）是明文 LRC。
- 该 3DES 为 QQ 专用实现，标准库 3DES 无法解密，请使用本文内嵌实现。
- 逐字时间轴保存在字标签 `文本(起始ms,时长ms)` 中；上方脚本生成的是整行 LRC，如需逐字请从 `--json` 的 `words` 字段取。
- 请求较频繁时服务端可能短暂变慢（响应到数秒甚至超时），建议加缓存与失败重试。
