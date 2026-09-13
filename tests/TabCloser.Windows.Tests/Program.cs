using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;
using TabCloser.Windows;
using TabCloser.Windows.Diagnostics;
using TabCloser.Windows.Input;
using TabCloser.Windows.Interop;

namespace TabCloser.Windows.Tests;

internal static class Program
{
    private const string RestoreRequestArgument = "--request-tray-icon-restore";

    public static int Main(string[] args)
    {
        if (args is [RestoreRequestArgument, string restoreName])
        {
            return RequestTrayIconRestore(restoreName);
        }

        (string Name, Action Test)[] tests =
        [
            (nameof(CompleteBatchSucceeds), CompleteBatchSucceeds),
            (nameof(ZeroInputsNeedsNoRecovery), ZeroInputsNeedsNoRecovery),
            (nameof(PartialBatchReleasesMiddleButton), PartialBatchReleasesMiddleButton),
            (nameof(FailedRecoveryDoesNotLoop), FailedRecoveryDoesNotLoop),
            (nameof(SecondaryInstanceRequestsTrayIconRestore), SecondaryInstanceRequestsTrayIconRestore),
            (nameof(StartupLaunchArgumentIsRecognized), StartupLaunchArgumentIsRecognized),
            (nameof(LaunchPolicyCoversPrimaryAndSecondaryStarts), LaunchPolicyCoversPrimaryAndSecondaryStarts),
            (nameof(StartupCommandIncludesMarker), StartupCommandIncludesMarker),
            (nameof(TrayIconHiddenStatePersists), TrayIconHiddenStatePersists),
            (nameof(DiagnosticsDoNotChangePartialInputRecovery), DiagnosticsDoNotChangePartialInputRecovery),
            (nameof(DiagnosticSnapshotsAreIndependentAndThreadSafe), DiagnosticSnapshotsAreIndependentAndThreadSafe),
            (nameof(DiagnosticErrorsExcludePrivateMessages), DiagnosticErrorsExcludePrivateMessages),
            (nameof(DiagnosticRecorderFlushesReadableLifecycle), DiagnosticRecorderFlushesReadableLifecycle),
            (nameof(DiagnosticSendReportsPreservePrimaryAndRecoveryErrors), DiagnosticSendReportsPreservePrimaryAndRecoveryErrors),
            (nameof(DiagnosticSuccessfulSendsIgnoreStaleErrors), DiagnosticSuccessfulSendsIgnoreStaleErrors),
            (nameof(DesktopDiagnosticsDoNotChangeTheCallingDesktop), DesktopDiagnosticsDoNotChangeTheCallingDesktop),
            (nameof(WorkerRestartRequiresDiagnosticsAndStartedWorker), WorkerRestartRequiresDiagnosticsAndStartedWorker),
            (nameof(WorkerRestartWaitsForExitAndResetsBeforeReplacement), WorkerRestartWaitsForExitAndResetsBeforeReplacement),
            (nameof(WorkerRestartTimeoutNeverOverlapsWorkers), WorkerRestartTimeoutNeverOverlapsWorkers),
            (nameof(WorkerRestartRejectsConcurrentRequests), WorkerRestartRejectsConcurrentRequests),
            (nameof(WorkerRestartCannotResurrectDisposedWorker), WorkerRestartCannotResurrectDisposedWorker),
            (nameof(WorkerRestartResetFailureStaysSuspended), WorkerRestartResetFailureStaysSuspended),
            (nameof(WorkerLifecycleReportsAreBounded), WorkerLifecycleReportsAreBounded),
            (nameof(RecoveryRequiresTwoRecentDenials), RecoveryRequiresTwoRecentDenials),
            (nameof(RecoveryRejectsOtherErrorsAndPartialSends), RecoveryRejectsOtherErrorsAndPartialSends),
            (nameof(RecoverySuccessCancelsPendingRequest), RecoverySuccessCancelsPendingRequest),
            (nameof(RecoveryCannotLoopWithoutSuccess), RecoveryCannotLoopWithoutSuccess),
            (nameof(RecoveryRearmStillHonorsCooldown), RecoveryRearmStillHonorsCooldown),
            (nameof(RecoveryExpiresPendingEvidence), RecoveryExpiresPendingEvidence),
            (nameof(RecoveryRequestIsConsumedOnlyOnce), RecoveryRequestIsConsumedOnlyOnce),
            (nameof(ProductionSendCapturesErrorWithoutDiagnostics), ProductionSendCapturesErrorWithoutDiagnostics),
            (nameof(AutomaticRecoveryWorksWithoutDiagnosticsAndNeverReplays), AutomaticRecoveryWorksWithoutDiagnosticsAndNeverReplays),
        ];

        try
        {
            foreach ((string name, Action test) in tests)
            {
                test();
                Console.WriteLine($"PASS {name}");
            }

            Console.WriteLine($"{tests.Length} tests passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {exception.Message}");
            return 1;
        }
    }

