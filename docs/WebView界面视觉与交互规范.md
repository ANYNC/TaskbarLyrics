# TaskbarLyrics WebView 界面视觉与交互规范

> 适用对象：维护 TaskbarLyrics WebView 界面的开发者与 AI 代理  
> 技术定位：使用纯 HTML、CSS、JavaScript 实现，以 shadcn/ui 为视觉与交互参考的 WebView2 界面规范  
> 权威性：本文档是本项目 WebView 界面设计与实现的最终权威；在线规则、组件示例和通用 Web 指南仅作为补充参考。发生冲突时，以本文档及仓库根目录 `AGENTS.md` 为准。

## 1. 规则标记

本文使用以下标记区分已经存在的实现与后续目标，禁止把建议描述成既有能力：

- **强制规则**：新增或修改界面时必须遵守；违反时需要在变更说明中给出具体理由。
- **项目现状**：已从当前 `TaskbarLyrics.App/Web/Settings/settings.css`、`TaskbarLyrics.App/Web/Lyrics/style.css` 及对应 HTML/JavaScript 中提取，修改时应保持视觉连续性。
- **建议标准**：当前尚未完全统一，适用于新组件；旧组件应在专项变更中逐步迁移，不得借普通修复进行大面积重绘。
- **通用 Web Guidelines**：来自语义化、可访问性、键盘操作、焦点、表单、动画、排版、溢出和性能等通用 Web 规则。
- **WebView2/Windows 适配**：针对本地 WebView2、Windows 键盘与 UI Automation、DPI、窗口托管和原生能力边界作出的调整。

## 2. 适用范围

本规范适用于：

- `TaskbarLyrics.App/Web/Settings` 设置界面；
- `TaskbarLyrics.App/Web/Lyrics` 任务栏歌词界面；
- `TaskbarLyrics.App/Web/SmtcMonitor`、`TaskbarLyrics.App/Web/SpectrumTuning` 等 WebView2 工具界面；
- 后续新增的 WebView2 页面、组件、视觉 Token、交互状态和体验审查；
- 生成、修改或审查上述内容的开发者与 AI。

本规范不直接约束原生 WPF 控件、托盘原生菜单或 Light 分支，但两套界面共享产品术语时应保持文案一致。

## 3. 技术边界

### 3.1 强制规则

- 生产界面只能使用原生 HTML、CSS 和 JavaScript。
- shadcn/ui 仅作为视觉层级、间距、圆角、颜色、阴影、状态反馈和动效的参考，不是源码或运行时依赖。
- 禁止引入 React、Vue、Svelte、Tailwind CSS、Radix UI、shadcn/ui 运行时或任何在线 CDN。
- 禁止为了单个组件新增前端打包、运行时注入或远程资源依赖。现有 Vitest/jsdom 仅用于开发测试，不进入发布包。
- 图标、字体、样式和脚本必须随应用本地发布；不得依赖网络可用性才能显示基础界面。
- C# 与 JavaScript 继续使用 WebView V1 信封 `{ version: 1, type, payload }`。未知版本、类型和错误 payload 必须安全忽略。
- 保留 `window.settingsApp`、`{{STYLE_CSS}}`、`{{APP_JS}}` 及歌词脚本确定性加载顺序。
- WebView 只负责展示、输入和本地交互状态；持久化、系统能力、外部链接和 Windows 原生操作由宿主边界处理。
- 不以在线 shadcn/ui 示例覆盖现有组件。先复用项目组件，再评估是否需要扩展。

### 3.2 WebView2/Windows 适配

- 外部链接通过 C# 宿主验证并打开，不直接在 WebView 内导航。
- 深浅主题由 WebView2 `PreferredColorScheme` 与 CSS `prefers-color-scheme` 协作，不假设浏览器拥有独立主题设置。
- WebView2 会参与 Windows 无障碍树；页面不能因为“运行在桌面应用里”而省略 HTML 语义、键盘操作和焦点状态。
- 浏览器专属的地址栏、历史记录、SEO、SSR、登录表单和密码管理规则不适用于本地工具页。

## 4. 视觉原则

### 4.1 强制规则

