# Task Plan

## Goal

Build a Windows-only C#/.NET tray application that closes a Google Chrome tab when the user double-left-clicks that native tab, without requiring a Chrome extension or a manual keyboard shortcut.

## Phases

### Phase 1: Architecture and Toolchain

**Status:** complete

Confirm the available .NET toolchain, select Windows APIs/project layout, and define fail-closed behavior.

### Phase 2: Application Implementation

**Status:** complete

Implement the tray lifecycle, low-level mouse hook, Windows double-click recognition, Chrome tab hit-testing, and exact-target tab closure.

### Phase 3: Tests and Documentation

**Status:** complete

Add Core and Windows console-runner coverage, live diagnostics, an ARIA fixture, and evidence-based documentation.

### Phase 4: Build and Verification

**Status:** complete

Build and publish natively on Windows, inspect Chrome's UIA tree and standalone artifact, exercise automated, live, runtime/tray, and physical acceptance paths, and document explicit environment limitations.

## Constraints

- Preserve the existing Chrome extension and any unrelated user changes.
- Target ordinary Windows 10/11 desktop Chrome without administrator access.
- Fail closed on ambiguous UI targets; never act on blank strip space, close buttons, New Tab, or non-Chrome windows.
- Keep the application local-only and network-free.
- The current host is native Windows 11 at 100% display scaling (96 DPI) with one monitor; unavailable hardware/session cases must remain explicitly unclaimed.

## Decisions

- Add the helper as a separate project rather than deleting the existing extension.
- Prefer semantic accessibility hit-testing over coordinate-only tab-strip guesses.
- Revalidate the same tab after the second click before injecting any close action.
- Reject UI Automation tab controls whose ancestor chain enters a webpage `Document`, and apply a DPI-aware native top-chrome geometry gate before UI Automation.
- Match complete left-button down/up pairs so timing follows Windows' down-to-down double-click model and drags/unmatched input reset the sequence.
- Recheck age, cursor position, window root, button/modifier state, target bounds, and foreground ownership immediately before `SendInput`.
- Target `.NET 10` and `net10.0-windows`; publish a self-contained `win-x64` single-file executable.
- Use managed Windows UI Automation from the official Windows Desktop reference pack; fail closed if Chrome does not expose a semantic tab.
- Keep the low-level hook callback minimal and run UI Automation on a dedicated MTA worker thread.
- Close the exact tab by re-hit-testing the current pointer and injecting Chrome's native middle-click gesture rather than synthesizing `Ctrl+W`.
- Bind down/up events to event-time root HWNDs and 64-bit monotonic time; reject a full 32-bit timestamp cycle.
- Capture the semantic down target while the hook-observed left press remains active, then require the separately hit-tested up target and final target to keep the same runtime ID/root.
- Invalidate work on pointer revision, input sequence, pause/resume, queue overflow, desktop switch, and disposal.
- Allowlist current Chrome browser-strip class name `Tab`; reject observed generic `TabbedPaneTab` controls and unknown future classes.
- Attempt one marked middle-button-up recovery if native batching inserts only the down event.
- Warm UI Automation on the MTA worker so the strict held-button capture remains fail-closed without paying the full cold-start cost on the first gesture.

## Historical Errors Encountered

| Error | Attempt | Resolution |
|---|---:|---|
| `apply_patch` rejected simultaneous delete/add operations for `task_plan.md`. | 1 | Replaced the existing content with a single update operation. |
| `dotnet` is not installed in the macOS workspace. | 1 | Use an official portable SDK in a temporary directory for restore/build/test without changing the user's system installation. |
| Cross-build could not resolve explicit `UIAutomationClient`, `UIAutomationTypes`, and `WindowsBase` references from a WinForms-only project. | 1 | Enable the Windows Desktop WPF reference surface, which supplies managed UI Automation types, and remove redundant bare references. |
| A direct reference-pack/package workaround duplicated the SDK's implicit download and then conflicted on `WindowsBase` versions. | 2 | Retain the supported WPF reference surface and bundle native libraries into the self-extracting single file. |
| A publish with `--no-restore` lacked the `win-x64` assets target. | 1 | Let the publish-profile invocation restore its RID-specific runtime assets. |

## Prior Completed Work

The repository-specific `AGENTS.md` contributor guide was created and validated in the preceding task.

## Current Verification Boundary

Native Release build, 19 Core tests, 4 Windows tests, live UIA checks, synthetic rejection, tray lifecycle, single-instance behavior, network inspection, and standalone packaging are verified. The full exercised physical matrix passes at 100% scaling, and the targeted 150% and 200% matrices pass; Chrome was measured at `144` DPI for 150% and independently confirmed restored to `96` DPI afterward. Mixed-DPI/negative-coordinate monitor coverage remains explicitly unclaimed on the one-monitor host. Clean-account/no-installed-runtime coverage remains explicitly unclaimed because Windows Sandbox is unavailable and no alternate account was provisioned. These are accepted environment limitations; Phase 4 is complete.
