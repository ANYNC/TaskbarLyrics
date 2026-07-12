# TaskbarLyrics Settings Prototype Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone, interactive HTML prototype of the redesigned TaskbarLyrics settings window without modifying production application files.

**Architecture:** Create one self-contained HTML artifact so the prototype can be opened directly from disk without a build step or external dependencies. A single JavaScript state object drives page navigation, expandable playback-source rendering, dependency disabling, preview updates, save feedback, and confirmation dialogs.

**Tech Stack:** Semantic HTML5, embedded CSS, vanilla JavaScript, inline SVG icons, PowerShell static checks, Node.js JavaScript syntax validation.

## Global Constraints

- Create only `C:/Users/ANYN/.codex/visualizations/2026/07/12/019f5529-3270-79f2-9ab5-9445a93748b0/settings-prototype-v2.html` as the prototype artifact.
- Do not modify files under `TaskbarLyrics.App`.
- Use `Microsoft YaHei UI, Microsoft YaHei, sans-serif`; do not depend on bundled Source Han Sans.
- Use only font weights 400, 600, and 700.
- The default canvas is 1180×760 and must remain usable at narrower widths.
- Playback sources must be rendered from data and support 1–12 items without structural changes.
- Do not load fonts, icons, scripts, or styles from the network.

---

### Task 1: Semantic shell and page structure

**Files:**
- Create: `C:/Users/ANYN/.codex/visualizations/2026/07/12/019f5529-3270-79f2-9ab5-9445a93748b0/settings-prototype-v2.html`

**Interfaces:**
- Produces: elements keyed by `data-page`, `data-nav`, `data-setting`, `data-preview`, and dialog IDs used by Tasks 2–3.

- [ ] **Step 1: Create a minimal semantic document**

Create a UTF-8 HTML document containing `.app-shell`, `aside.sidebar`, `main.workspace`, seven navigation buttons, seven page sections, `#restoreDialog`, and `#clearDialog`. Use actual `button`, `input`, `select`, `textarea`, and `dialog` elements.

- [ ] **Step 2: Add all page content**

Add these exact pages and groups:

```text
sources: 已启用的播放源 / 识别优先级 / 本地歌词
lyrics: 显示行为 / 频谱
appearance: 文字 / 封面 / taskbar preview
window: 窗口外观 / 位置与行为 / taskbar preview
general: 启动与后台 / 更新
advanced: 诊断工具 / 数据维护
about: product summary / version / license / links
```

Mark only `sources` as active initially. Include a top status element with text `所有更改均已保存` and a low-emphasis `恢复默认` button.

- [ ] **Step 3: Add scalable source fixtures**

Define twelve source records in JavaScript so layout can be stress-tested without changing markup:

```js
const sourceCatalog = [
  { id: "qqmusic", name: "QQ 音乐", adapter: "QQMusic", monogram: "Q", enabled: true, available: true },
  { id: "netease", name: "网易云音乐", adapter: "Netease", monogram: "云", enabled: true, available: true },
  { id: "kugou", name: "酷狗音乐", adapter: "Kugou", monogram: "K", enabled: true, available: true },
  { id: "spotify", name: "Spotify", adapter: "Spotify", monogram: "S", enabled: false, available: true },
  { id: "applemusic", name: "Apple Music", adapter: "AppleMusic", monogram: "A", enabled: false, available: false },
  { id: "foobar", name: "foobar2000", adapter: "Foobar", monogram: "F", enabled: false, available: true },
  { id: "musicbee", name: "MusicBee", adapter: "MusicBee", monogram: "M", enabled: false, available: true },
  { id: "aimp", name: "AIMP", adapter: "AIMP", monogram: "A", enabled: false, available: true },
  { id: "vlc", name: "VLC", adapter: "VLC", monogram: "V", enabled: false, available: true },
  { id: "winamp", name: "Winamp", adapter: "Winamp", monogram: "W", enabled: false, available: true },
  { id: "tidal", name: "TIDAL", adapter: "Tidal", monogram: "T", enabled: false, available: false },
  { id: "generic", name: "其他 SMTC 播放器", adapter: "GenericSMTC", monogram: "+", enabled: false, available: true }
];
```

- [ ] **Step 4: Run structural checks**

Run a PowerShell check that decodes UTF-8 strictly, confirms seven `data-page` sections, seven `data-nav` buttons, two dialogs, balanced `<div>` tags, and no replacement characters. Expected result: all checks print `True`.

### Task 2: Visual system and responsive layout

**Files:**
- Modify: `C:/Users/ANYN/.codex/visualizations/2026/07/12/019f5529-3270-79f2-9ab5-9445a93748b0/settings-prototype-v2.html`

**Interfaces:**
- Consumes: semantic classes and data attributes from Task 1.
- Produces: responsive page layouts, source-card grid, control states, taskbar preview, and focus styles.

- [ ] **Step 1: Define tokens and shell layout**

Add CSS variables for dark neutral surfaces, `#6ea8fe` accent, text, muted text, border, danger, radii, and shadows. Set the shell to `grid-template-columns: 220px minmax(0, 1fr)`, width `min(1180px, 100vw)`, height `min(760px, 100vh)`, and the Microsoft YaHei font stack.