    private static void CompleteBatchSucceeds()
    {
        List<NativeMethods.NativeInput[]> calls = [];
        bool result = MiddleClickInjector.SendPreparedMiddleClick(
            MiddleClickInjector.CreateMiddleClickInputs(),
            inputs =>
            {
                calls.Add(inputs.ToArray());
                return 2;
            });

        True(result);
        Equal(1, calls.Count);
        AssertMiddleClick(calls[0]);
    }

    private static void ZeroInputsNeedsNoRecovery()
    {
        List<NativeMethods.NativeInput[]> calls = [];
        bool result = MiddleClickInjector.SendPreparedMiddleClick(
            MiddleClickInjector.CreateMiddleClickInputs(),
            inputs =>
            {
                calls.Add(inputs.ToArray());
                return 0;
            });

        False(result);
        Equal(1, calls.Count);
        AssertMiddleClick(calls[0]);
    }

    private static void PartialBatchReleasesMiddleButton()
    {
        List<NativeMethods.NativeInput[]> calls = [];
        Queue<uint> results = new([1, 1]);
        bool result = MiddleClickInjector.SendPreparedMiddleClick(
            MiddleClickInjector.CreateMiddleClickInputs(),
            inputs =>
            {
                calls.Add(inputs.ToArray());
                return results.Dequeue();
            });

        False(result);
        Equal(2, calls.Count);
        AssertMiddleClick(calls[0]);
        AssertMiddleUp(calls[1]);
    }

    private static void FailedRecoveryDoesNotLoop()
    {
        int callCount = 0;
        Queue<uint> results = new([1, 0]);
        bool result = MiddleClickInjector.SendPreparedMiddleClick(
            MiddleClickInjector.CreateMiddleClickInputs(),
            _ =>
            {
                callCount++;
                return results.Dequeue();
            });

        False(result);
        Equal(2, callCount);
        Equal(0, results.Count);
    }

