# Windows Manual Acceptance Tests

Run these checks on a supported x64 Windows system with current stable Google Chrome. Use the published `TabCloser.exe`, not a debugger-hosted build.

## Recorded Results — 2026-08-25

Environment: Windows 11 Pro 25H2 build `10.0.26200.9168`, Chrome Stable `151.0.7922.170`, x64, one 1920×1080 display at 96 DPI/100%.

| Area | Result |
|---|---|
| Release solution build | Passed with 0 warnings and 0 errors. |
| Automated runners | 19/19 Core cases and 4/4 Windows injection-batch cases passed. |
| Maximized live UIA | 42/42 native tabs accepted, including 41 narrow inactive tabs (minimum observed width about 57 px); exposed close buttons rejected. Two top-of-page ARIA tabs and two Chrome `TabbedPaneTab` controls were rejected. |
| Restored live UIA | Four native tabs accepted in an unobscured 1200×800 window; two ARIA tabs and four exposed close-button points were rejected. A later injected-input probe kept all 5/5 native tabs open. |
| Hit-test timing | 20/20 accepted with one stable identity; first warmed query 22.8 ms, median 5.5 ms, p95 6.3 ms. |
| Physical intended gesture | At 100% scaling, the user reported passes for maximized active, maximized inactive, restored active, restored inactive, and narrow inactive tabs; the intended tab closed in each case. |
| Physical ARIA exclusion | User reported that double-left-clicking both the linked and button ARIA tabs closed no native tabs; a subsequent real browser-tab gesture still closed correctly. |
| Physical race exclusions | User reported passes for a pair split across two tabs within 500 ms (neither closed), a greater-than-20-pixel drag away and back (no close), and a rapid move into page content at the second release (no tab close and no observed middle-click landing). |
| Physical timing/input exclusions | At the current 500 ms Windows setting, the user reported passes for a single left-click, a roughly one-second slow pair, Ctrl/Shift/Alt/Windows-modified double-clicks, a double-right-click, and a wheel-interrupted left-click pair; none closed a tab. |
| Physical New Tab/title-bar exclusions | The user reported that double-left-clicking New Tab caused no existing tab to close and double-left-clicking blank tab-strip/title-bar space closed no tab; ordinary Chrome side effects were allowed. |
| Physical Chrome-control exclusions | The user reported passes for double-left-clicking the address bar/toolbar, bookmarks area, page content, Downloads control/panel, and maximize/restore control; ordinary control behavior occurred without closing a tab. |
| Physical native-close/reflow checks | The user reported that clicking tab A's close button and quickly clicking adjacent tab B closed only A, while one native middle-click closed exactly its intended tab and no additional tab. |
| Physical process/window boundaries | The user reported that a File Explorer tab ignored the gesture, a sub-500-ms pair split across two Chrome windows closed neither tab, and a pair split from Chrome to File Explorer affected neither tab. |
| Physical pause/re-enable checks | The user reported that gestures were ignored while paused, input begun while paused did not bridge across re-enable, and an ordinary double-click closed exactly one tab after re-enable. |
| Physical Windows timing changes | The user reported that the running helper accepted a deliberately slow OS-calibrated double-click at a Slow setting, rejected that cadence at Fast, accepted a genuinely fast double-click, and worked normally after restoration. A read-only registry check confirmed the restored value is `500` ms. |
| Physical lock/UAC recovery | The user reported that input begun before workstation lock or a secure-desktop UAC prompt did not bridge after return; ordinary double-click closure recovered after both unlock and UAC cancellation. |
| Physical privilege boundary | The user reported that the unelevated helper did not close a tab in elevated Chrome, its tray menu remained responsive, and exact one-tab closure recovered after reopening Chrome normally. |
| Physical sign-out/in startup | Before sign-out, the exact quoted startup path matched the existing artifact and one helper was running. The user reported one tray instance auto-started after sign-in, tab closure worked, and a manual second launch created no duplicate. After disabling startup, read-only checks confirmed one helper process and no Run entry. |
| Physical 150% scaling | After Chrome restart, independent pre/post probes reported `144` DPI. The user reported all 11 targeted cases passed: active/inactive tabs in maximized/restored windows, narrow inactive tabs, New Tab and blank-strip exclusions, both ARIA controls plus native-tab recovery, close-button reflow, drag-return, and post-release pointer movement. |
| Physical 200% scaling | The user explicitly confirmed Windows display scaling was 200% and all 11 targeted cases passed, then restored 100%. A post-test probe independently confirmed Chrome returned to `96` DPI with one helper instance. |
| Mixed-DPI/negative-coordinate monitors | Not exercised: only one monitor is available; explicitly unclaimed as an accepted environment limitation. |
| Clean account/no installed runtime | Not exercised: Windows Sandbox is unavailable and no alternate account was provisioned; explicitly unclaimed as an accepted environment limitation. |
| Published lifecycle | One GUI executable launched standalone, blocked a second instance, exposed all tray items, paused/re-enabled, created and removed the exact startup value, made no observed TCP/UDP endpoints, and exited through the tray. |
| Packaging | PE32+ x64 Windows GUI; embedded `asInvoker`, `uiAccess="false"`, Per-Monitor V2, and a custom multi-resolution executable/tray icon; unsigned. Fresh size/hash are recorded in `findings.md`. |

The intended physical close gesture and all available control, input, race, reflow, process/window-boundary, pause/timing, lock/UAC, privilege, startup, and single-monitor scaling checks pass. The original `96` DPI/100% baseline is restored. The two unavailable environments above are explicitly unclaimed rather than treated as failures.

## Startup and Tray

- Launch the executable and confirm exactly one notification-area icon appears.
- Launch it again; confirm a second instance exits without another icon.
- Toggle **Enabled** off and confirm all gestures are ignored; turn it back on.
- Enable **Start with Windows**, sign out/in, and confirm one instance starts. Disable it again and confirm the startup entry is removed.
- Choose **Exit** and confirm the process ends.

## Intended Gesture

- In normal, maximized, and restored Chrome windows, double-left-click the center of active and inactive tabs; only that tab should close.
- Repeat at 100%, 150%, and 200% display scaling where available.
- Change Windows' double-click speed and confirm the helper follows the new setting.

## Must Not Close

- Single clicks; slow pairs; right/middle clicks; Ctrl/Shift/Alt/Windows-modified clicks.
- Drags, including moving away and returning before release.
- Chrome tab close buttons, New Tab, blank strip/title-bar space, toolbar, bookmarks, page content, downloads bubble, and window controls.
- Tabs or tab-like controls in another browser or application.
- A webpage fixture containing `role="tablist"` and `role="tab"`, including a linked ARIA tab near the top of the page.
- A click pair split across two tabs or two Chrome windows.

## Race and Failure Checks

- Move the pointer into page content immediately after the second release; no middle click should land there.
- Rapidly click while switching windows; the helper must never act outside the validated foreground Chrome window.
- Run Chrome as administrator while the helper is unelevated; the helper should do nothing and remain responsive.
- Lock the workstation or show a UAC secure-desktop prompt, then return; ordinary clicks must continue working and the helper must not replay stale input.
- Pause, overflow the input queue, or switch desktops between the two clicks; no stale pair may complete after re-enabling or returning.
- Press New Tab or a tab close button and hold briefly; any tab-strip reflow after release must not become a valid historical down target.
