# 全局播放器快捷键规格

## Problem Statement

TaskbarLyrics 已能通过 SMTC 识别当前歌曲并在任务栏显示歌词，但用户无法在其他应用前台时快速控制当前歌词对应的播放器。系统已有媒体快捷键在不同设备和播放器上并不总是可用，也不能保证与 TaskbarLyrics 的播放器识别优先级一致。

用户需要一组可配置的全局快捷键，用于控制歌词显示、播放、切歌和短距离跳转，并且需要明确知道某个组合键是否已经成功注册或被占用。

## Solution

在设置页左侧导航中新增“快捷键”页面。用户可先启用总开关，再通过按键录制方式为常用播放器操作设置组合键。快捷键在其他应用前台时仍生效，并始终控制当前被 TaskbarLyrics 识别和用于歌词显示的同一 SMTC 会话。

默认总开关开启，常用组合键可在首次启动后直接使用。默认绑定为：

| 操作 | 默认快捷键 |
| --- | --- |
| 显示 / 隐藏歌词 | Alt+Shift+D |
| 播放 / 暂停 | Alt+Shift+P |
| 上一首 | Alt+Shift+Left |
| 下一首 | Alt+Shift+Right |
| 后退 5 秒 | Ctrl+Alt+Shift+Left |
| 前进 5 秒 | Ctrl+Alt+Shift+Right |

每项显示当前组合键、注册状态和“恢复”操作。无可用 SMTC 会话、播放器未实现某项控制或控制调用失败时，应用静默忽略，不显示通知或错误提示。

## User Stories

1. As a TaskbarLyrics user, I want a global shortcut master switch, so that I can decide whether the application registers any system-wide media shortcuts.
2. As a user installing or restoring TaskbarLyrics, I want global shortcuts to be enabled by default, so that the common controls are ready to use immediately.
3. As a user, I want to show or hide the lyrics window with Alt+Shift+D by default, so that I can quickly clear or restore the taskbar area.
4. As a user, I want to toggle play and pause with Alt+Shift+P by default, so that I can control music without leaving my current application.
5. As a user, I want to go to the previous track with Alt+Shift+Left by default, so that I can quickly return to a song.
6. As a user, I want to go to the next track with Alt+Shift+Right by default, so that I can skip a song quickly.
7. As a user, I want to seek backward five seconds with Ctrl+Alt+Shift+Left by default, so that I can replay a short passage.
8. As a user, I want to seek forward five seconds with Ctrl+Alt+Shift+Right by default, so that I can skip a short passage.
9. As a user, I want to record a shortcut by focusing its control and pressing a key combination, so that I do not need to type platform-specific shortcut names manually.
10. As a user, I want shortcut recording to require Ctrl, Alt, or Shift, so that ordinary typing keys are not registered globally by mistake.
11. As a user, I want to cancel an in-progress shortcut recording, so that an accidental key press does not replace my configured binding.
12. As a user, I want each action to show whether its shortcut is registered, disabled, invalid, duplicated, or occupied, so that I can resolve unavailable controls myself.
13. As a user, I want duplicate shortcut assignments inside TaskbarLyrics to be rejected clearly, so that one key press cannot trigger multiple media actions.
14. As a user, I want to know when Windows or another application has already occupied a shortcut, so that I can choose a different binding.
15. As a user, I want to restore one shortcut to its default without resetting unrelated settings, so that experimentation is reversible.
16. As a user, I want configured shortcuts to remain editable while the master switch is off, so that I can prepare bindings before activating them.
17. As a user using several players, I want shortcuts to control the same player session selected for lyrics, so that playback control and displayed lyrics never target different players.
18. As a user who changes enabled player sources or recognition order, I want subsequent shortcuts to follow those changes, so that the control target remains predictable.
19. As a user whose player does not expose a supported SMTC command, I want the action to fail quietly, so that unsupported controls do not interrupt my work.
20. As a user with no active or usable media session, I want a shortcut press to do nothing quietly, so that background use remains unobtrusive.
21. As a user restarting TaskbarLyrics, I want enabled shortcuts to be registered again from saved settings, so that the feature remains reliable across launches.
22. As a user closing TaskbarLyrics, I want its registered shortcuts to be released immediately, so that other applications can use them.
23. As a diagnostic user, I want the settings page to update registration status after I change a binding or the master switch, so that status reflects the current registration result rather than a stale value.
24. As a user restoring all application defaults, I want global shortcut settings to return to their enabled default state and default bindings, so that the application reset remains internally consistent.

## Implementation Decisions