    private static void SecondaryInstanceRequestsTrayIconRestore()
    {
        string name = $"Local\\TabCloser.Tests.{Guid.NewGuid():N}";

        using SingleInstance primary = new(name);
        True(primary.IsPrimary);
        False(primary.ConsumeTrayIconRestoreRequest());

        string executablePath = Environment.ProcessPath ??
            throw new InvalidOperationException("The test executable path is unavailable.");
        ProcessStartInfo startInfo = new(executablePath)
        {
            CreateNoWindow = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add(RestoreRequestArgument);
        startInfo.ArgumentList.Add(name);

        using Process secondary = Process.Start(startInfo) ??
            throw new InvalidOperationException("The secondary test process did not start.");
        if (!secondary.WaitForExit(5_000))
        {
            secondary.Kill(entireProcessTree: true);
            secondary.WaitForExit();
            throw new InvalidOperationException(
                "The secondary test process did not exit promptly.");
        }

        Equal(0, secondary.ExitCode);
        True(primary.ConsumeTrayIconRestoreRequest());
        False(primary.ConsumeTrayIconRestoreRequest());
    }

    private static int RequestTrayIconRestore(string name)
    {
        using SingleInstance instance = new(name);
        if (instance.IsPrimary)
        {
            return 2;
        }

        instance.RequestTrayIconRestore();
        return 0;
    }

    private static void StartupLaunchArgumentIsRecognized()
    {
        True(StartupRegistration.IsStartupLaunch(["--startup"]));
        True(StartupRegistration.IsStartupLaunch(["--STARTUP"]));
        False(StartupRegistration.IsStartupLaunch([]));
        False(StartupRegistration.IsStartupLaunch(["--unrelated"]));
    }

    private static void LaunchPolicyCoversPrimaryAndSecondaryStarts()
    {
        Equal(LaunchAction.RunVisible, LaunchPolicy.Decide(
            isPrimary: true,
            startedWithWindows: false));
        Equal(LaunchAction.RunUsingSavedVisibility, LaunchPolicy.Decide(
            isPrimary: true,
            startedWithWindows: true));
        Equal(LaunchAction.RequestTrayIconRestore, LaunchPolicy.Decide(
            isPrimary: false,
            startedWithWindows: false));
        Equal(LaunchAction.Exit, LaunchPolicy.Decide(
            isPrimary: false,
            startedWithWindows: true));
    }

    private static void StartupCommandIncludesMarker()
    {
        const string executablePath = @"C:\Apps With Spaces\TabCloser.exe";
        Equal(
            "\"C:\\Apps With Spaces\\TabCloser.exe\" --startup",
            StartupRegistration.BuildCommand(executablePath));
        True(StartupRegistration.IsCommandForExecutable(
            $"\"{executablePath}\"",
            executablePath));
        True(StartupRegistration.IsCommandForExecutable(
            StartupRegistration.BuildCommand(executablePath),
            executablePath));
        False(StartupRegistration.IsCommandForExecutable(
            "\"C:\\Other\\TabCloser.exe\" --startup",
            executablePath));
    }

    private static void TrayIconHiddenStatePersists()
    {
        string keyPath = $@"Software\TabCloser.Tests.{Guid.NewGuid():N}";

        try
        {
            False(TrayIconSettings.IsHidden(keyPath));
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(
                       keyPath,
                       writable: true))
            {
                key.SetValue("TrayIconHidden", "invalid", RegistryValueKind.String);
            }

            False(TrayIconSettings.IsHidden(keyPath));
            TrayIconSettings.SetHidden(hidden: true, keyPath);
            True(TrayIconSettings.IsHidden(keyPath));
            TrayIconSettings.SetHidden(hidden: false, keyPath);
            False(TrayIconSettings.IsHidden(keyPath));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }

    private static void DiagnosticsDoNotChangePartialInputRecovery()
    {
        RuntimeDiagnostics diagnostics = new();
        List<NativeMethods.NativeInput[]> calls = [];
        Queue<uint> results = new([1, 0]);
        bool result = MiddleClickInjector.SendPreparedMiddleClick(
            MiddleClickInjector.CreateMiddleClickInputs(),
            inputs =>
            {
                calls.Add(inputs.ToArray());
                return results.Dequeue();
            },
            diagnostics);

        False(result);
        Equal(2, calls.Count);
        AssertMiddleClick(calls[0]);
        AssertMiddleUp(calls[1]);
        DiagnosticSnapshot snapshot = diagnostics.Snapshot();
        Equal(2L, snapshot.Counters[nameof(DiagnosticCounter.SendInputCalls)]);
        Equal(3L, snapshot.Counters[nameof(DiagnosticCounter.SendInputRequested)]);
        Equal(1L, snapshot.Counters[nameof(DiagnosticCounter.SendInputInserted)]);
        Equal(1L, snapshot.Counters[nameof(DiagnosticCounter.SendInputPartialResults)]);
        Equal(1L, snapshot.Counters[nameof(DiagnosticCounter.SendInputZeroResults)]);
    }

    private static void DiagnosticSnapshotsAreIndependentAndThreadSafe()
    {
        RuntimeDiagnostics diagnostics = new();
        DiagnosticSnapshot before = diagnostics.Snapshot();
        Parallel.For(0, 10000, _ => diagnostics.Count(DiagnosticCounter.HookEntered));
        DiagnosticSnapshot after = diagnostics.Snapshot();
        Equal(0L, before.Counters[nameof(DiagnosticCounter.HookEntered)]);
        Equal(10000L, after.Counters[nameof(DiagnosticCounter.HookEntered)]);
        Equal<long?>(null, before.LastUiPulseAgeMilliseconds);
        diagnostics.UiPulse();
        True(diagnostics.Snapshot().LastUiPulseAgeMilliseconds is >= 0);
    }

    private static void DiagnosticErrorsExcludePrivateMessages()
    {
        RuntimeDiagnostics diagnostics = new();
        diagnostics.Error("HitTest", new InvalidOperationException(
            "private-title https://private.example/path C:\\private-user\\secret"));
        string json = JsonSerializer.Serialize(diagnostics.Snapshot());
        True(json.Contains("InvalidOperationException", StringComparison.Ordinal));
        False(json.Contains("private", StringComparison.Ordinal));
        False(json.Contains("secret", StringComparison.Ordinal));
    }

    private static void DiagnosticRecorderFlushesReadableLifecycle()
    {
        string directory = Path.Combine(Path.GetTempPath(), $"TabCloser.Diagnostics.Tests.{Guid.NewGuid():N}");
        string? logPath = null;
        try
        {
            RuntimeDiagnostics diagnostics = new();
            using (DiagnosticRecorder recorder = new(diagnostics, directory))
            {
                logPath = recorder.LogPath;
                diagnostics.Stage("HitTest.UIA");
                diagnostics.Count(DiagnosticCounter.HitTests);
                diagnostics.UiPulse();
                recorder.SampleNow();

                // The reader must see a complete sample while the writer remains open.
                using StreamReader reader = new(new FileStream(
                    logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
                string[] lines = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries);
                True(lines.Length >= 2);
                using JsonDocument sample = JsonDocument.Parse(lines[1]);
                Equal("Sample", sample.RootElement.GetProperty("Kind").GetString());
                JsonElement runtime = sample.RootElement.GetProperty("Runtime");
                Equal("HitTest.UIA", runtime.GetProperty("WorkerStage").GetString());
                Equal(1L, runtime.GetProperty("Counters").GetProperty("HitTests").GetInt64());
                False(sample.RootElement.TryGetProperty("CursorX", out _));
                False(sample.RootElement.TryGetProperty("WindowTitle", out _));

                // Multiple attempts between samples must survive shutdown as
                // separate records, including their independently captured errors.
                diagnostics.RecordSendResult(2, 0, 5, null, null);
                diagnostics.RecordSendResult(2, 0, 170, null, null);
                diagnostics.RecordWorkerLifecycle("RestartRequested", 1, 123);
            }

            string[] completedLines = File.ReadAllLines(logPath);
            using JsonDocument last = JsonDocument.Parse(completedLines[^1]);
            Equal("Stop", last.RootElement.GetProperty("Kind").GetString());
            List<int> sendErrors = [];
            foreach (string line in completedLines)
            {
                using JsonDocument record = JsonDocument.Parse(line);
                if (record.RootElement.GetProperty("Kind").GetString() == "SendInput")
                {
                    sendErrors.Add(record.RootElement.GetProperty("Result")
                        .GetProperty("Win32Error").GetInt32());
                }
            }

            Equal(2, sendErrors.Count);
            Equal(5, sendErrors[0]);
            Equal(170, sendErrors[1]);
            using JsonDocument start = JsonDocument.Parse(completedLines[0]);
            Equal(4, start.RootElement.GetProperty("SchemaVersion").GetInt32());
            True(completedLines.Any(line => line.Contains("WorkerLifecycle", StringComparison.Ordinal)));
        }
        finally
        {
            if (logPath is not null)
            {
                File.Delete(logPath);
            }

            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: false);
            }
        }
    }

