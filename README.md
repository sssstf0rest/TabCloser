# TabCloser

`TabCloser.Windows` is a WinForms notification-area application targeting `net10.0-windows`. It closes only a Chrome tab that receives two complete left clicks under the user's Windows double-click settings.

This directory is self-contained and separate from the legacy Chrome extension at the repository root.

## Architecture

- `TabCloser.Core` contains platform-neutral click assembly, timing, movement, and same-target decisions.
- `TabCloser.Windows` owns the tray UI, low-level mouse hook, Chrome UI Automation hit-testing, and final middle-click injection.
- `TabCloser.Core.Tests` is a zero-dependency console test runner.
- `TabCloser.Windows.Tests` covers native input batching and partial-injection recovery without sending real input.
- `TabCloser.Diagnostics` inspects the live UI Automation surface and measures hit-test latency without recording titles, URLs, or page text.

The hook callback records button events, event-time root HWNDs, input sequence, pointer revision, and maximum drag excursion, then returns immediately. A bounded channel moves work to a dedicated MTA thread. Queue overflow, pause changes, desktop/UAC switches, disposal, new input, or post-release pointer motion invalidates pending work.

UI Automation candidates must have a nonzero process ID matching a `chrome.exe` root, be in its DPI-scaled top band, expose `TabItem` with class name `Tab`, sit under a native tab list, reach the root HWND, and never cross a webpage `Document`. Descendant `Button` points are rejected. The target is captured while each observed left press is still held, re-hit-tested on release, and revalidated at the exact final cursor point. If UI Automation is slow or unavailable, the click safely does nothing.

## Commands

From this `TabCloser` directory:

```powershell
dotnet build TabCloser.sln -c Release
dotnet run --project tests/TabCloser.Core.Tests -c Release
dotnet run --project tests/TabCloser.Windows.Tests -c Release
dotnet publish src/TabCloser.Windows/TabCloser.Windows.csproj -p:PublishProfile=win-x64 -o artifacts/win-x64
```

Publishing creates one self-contained `TabCloser.exe`; users do not need a .NET runtime. The output is intentionally untrimmed because WinForms and UI Automation rely on framework metadata.

The executable and notification-area icon come from `icons/tab-closer.ico`. The editable vector source is `dev-kit/tab-closer-icon.svg`, with `dev-kit/tab-closer-icon.png` as its 1024 px raster master.

## Runtime Notes

The application runs as the current user, stores the optional startup entry under `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, and never requests elevation or network access. If Chrome is elevated, Windows integrity rules prevent injection and the gesture safely does nothing. A partial native injection receives one bounded best-effort middle-button release; it is never retried recursively.

For live inspection, pass a Chrome root HWND to the diagnostics project. Add a screen X/Y to benchmark one point, or pass `--tray` to inspect only the helper's tray surface and sanitized system-tray properties:

```powershell
dotnet run --project tools/TabCloser.Diagnostics -c Release -- <HWND>
dotnet run --project tools/TabCloser.Diagnostics -c Release -- <HWND> <X> <Y>
dotnet run --project tools/TabCloser.Diagnostics -c Release -- --tray
```

Complete [MANUAL_TESTS.md](MANUAL_TESTS.md) on Windows after changing hooks, hit-testing, packaging, or target-framework settings.
