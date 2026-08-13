---
name: taskbarlyrics-clean-code-guardian
description: Enforces the TaskbarLyrics-specific Clean Code workflow and behavior-safety gates across C#, WPF, WebView2 HTML/CSS/JavaScript, settings, caches, SMTC, hotkeys, native windows, tests, scripts, and build configuration. Use whenever Codex plans, implements, fixes, refactors, reviews, formats, or verifies code or configuration in this repository, including apparently small changes. Do not use for copy-only documentation edits unless they alter architecture, behavior contracts, or engineering policy.
---

# TaskbarLyrics Clean Code Guardian

Apply Clean Code as a behavior-preserving engineering discipline. Optimize for correctness, clarity, cohesion, explicit boundaries, testability, and safe evolution—not arbitrary class or method counts.

## Load the project rules

Before acting, read both files completely:

- `references/repository-contracts.md` for TaskbarLyrics architecture and compatibility boundaries.
- `references/clean-code-rubric.md` for the required review dimensions.

Treat root `AGENTS.md` as the entry-point policy and these references as its detailed standard.

## Load the WebView UI standard when applicable

When a task involves WebView interface design, CSS or visual changes, component implementation, interaction behavior, accessibility, or user-experience review, read `../../../docs/WebView界面视觉与交互规范.md` completely before planning or acting. Treat that local document as the final project authority; online Web guidelines and shadcn/ui examples are supplementary references only.

## Classify the request

- For review or explanation, inspect and report evidence; do not edit.
- For diagnosis, reproduce or trace the failure and identify the cause; do not implement unless requested.
- For implementation or refactoring, preserve the current behavior contract, implement the smallest complete change, test it, and perform a final rubric review.
- Stop and ask before breaking stored settings, WebView messages, public behavior, release packaging, or user data.

## Establish the change contract

Before editing:

1. Inspect `git status` and separate pre-existing changes from task changes.
2. Locate the owning component, callers, tests, persistence formats, and UI/protocol consumers.
3. State the behavior that must remain unchanged and the failure being corrected or capability being added.
4. Identify thread, lifetime, cache, compatibility, native-coordinate, and packaging risks.
5. Prefer an existing seam. Add an abstraction only when it removes a real dependency, duplication, or testing barrier.

## Implement cleanly

- Keep policy in Core and Windows/UI mechanisms in App.
- Keep windows and WebView handlers thin; delegate domain behavior to focused services.
- Maintain one source of truth for settings keys, protocol types, hotkey actions, cache versions, and status codes.
- Use names that expose intent and units. Avoid boolean blindness, temporal coupling, hidden global state, and unrelated parameters.
- Keep functions at one abstraction level with explicit guard clauses and failure behavior.
- Propagate cancellation and make ownership/disposal visible. Never introduce unobserved fire-and-forget work.
- Validate data at boundaries. Preserve atomic persistence and safe fallback behavior.
- Do not retain dead compatibility branches after callers have migrated unless a documented external contract requires them.
- Avoid analyzer suppressions, catch-all exception swallowing, test-only production hooks, and comments that merely restate code.

## Prove the change

Run verification proportional to impact:

1. Add or update a regression test for changed logic and relevant failure paths.
2. Run targeted tests while iterating.
3. For settings UI changes, run `tests/contracts/settings-contract.tests.ps1`.
4. Before handing off non-trivial code changes, run `scripts/verify.ps1`, `dotnet build TaskbarLyrics.sln`, and `git diff --check`.
5. Require zero build warnings. Fix the cause unless an unavoidable platform boundary is documented.
6. Record manual checks for SMTC, tray, hotkeys, taskbar attachment, audio capture, WebView2, DPI, or multi-monitor behavior when automated coverage is insufficient.
7. When the root rules require an engineering change record, insert it at the top of the record list in `docs/工程变更记录.md`; preserve descending date order, newest-completed-first order within a date, and the template at the end.
8. After automated verification succeeds for a change that affects the runnable app, run `powershell -ExecutionPolicy Bypass -File scripts/restart-app.ps1` and leave the app running for immediate user validation. Skip restart for documentation-only, test-only, instruction-only, or build-only changes, or when the user opts out.

Do not claim completion when required verification was skipped or failed. Report the exact missing evidence.

## Self-review before handoff

Review the final diff against every dimension in `references/clean-code-rubric.md`. Check specifically for:

- accidental behavior or persistence changes;
- new duplication or competing sources of truth;
- dependency-direction violations;
- missing cancellation, disposal, or event unsubscription;
- unsafe cache reuse or migration;
- UI/protocol drift across C# and JavaScript;
- tests that assert implementation details instead of observable behavior;
- unrelated formatting or cleanup that obscures the change.

Report the outcome first, then changed behavior, verification evidence, and any remaining manual checks or risks. Keep unrelated user changes out of the summary.
