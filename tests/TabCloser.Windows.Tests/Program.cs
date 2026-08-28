using System.Diagnostics;
using Microsoft.Win32;
using TabCloser.Core;
using TabCloser.Windows;
using TabCloser.Windows.Browser;
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
            (nameof(DeferredWorkerWaitsBeforeProcessing), DeferredWorkerWaitsBeforeProcessing),
            (nameof(DeferredWorkerKeepsNewestQueuedRequest), DeferredWorkerKeepsNewestQueuedRequest),
            (nameof(DeferredWorkerCancellationSkipsProcessing), DeferredWorkerCancellationSkipsProcessing),
            (nameof(DeferredWorkerRecoversAfterRequestFailure), DeferredWorkerRecoversAfterRequestFailure),
            (nameof(DeferredWorkerCreatesOneProcessorOnMtaThread), DeferredWorkerCreatesOneProcessorOnMtaThread),
            (nameof(DeferredWorkerCompletesWithoutCreatingProcessor), DeferredWorkerCompletesWithoutCreatingProcessor),
            (nameof(SupportedBrowserRootsAreClassifiedExactly), SupportedBrowserRootsAreClassifiedExactly),
            (nameof(UnsupportedBrowserRootsAreRejected), UnsupportedBrowserRootsAreRejected),
            (nameof(ExpectedTabClassesAreBrowserSpecific), ExpectedTabClassesAreBrowserSpecific),
            (nameof(BrowserGesturePoliciesAreBrowserSpecific), BrowserGesturePoliciesAreBrowserSpecific),
            (nameof(EdgeInjectionConfigurationAddsSettlingWindow), EdgeInjectionConfigurationAddsSettlingWindow),
            (nameof(PostReleaseAgeBoundariesAreExact), PostReleaseAgeBoundariesAreExact),
            (nameof(EdgeHorizontalAndUnknownTopStripsAreAccepted), EdgeHorizontalAndUnknownTopStripsAreAccepted),
            (nameof(EdgeVerticalSideAndTallStripsAreRejected), EdgeVerticalSideAndTallStripsAreRejected),
            (nameof(EdgeTabStripBoundariesAreExact), EdgeTabStripBoundariesAreExact),
            (nameof(EdgeTabMustBeContainedByTopStrip), EdgeTabMustBeContainedByTopStrip),
            (nameof(TopBandScalesWithWindowDpi), TopBandScalesWithWindowDpi),
            (nameof(InvalidEdgeRectanglesAreRejected), InvalidEdgeRectanglesAreRejected),
            (nameof(EdgeTargetIdentityIsDetectedExactly), EdgeTargetIdentityIsDetectedExactly),
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

    private static void SupportedBrowserRootsAreClassifiedExactly()
    {
        Equal<BrowserKind?>(BrowserKind.Chrome, BrowserTabPolicy.ClassifyRoot(
            "chrome",
            BrowserTabPolicy.ChromiumRootWindowClass));
        Equal<BrowserKind?>(BrowserKind.Chrome, BrowserTabPolicy.ClassifyRoot(
            "CHROME",
            BrowserTabPolicy.ChromiumRootWindowClass));
        Equal<BrowserKind?>(BrowserKind.Edge, BrowserTabPolicy.ClassifyRoot(
            "msedge",
            BrowserTabPolicy.ChromiumRootWindowClass));
        Equal<BrowserKind?>(BrowserKind.Edge, BrowserTabPolicy.ClassifyRoot(
            "MSEDGE",
            BrowserTabPolicy.ChromiumRootWindowClass));
    }

    private static void UnsupportedBrowserRootsAreRejected()
    {
        string[] unsupportedProcesses =
        [
            "chromium",
            "brave",
            "opera",
            "vivaldi",
            "msedgewebview2",
            "chrome.exe",
            "msedge.exe",
        ];
        foreach (string processName in unsupportedProcesses)
        {
            Equal<BrowserKind?>(null, BrowserTabPolicy.ClassifyRoot(
                processName,
                BrowserTabPolicy.ChromiumRootWindowClass));
        }

        Equal<BrowserKind?>(null, BrowserTabPolicy.ClassifyRoot(
            "chrome",
            "chrome_widgetwin_1"));
        Equal<BrowserKind?>(null, BrowserTabPolicy.ClassifyRoot(
            "msedge",
            "Chrome_WidgetWin_0"));
        Equal<BrowserKind?>(null, BrowserTabPolicy.ClassifyRoot(
            "msedge",
            string.Empty));
    }

    private static void ExpectedTabClassesAreBrowserSpecific()
    {
        True(BrowserTabPolicy.IsExpectedTabClass(BrowserKind.Chrome, "Tab"));
        False(BrowserTabPolicy.IsExpectedTabClass(BrowserKind.Chrome, "EdgeTab"));
        False(BrowserTabPolicy.IsExpectedTabClass(BrowserKind.Chrome, "tab"));
        False(BrowserTabPolicy.IsExpectedTabClass(
            BrowserKind.Chrome,
            "TabbedPaneTab"));

        True(BrowserTabPolicy.IsExpectedTabClass(BrowserKind.Edge, "EdgeTab"));
        False(BrowserTabPolicy.IsExpectedTabClass(BrowserKind.Edge, "Tab"));
        False(BrowserTabPolicy.IsExpectedTabClass(BrowserKind.Edge, "edgetab"));
        False(BrowserTabPolicy.IsExpectedTabClass(
            BrowserKind.Edge,
            "TabbedPaneTab"));

        True(BrowserTabPolicy.IsExpectedTabStripClass(
            BrowserKind.Chrome,
            "TabStrip"));
        True(BrowserTabPolicy.IsExpectedTabStripClass(
            BrowserKind.Edge,
            "EdgeTabStripRegionView"));
        False(BrowserTabPolicy.IsExpectedTabStripClass(
            BrowserKind.Edge,
            "edgetabstripregionview"));
        False(BrowserTabPolicy.IsExpectedTabStripClass(
            BrowserKind.Edge,
            "EdgeTabStripRegionView "));
        False(BrowserTabPolicy.IsExpectedTabStripClass(
            BrowserKind.Edge,
            string.Empty));
        False(BrowserTabPolicy.IsExpectedTabStripClass(
            BrowserKind.Edge,
            "EdgeTabContainerImpl"));
        False(BrowserTabPolicy.IsExpectedTabStripClass(
            BrowserKind.Edge,
            "EdgeVerticalTabStripRegionView"));
    }

    private static void BrowserGesturePoliciesAreBrowserSpecific()
    {
        ScreenRectangle bounds = new(0, 0, 100, 40);
        TabTarget chromeTarget = new("chrome:1:42", RootWindow: 1, bounds);
        TabTarget edgeTarget = new("edge:2:43", RootWindow: 2, bounds);

        True(BrowserGesturePolicy.IsBrowserEnabled(
            BrowserKind.Chrome,
            edgeEnabled: false));
        True(BrowserGesturePolicy.IsBrowserEnabled(
            BrowserKind.Chrome,
            edgeEnabled: true));
        False(BrowserGesturePolicy.IsBrowserEnabled(
            BrowserKind.Edge,
            edgeEnabled: false));
        True(BrowserGesturePolicy.IsBrowserEnabled(
            BrowserKind.Edge,
            edgeEnabled: true));
        True(BrowserGesturePolicy.IsTargetEnabled(chromeTarget, edgeEnabled: false));
        True(BrowserGesturePolicy.IsTargetEnabled(chromeTarget, edgeEnabled: true));
        False(BrowserGesturePolicy.IsTargetEnabled(edgeTarget, edgeEnabled: false));
        True(BrowserGesturePolicy.IsTargetEnabled(edgeTarget, edgeEnabled: true));
        False(BrowserGesturePolicy.RequiresNativeCloseSettling(chromeTarget));
        True(BrowserGesturePolicy.RequiresNativeCloseSettling(edgeTarget));
    }

    private static void EdgeInjectionConfigurationAddsSettlingWindow()
    {
        ScreenRectangle bounds = new(0, 0, 100, 40);
        TabTarget chromeTarget = new("chrome:1:42", RootWindow: 1, bounds);
        TabTarget edgeTarget = new("edge:2:43", RootWindow: 2, bounds);
        DoubleClickConfiguration configuration = new(
            MaximumDelayMilliseconds: 500,
            RectangleWidth: 8,
            RectangleHeight: 10);

        Equal(configuration, BrowserGesturePolicy.GetInjectionConfiguration(
            chromeTarget,
            configuration));

        DoubleClickConfiguration edgeConfiguration =
            BrowserGesturePolicy.GetInjectionConfiguration(edgeTarget, configuration);
        Equal<uint>(700, edgeConfiguration.MaximumDelayMilliseconds);
        Equal(configuration.RectangleWidth, edgeConfiguration.RectangleWidth);
        Equal(configuration.RectangleHeight, edgeConfiguration.RectangleHeight);

        Throws<OverflowException>(() =>
            BrowserGesturePolicy.GetInjectionConfiguration(
                edgeTarget,
                configuration with { MaximumDelayMilliseconds = uint.MaxValue }));
    }

    private static void PostReleaseAgeBoundariesAreExact()
    {
        DoubleClickConfiguration configuration = new(
            MaximumDelayMilliseconds: 700,
            RectangleWidth: 8,
            RectangleHeight: 10);

        False(BrowserGesturePolicy.IsPostReleaseAgeAllowed(-1, configuration));
        True(BrowserGesturePolicy.IsPostReleaseAgeAllowed(0, configuration));
        True(BrowserGesturePolicy.IsPostReleaseAgeAllowed(700, configuration));
        False(BrowserGesturePolicy.IsPostReleaseAgeAllowed(701, configuration));
    }

    private static void EdgeHorizontalAndUnknownTopStripsAreAccepted()
    {
        ScreenRectangle rootBounds = new(100, 200, 1700, 1100);
        ScreenRectangle topStripBounds = new(100, 200, 1600, 276);
        ScreenRectangle observedEdgeRoot = new(-8, -8, 1928, 1040);
        ScreenRectangle observedEdgeTopStrip = new(2, 0, 1782, 41);

        True(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Horizontal,
            topStripBounds,
            rootBounds,
            dpi: 96));
        True(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            topStripBounds,
            rootBounds,
            dpi: 96));
        True(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            observedEdgeTopStrip,
            observedEdgeRoot,
            dpi: 96));
    }

    private static void EdgeVerticalSideAndTallStripsAreRejected()
    {
        ScreenRectangle rootBounds = new(100, 200, 1700, 1100);
        ScreenRectangle topStripBounds = new(100, 200, 1600, 276);
        ScreenRectangle sideStripBounds = new(100, 200, 360, 1000);
        ScreenRectangle tallTopBounds = new(100, 200, 220, 295);
        ScreenRectangle belowTopBandBounds = new(100, 200, 1600, 297);

        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Vertical,
            topStripBounds,
            rootBounds,
            dpi: 96));
        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Horizontal,
            sideStripBounds,
            rootBounds,
            dpi: 96));
        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            sideStripBounds,
            rootBounds,
            dpi: 96));
        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Horizontal,
            tallTopBounds,
            rootBounds,
            dpi: 96));
        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Horizontal,
            belowTopBandBounds,
            rootBounds,
            dpi: 96));
    }

    private static void EdgeTabStripBoundariesAreExact()
    {
        ScreenRectangle rootBounds = new(0, 0, 300, 500);
        ScreenRectangle aspectRootBounds = new(0, 0, 240, 500);
        ScreenRectangle exactAspectBounds = new(0, 0, 100, 50);
        ScreenRectangle exactRootFractionBounds = new(0, 0, 100, 40);

        True(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            exactAspectBounds,
            aspectRootBounds,
            dpi: 96));
        True(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            exactRootFractionBounds,
            rootBounds,
            dpi: 96));
        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            new ScreenRectangle(0, 0, 99.9, 50),
            aspectRootBounds,
            dpi: 96));
        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            new ScreenRectangle(0, 0, 99.9, 40),
            rootBounds,
            dpi: 96));
        True(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            new ScreenRectangle(200, 0, 300, 50),
            rootBounds,
            dpi: 96));
        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            new ScreenRectangle(-0.1, 0, 100, 50),
            rootBounds,
            dpi: 96));
        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            new ScreenRectangle(200, 0, 300.1, 50),
            rootBounds,
            dpi: 96));
        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            new ScreenRectangle(0, -0.1, 100, 49.9),
            rootBounds,
            dpi: 96));
        True(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            new ScreenRectangle(0, 46, 300, 96),
            rootBounds,
            dpi: 96));
        False(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            new ScreenRectangle(0, 46, 300, 96.1),
            rootBounds,
            dpi: 96));
        True(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            new ScreenRectangle(0, 94, 300, 144),
            rootBounds,
            dpi: 144));
        True(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            new ScreenRectangle(0, 142, 300, 192),
            rootBounds,
            dpi: 192));
        True(BrowserTabPolicy.IsSupportedTabStrip(
            BrowserKind.Edge,
            TabStripOrientation.Unknown,
            exactRootFractionBounds,
            rootBounds,
            dpi: 0));
    }

    private static void EdgeTabMustBeContainedByTopStrip()
    {
        ScreenRectangle stripBounds = new(0, 0, 300, 50);

        True(BrowserTabPolicy.IsTabContainedByStrip(
            new ScreenRectangle(0, 0, 300, 50),
            stripBounds));
        True(BrowserTabPolicy.IsTabContainedByStrip(
            new ScreenRectangle(10, 5, 100, 45),
            stripBounds));
        False(BrowserTabPolicy.IsTabContainedByStrip(
            new ScreenRectangle(-0.1, 0, 100, 50),
            stripBounds));
        False(BrowserTabPolicy.IsTabContainedByStrip(
            new ScreenRectangle(0, 0, 300.1, 50),
            stripBounds));
        False(BrowserTabPolicy.IsTabContainedByStrip(
            new ScreenRectangle(0, -0.1, 100, 40),
            stripBounds));
        False(BrowserTabPolicy.IsTabContainedByStrip(
            new ScreenRectangle(0, 0, 100, 50.1),
            stripBounds));
        False(BrowserTabPolicy.IsTabContainedByStrip(
            new ScreenRectangle(0, 0, 100, 200),
            stripBounds));
        False(BrowserTabPolicy.IsTabContainedByStrip(
            new ScreenRectangle(double.NaN, 0, 100, 40),
            stripBounds));
    }

    private static void TopBandScalesWithWindowDpi()
    {
        ScreenRectangle rootBounds = new(100, 200, 1700, 1100);
        ScreenRectangle shortRootBounds = new(0, 0, 300, 60);
        (uint Dpi, int BandBottom)[] cases =
        [
            (96, 296),
            (144, 344),
            (192, 392),
        ];

        foreach ((uint dpi, int bandBottom) in cases)
        {
            True(BrowserTabPolicy.IsPointInsideTopBand(
                rootBounds,
                dpi,
                new ScreenPoint(100, bandBottom - 1)));
            True(BrowserTabPolicy.IsPointInsideTopBand(
                rootBounds,
                dpi,
                new ScreenPoint(1699, 200)));
            False(BrowserTabPolicy.IsPointInsideTopBand(
                rootBounds,
                dpi,
                new ScreenPoint(100, bandBottom)));
            False(BrowserTabPolicy.IsPointInsideTopBand(
                rootBounds,
                dpi,
                new ScreenPoint(99, 200)));
            False(BrowserTabPolicy.IsPointInsideTopBand(
                rootBounds,
                dpi,
                new ScreenPoint(1700, 200)));
        }

        False(BrowserTabPolicy.IsPointInsideTopBand(
            rootBounds,
            dpi: 96,
            new ScreenPoint(100, 199)));
        True(BrowserTabPolicy.IsPointInsideTopBand(
            rootBounds,
            dpi: 0,
            new ScreenPoint(100, 295)));
        False(BrowserTabPolicy.IsPointInsideTopBand(
            rootBounds,
            dpi: 0,
            new ScreenPoint(100, 296)));
        True(BrowserTabPolicy.IsPointInsideTopBand(
            shortRootBounds,
            dpi: 192,
            new ScreenPoint(0, 59)));
        False(BrowserTabPolicy.IsPointInsideTopBand(
            shortRootBounds,
            dpi: 192,
            new ScreenPoint(0, 60)));
    }

    private static void InvalidEdgeRectanglesAreRejected()
    {
        ScreenRectangle validBounds = new(0, 0, 1600, 900);
        ScreenRectangle[] invalidBounds =
        [
            new(0, 0, 0, 100),
            new(100, 0, 0, 100),
            new(0, 100, 100, 100),
            new(0, 100, 100, 0),
            new(double.NaN, 0, 100, 100),
            new(0, double.PositiveInfinity, 100, 100),
            new(0, 0, double.NegativeInfinity, 100),
            new(0, 0, 100, double.NaN),
        ];

        foreach (ScreenRectangle invalidBoundsItem in invalidBounds)
        {
            False(BrowserTabPolicy.IsPointInsideTopBand(
                invalidBoundsItem,
                dpi: 96,
                new ScreenPoint(0, 0)));
            False(BrowserTabPolicy.IsSupportedTabStrip(
                BrowserKind.Edge,
                TabStripOrientation.Horizontal,
                invalidBoundsItem,
                validBounds,
                dpi: 96));
            False(BrowserTabPolicy.IsSupportedTabStrip(
                BrowserKind.Edge,
                TabStripOrientation.Horizontal,
                validBounds,
                invalidBoundsItem,
                dpi: 96));
        }
    }

    private static void EdgeTargetIdentityIsDetectedExactly()
    {
        ScreenRectangle bounds = new(0, 0, 100, 40);
        Equal("chrome", BrowserTabPolicy.IdentityPrefix(BrowserKind.Chrome));
        Equal("edge", BrowserTabPolicy.IdentityPrefix(BrowserKind.Edge));
        True(BrowserTabPolicy.IsEdgeTarget(new TabTarget(
            "edge:1A:42.7",
            RootWindow: 0x1A,
            bounds)));
        False(BrowserTabPolicy.IsEdgeTarget(new TabTarget(
            "chrome:1A:42.7",
            RootWindow: 0x1A,
            bounds)));
        False(BrowserTabPolicy.IsEdgeTarget(new TabTarget(
            "Edge:1A:42.7",
            RootWindow: 0x1A,
            bounds)));
        False(BrowserTabPolicy.IsEdgeTarget(new TabTarget(
            "edge",
            RootWindow: 0x1A,
            bounds)));
    }

    private static void DeferredWorkerWaitsBeforeProcessing()
    {
        using CancellationTokenSource cancellation = new();
        using ManualResetEventSlim waitEntered = new();
        using ManualResetEventSlim releaseWait = new();
        using ManualResetEventSlim processed = new();
        int processedValue = -1;
        DeferredRequestWorker<int> worker = new(
            cancellation.Token,
            BrowserGesturePolicy.EdgeNativeCloseSettlingMilliseconds,
            () => request =>
            {
                Volatile.Write(ref processedValue, request);
                processed.Set();
            },
            "Deferred worker wait test",
            (delayMilliseconds, token) =>
            {
                Equal(
                    BrowserGesturePolicy.EdgeNativeCloseSettlingMilliseconds,
                    delayMilliseconds);
                waitEntered.Set();
                return WaitHandle.WaitAny(
                    [releaseWait.WaitHandle, token.WaitHandle]) == 1;
            });

        try
        {
            worker.Start();
            True(worker.TryWrite(42));
            WaitForSignal(waitEntered, "deferred wait to start");
            False(processed.IsSet);

            releaseWait.Set();
            WaitForSignal(processed, "deferred request to be processed");
            Equal(42, Volatile.Read(ref processedValue));
        }
        finally
        {
            cancellation.Cancel();
            worker.Complete();
            True(worker.Join(TimeSpan.FromSeconds(5)));
        }
    }

    private static void DeferredWorkerKeepsNewestQueuedRequest()
    {
        using CancellationTokenSource cancellation = new();
        using ManualResetEventSlim firstWaitEntered = new();
        using ManualResetEventSlim releaseFirstWait = new();
        using ManualResetEventSlim secondWaitEntered = new();
        using ManualResetEventSlim releaseSecondWait = new();
        using ManualResetEventSlim twoRequestsProcessed = new();
        object processedGate = new();
        List<int> processed = [];
        int waitCount = 0;
        DeferredRequestWorker<int> worker = new(
            cancellation.Token,
            BrowserGesturePolicy.EdgeNativeCloseSettlingMilliseconds,
            () => request =>
            {
                lock (processedGate)
                {
                    processed.Add(request);
                    if (processed.Count == 2)
                    {
                        twoRequestsProcessed.Set();
                    }
                }
            },
            "Deferred worker queue test",
            (_, token) =>
            {
                int currentWait = Interlocked.Increment(ref waitCount);
                if (currentWait == 1)
                {
                    firstWaitEntered.Set();
                    return WaitHandle.WaitAny(
                        [releaseFirstWait.WaitHandle, token.WaitHandle]) == 1;
                }

                secondWaitEntered.Set();
                return WaitHandle.WaitAny(
                    [releaseSecondWait.WaitHandle, token.WaitHandle]) == 1;
            });

        try
        {
            worker.Start();
            True(worker.TryWrite(1));
            WaitForSignal(firstWaitEntered, "first deferred wait to start");
            True(worker.TryWrite(2));
            True(worker.TryWrite(3));

            releaseFirstWait.Set();
            WaitForSignal(secondWaitEntered, "newest deferred wait to start");
            lock (processedGate)
            {
                Equal(1, processed.Count);
                Equal(1, processed[0]);
            }

            releaseSecondWait.Set();
            WaitForSignal(twoRequestsProcessed, "newest deferred request to run");

            int[] snapshot;
            lock (processedGate)
            {
                snapshot = [.. processed];
            }

            Equal(2, snapshot.Length);
            Equal(1, snapshot[0]);
            Equal(3, snapshot[1]);
        }
        finally
        {
            cancellation.Cancel();
            worker.Complete();
            True(worker.Join(TimeSpan.FromSeconds(5)));
        }
    }

    private static void DeferredWorkerCancellationSkipsProcessing()
    {
        using CancellationTokenSource cancellation = new();
        using ManualResetEventSlim waitEntered = new();
        using ManualResetEventSlim processed = new();
        DeferredRequestWorker<int> worker = new(
            cancellation.Token,
            BrowserGesturePolicy.EdgeNativeCloseSettlingMilliseconds,
            () => _ => processed.Set(),
            "Deferred worker cancellation test",
            (_, _) =>
            {
                waitEntered.Set();
                cancellation.Cancel();
                return false;
            });

        try
        {
            worker.Start();
            True(worker.TryWrite(1));
            WaitForSignal(waitEntered, "cancellable deferred wait to start");
            True(worker.Join(TimeSpan.FromSeconds(5)));
            False(processed.IsSet);
        }
        finally
        {
            cancellation.Cancel();
            worker.Complete();
            True(worker.Join(TimeSpan.FromSeconds(5)));
        }
    }

    private static void DeferredWorkerRecoversAfterRequestFailure()
    {
        using CancellationTokenSource cancellation = new();
        using ManualResetEventSlim firstAttempted = new();
        using ManualResetEventSlim secondProcessed = new();
        DeferredRequestWorker<int> worker = new(
            cancellation.Token,
            delayMilliseconds: 0,
            () => request =>
            {
                if (request == 1)
                {
                    firstAttempted.Set();
                    throw new InvalidOperationException("Expected test failure.");
                }

                if (request == 2)
                {
                    secondProcessed.Set();
                }
            },
            "Deferred worker recovery test",
            (_, token) => token.IsCancellationRequested);

        try
        {
            worker.Start();
            True(worker.TryWrite(1));
            WaitForSignal(firstAttempted, "failing deferred request to run");
            True(worker.TryWrite(2));
            WaitForSignal(secondProcessed, "deferred worker to recover");
        }
        finally
        {
            cancellation.Cancel();
            worker.Complete();
            True(worker.Join(TimeSpan.FromSeconds(5)));
        }
    }

    private static void DeferredWorkerCreatesOneProcessorOnMtaThread()
    {
        using CancellationTokenSource cancellation = new();
        using ManualResetEventSlim processed = new();
        int callerThreadId = Environment.CurrentManagedThreadId;
        int factoryThreadId = -1;
        int factoryCallCount = 0;
        ApartmentState factoryApartment = ApartmentState.Unknown;
        DeferredRequestWorker<int> worker = new(
            cancellation.Token,
            delayMilliseconds: 0,
            () =>
            {
                Interlocked.Increment(ref factoryCallCount);
                factoryThreadId = Environment.CurrentManagedThreadId;
                factoryApartment = Thread.CurrentThread.GetApartmentState();
                return _ => processed.Set();
            },
            "Deferred worker factory test",
            (_, token) => token.IsCancellationRequested);

        try
        {
            Equal(0, Volatile.Read(ref factoryCallCount));
            worker.Start();
            True(worker.TryWrite(1));
            WaitForSignal(processed, "deferred processor to run");
            True(factoryThreadId != callerThreadId);
            Equal(ApartmentState.MTA, factoryApartment);
            Equal(1, Volatile.Read(ref factoryCallCount));
        }
        finally
        {
            cancellation.Cancel();
            worker.Complete();
            True(worker.Join(TimeSpan.FromSeconds(5)));
        }
    }

    private static void DeferredWorkerCompletesWithoutCreatingProcessor()
    {
        using CancellationTokenSource cancellation = new();
        int factoryCallCount = 0;
        DeferredRequestWorker<int> worker = new(
            cancellation.Token,
            delayMilliseconds: 0,
            () =>
            {
                Interlocked.Increment(ref factoryCallCount);
                return _ => { };
            },
            "Deferred worker completion test");

        worker.Start();
        worker.Complete();

        True(worker.Join(TimeSpan.FromSeconds(5)));
        Equal(0, Volatile.Read(ref factoryCallCount));
        False(worker.TryWrite(1));
    }

    private static void WaitForSignal(
        ManualResetEventSlim signal,
        string description)
    {
        if (!signal.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new InvalidOperationException($"Timed out waiting for {description}.");
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

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException(
            $"Expected {typeof(TException).Name} to be thrown.");
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