- 信息层级优先于装饰：标题、说明、控件和状态必须能在不依赖动画的情况下被理解。
- 保持紧凑但不拥挤。TaskbarLyrics 是桌面工具，不机械套用移动端布局，也不能以“紧凑”为理由牺牲可读性和键盘操作。
- 使用语义 Token 表达背景、前景、边框、强调、危险和状态，不在新组件中散落相近但不同的颜色。
- 同类组件的高度、圆角、边框、焦点环和状态反馈保持一致。
- 颜色不能成为状态的唯一表达方式；重要状态还需要文字、图标、ARIA 属性或结构变化。
- 歌词窗口以低干扰、清晰、稳定为首要目标；设置页以可发现性、可操作性和错误可恢复为首要目标。

### 4.2 项目现状

- 设置页已经形成偏中性的 shadcn/ui 风格：低彩度 OKLCH 色板、语义化表面层级、细边框、小圆角、短时长动画和克制阴影。
- 歌词页采用透明表面、两行歌词、可选封面和频谱的紧凑任务栏布局。
- 现有组件已大量使用 `minmax(0, 1fr)`、`min-width: 0`、ellipsis 和响应式断点处理有限窗口空间，应继续保留。

## 5. CSS 设计 Token

### 5.1 设置页颜色 Token（项目现状）

深色模式：

| Token | 当前值 | 用途 |
| --- | --- | --- |
| `--background` | `oklch(0.145 0 0)` | 页面背景 |
| `--foreground` | `oklch(0.985 0 0)` | 主文字 |
| `--card` / `--popover` | `oklch(0.205 0 0)` | 卡片和浮层 |
| `--primary` | `oklch(0.922 0 0)` | 主按钮、选中和焦点基色 |
| `--primary-foreground` | `oklch(0.205 0 0)` | 主色上的文字 |
| `--secondary` / `--muted` / `--accent` | `oklch(0.269 0 0)` | 次级控件和悬停表面 |
| `--muted-foreground` | `oklch(0.708 0 0)` | 次要说明 |
| `--destructive` | `oklch(0.704 0.191 22.216)` | 危险操作和错误 |
| `--border` | `oklch(1 0 0 / 10%)` | 普通边框 |
| `--input` | `oklch(1 0 0 / 15%)` | 输入和强调边框 |
| `--ring` | `var(--primary)` | 焦点环 |
| `--sidebar` | `oklch(0.205 0 0)` | 侧栏表面 |
| `--surface-hover` | `oklch(0.31 0 0)` | 控件悬停表面 |
| `--nav-hover` | `oklch(0.245 0 0)` | 导航悬停表面 |
| `--subtle` | `oklch(0.62 0 0)` | 更低优先级文字 |
| `--destructive-soft` | `oklch(0.704 0.191 22.216 / 12%)` | 危险操作弱背景 |
| `--success` | `oklch(0.765 0.177 157deg)` | 成功状态 |

浅色模式沿用相同语义名称，当前主要值为：

| Token | 当前值 |
| --- | --- |
| `--background` | `oklch(0.985 0 0)` |
| `--foreground` | `oklch(0.145 0 0)` |
| `--card` / `--popover` | `oklch(1 0 0)` |
| `--primary` | `oklch(0.205 0 0)` |
| `--secondary` / `--muted` | `oklch(0.955 0 0)` |
| `--muted-foreground` | `oklch(0.46 0 0)` |
| `--destructive` | `oklch(0.58 0.205 24)` |
| `--border` | `oklch(0 0 0 / 10%)` |
| `--input` | `oklch(0 0 0 / 15%)` |
| `--sidebar` | `oklch(0.97 0 0)` |
| `--surface-hover` | `oklch(0.91 0 0)` |
| `--nav-hover` | `oklch(0.935 0 0)` |
| `--subtle` | `oklch(0.48 0 0)` |
| `--destructive-soft` | `oklch(0.58 0.205 24 / 10%)` |
| `--success` | `oklch(0.49 0.145 157deg)` |

**强制规则：**新设置页组件优先消费已有语义 Token。只有现有 Token 无法表达稳定语义时才新增 Token，并同时定义深色和浅色值。

### 5.2 圆角、阴影和动画 Token（项目现状）

