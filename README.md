<p align="center">
  <img src="dev-kit/tab-closer-icon.png" alt="TabCloser icon" width="128">
</p>

<h1 align="center">TabCloser</h1>

<p align="center">
  Close a Google Chrome tab by double-clicking it.
</p>

TabCloser is a lightweight Windows tray app that adds a simple double-click-to-close gesture to Chrome's native tab bar. It runs locally, requires no browser extension, and never reads page content, titles, or URLs.

## Features

- Double-left-click a Chrome tab to close that exact tab.
- Uses your Windows double-click speed and movement settings.
- Ignores page content, toolbar controls, close buttons, blank tab-strip space, modified clicks, and ambiguous targets.
- Pause or resume the gesture from the notification-area icon.
- Optionally start TabCloser with Windows.
- Runs without administrator access or network connectivity.

## Get Started

1. Download [TabCloser.exe](artifacts/win-x64/TabCloser.exe).
2. Run the executable. No installation or separate .NET runtime is required.
3. Find the TabCloser icon in the Windows notification area.
4. Double-click the center of a Chrome tab to close it.

Windows may show a security warning because the executable is currently unsigned. Review the prompt before choosing to run it.

## Tray Controls

Right-click the TabCloser icon to:

- Enable or pause tab closing.
- Turn **Start with Windows** on or off.
- Exit the app.

## Compatibility

TabCloser supports x64 Windows 10 and Windows 11 with Google Chrome. It intentionally does nothing when the target cannot be identified safely or when Chrome is running with higher privileges than TabCloser.

For detailed verification scenarios, see [MANUAL_TESTS.md](MANUAL_TESTS.md).
