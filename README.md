<p align="center">
  <img src="dev-kit/tab-closer-icon.png" alt="TabCloser icon" width="128">
</p>

<h1 align="center">TabCloser</h1>

<p align="center">
  Close Chrome tabs, with optional Edge top-tab support.
</p>

TabCloser is a lightweight Windows tray app that adds a simple double-click-to-close gesture to Google Chrome's normal tab bar. It also includes an experimental, session-only option for Microsoft Edge top tabs. It runs locally, requires no browser extension, and never reads page content, titles, or URLs.

<p align="center">
  <img src="promotion%20sources/promo%20gif.gif" alt="TabCloser closing a Chrome tab with a double-click">
</p>

<!--
## Features

- Double-left-click a Chrome or Edge top tab to close that exact tab.
- Uses your Windows double-click speed and movement settings.
- Ignores page content, toolbar controls, close buttons, blank tab-strip space, modified clicks, and ambiguous targets.
- Pause or resume the gesture from the notification-area icon.
- Optionally start TabCloser with Windows.
- Runs without administrator access or network connectivity. -->

## Get Started

1. Download [TabCloser.exe](https://github.com/sssstf0rest/TabCloser/releases/latest/download/TabCloser.exe) from the latest release.
2. Run the executable.
3. Double-click a Chrome tab to close it.

## Microsoft Edge (Experimental)

Edge support is off after every TabCloser start. Open the tray menu and enable **Microsoft Edge top tabs (experimental)** for the current session.

TabCloser cannot inspect Edge's per-profile double-click setting. Check every Edge profile you will use. If it shows **Use double-click to close browser tabs** under **Settings > Appearance**, turn that option off first; otherwise both features could react and close two tabs.

Only Edge's normal horizontal top tabs are supported. The experimental classifier is designed to ignore vertical tabs; complete live vertical-layout verification is still pending.
