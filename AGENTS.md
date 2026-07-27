# TaskbarLyrics repository instructions

Read this file before changing the repository. Keep user-visible behavior and stored user data stable unless the user explicitly approves a migration.

## Mandatory Clean Code workflow

- For every code/configuration plan, implementation, bug fix, refactor, code review, or verification task, read `.agents/skills/taskbarlyrics-clean-code-guardian/SKILL.md` completely before acting.
- Read every reference that skill marks as required. This applies even when the skill is not shown in the active skill catalog.
- Documentation-only copy edits do not require the skill unless they change development policy, architecture, behavior contracts, or verification instructions.

## Build and verification

- Requires .NET 8 SDK, Windows x64, and Windows 11 SDK 10.0.22621.
- Run: `dotnet run --project TaskbarLyrics.App`
- Build: `dotnet build TaskbarLyrics.sln`
- Full verification: `powershell -ExecutionPolicy Bypass -File scripts/verify.ps1`
- Restart local app: `powershell -ExecutionPolicy Bypass -File scripts/restart-app.ps1`
- Release packaging: `powershell -ExecutionPolicy Bypass -File scripts/publish-release.ps1`
- Before handoff, also run `git diff --check`. For production code changes, require a zero-warning solution build.
- After verification succeeds for a change that affects the runnable app, run `scripts/restart-app.ps1` and leave the app ready for user validation. Skip this for documentation-only, test-only, instruction-only, or build-only changes, or when the user opts out.

The verification script runs Vitest/jsdom web tests, App tests, Core tests, the settings contract test, and `dotnet format --verify-no-changes`.

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
- Changes to settings HTML/JS/CSS/C# must update and pass the settings contract test.
- Cache formats must be versioned. Never allow stale or fuzzy matches to become durable without a stable media identity.
- Keep windows as UI hosts. Cross-domain construction belongs in `AppCompositionRoot`.

## Windows and concurrency boundaries

- `LyricsWindowHost` owns a separate STA thread. Access its window only through its dispatcher boundary.
- Propagate cancellation, observe asynchronous failures, dispose native/audio/WebView resources, and unsubscribe events.
- Do not mix WPF device-independent units with Win32 physical pixels. Native positioning changes must consider DPI changes, resolution changes, taskbar edge, and multiple monitors.

## Independent technical judgment

- Treat the user's goal as authoritative, but treat a proposed implementation as a hypothesis that must be evaluated.
- Do not agree with or implement a proposal merely because the user suggested it.
- When a proposal conflicts with repository contracts, adds unnecessary complexity, weakens correctness, compatibility, testability, maintainability, or user experience, or when a materially better solution exists, state the concern clearly before acting.
- Support objections with concrete evidence, likely consequences, and a recommended alternative. Distinguish objective defects from subjective preferences.
- Preserve the user's underlying goal when recommending another approach; challenge the implementation, not the user.
- Do not manufacture objections over minor stylistic differences. Raise concerns only when they materially affect the project.
- When multiple approaches are valid, explain the relevant trade-offs and make a clear recommendation instead of giving unqualified agreement.
- If the user knowingly accepts a reversible trade-off that does not violate safety or repository contracts, follow that decision and report the remaining risk honestly.

## Change discipline

- Inspect `git status` before editing and preserve unrelated user changes.
- Diagnose requests are read-only unless the user asks for a fix.
- Prefer the smallest complete behavior-preserving change; do not add speculative abstractions.
- Do not suppress analyzer warnings to hide design problems. Document any unavoidable suppression next to the boundary it protects.
- Add regression tests for changed logic and failure paths. Record manual checks for Windows behavior that cannot be automated reliably.
- Update `docs/工程变更记录.md` once when a complete, independently verifiable feature, fix, migration, or refactoring stage is completed and verified. Record user impact, technical boundaries, compatibility, verification, and remaining work; do not log conversation turns, abandoned attempts, file-by-file edits, formatting, or trivial renames.
- Do not commit, push, publish, delete user data, or change external state unless the user authorizes it.

## Generated and local data

- Ignore `publish/`, `tmp/`, `bin/`, `obj/`, `build_verify*/`, `node_modules/`, logs, and runtime data under `%APPDATA%\TaskbarLyrics`.
- Test projects and frontend development dependencies are not included in the release ZIP.