    private static void DiagnosticSendReportsPreservePrimaryAndRecoveryErrors()
    {
        RuntimeDiagnostics diagnostics = new();
        int calls = 0;
        bool inserted = MiddleClickInjector.SendPreparedMiddleClick(
            MiddleClickInjector.CreateMiddleClickInputs(),
            inputs =>
            {
                calls++;
                if (calls == 1)
                {
                    AssertMiddleClick(inputs);
                    Marshal.SetLastPInvokeError(5);
                    return 1;
                }

                AssertMiddleUp(inputs);
                Marshal.SetLastPInvokeError(170);
                return 0;
            },
            diagnostics);

        False(inserted);
        Equal(2, calls);
        InputSendReport report = diagnostics.DrainSendReports().Single();
        Equal(1u, report.Inserted);
        Equal<int?>(5, report.Win32Error);
        Equal<uint?>(0, report.RecoveryInserted);
        Equal<int?>(170, report.RecoveryWin32Error);
        Equal(NativeMethods.GetCurrentThreadId(), report.DesktopAfterSend!.SenderNativeThreadId);
    }

    private static void DiagnosticSuccessfulSendsIgnoreStaleErrors()
    {
        RuntimeDiagnostics diagnostics = new();
        diagnostics.WorkerStarted(2);
        True(MiddleClickInjector.SendPreparedMiddleClick(
            MiddleClickInjector.CreateMiddleClickInputs(),
            _ =>
            {
                Marshal.SetLastPInvokeError(5);
                return 2;
            },
            diagnostics));

        InputSendReport report = diagnostics.DrainSendReports().Single();
        Equal(2u, report.Inserted);
        Equal(2, report.WorkerGeneration);
        Equal<int?>(null, report.Win32Error);
        Equal<uint?>(null, report.RecoveryInserted);
        Equal<int?>(null, report.RecoveryWin32Error);
    }

