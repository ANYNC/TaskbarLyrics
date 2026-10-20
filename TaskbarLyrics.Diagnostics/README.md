# Lyric pipeline diagnostics

This developer-only console runs the real lyric resolution pipeline against the current SMTC track and prints a JSON report. It uses a no-op cache, so searches, forced fetches, and parsed lyrics are never persisted.

Run the complete online fallback chain:

```powershell
dotnet run --project TaskbarLyrics.Diagnostics -- --output tmp/current-lyrics.json
```

Inspect only QQ Music:

```powershell
dotnet run --project TaskbarLyrics.Diagnostics -- --provider QQMusic
```

Fetch and parse a rejected candidate explicitly:

```powershell
dotnet run --project TaskbarLyrics.Diagnostics -- --provider QQMusic --force-provider QQMusic --force-candidate 575132683
```

When SMTC is unavailable or its values need to be reproduced exactly, supply manual metadata:

```powershell
dotnet run --project TaskbarLyrics.Diagnostics -- --provider QQMusic --title "Mystical Magical" --artist "Benson Boone" --source Netease --duration-seconds 200 --song-id 2697549465
```

The report includes the SMTC timeline snapshot, effective track identity, search variants, candidate metadata, identity evaluation, per-source terminal state, final selection, and an optional 20-line forced-candidate preview. Fetch metadata values are intentionally omitted because some providers use sensitive access keys.

## Taskbar space diagnostics (interactive Windows 11)

These opt-in commands use the production taskbar placement and occupancy reader without starting the music application, changing startup registration, or opening user settings/databases:

```powershell
dotnet run -c Release --project TaskbarLyrics.Diagnostics -- --taskbar-space verify
dotnet run -c Release --project TaskbarLyrics.Diagnostics -- --taskbar-space benchmark 15
```

`verify` briefly creates transparent test surfaces and a temporary minimized taskbar icon, then closes its own windows. It checks live control avoidance as icons appear/disappear, relocation across controls when the preferred region is too narrow, hiding when the entire taskbar is occupied, recovery at the configured position, and a negative-origin parent. Isolated left/right sidebar fixtures smoke-test vertical relocation after an orientation change; the App regression suite owns the detailed shortening, hiding, restoration and DPI scenarios. These fixtures do not constitute acceptance on an actual vertical Explorer taskbar. It does not restart Explorer or change taskbar preferences.

`benchmark` runs six alternating trials (`idle`, broad subtree scan, scoped Explorer scan, reverse order), with 15 seconds per trial by default (allowed: 5–60). It reports client CPU normalized by logical processor count, query latency and allocations. The idle mode retains the scheduler but performs no UI Automation query. Broad/scoped reads must first identify identical occupied regions. This isolates the query client; it does not measure whole-application or Explorer CPU and is not a battery benchmark. Keep the desktop reasonably stable while sampling.

On Windows 11, production avoidance projects controls onto the taskbar's long axis: left/right bounds for horizontal bars, top/bottom bounds for sidebars. The same nearest-gap algorithm applies to width or height with an 8 DIP safety gap and a 100 DIP minimum display length; persisted width/length and offsets are not rewritten. Windows 10 retains its existing embedding path.

The production reader is limited to one in-flight query and a 500 ms cadence. A query that stalls cannot block the lyrics dispatcher or create overlapping workers. Accepted snapshots notify the owning dispatcher immediately; placement does not wait for the next timer tick. Detaching or supplying an invalid native target invalidates the snapshot and rejects results from the previous generation. Orientation is derived from the taskbar rectangle, which is part of the cache identity, so rotating the bar invalidates geometry from the old axis. Last-known geometry expires after two seconds, measured from query start; expired/unknown geometry hides the native lyric surface until a valid layout is available. Live diagnostic reads also run off the dispatcher, and failures include the measured window/control bounds without weakening the safety-gap assertion.