| 类别 | 当前值 |
| --- | --- |
| 基础圆角 | `--radius: 0.625rem`，即当前默认字体下约 10px |
| 小圆角 | `--radius-sm: calc(var(--radius) - 3px)` |
| 中圆角 | `--radius-md: var(--radius)` |
| 大圆角 | `--radius-lg: calc(var(--radius) + 2px)` |
| 快速动画 | `--duration-fast: 120ms` |
| 常规动画 | `--duration: 180ms` |
| 慢速动画 | `--duration-slow: 220ms` |
| 页面阴影 | `--shadow: 0 18px 60px rgba(0, 0, 0, .38)`，浅色为 `.16` alpha |
| 小阴影 | `--shadow-sm: 0 1px 2px rgba(0, 0, 0, .28)`，浅色使用 `0 1px 3px rgba(0, 0, 0, .1)` |
| 深色浮层阴影 | `--popover-shadow: 0 12px 32px rgba(0, 0, 0, .32), 0 2px 8px rgba(0, 0, 0, .18)` |
| 浅色浮层阴影 | `0 12px 32px rgba(0, 0, 0, .14), 0 2px 8px rgba(0, 0, 0, .08)` |
| 深色对话框阴影 | `--dialog-shadow: 0 28px 80px rgba(0, 0, 0, .6)` |
| 浅色对话框阴影 | `0 28px 80px rgba(0, 0, 0, .22)` |
| 遮罩 | 深色 `rgba(0, 0, 0, .62)`，浅色 `rgba(0, 0, 0, .34)` |

现有局部圆角还包括 5、6、7、8、9、10、16px 和胶囊形 `99px/999px`。这些是组件现状，不代表应继续增加新的相近值。

**建议标准：**新组件优先使用 `--radius-sm`、`--radius`、`--radius-lg`；仅色点、封面和品牌图形允许明确的专用圆角。

### 5.3 歌词页 Token（项目现状）

| Token | 当前值 | 用途 |
| --- | --- | --- |
| `--font-family` | 思源黑体、SF Pro、Segoe UI Variable、微软雅黑回退栈 | 歌词字体 |
| `--font-size` / `--font-weight` | `13px` / `500` | 用户请求的基础字号与字重 |
| `--primary` | `rgba(255, 255, 255, 0.90)` | 当前行与逐词扫描主色 |
| `--secondary` | `rgba(255, 255, 255, 0.60)` | 下一行 |
| `--translation` | `rgba(255, 255, 255, 0.70)` | 翻译行 |
| `--word-scan-overlay` | `rgba(255, 255, 255, 0.75)` | 叠加于次色底层后形成约90%的有效扫描主色 |
| `--row-height` / `--row-gap` | `14px` / `1px` | 双行排版基线 |
| `--current-size` / `--next-size` | `13px` / `12px` | 两行层级 |
| `--cover-size` / `--cover-gap` | `34px` / `8px` | 封面布局 |
| `--cover-radius` | `6px` | 封面圆角 |
| `--surface-color` / `--surface-shadow` | `transparent` / `none` | 任务栏表面 |
| `--text-shadow` | `0 1px 2px rgba(0, 0, 0, 0.36)` | 复杂任务栏背景上的可读性 |

这些值会被用户设置和宿主消息动态覆盖。不得在组件选择器中复制一份相互竞争的固定值。

## 6. 布局和间距

### 6.1 项目现状

- 设置页常用间距集中在 3、5、7、8、10、11、12、14、16、18、20、22、24、28、30px，但当前没有正式的间距变量体系。
- 常规面板间距为 16px；面板标题通常使用 `15px 18px 13px`；设置行通常使用 `10px 18px`；页面边距通常为 `22px 28px 30px`。
- 常规控件最小高度为 36px，小按钮为 28px；卡片和设置行根据内容使用 42–82px 的最小高度。
- 设置行使用 `minmax(0, 1fr)` 给说明区弹性空间，控制区一般限制在 180–300px。
- 窗口宽度在 980px 和 720px 处调整表格、侧栏和设置行布局。

### 6.2 强制规则

- 新布局必须使用 Grid 或 Flex 表达关系，不用绝对定位拼装常规表单。
- 可收缩文本列必须设置 `min-width: 0`；长标题必须明确选择换行或 ellipsis。
- 主要内容不得因固定宽度导致水平滚动。浮层宽度必须限制在当前 WebView 可视区域内。
- 固定定位浮层在窗口缩放、页面滚动、失焦和 DPI 变化后必须关闭或重新定位。
- 不使用 WPF 设备无关单位的数值直接推断 WebView/Win32 物理像素。

### 6.3 建议标准

- 新组件优先从 4、8、12、16、20、24px 中选择间距；若需要匹配现有组件，可沿用其已存在的 3、5、7、10、11、14、18px。
- 不在本次规范中强行把既有间距机械迁移成变量。间距 Token 化应作为单独、可视化回归可验证的变更。

