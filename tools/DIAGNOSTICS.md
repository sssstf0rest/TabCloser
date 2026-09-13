# CS2 Exit Investigation

## Current evidence

The user reports that TabCloser works after opening Perfect World, launching CS2,
playing deathmatch, and leaving the match. It fails only after exiting CS2 itself.
Steam-only CS2 does not reproduce the issue. Enabled off/on does not restore it;
fully exiting and relaunching TabCloser does. The captured reproduction below
identifies input insertion as the failing stage.

The hook is installed once on the tray UI thread. Windows can silently remove a
low-level mouse hook after a timeout; the current app has no hook recovery. The
UIA worker can also stall or exit without removing the tray icon. Diagnostics
observe these possibilities without reinstalling hooks or changing game settings.

## Captured reproduction — 2026-09-13

Evidence: `session-20260913-030227-7816-a24170d9743e4fdf9ef91d4f371ceebb.jsonl`
in `%LOCALAPPDATA%\TabCloser\Diagnostics`, build ID
`aae51b2c-c1d5-4df8-ae9e-dec5754f95f8`. Times below are local UTC+08:00.

| Time | Observation |
| --- | --- |
| 11:04:19 and 11:05:45 | Two recognized double-clicks each inserted both middle-button events. |
| 11:06:16 | CS2 first observed running. |
| 11:06:44 and 11:06:56 | Two further double-clicks inserted both events while CS2 was running. |
| 11:07:04 | CS2 first observed no longer running (one-second sampling precision). |
| 11:07:07–11:07:52 | Nine further double-clicks reached SendInput; all nine returned zero. |

Across the failing attempts, hook callbacks continued, hit-tests completed and
accepted Chrome tab targets, and the worker continued processing events. Totals
were 13 close attempts, four complete batches inserted, nine zero results, and
zero partial results. There were no recorded hook errors, worker failures, queue
overflows, or hit-test exceptions. The maximum completed hook duration was 16 ms.
The desktop-switch counter remained at two across CS2 exit and subsequent failures;
both earlier switches preceded successful tab closes.

Live read-only token inspection after the failure found both the same Chrome
browser process (PID 26552) and TabCloser (PID 7816) at Medium integrity (`0x2000`).
The usual lower-to-higher integrity mismatch is therefore not supported by this
check. This is a post-failure process-level observation, not a historical trace of
every thread token at the time of each call.

Conclusion: the observed failure is at synthetic input insertion, not missing
double-click recognition or a stuck UIA worker. Perfect World remained running
according to the user. Anti-cheat involvement remains a hypothesis: a zero return
does not identify the blocking component, and this build did not capture the
immediate native error code. Earlier periods of absent hook callbacks recovered
before subsequent successful closes and should not be mistaken for the final
failure. Next discriminating check: exit Perfect World while retaining the failed
TabCloser process, then repeat a Chrome test close.

Follow-up: the user fully exited Perfect World without restarting TabCloser and
confirmed that closing still failed. At 11:11:48, the same diagnostic process had
recorded 14 recognized close attempts, four complete batches inserted and ten
zero results, with continuing hook/worker activity and no recorded errors. The
failure therefore persists after the platform's visible application exits; this
does not establish that all of its services or drivers have unloaded. The user
previously confirmed that fully quitting and relaunching TabCloser restores it.
The remaining investigation is the cause of rejected SendInput calls, including
immediate native error codes and thread/input-desktop state, rather than hook
reinstallation. No anti-cheat attribution or recovery fix is yet verified.

## Version 2 evidence — 2026-09-13

Session `session-20260913-032701-12636-f30df688f2574b6cb08b8bd0489e686e.jsonl`,
build `e4798ac3-e3cb-4356-9c6f-a577bdc23ffc`, captured eight successful batches
before CS2 exited. An isolated failure at 11:30:18 returned zero with error 5,
then four further attempts succeeded. CS2 first appeared stopped at 11:31:33;
four attempts at 11:31:39–11:31:44 all returned zero with error 5 (Access denied).
Times are UTC+08:00. Each send used native thread 15060, and all desktop probes
reported matching Default desktops, receiving input, with no query errors.
Hook callbacks, accepted Chrome hit-tests, and worker processing continued.