    private static void DesktopDiagnosticsDoNotChangeTheCallingDesktop()
    {
        uint threadId = NativeMethods.GetCurrentThreadId();
        nint before = NativeMethods.GetThreadDesktop(threadId);
        InputDesktopSnapshot snapshot = InputDesktopDiagnostics.Capture();
        Equal(threadId, snapshot.SenderNativeThreadId);
        Equal(before, NativeMethods.GetThreadDesktop(threadId));
        Equal("Other", InputDesktopDiagnostics.CategorizeName("private-desktop-name"));
        Equal("Unavailable", InputDesktopDiagnostics.CategorizeName(null));
        Equal("Default", InputDesktopDiagnostics.CategorizeName("DEFAULT"));
        Console.WriteLine($"DESKTOP PROBE {JsonSerializer.Serialize(snapshot)}");
    }

    private static void WorkerRestartRequiresDiagnosticsAndStartedWorker()
    {
        using TabCloseWorker unstarted = new(_ => { }, () => { }, () => { }, new());
        Equal(WorkerRestartResult.Unavailable, unstarted.RestartForDiagnosticsAsync().GetAwaiter().GetResult());
        using TabCloseWorker normal = new(token => token.WaitHandle.WaitOne(), () => { }, () => { }, null);
        normal.Start();
        Equal(WorkerRestartResult.Unavailable, normal.RestartForDiagnosticsAsync().GetAwaiter().GetResult());
        False(normal.IsSuspended);
    }

    private static void WorkerRestartWaitsForExitAndResetsBeforeReplacement()
    {
        RuntimeDiagnostics diagnostics = new();
        using ManualResetEventSlim firstReady = new();
        using ManualResetEventSlim secondReady = new();
        int runs = 0;
        int active = 0;
        int resets = 0;
        int invalidations = 0;
        bool resetWhileSuspended = false;
        bool oldStoppedBeforeReset = false;
        bool secondSawReset = false;
        TabCloseWorker? worker = null;
        using (worker = new TabCloseWorker(token =>
        {
            Interlocked.Increment(ref active);
            int run = Interlocked.Increment(ref runs);
            if (run == 1)
            {
                firstReady.Set();
            }
            else
            {
                secondSawReset = Volatile.Read(ref resets) == 1;
                secondReady.Set();
            }

            token.WaitHandle.WaitOne();
            Interlocked.Decrement(ref active);
        }, () => Interlocked.Increment(ref invalidations), () =>
        {
            resetWhileSuspended = worker!.IsSuspended;
            oldStoppedBeforeReset = Volatile.Read(ref active) == 0;
            Interlocked.Increment(ref resets);
        }, diagnostics))
        {
            worker.Start();
            True(firstReady.Wait(TimeSpan.FromSeconds(3)));
            Equal(WorkerRestartResult.Restarted, worker.RestartForDiagnosticsAsync().GetAwaiter().GetResult());
            True(secondReady.Wait(TimeSpan.FromSeconds(3)));
            True(resetWhileSuspended);
            True(oldStoppedBeforeReset);
            True(secondSawReset);
            Equal(1, invalidations);
            Equal(1, active);
            False(worker.IsSuspended);
            Equal(2, diagnostics.Snapshot().WorkerGeneration);
            Equal(0L, diagnostics.Snapshot().Counters[nameof(DiagnosticCounter.HookInstalled)]);
            WorkerLifecycleReport[] reports = diagnostics.DrainWorkerReports().ToArray();
            Equal(2, reports.Count(report => report.Event == "Started"));
            True(Array.FindIndex(reports, report => report.Event == "Stopped" && report.Generation == 1)
                < Array.FindIndex(reports, report => report.Event == "Started" && report.Generation == 2));
            True(reports.Where(report => report.Event == "Started").All(report => report.NativeThreadId > 0));
        }
    }