- Add a persisted global-media-hotkey settings model containing the master switch and six bindings. Missing settings from older configuration files must resolve to the enabled state and the default bindings.
- Register shortcuts through the Windows global-hotkey mechanism, hosted by a non-visible application message target. Registration must use no-repeat behavior so holding a shortcut does not repeatedly fire media commands.
- Normalize supported bindings to a single canonical string representation. Recording accepts a supported base key combined with one or more of Ctrl, Alt, and Shift; the initial supported set covers letters, digits, function keys, arrows, Space, and common navigation keys.
- Treat duplicate bindings within TaskbarLyrics as configuration conflicts before asking Windows to register them. Treat a failed Windows registration as an external occupancy conflict.
- Keep registration status in a form that the settings bridge can send to the WebView UI. Expected user-facing states are: disabled, registered, invalid combination, duplicate within TaskbarLyrics, and occupied by Windows or another application.
- Place the controls in a dedicated “快捷键” settings page. Each row contains the action name, a press-to-record binding control, current status, and a per-action restore button.
- Escape cancels shortcut recording and preserves the previous binding. A plain key without Ctrl, Alt, or Shift is not accepted as a global shortcut.
- Reuse the existing SMTC session selection policy rather than using a separately selected system media session. The selected session must continue to respect enabled player sources, recognition order, active playback preference, and existing generic-session fallback behavior.
- Route shortcut execution through the lyrics window host's existing dispatcher boundary before reaching the SMTC provider. The lyrics window remains the owner of the SMTC provider and must not be accessed directly from the main UI thread.
- Route the lyrics visibility action to the application's existing show/hide behavior. Map the other five actions to SMTC play/pause, previous-track, next-track, and playback-position commands. Seek commands calculate a target from the current SMTC timeline, move by five seconds, and clamp at the timeline start and end.
- All media-command failures are best-effort and intentionally silent, including no selected session, unsupported command, failed command, and transient SMTC errors.
- Apply saved registrations during application startup, refresh them whenever settings are saved, and unregister all bindings during application shutdown.
- Extend the existing settings WebMessage contract for binding updates, per-action reset requests, and status payloads. A shortcut binding update must persist before the UI refreshes its registration status.

## Testing Decisions

- Test externally observable behavior rather than WPF or WebView implementation details.
- Add focused automated coverage around shortcut parsing and normalization: supported combinations are accepted, plain keys and unsupported keys are rejected, and the canonical binding string is stable.
- Add focused automated coverage around registration decisions through a replaceable registration boundary: disabled settings register nothing; duplicate bindings are reported without registration; a registration failure is surfaced as occupied; successful registration reports registered; disposing the service releases registered bindings.
- Add focused automated coverage around media-command routing through an SMTC-facing seam: play/pause chooses play or pause from the current playback state; previous and next call their matching command; seek moves exactly five seconds when in range and clamps to valid timeline bounds.
- Add an integration-level check that shortcut execution uses the same selected session policy as lyric acquisition, including player-source enablement and recognition-order changes.
- Extend the existing settings contract test so that settings markup, JavaScript bridge messages, C# message handling, persisted setting keys, and status payload keys remain synchronized.
- Perform a manual Windows verification matrix after a build: enable the master switch, validate the lyrics visibility binding and the five media bindings with a supported player, validate pause and play states, validate both seek boundaries, validate an internally duplicated binding, validate a binding occupied by another application, validate a player that does not support seek, then restart and exit the application to confirm re-registration and release.

## Out of Scope

- Volume, mute, stop, shuffle, repeat, playback-speed, playlist, or other media controls beyond the six specified actions.
- Per-player shortcut profiles, multiple shortcut profiles, macros, multi-step commands, or tray-menu shortcut editing.
- Keyboard injection, controlling players that do not expose a usable SMTC session, or workarounds for a player that rejects an SMTC command.
- Notifications, toast messages, logs, or UI popups for failed shortcut execution.
- Windows-key bindings and arbitrary unsupported key forms in the first version.
- Changes to the existing lyrics rendering architecture, WebView process model, player recognition rules, or lyrics-source routing.

## Further Notes

- The feature is intentionally global but conservative: registrations are disabled by default and require a modifier key.
- “当前歌词对应的播放器” means the session selected by the existing SMTC recognition policy, not necessarily Windows' generic current media session.
- Shortcut registration is a Windows resource. A combination can be unavailable because Windows, a driver utility, a game overlay, or another application already owns it.
- The saved setting should preserve bindings even when the master switch is disabled; only registration is suspended.