Live post-failure inspection found TabCloser PID 12636 and Chrome PID 26552 at
Medium integrity (0x2000), neither elevated nor UIAccess-enabled. The sending
thread had no impersonation token at inspection time (OpenThreadToken error
1008). These observations do not establish token state at every earlier send.
They weaken ordinary elevation/desktop-mismatch explanations but do not identify
the component denying input. No anti-cheat attribution is confirmed.

## Version 3 evidence — manual recovery confirmed

Session `session-20260913-034623-18512-2d4e3d9815754b4b84d178f7d412aedf.jsonl`,
build `76a282a3-e8ce-453c-bd9f-01230128a5a5`, recorded six successful batches on
thread 30476, then two zero/error-5 results after CS2 exited. At 11:49:48 the
user requested a worker restart: generation 1 stopped and generation 2 started
on thread 8200. Both subsequent attempts inserted two events and the user
confirmed that tabs closed. The process remained PID 18512, HookInstalled
remained one, and there were no desktop switches or Enabled toggles. The user
reports Perfect World stayed running throughout. This demonstrates recovery in
one controlled trial, not proof of a specific anti-cheat or thread-only cause.

## Run the automatic-recovery build — version 4

1. Choose **Exit** from the existing TabCloser tray menu. If hidden, launch the
   existing executable once to restore the icon, then choose Exit.
2. From the repository root, run
   `& .\artifacts\win-x64\TabCloser.exe --diagnostics`.
   Confirm the tray menu includes **Diagnostic session v4 (auto recovery)**. Where PowerShell
   scripts are allowed, `powershell -File .\tools\Start-Diagnostics.ps1` also
   checks for an already-running instance; no execution-policy change is needed
   for the direct executable command.
3. Confirm a Chrome test tab closes, then repeat the five stages above. After CS2
   exits, move the pointer and try two double-clicks on disposable Chrome tabs.
4. Make two failed double-click attempts within 10 seconds, then wait 3 seconds.
   Do **not** choose Restart worker: this test must recover automatically. Keep
   Perfect World and Chrome running and do not toggle Enabled during the comparison.
5. Make two fresh double-click attempts on disposable Chrome tabs. Wait another
   10 seconds and report whether closing recovered; keep TabCloser running.
   A recovery-stopped notification means the worker could not be replaced safely.
6. Keep the newest `session-*.jsonl` in `%LOCALAPPDATA%\TabCloser\Diagnostics`.
   For comparison, a separate session following the Steam-only route is useful.

The fix is published to the normal `artifacts/win-x64/TabCloser.exe`; automatic
recovery works with or without `--diagnostics`. That flag only enables diagnostic
recording and the manual diagnostic menu. Older v1/v2/v3 artifacts are retained.
The launcher does not change startup registration. Starting without the flag
does not create logs or run the extra desktop probes.

```powershell
dotnet publish src/TabCloser.Windows/TabCloser.Windows.csproj -p:PublishProfile=win-x64 -o artifacts/win-x64
```

## Read the evidence

Version 4 logs `SchemaVersion: 4`. Two consecutive validated two-event close
batches returning zero/error-5 within 10 seconds request automatic recovery.
Success, another error, partial insertion, pause, or interaction invalidation
clears the evidence. Pending evidence expires after 10 seconds. The existing
200-ms tray timer consumes a request once, outside the sending worker.

Only one attempt is allowed until a later complete send rearms recovery, with a
minimum 60-second gap between attempts. Neither time passing nor Enabled toggling
alone grants another attempt. Fresh evidence is required after cooldown; there
is no delayed retry loop. This is generic recovery, not game-process detection,
privilege elevation, input-method fallback, or anti-cheat modification.

The restart mechanism introduced in version 3 cooperatively cancels only
the UIA/input worker and waits up to two seconds off the tray message loop. Input
is suspended, queued clicks are discarded, and gesture state is reset before a
replacement MTA worker creates its own hit-tester. Enabled state is preserved.
The process, mouse hook, startup settings, and input-injection method are unchanged.
No failed click is replayed; the user must make a new complete gesture. If the old
worker does not exit, no replacement starts and tab closing stays suspended.
Timeout/failure leaves input suspended, without a replacement running alongside
the old worker. A successful Thread.Start is not proof that tab closing recovered.

