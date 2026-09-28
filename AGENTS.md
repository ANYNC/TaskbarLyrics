# TaskbarLyrics repository instructions

Read this file before changing the repository. Keep user-visible behavior and stored user data stable unless the user explicitly approves a migration.

## Project guidance

- Use the relevant sections of `.agents/skills/taskbarlyrics-clean-code-guardian/references/repository-contracts.md` when a change touches a compatibility or platform boundary. The Clean Code skill and rubric are optional review aids for complex changes, not prerequisites for routine work.
- For WebView interface work, consult the relevant sections of `docs/WebView界面视觉与交互规范.md`. Treat current tokens and components as the starting point; a justified improvement may update the standard.

## Build and verification

- Requires .NET 8 SDK, Windows x64, and Windows 11 SDK 10.0.22621. Run with `dotnet run --project TaskbarLyrics.App`; build with `dotnet build TaskbarLyrics.sln`.
- Choose verification by impact. Use `scripts/verify.ps1 -Tier Targeted -Area <Core|App|Web|Settings> -Filter <filter>` while iterating (`Settings` needs no filter), or `-Tier Project -Area <areas>` for affected areas. Run its default `Full` tier for release work, broad cross-module changes, and compatibility-sensitive changes. Run a zero-warning solution build for production C# integration or release work.
- Add or update tests when observable behavior or a compatibility contract changes. Cover settings page behavior in Vitest and settings persistence or message parsing in App tests. Full verification also checks that development artifacts stay out of production Web assets.
- Before handoff, run `git diff --check` and report which checks passed, failed, or were not run. Keep full verification output in ignored `tmp/verify-logs/` and show only a concise result or actionable failure excerpt.
- If a running app locks build output, stop it with `powershell -ExecutionPolicy Bypass -File scripts/restart-app.ps1 -StopOnly`. Restart with `-NoWait` when a real-app check is needed or the user requests an app ready for validation; do not restart as a default delivery step.
- Release packaging uses `powershell -ExecutionPolicy Bypass -File scripts/publish-release.ps1`.

## Solution layout

- `TaskbarLyrics.Core`: platform-neutral lyric retrieval, matching, parsing, caching, local media indexing, persistence, and policies. Do not add WPF, WebView2, WinForms, or native-window dependencies here.
- `TaskbarLyrics.App`: Windows host using WPF, WinForms NotifyIcon, WebView2, SMTC, audio capture, native-window integration, and the composition root.
- `TaskbarLyrics.Core.Tests` and `TaskbarLyrics.App.Tests`: xUnit regression tests.
- `tests/web`: Vitest/jsdom behavior tests.
- `TaskbarLyrics.App/Web`: modular lyrics and settings interfaces hosted in WebView2.

## Behavior contracts

- Preserve existing `settings.json` fields and migration behavior unless a migration is explicitly requested and tested.
- Preserve the WebView V1 envelope `{ version: 1, type, payload }`; unknown versions, types, and invalid payloads must fail safely.
- Keep the production Web UI framework-free: use native HTML, CSS, and JavaScript. Treat shadcn/ui as a design reference, not a runtime or source dependency.
- Keep `{{STYLE_CSS}}` and `{{APP_JS}}` injection markers and deterministic lyric-script ordering.
- Cover settings field, navigation, and WebView message changes with relevant Web and App behavior tests.
- Cache formats must be versioned. Never allow stale or fuzzy matches to become durable without a stable media identity.
- Keep windows as UI hosts. Cross-domain construction belongs in `AppCompositionRoot`.

## Windows and concurrency boundaries

- `LyricsWindowHost` owns a separate STA thread. Access its window only through its dispatcher boundary.
- Propagate cancellation, observe asynchronous failures, dispose native/audio/WebView resources, and unsubscribe events.
- Do not mix WPF device-independent units with Win32 physical pixels. Native positioning changes must consider DPI changes, resolution changes, taskbar edge, and multiple monitors.

## Independent technical judgment

- Treat the user's goal as authoritative. Evaluate proposed implementations against compatibility, correctness, maintainability, and user experience; raise material concerns with evidence and a recommended alternative.
- Resolve minor choices without seeking approval. When the user accepts a reversible trade-off that respects project contracts, follow that decision and report any remaining risk.

## Delegation

- Delegation is optional. Use it only for bounded, independent work with clear file ownership and acceptance criteria; the primary agent remains responsible for integration and the final result. Do not assign overlapping writes.

## Change discipline

- Inspect `git status` before editing and preserve unrelated user changes.
- Diagnose requests are read-only unless the user asks for a fix.
- Prefer the smallest complete behavior-preserving change; do not add speculative abstractions.
- Do not suppress analyzer warnings to hide design problems. Document any unavoidable suppression next to the boundary it protects.
- Add regression tests for changed logic and failure paths when practical. Record relevant manual checks for Windows behavior that cannot be automated reliably.
- Update `docs/工程变更记录.md` for migrations, protocol or architecture changes, important features or fixes, and engineering-policy changes. Keep newest completed entries first and the template last; routine fixes and mechanical edits can rely on Git history and tests.
- Do not commit, push, publish, delete user data, or change external state unless the user authorizes it.

## Generated and local data

- Ignore `publish/`, `tmp/`, `bin/`, `obj/`, `build_verify*/`, `node_modules/`, logs, and runtime data under `%APPDATA%\TaskbarLyrics`.
- Test projects and frontend development dependencies are not included in the release ZIP.
