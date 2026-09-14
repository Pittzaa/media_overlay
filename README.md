# TopMediaBar

A macOS-notch-style media bar for Windows. It hides at the very top edge of the
primary screen and slides down when you move the cursor near the top, showing
the current track (title, artist, artwork), transport controls, a seekable
progress bar, and per-app volume — then slides away again and shows a brief
"now playing" toast on track changes.

## How it works

- **Now playing / transport** — reads and controls playback through the
  Windows System Media Transport Controls (SMTC), so it follows whatever app
  currently owns the system media session (Spotify, browsers, etc.).
- **Volume** — controls the volume of the specific app session that owns the
  active media (via NAudio's Core Audio API), not the system master volume.
- **Reveal/hide** — polls the real screen cursor position (rather than WPF's
  hit-testing) to avoid flicker/bounce near the screen edge, and shows the
  bar with a short animated slide.
- **Toast** — flashes a small artwork + title/artist toast on track changes
  when the bar itself isn't already expanded.

## Requirements

- Windows 10 (1903+) / Windows 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Run

```powershell
dotnet run --project TopMediaBar.csproj
```

A tray icon is added on startup; use its context menu to quit the app.

## Build a distributable

```powershell
dotnet publish TopMediaBar.csproj -c Release
```

This produces a self-contained `TopMediaBar.exe` (no separate .NET runtime
install required) plus its dependency DLLs under `bin/Release/net8.0-windows*/win-x64/publish/`.
`PublishSingleFile` is intentionally left off — see the comment in
`TopMediaBar.csproj` for why.

## Tests

Regression checks live under [`tests/`](tests/README.md):

```powershell
dotnet run --project tests/TopMediaBar.RegressionTests.csproj -c Release
```

See `tests/README.md` for optional `--audio`, `--media`, and `--artwork`
checks.

## Project layout

| File | Purpose |
|---|---|
| `MainWindow.xaml(.cs)` | The notch bar UI, reveal/hide animation, and input handling |
| `MediaSessionService.cs` | SMTC integration (now playing, transport, seeking) |
| `VolumeService.cs` | Per-app audio session volume/mute via NAudio |
| `ToastWindow.xaml(.cs)` | The track-change toast popup |
| `PlaybackTimeline.cs` | Estimates live playback position between SMTC updates |
| `NativeMethods.cs` | Win32 interop (cursor position, tool-window styling) |