## 7. 字体和文案

### 7.1 项目现状

- 设置页内嵌 `TaskbarLyrics Source Han Sans SC` Regular，并回退到微软雅黑。
- 歌词页内嵌思源黑体 Regular/Bold，并回退到 SF Pro、Segoe UI Variable 和微软雅黑。
- 设置页标题通常为 14–19px，主要标签为 12–13px，辅助文字大量使用 10–11px，版本徽标为 9px。
- 数值和快捷键使用 Consolas 或 Cascadia Mono 等等宽字体。

### 7.2 强制规则

- 文案使用简体中文，术语与 README、设置字段和系统菜单保持一致。
- UI 自有中文页面使用 `<html lang="zh-CN">`。歌词正文语言未知时不得仅凭字符猜测并频繁切换 `lang`。
- 按钮使用动作词，如“检查更新”“清除缓存”；状态使用结果词，如“已注册”“检查失败”。
- 进行中的动作使用单字符省略号 `…`，例如“正在检查…”，不使用三个句点。
- 避免模糊文案，如“确定”“处理”“操作一下”；危险操作必须说明对象和后果。
- 数字状态需要稳定宽度时使用 `font-variant-numeric: tabular-nums`。
- 动态内容必须设置 `title`、换行或 ellipsis 策略，不能依赖用户缩放窗口才能读完。

### 7.3 建议标准

- 新增的正文和说明文字以 12px 为最低基线；徽标和低优先级元数据不低于 10–11px。
- 现有 9–11px 内容尚未整体迁移。调整前需要在 Windows 100%、125%、150% 缩放下进行视觉比较，避免一次性改变信息密度。

## 8. 组件规范

### 8.1 按钮

- 使用原生 `<button type="button">`，禁止用可点击 `div` 代替。
- 文字按钮必须有明确动作名称；仅图标按钮必须提供 `aria-label`，装饰 SVG 使用 `aria-hidden="true"`。
- 主按钮使用 `--primary`；次级按钮使用 `--secondary`；幽灵按钮保持透明；危险按钮使用 `--destructive` 和 `--destructive-soft`。
- 必须实现 Hover、Pressed、Focus、Disabled；异步操作还必须实现 Loading，并防止重复提交。

### 8.2 输入框

- 使用与数据匹配的原生类型：搜索使用 `search`，数值使用 `number`，连续范围使用 `range`。
- 每个输入必须有可访问名称。可见标签优先；空间不足时使用准确的 `aria-label`。
- 数值必须声明合理的 `min`、`max`、`step`，在 JavaScript 和 C# 边界再次验证。
- Error 状态不能只加红色边框；必须同步 `aria-invalid`，并用 `aria-describedby` 关联持久错误说明。
- 二维颜色区域等非原生控件必须向辅助技术表达角色、当前值和调整方式。若单一 ARIA 角色无法准确表达两个维度，优先拆成两个原生 range，而不是保留只有 `tabindex` 的可点击 `div`。
- 原生表单提交、`name`、浏览器自动填充规则仅在页面真的存在表单提交时适用；当前设置页不为满足网页检查器机械添加无意义字段。

### 8.3 开关

- 项目现有模式为原生 checkbox 加 `.switch-track`。保留 checkbox 作为真实可聚焦控件。
- 必须支持 Space 切换、`:focus-visible` 焦点环、checked 和 disabled 状态。
- 关闭依赖项时保留已保存值，禁用对应控件，并通过说明文字解释依赖关系。

### 8.4 选择器

- 简单、样式要求低的选择优先原生 `<select>`；现有自定义选择器使用 trigger + `role="listbox"` + `role="option"`。
- 自定义选择器必须维护 `aria-expanded`、`aria-controls`、`aria-selected` 和 `aria-activedescendant`。
- 支持 ArrowUp/Down、Home、End、Enter、Space、Escape 和 Tab；选择后把焦点返回 trigger。
- 打开时高亮当前值，滚动到可见区域；窗口缩放、页面滚动和点击外部时关闭。

### 8.5 导航

- 使用 `<nav>` 和原生按钮。当前页除了 `.active` 视觉状态，还必须使用 `aria-current="page"`。
- 页面切换后将焦点移动到目标页标题；动画期间不可让隐藏页面进入 Tab 顺序。
- 折叠侧栏后按钮仍必须保留可访问名称，不能只依赖 CSS tooltip。

