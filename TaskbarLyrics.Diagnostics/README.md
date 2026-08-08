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
