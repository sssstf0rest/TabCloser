using System.Diagnostics;
using Microsoft.Win32;
using TabCloser.Windows;
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