### 8.6 卡片和设置行

- 卡片使用 `--card`、`--border`、`--radius` 和 `--shadow-sm`，避免为单页复制一套新表面。
- 设置行默认是“左侧标签与说明、右侧控件”；窄窗口可折叠为单列。
- 卡片本身不是按钮时不得添加虚假交互样式；内部按钮焦点可通过 `:focus-within` 强化卡片边界。

### 8.7 对话框和抽屉

- 优先使用原生 `<dialog>` 和 `showModal()`，由浏览器处理模态焦点边界。
- 每个 dialog 必须通过 `aria-labelledby` 指向可见标题；说明可通过 `aria-describedby` 关联。
- Escape、取消按钮和标题栏关闭按钮走同一关闭路径，并恢复到触发控件。
- 破坏性操作必须二次确认，标题和正文明确说明被删除的数据及不会受影响的数据。
- 当前播放器设置抽屉属于右侧 modal dialog，不应降级成无焦点约束的普通浮层。

### 8.8 菜单和弹出层

- 选项列表使用 listbox 语义；命令菜单才使用 menu/menuitem，不能因为外观相似混用角色。
- 非模态弹出层必须支持 Escape、点击外部、窗口失焦和滚动关闭。
- 弹出层不得超出 WebView 边缘；位置计算应集中执行，避免交错读取和写入布局属性。
- Windows 原生托盘菜单不受本节 CSS 约束，但命令名称和状态文案应与 WebView 一致。

### 8.9 提示、状态和空状态

- 短暂操作反馈使用 Toast；Toast 使用 `role="status"` 和 polite live-region，不抢夺焦点。
- 异步更新结果、搜索结果数量等需要被辅助技术感知时，使用节流后的 `aria-live="polite"`。
- Empty 状态包含“发生了什么”和“下一步可以做什么”，不只显示空白区域或单个图标。
- Error 状态提供恢复动作；不可恢复错误才允许只提供关闭。

## 9. 交互状态

所有交互组件必须逐项判断下列状态是否适用：

| 状态 | 强制表现 |
| --- | --- |
| Hover | 改变表面、边框或前景之一；不能造成布局位移 |
| Pressed | 短暂、可中断反馈；现有按钮通常使用 `scale(.98)` |
| Selected | 文字或 ARIA 状态与视觉背景同时表达 |
| Focus | 使用 `:focus-visible`；现有基线是 2px `--ring` 和 2px offset |
| Disabled | 原生 `disabled`，降低强调度并使用 `not-allowed`；不得仍响应点击 |
| Loading | 禁止重复触发，显示 spinner 和“处理中…”文案；完成结果需要播报 |
| Error | `--destructive`、明确错误文案、`aria-invalid` 和恢复方式 |
| Empty | 说明为空的原因或条件，并给出可执行下一步 |

**通用 Web Guidelines：**不得使用 `outline: none` 而不提供等价焦点表现；不得只依赖 Hover 暴露关键操作。  
**WebView2/Windows 适配：**桌面控件不强制套用移动端 44px 最小尺寸，但必须能被键盘完整操作，并在 100% DPI 下拥有清晰的鼠标命中区。

## 10. 键盘与可访问性

### 10.1 强制规则

- 所有功能必须能只用键盘完成；至少验证 Tab、Shift+Tab、Enter、Space 和 Escape。
- 使用原生语义元素；确需 ARIA 组件时，必须完整实现其键盘模式，不能只添加角色名称。
- radiogroup 支持方向键、Home、End 和 roving tabindex。
- listbox 支持方向键、Home、End、Enter、Space、Escape 和焦点返回。
- 可拖动排序必须提供键盘替代，并通过 live-region 说明新位置。当前播放源优先级使用 `Alt+ArrowUp/Down`，应继续保留。
- 当前导航项使用 `aria-current="page"`；展开控件同步 `aria-expanded`。
- 装饰 SVG、封面和频谱应从无障碍树隐藏；携带信息的图片提供准确 alt。
- 对话框、异步状态、验证错误和动态结果必须拥有准确 Name、Role、State。
- 焦点不可进入 CSS 隐藏或转场中的页面，也不能在重新渲染后无故丢失。

### 10.2 歌词窗口适配

歌词内容会高频更新。不得直接把每句歌词设为连续 `aria-live`，否则会造成读屏干扰。应先确定歌词窗口在 Windows UI Automation 中的产品定位：