`WorkerLifecycle` records identify Started, Stopped, RestartRequested,
RestartCompleted, RestartTimedOut, or RestartFailed transitions. Started/Stopped
include native thread IDs; all include generation and managed thread ID.
Automatic requests use AutomaticRestartRequested/Completed/TimedOut/Failed instead.
`RestartCompleted` means Thread.Start succeeded, not that initialization or tab
closing succeeded. Samples and SendInput records include `WorkerGeneration`;
`WorkerInitialDesktop` refers to that worker's initialization. Compare send results
across generations while `ProcessId` stays fixed and `HookInstalled` stays at one.
Native thread IDs may be reused by Windows, so use generation as well as IDs.
Lifecycle and send queues are drained separately; compare their timestamps, not
just file order. Each queue holds at most 128 reports and counts dropped records.

Recovery after this action supports a worker-context/lifecycle-related issue,
but does not isolate thread identity from refreshed UIA and gesture state. Continued
access denial means worker replacement was insufficient; it does not by itself
prove a process-wide block. Neither result identifies a specific anti-cheat driver.
The version 4 automatic path still requires a live Perfect World/CS2 acceptance
test; automated tests simulate denied sends without injecting input into games.

Version 2 introduced a separate
`Kind: SendInput` record for every send batch. `Win32Error` is captured immediately
after a short/zero result, before desktop probing can overwrite it; success has
null error, while zero error on a failed call means no useful reason was supplied.
`RecoveryInserted` and `RecoveryWin32Error` describe the existing one-time
middle-button release recovery separately. Desktop probing starts only after that
recovery, never between middle-down and the required middle-up.

`WorkerInitialDesktop` is captured on the UIA worker at startup.
`DesktopAfterSend` records the actual sending native thread ID, sender/input
desktop categories, each desktop's `UOI_IO` input-receiving flag, desktop-name
equality, and window-station category. Every failed desktop query retains its own
error; unavailable is not interpreted as a mismatch. Probes only read state and
close the handle opened by OpenInputDesktop. They do not switch desktops, attach
input queues, elevate, retry failed click batches, or alter anti-cheat settings.

These are **post-send** observations: a desktop can change during the probe, so
compare repeated failures with successful baseline records. A persistent sender
desktop mismatch supports a desktop-context issue. Matching desktops with a
zero/error result narrows the cause but still does not identify an anti-cheat
component. The error may also be uninformative; UIPI has no unique SendInput error.
At most 128 send reports are queued; overflow increments `SendReportsDropped`.

Each one-second sample contains cumulative counters. Compare several consecutive
samples before and after `Cs2Running` changes to false, while Chrome is foreground.
Snapshots are concurrent observations, not atomic transactions; a one-sample
difference between entry and return counts is normal.

| Sustained observation after exit | Interpretation |
| --- | --- |
| `CursorChanged` continues, UI pulses remain recent, but `HookEntered` stops increasing | TabCloser no longer receives hook callbacks; removal or filtering is suspected. This alone does not identify PAC or prove a timeout. |
| Hook activity continues, `HitTests` exceeds `HitTestsCompleted`, and `HitTest.UIA` stage age grows | Accessibility hit-testing is stalled. |
| `WorkerStage` is `Faulted`, with `WorkerFailures` increased | Background worker exited; inspect the recorded exception type. |
| Double-click recognition and close attempts increase, but `SendInputZeroResults` increases | Input insertion failed. Return values cannot establish whether anti-cheat or Windows integrity restrictions caused it. |
| `SendInputInserted` increases but tabs remain open | Windows accepted input into its stream; this does not prove Chrome received it or a tab closed. |
| UI pulse age grows | Tray message-loop delay; correlate with hook duration and callback loss. |

`MaximumHookDurationMilliseconds` includes time spent passing to downstream hooks;
it cannot attribute a delay to TabCloser alone. Lack of an observed long callback
does not rule out timeout while the installing thread was unable to run.

Logs contain timestamps, build ID, counters, stages, exception types, CS2 presence,
foreground category (Chrome/CS2/TabCloser/Other), and coarse cursor/button/modifier
state. They do not record positions, key identities, browser titles, URLs, game
memory, or exception messages. Version 2 also records desktop categories (arbitrary
desktop names are reduced to Other), native thread IDs, and API error numbers.
File writes run off the hook/UIA threads; desktop probes run on the sending worker
after input submission. Instrumentation can affect timing, so a non-reproduction
does not establish that the underlying issue is fixed. Logging is
limited to approximately 32 MiB per session; earlier logs are never overwritten.

References: [Microsoft low-level mouse hook documentation](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelmouseproc),
[Microsoft SendInput documentation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput).
