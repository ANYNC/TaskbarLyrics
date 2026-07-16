# TaskbarLyrics Light 协作模式

本文用于整理 TaskbarLyrics Light 版的协作边界、分支策略、项目资产对齐方式，以及对贡献者的简要回复模板。

## 1. 当前结论

TaskbarLyrics Light 版作为独立长期开发方向保留，但不直接合入 `main` 分支。

推荐创建 `light` 分支作为 Light 版的正式开发分支。PR #19 在修复关键问题后，可以考虑合入 `light` 分支继续维护。

`main` 继续作为原版主线和 `TaskbarLyrics.Core` 的权威来源。`light` 可以定期从 `main` 同步通用修复和核心能力，但不把整个 `light` 反向合回 `main`。

如果 `light` 中出现适合主线的通用改动，应拆成单独 PR 合入 `main`，或通过 cherry-pick / 按文件提取的方式谨慎同步。

## 2. Light 版定位

TaskbarLyrics 最早也是从纯 WPF 实现开始的，后来演进到 WPF + WebView2，并不是因为纯 WPF 路线不可用，而是因为项目主线更重视视觉表现和桌面歌词的观感体验。

当前纯 WPF 实现很难达到主线希望追求的视觉效果。对 `main` 来说，回到纯 WPF 路线更像一次视觉降级，无法很好满足项目最初的体验目标。

因此，`main` 会继续以视觉表现和体验完整度为主导。性能优化仍然重要，但在当前阶段是次要考虑点，更适合在项目成熟后再做系统性的性能重构。

Light 版的方向与主线并不冲突。它更适合作为纯 WPF 路线的延伸，面向重视启动速度、进程数量、内存占用、部署体积和低依赖的用户。

Light 版可以拥有自己的原生 WPF 歌词窗、设置页、托盘菜单、诊断窗口、发布脚本和 Light 专属体验，同时共享 `TaskbarLyrics.Core` 的歌词检索和通用逻辑，避免核心能力分叉。

## 3. 分支职责边界

| 分支 | 职责 |
| --- | --- |
| `main` | 原版主线、视觉表现优先、`TaskbarLyrics.Core` 权威来源、通用功能和修复 |
| `light` | Light 版 App 层、纯 WPF 轻量化方向、Light 设置、Light 发布和 Light 专属体验 |

`main -> light`：允许定期 merge，用于同步 Core、歌词检索、SMTC、本地歌词、通用修复和其他适合两个版本共享的能力。

`light -> main`：不整体合并。只有通用建设性改动可以单独提 PR 到 `main`，或通过 cherry-pick / 按文件提取的方式同步。

推荐同步命令：

```bash
git checkout light
git fetch origin
git merge origin/main
git push origin light
```

## 4. 代码资产边界

代码资产按职责归属维护：

| 资产 | 归属 |
| --- | --- |
| `TaskbarLyrics.Core` | 共享核心，权威来源在 `main` |
| `TaskbarLyrics.App` | 原版 App 层，归 `main` |
| `TaskbarLyrics.Light` | Light App 层，归 `light` |
| 歌词源、歌词解析、SMTC 时间轴、本地歌词匹配等通用逻辑 | 优先进 `main` |
| Light 专属窗口、设置页、频谱样式、封面布局、自动更新安装器、Light 打包脚本 | 进 `light` |

总体原则是：共享逻辑尽量上移到 Core，产品形态差异留在各自 App 层。

## 5. PR 协作流程

Light 相关功能默认向 `light` 分支提 PR。Core 或通用逻辑修复默认向 `main` 分支提 PR。

如果一个 PR 同时修改 Core 和 Light App，建议拆成两个 PR：

1. 先将 Core / 通用修复合入 `main`。
2. 再从 `main` 同步到 `light`。
3. 最后将 Light 专属改动合入 `light`。

PR 描述建议包含：

- 改动目标
- 影响范围：`main` / `light` / `Core`
- 构建验证结果
- 是否影响发布、自动更新或安装逻辑
- 是否需要同步到另一个分支

## 6. 权限与保护策略

暂不建议开放主仓库直接 push 权限。贡献者可以继续从 fork 向 `ANYNC/TaskbarLyrics:light` 提 PR。

`light` 分支也建议开启保护规则：

- Require a pull request before merging
- Require approvals: 1
- Require status checks to pass
- Require conversation resolution before merging
- Do not allow force pushes
- Do not allow deletions
- Restrict who can push

这样可以鼓励长期协作，同时保留主仓库发布链路和关键分支的维护边界。

## 7. 发布与项目资产对齐

GitHub Release 统一由主仓库发布。正式发布时，`main` 和 `light` 尽量使用一致版本号。

推荐一个 Release 同时包含原版和 Light 版资产：

```text
Release v1.3.0
Assets:
- TaskbarLyrics_v1.3.0.zip
- TaskbarLyrics_light_v1.3.0.zip
```

Changelog 可以分为：

```md
### Main

### Light
```

不建议拆成两个 Release，例如 `v1.3.0` 和 `v1.3.0-light`。这样会增加自动更新和用户理解成本。

如果只修 Light，也可以发布 `v1.3.1`，并在 changelog 中说明 Main 无变化 / Light 修复内容。

发布资产需要保持统一：

- Light 自动更新源必须指向 `ANYNC/TaskbarLyrics`。
- 发布包命名应由主仓库统一管理。
- 更新包结构应可 review、可验证、可复现。
- README 下载入口、截图说明、Release 说明应以主仓库为准。
- 涉及自动更新、安装器、发布脚本的变更需要单独 review。

## 8. PR #19 合入前要求

PR #19 可以作为 Light 版长期分支的起点，但合入 `light` 前应先处理以下问题：

1. `UpdateChecker` 更新源改为 `ANYNC/TaskbarLyrics`，或先禁用 Light 自动更新。
2. 修复 `LyricsWindowHost.Close()` 导致窗口关闭和资源清理不执行的问题。
3. `UpdateInstaller` / 发布脚本需要保持可 review、可控，至少限制发布源、资产命名和包内结构。
4. README 中 Light 说明不要暗示 Light 直接替代 main，而应说明它是轻量版本并行提供。

其中自动更新和安装逻辑属于发布链路和信任边界，不能只按功能可用来判断是否可以合入。

## 9. 后续维护建议

`main` 按原版方向继续正常开发，优先保证视觉表现和体验完整度。

`light` 作为纯 WPF 轻量化方向长期维护，定期从 `main` 同步通用更新。

Light 贡献者可以主要负责 Light App 层体验，包括原生 WPF 歌词窗、设置页、频谱样式、封面布局、托盘菜单、诊断窗口和 Light 发布流程。

通用能力尽量沉淀到 `TaskbarLyrics.Core`。发布前应同时构建并检查两个版本。

每次涉及自动更新、安装器、发布脚本、Release 资产命名和下载源的变更，都需要作为发布链路变更单独 review。