- 若窗口仅为不可交互的视觉叠加层，装饰结构应从无障碍树隐藏；
- 若未来要求读屏访问歌词，应提供用户主动打开、节流且可暂停的独立朗读视图，而不是播报每一帧进度。

### 10.3 Windows 高对比度

**建议标准：**在 Windows 对比度主题下验证开关、滑块、选择状态、危险按钮、焦点环、Dialog 和颜色选择器。只有实际状态丢失时才增加 `@media (forced-colors: active)`，优先使用 `Canvas`、`CanvasText`、`ButtonFace`、`Highlight` 等系统色；不要无依据关闭 `forced-color-adjust`。

## 11. 动画与减少动态效果

### 11.1 项目现状

- 设置页统一 Token 为 120、180、220ms，局部悬停约 140–160ms。
- 页面转场约 180–220ms，对话框和浮层以 opacity + transform 为主。
- 歌词切换约 560ms，封面淡入约 360–520ms，频谱待机动画约 1160–1540ms。
- 设置页已经使用 `prefers-reduced-motion`，页面 JavaScript 也会跳过转场；歌词页尚未完整适配。

### 11.2 强制规则

- 常规界面动画只修改 `transform` 和 `opacity`；避免动画化 width、height、top、left 等会触发布局的属性。
- 禁止 `transition: all`。
- Hover/Pressed 动画应在 120–180ms 内完成；浮层、页面和抽屉一般不超过 220ms。更长动画必须有明确的信息表达目的。
- 动画必须可中断；快速重复操作不能积累定时器或在过期回调中覆盖新状态。
- CSS 与 JavaScript 都必须响应 `prefers-reduced-motion: reduce`。减少动态效果时，立即完成页面、歌词和封面切换，并停止非必要的无限动画。
- 音频频谱若保留动态，应只使用固定尺寸元素的 `scaleY`/opacity，不在每帧写入 height。

## 12. DPI、分辨率和内容溢出

### 12.1 强制规则

- 在 Windows 100%、125%、150% 和 200% 缩放下检查文字、焦点环、边框、浮层和窗口控制。
- 设置窗口至少检查常规宽度、980px 以下和 720px 以下布局。
- 不依赖固定字符长度。歌曲、歌手、字体名称、版本号、错误信息和路径都视为不受控长文本。
- 单行元数据使用 `white-space: nowrap; overflow: hidden; text-overflow: ellipsis`；需要完整理解的说明文字必须允许换行。
- Grid/Flex 子项使用 `min-width: 0`；表格在窄窗口改为卡片式行布局，不强制横向滚动。
- Dialog 使用 `min()`/`calc()` 限制于 viewport，并为长内容提供内部滚动。
- 歌词窗口继续由宿主处理任务栏边缘、屏幕工作区和物理像素定位；Web UI 只根据自身 viewport 排版。

## 13. 性能约束

### 13.1 强制规则

- 高频动画只修改 transform/opacity，并使用单一 `requestAnimationFrame` 所有权；离开对应模式或释放页面时取消帧循环。
- 在同一帧内先集中读取布局，再集中写入样式；避免读写交错造成强制同步布局。
- WebView 消息不得按每根频谱柱或每个 DOM 节点逐条发送；继续批量传递数据，并由前端一次渲染。
- 动态列表使用 `DocumentFragment`、`replaceChildren` 或一次性模板更新，禁止循环中反复触发布局。
- 事件委托用于重复列表项；独立窗口关闭时清理监听器、定时器、ResizeObserver 和动画帧。
- 不为本地小型资源机械添加 lazy-loading。只有资源数量、尺寸或加载时机证明有收益时才使用。
- 不提前使用 `will-change` 覆盖大量长期存在的元素；只用于实际持续动画的少量节点。

### 13.2 建议标准

- 对频谱、歌词转场、窗口缩放和长列表变更使用 Edge DevTools Performance 面板抽样验证；优化结构性热点，不做牺牲清晰度的微优化。

## 14. 通用规则与项目适配的边界

### 14.1 直接采用的通用 Web Guidelines

- 语义化 HTML、可访问名称、键盘完整操作和可见焦点。
- 表单标签、输入约束、行内错误、`aria-invalid` 和动态状态播报。
- reduced-motion、仅 transform/opacity 动画、禁止 `transition: all`。
- 长文本、空状态、加载状态、危险操作确认和明确文案。
- 图片尺寸稳定、批量 DOM 更新和避免布局抖动。