- [ ] **Step 2: Style navigation and controls**

Use inline SVG icons with consistent 16×16 view boxes. Provide visible `:hover`, `:active`, `:focus-visible`, and `:disabled` states. Build switches from real checkbox inputs and styled labels; never hide focus outlines without a replacement.

- [ ] **Step 3: Implement scalable source layout**

Use this grid contract:

```css
.source-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 10px;
  max-height: 250px;
  overflow: auto;
}
```

Style enabled, disabled, unavailable, hover, and focus-within states distinctly. Keep source actions readable at two, three, or four columns.

- [ ] **Step 4: Build settings and preview layouts**

Use 58px setting rows with a flexible label column and bounded control column. For appearance and window pages, use `grid-template-columns: minmax(360px, 1fr) minmax(330px, .82fr)` and a sticky preview. At widths below 980px, switch to one column and place preview after settings; below 720px, collapse sidebar labels and reduce sidebar width to 68px.

- [ ] **Step 5: Respect reduced motion**

Add:

```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after { scroll-behavior: auto !important; transition-duration: .01ms !important; }
}
```

- [ ] **Step 6: Run CSS contract checks**

Search for the Microsoft YaHei stack, `auto-fit`, `minmax(180px`, `prefers-reduced-motion`, `focus-visible`, and both responsive breakpoints. Expected result: every contract is found at least once.

### Task 3: State, navigation, dependencies, and preview

**Files:**
- Modify: `C:/Users/ANYN/.codex/visualizations/2026/07/12/019f5529-3270-79f2-9ab5-9445a93748b0/settings-prototype-v2.html`

**Interfaces:**
- Consumes: `sourceCatalog`, semantic data attributes, control IDs, and preview hooks.
- Produces: `renderSources()`, `renderPriority()`, `activatePage(pageId)`, `applyDependencies()`, `updatePreview()`, `markSaved()`, and `resetState()`.

- [ ] **Step 1: Define state and rendering functions**

Use one plain object with exact defaults:

```js
const defaults = {
  page: "sources", localLyrics: true, folders: "D:\\Music", showOnStartup: true,
  translation: false, spectrum: true, spectrumMode: "missing", fontSize: 14,
  fontFamily: "Microsoft YaHei UI", fontWeight: 600, textColor: "#ffffff",
  textShadow: true, coverSize: 34, coverGap: 8, coverRadius: 6,
  background: true, border: false, opacity: .55, windowWidth: 420,
  anchor: "left", alwaysOnTop: true, startWithWindows: false, autoUpdate: true,
  smtcMonitor: false
};
```

Clone defaults for live state. Render source cards and priority items from data; do not duplicate source-specific HTML.

- [ ] **Step 2: Implement navigation and save feedback**

`activatePage(pageId)` must set exactly one active navigation button and one visible page, update title and subtitle from `pageMeta`, and move focus to the page heading. `markSaved()` must show `正在保存…`, then `所有更改均已保存` after a short timer.

- [ ] **Step 3: Implement settings and dependencies**

Use delegated `change` and `input` handlers. Disable local folder controls when `localLyrics` is false, spectrum mode when `spectrum` is false, and opacity when `background` is false. Preserve child values while disabled.

- [ ] **Step 4: Implement source enablement and priority**

Checkbox changes update the matching source record and re-render priority. Up/down buttons reorder only enabled sources; each button receives a descriptive accessible label. Unavailable sources cannot be enabled and show `适配器未安装`.

- [ ] **Step 5: Implement live preview**

`updatePreview()` must update CSS custom properties for font size, weight, color, shadow, cover size, gap, radius, window width, opacity, border, and anchor. It must also update visible numeric outputs.

- [ ] **Step 6: Implement confirmation dialogs**

Restore default and clear-cache actions open their respective dialogs. Confirming restore runs `resetState()` and returns to sources; confirming clear cache closes the dialog and shows a non-destructive success toast. Cancel buttons only close dialogs.

- [ ] **Step 7: Validate JavaScript syntax**

Extract the inline script text to a temporary `.js` file using PowerShell, run `node --check`, then remove the temporary file. Expected result: exit code 0 and no syntax errors.

### Task 4: Final static verification and handoff

**Files:**
- Verify: `C:/Users/ANYN/.codex/visualizations/2026/07/12/019f5529-3270-79f2-9ab5-9445a93748b0/settings-prototype-v2.html`

**Interfaces:**
- Consumes: completed artifact from Tasks 1–3.
- Produces: verification evidence and a clickable artifact link.

- [ ] **Step 1: Run complete verification script**

Verify strict UTF-8 decoding, balanced common tags, seven pages, seven navigation buttons, twelve source fixtures, two dialogs, no unfinished placeholder markers, no external network resources, and no references to Source Han Sans. Expected result: all assertions pass.

- [ ] **Step 2: Inspect the artifact metadata**

Report full path, byte length, SHA-256, and last-write timestamp so the delivered artifact is unambiguous.

- [ ] **Step 3: Hand off the prototype**

Provide a clickable local link and summarize implemented navigation, expandable source grid, dependency behavior, preview controls, responsive behavior, and the limitation that the prototype is not connected to production settings.