    private static void WorkerRestartTimeoutNeverOverlapsWorkers()
    {
        using ManualResetEventSlim releaseOld = new();
        using ManualResetEventSlim firstReady = new();
        using ManualResetEventSlim oldFinished = new();
        int runs = 0;
        int resets = 0;
        using TabCloseWorker worker = new(token =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                firstReady.Set();
                releaseOld.Wait(); // Simulates an uncooperative UIA call.
                oldFinished.Set();
            }
            else
            {
                token.WaitHandle.WaitOne();
            }
        }, () => { }, () => Interlocked.Increment(ref resets), new(), TimeSpan.FromMilliseconds(50));
        try
        {
            worker.Start();
            True(firstReady.Wait(TimeSpan.FromSeconds(3)));
            Equal(WorkerRestartResult.TimedOut, worker.RestartForDiagnosticsAsync().GetAwaiter().GetResult());
            Equal(1, runs);
            Equal(0, resets);
            True(worker.IsSuspended);
            releaseOld.Set();
            True(oldFinished.Wait(TimeSpan.FromSeconds(3)));
            True(worker.IsSuspended); // Late exit must not trigger an automatic restart.
            Equal(WorkerRestartResult.Restarted, worker.RestartForDiagnosticsAsync().GetAwaiter().GetResult());
            Equal(1, resets);
            False(worker.IsSuspended);
        }
        finally
        {
            releaseOld.Set();
        }
    }

    private static void WorkerRestartRejectsConcurrentRequests()
    {
        using ManualResetEventSlim releaseOld = new();
        using ManualResetEventSlim ready = new();
        int runs = 0;
        using TabCloseWorker worker = new(token =>
        {
            if (Interlocked.Increment(ref runs) == 1)
            {
                ready.Set();
                releaseOld.Wait();
            }
            else
            {
                token.WaitHandle.WaitOne();
            }
        }, () => { }, () => { }, new());
        try
        {
            worker.Start();
            True(ready.Wait(TimeSpan.FromSeconds(3)));
            Task<WorkerRestartResult> restart = worker.RestartForDiagnosticsAsync();
            Equal(WorkerRestartResult.Busy, worker.RestartForDiagnosticsAsync().GetAwaiter().GetResult());
            releaseOld.Set();
            Equal(WorkerRestartResult.Restarted, restart.GetAwaiter().GetResult());
        }
        finally
        {
            releaseOld.Set();
        }
    }

    private static void WorkerRestartCannotResurrectDisposedWorker()
    {
        using ManualResetEventSlim releaseOld = new();
        using ManualResetEventSlim ready = new();
        using ManualResetEventSlim finished = new();
        int runs = 0;
        using TabCloseWorker worker = new(_ =>
        {
            Interlocked.Increment(ref runs);
            ready.Set();
            releaseOld.Wait();
            finished.Set();
        }, () => { }, () => { }, new(), TimeSpan.FromMilliseconds(50));
        try
        {
            worker.Start();
            True(ready.Wait(TimeSpan.FromSeconds(3)));
            Task<WorkerRestartResult> restart = worker.RestartForDiagnosticsAsync();
            worker.Dispose();
            releaseOld.Set();
            Equal(WorkerRestartResult.Unavailable, restart.GetAwaiter().GetResult());
            True(finished.Wait(TimeSpan.FromSeconds(3)));
            Equal(1, runs);
            True(worker.IsSuspended);
            Equal(WorkerRestartResult.Unavailable, worker.RestartForDiagnosticsAsync().GetAwaiter().GetResult());
        }
        finally
        {
            releaseOld.Set();
        }
    }

    private static void WorkerRestartResetFailureStaysSuspended()
    {
        int runs = 0;
        using ManualResetEventSlim ready = new();
        RuntimeDiagnostics diagnostics = new();
        using TabCloseWorker worker = new(token =>
        {
            Interlocked.Increment(ref runs);
            ready.Set();
            token.WaitHandle.WaitOne();
        }, () => { }, () => throw new InvalidOperationException("private"), diagnostics);
        worker.Start();
        True(ready.Wait(TimeSpan.FromSeconds(3)));
        Equal(WorkerRestartResult.Failed, worker.RestartForDiagnosticsAsync().GetAwaiter().GetResult());
        Equal(1, runs);
        True(worker.IsSuspended);
        Equal("WorkerRestart", diagnostics.Snapshot().LastErrorSource);
        False(JsonSerializer.Serialize(diagnostics.Snapshot()).Contains("private", StringComparison.Ordinal));
    }

    private static void WorkerLifecycleReportsAreBounded()
    {
        RuntimeDiagnostics diagnostics = new();
        for (int i = 0; i < 129; i++)
        {
            diagnostics.RecordWorkerLifecycle("RestartRequested", 1, 123);
        }

        Equal(128, diagnostics.DrainWorkerReports().Count);
        Equal(1L, diagnostics.Snapshot().Counters[nameof(DiagnosticCounter.WorkerReportsDropped)]);
        Equal(0, diagnostics.DrainWorkerReports().Count);
    }

    private static readonly InputSendOutcome Denied = new(2, 0, 5);
    private static readonly InputSendOutcome Sent = new(2, 2, null);

    private static void RecoveryRequiresTwoRecentDenials()
    {
        InputRecoveryPolicy policy = new();
        policy.Observe(Denied, 100);
        False(policy.TryTakeRequest(100));
        policy.Observe(Denied, 10_101);
        False(policy.TryTakeRequest(10_101));
        policy.Observe(Denied, 20_101); // Exact window boundary is accepted.
        True(policy.TryTakeRequest(20_101));
    }

    private static void RecoveryRejectsOtherErrorsAndPartialSends()
    {
        InputSendOutcome[] rejected = [new(2, 0, 0), new(2, 0, null), new(2, 0, 170),
            new(2, 1, 5), new(1, 0, 5), new(0, 0, 5)];
        foreach (InputSendOutcome outcome in rejected)
        {
            InputRecoveryPolicy policy = new();
            policy.Observe(Denied, 100);
            policy.Observe(outcome, 200);
            policy.Observe(Denied, 300);
            False(policy.TryTakeRequest(300));
        }
    }

    private static void RecoverySuccessCancelsPendingRequest()
    {
        InputRecoveryPolicy policy = new();
        policy.Observe(Denied, 100);
        policy.Observe(Denied, 200);
        policy.Observe(Sent, 300);
        False(policy.TryTakeRequest(300));
    }

    private static void RecoveryCannotLoopWithoutSuccess()
    {
        InputRecoveryPolicy policy = new();
        policy.Observe(Denied, 100);
        policy.Observe(Denied, 200);
        True(policy.TryTakeRequest(200));
        for (long now = 100_000; now < 1_000_000; now += 1000)
        {
            policy.ClearEvidence(); // Pause/desktop switches do not grant a new budget.
            policy.Observe(Denied, now);
            policy.Observe(Denied, now + 1);
            False(policy.TryTakeRequest(now + 1));
        }
    }

    private static void RecoveryRearmStillHonorsCooldown()
    {
        InputRecoveryPolicy policy = new();
        policy.Observe(Denied, 100);
        policy.Observe(Denied, 200);
        True(policy.TryTakeRequest(200));
        policy.Observe(Sent, 300);
        policy.Observe(Denied, 400);
        policy.Observe(Denied, 500);
        False(policy.TryTakeRequest(500));
        False(policy.TryTakeRequest(60_200)); // No deferred request at cooldown expiry.
        policy.Observe(Denied, 60_200);
        policy.Observe(Denied, 60_201);
        True(policy.TryTakeRequest(60_201));
    }

    private static void RecoveryExpiresPendingEvidence()
    {
        InputRecoveryPolicy policy = new();
        policy.Observe(Denied, 100);
        policy.Observe(Denied, 200);
        False(policy.TryTakeRequest(10_201));
        policy.Observe(Denied, 20_000);
        policy.Observe(Denied, 20_001);
        policy.ClearEvidence();
        False(policy.TryTakeRequest(20_002));
        policy.Observe(Denied, 30_000);
        policy.Observe(Denied, 29_999); // Backwards timestamps cannot complete the pair.
        False(policy.TryTakeRequest(30_000));
    }

    private static void RecoveryRequestIsConsumedOnlyOnce()
    {
        InputRecoveryPolicy policy = new();
        policy.Observe(Denied, 100);
        policy.Observe(Denied, 200);
        int requests = 0;
        Parallel.For(0, 100, _ =>
        {
            if (policy.TryTakeRequest(201))
            {
                Interlocked.Increment(ref requests);
            }
        });
        Equal(1, requests);
    }

    private static void ProductionSendCapturesErrorWithoutDiagnostics()
    {
        InputSendOutcome? observed = null;
        False(MiddleClickInjector.SendPreparedMiddleClick(
            MiddleClickInjector.CreateMiddleClickInputs(), _ =>
            {
                Marshal.SetLastPInvokeError(5);
                return 0;
            }, onSendCompleted: outcome => observed = outcome));
        Equal<InputSendOutcome?>(Denied, observed);
        True(MiddleClickInjector.SendPreparedMiddleClick(
            MiddleClickInjector.CreateMiddleClickInputs(), _ =>
            {
                Marshal.SetLastPInvokeError(5);
                return 2;
            }, onSendCompleted: outcome => observed = outcome));
        Equal<InputSendOutcome?>(Sent, observed);
    }

    private static void AutomaticRecoveryWorksWithoutDiagnosticsAndNeverReplays()
    {
        InputRecoveryPolicy policy = new();
        using System.Collections.Concurrent.BlockingCollection<int> gestures = new();
        using ManualResetEventSlim failuresRecorded = new();
        using ManualResetEventSlim replacementReady = new();
        using ManualResetEventSlim successRecorded = new();
        int generation = 0;
        int sends = 0;
        int discarded = 0;
        using TabCloseWorker worker = new(token =>
        {
            int current = Interlocked.Increment(ref generation);
            if (current == 2)
            {
                replacementReady.Set();
            }

            foreach (int gesture in gestures.GetConsumingEnumerable(token))
            {
                MiddleClickInjector.SendPreparedMiddleClick(MiddleClickInjector.CreateMiddleClickInputs(), _ =>
                {
                    Interlocked.Increment(ref sends);
                    Marshal.SetLastPInvokeError(5);
                    return current == 1 ? 0u : 2u;
                }, onSendCompleted: outcome => policy.Observe(outcome, 100 + gesture));
                if (current == 1 && sends == 2)
                {
                    failuresRecorded.Set();
                    token.WaitHandle.WaitOne();
                    break;
                }
                if (current == 2)
                {
                    successRecorded.Set();
                }
            }
        }, policy.ClearEvidence, () =>
        {
            while (gestures.TryTake(out _))
            {
                discarded++;
            }
        }, diagnostics: null);

        worker.Start();
        gestures.Add(1);
        gestures.Add(2);
        True(failuresRecorded.Wait(TimeSpan.FromSeconds(3)));
        gestures.Add(3); // Pending input must be discarded, not retried on replacement.
        True(policy.TryTakeRequest(103));
        Equal(WorkerRestartResult.Restarted, worker.RestartAutomaticallyAsync().GetAwaiter().GetResult());
        True(replacementReady.Wait(TimeSpan.FromSeconds(3)));
        Equal(2, sends);
        Equal(1, discarded);
        gestures.Add(4); // Only a new gesture may send input after restart.
        True(successRecorded.Wait(TimeSpan.FromSeconds(3)));
        Equal(3, sends);
        False(policy.TryTakeRequest(104));
    }

    private static void AssertMiddleClick(NativeMethods.NativeInput[] inputs)
    {
        Equal(2, inputs.Length);
        Equal(NativeMethods.MouseEventMiddleDown, inputs[0].Data.Mouse.Flags);
        Equal(NativeMethods.MouseEventMiddleUp, inputs[1].Data.Mouse.Flags);
        Equal(NativeMethods.InjectionMarker, inputs[0].Data.Mouse.ExtraInfo);
        Equal(NativeMethods.InjectionMarker, inputs[1].Data.Mouse.ExtraInfo);
    }

    private static void AssertMiddleUp(NativeMethods.NativeInput[] inputs)
    {
        Equal(1, inputs.Length);
        Equal(NativeMethods.MouseEventMiddleUp, inputs[0].Data.Mouse.Flags);
        Equal(NativeMethods.InjectionMarker, inputs[0].Data.Mouse.ExtraInfo);
    }

    private static void True(bool value)
    {
        if (!value)
        {
            throw new InvalidOperationException("Expected true.");
        }
    }

    private static void False(bool value)
    {
        if (value)
        {
            throw new InvalidOperationException("Expected false.");
        }
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"Expected {expected}, but found {actual}.");
        }
    }
}