### 14.2 经 WebView2/Windows 适配后采用

- 不要求地址栏、URL 状态和浏览器历史；设置页导航状态由应用内部维护。
- 外部链接交给 C# 宿主，避免 WebView 内任意导航。
- 深浅主题由宿主 PreferredColorScheme 驱动，CSS 继续消费 `prefers-color-scheme`。
- 紧凑桌面控件不机械满足移动端 44px，但必须保障键盘、焦点和鼠标命中。
- Windows 高对比度通过真实对比度主题验收，不直接照搬网站配色覆盖策略。
- 歌词高频变化不使用连续 live-region；无障碍策略需要与桌面叠加窗口定位一致。

### 14.3 不适用于本项目

- React hydration、Vue/Svelte 状态管理、SSR 和服务端渲染规则。
- Tailwind、Radix UI、shadcn/ui CLI、组件包或在线 CDN 的接入方式。
- SEO、Open Graph、营销落地页、浏览器前进后退和深链接。
- 登录、支付、地址、密码管理器和传统表单提交规范。
- 针对长网页的图片懒加载和移动端安全区规则，除非未来出现对应真实场景。

## 15. 变更工作流

涉及 WebView 界面设计、样式修改、组件实现或体验审查时：

1. 读取根目录 `AGENTS.md`、TaskbarLyrics Clean Code Guardian 及本文档。
2. 检查 `git status`，保留无关用户改动。
3. 确认受影响页面、组件、WebView V1 消息、设置契约和测试范围。
4. 优先复用现有 Token 和组件；新增规则注明“项目现状”或“建议标准”。
5. 修改设置页 HTML/JS/CSS/C# 时同步更新并运行设置契约测试。
6. 为交互逻辑补充 Vitest/jsdom 测试；视觉内容记录 Windows 手工检查矩阵。
7. 按仓库规则运行验证。影响可运行界面且验证通过时才重启应用；纯文档或规则修改不重启。

## 16. 验收清单

### 技术与一致性

- [ ] 仍为纯 HTML、CSS、JavaScript，无禁止依赖和在线 CDN。
- [ ] 复用现有语义 Token；新增 Token 同时定义深浅主题且理由明确。
- [ ] WebView V1 信封、`window.settingsApp` 和歌词注入标记未被破坏。
- [ ] 没有把业务规则、持久化或原生系统能力移入前端。

### 视觉与内容

- [ ] 深色、浅色及 Windows 对比度主题下信息层级可辨识。
- [ ] 间距、圆角、阴影与现有组件一致；建议标准没有伪装成既有实现。
- [ ] 文案明确、使用中文省略号，危险操作说明影响范围。
- [ ] 长歌曲名、歌手名、字体名、路径、版本号和错误信息不会破坏布局。

### 交互与可访问性

- [ ] Hover、Pressed、Selected、Focus、Disabled、Loading、Error、Empty 状态按需实现。
- [ ] 只用键盘可以完成全部操作，焦点顺序与恢复位置合理。
- [ ] 自定义 radio、listbox、dialog、popover 实现完整语义和键盘模式。
- [ ] 动态状态适度播报；装饰 SVG、封面和频谱不会污染无障碍树。
- [ ] 错误不仅依赖颜色，输入同步 `aria-invalid` 和说明关联。

### 动画、DPI 与性能

- [ ] CSS 和 JavaScript 都尊重 `prefers-reduced-motion`。
- [ ] 动画不修改布局属性，没有 `transition: all`，可被快速操作中断。
- [ ] 在 100%、125%、150%、200% DPI 和 980/720px 窄窗口下检查。
- [ ] 高频更新没有逐节点 WebView 消息、强制布局循环或未清理的 rAF/定时器。

### 验证

- [ ] 设置契约测试、相关 Vitest/jsdom 测试和仓库验证按影响范围通过。
- [ ] `git diff --check` 通过。
- [ ] 需要人工确认的 WebView2、焦点、DPI、高对比度和动画结果已记录。

## 17. 补充参考

- 在线 Web Interface Guidelines 可用于发现新问题，但不能自动覆盖本文档。
- Microsoft WebView2、Edge 和 Windows 无障碍文档用于确认运行时行为，但具体产品取舍仍以本规范为准。
- shadcn/ui 可用于观察组件层级与交互细节，不得复制其 React/TSX 实现或引入其运行时依赖。
