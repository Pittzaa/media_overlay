# Regression checks

Run on Windows with the .NET 8 SDK:

```powershell
dotnet run --project tests/TopMediaBar.RegressionTests.csproj -c Release
```

Add `-- --audio` to check the active Chrome audio session. This briefly lowers the
Chrome session volume by one percent and toggles mute, restoring both values afterward.

Add `-- --media` to create a silent, local 120-second media source and check real
SMTC pause/resume, track-click seeking, drag preview and release. The fixture checks
its unique track title before sending commands and removes its WAV file afterward.

Add `-- --artwork` for a read-only check of the active session's thumbnail. It saves
the source bytes and decoded PNG beside the test executable for visual inspection.
This requires access to the desktop user's Windows media service.

For an offline restore when all dependencies are already cached:

```powershell
dotnet restore tests/TopMediaBar.RegressionTests.csproj --configfile tests/NuGet.Offline.Config -p:NuGetAudit=false
dotnet run --project tests/TopMediaBar.RegressionTests.csproj -c Release --no-restore -- --audio
```

Latest artwork validation: 27 timeline, WPF layout, delayed-toast-artwork and live
Chrome thumbnail checks passed. The six toast checks cover late replacement,
unchanged toast lifetime, failed refresh, track changes, stale artwork and hidden
toasts. Earlier validation also passed seven real endpoint audio checks.
Both projects build without warnings. The additional media fixture builds,
but its execution was blocked by Windows Smart App Control. The published app was
restarted successfully; Windows UI Automation confirmed enabled progress, transport,
mute and volume controls, with a usable volume track width.
