using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Windows.Automation;
using TabCloser.Core;
using TabCloser.Windows.Browser;
using TabCloser.Windows.Interop;
using AutomationPoint = System.Windows.Point;
using AutomationRect = System.Windows.Rect;

namespace TabCloser.Diagnostics;

internal static class Program
{
    private const int MaximumAncestorDepth = 48;

    public static int Main(string[] args)
    {
        bool trayMode = args is ["--tray"];
        bool inspectMode = args.Length == 1 && !trayMode;
        bool benchmarkMode = args.Length == 3;
        long rootWindow = 0;
        int benchmarkX = 0;
        int benchmarkY = 0;
        if (!trayMode &&
            ((!inspectMode && !benchmarkMode) ||
             !long.TryParse(
                 args[0],
                 NumberStyles.Integer,
                 CultureInfo.InvariantCulture,
                 out rootWindow) ||
             rootWindow == 0 ||
             (benchmarkMode &&
              (!int.TryParse(
                   args[1],
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out benchmarkX) ||
               !int.TryParse(
                   args[2],
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out benchmarkY)))))
        {
            Console.Error.WriteLine(
                "Pass --tray, or a Chrome root HWND optionally followed by a screen X and Y.");
            return 64;
        }

        ScreenPoint? benchmarkPoint = benchmarkMode
            ? new ScreenPoint(benchmarkX, benchmarkY)
            : null;

        string? output = null;
        string? errorType = null;
        Thread worker = new(() =>
        {
            try
            {
                object report = trayMode
                    ? InspectTrayIcon()
                    : benchmarkPoint is ScreenPoint point
                        ? BenchmarkHitTest(new nint(rootWindow), point)
                        : Inspect(new nint(rootWindow));
                output = JsonSerializer.Serialize(
                    report,
                    new JsonSerializerOptions { WriteIndented = true });
            }
            catch (Exception exception)
            {
                errorType = exception.GetType().Name;
            }
        })
        {
            IsBackground = true,
            Name = "Chrome UIA diagnostic worker",
        };
        worker.SetApartmentState(ApartmentState.MTA);
        worker.Start();

        if (!worker.Join(TimeSpan.FromSeconds(15)))
        {
            Console.WriteLine("{\"Status\":\"TimedOut\"}");
            Environment.Exit(2);
        }

        if (errorType is not null)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                Status = "Failed",
                ErrorType = errorType,
            }));
            return 1;
        }

        Console.WriteLine(output);
        return 0;
    }

    private static TrayIconDiagnosticReport InspectTrayIcon()
    {
        AutomationElement root = AutomationElement.RootElement;
        OrCondition names = new(
            new PropertyCondition(
                AutomationElement.NameProperty,
                "TabCloser"),
            new PropertyCondition(
                AutomationElement.NameProperty,
                "TabCloser (paused)"));
        AutomationElementCollection matches = root.FindAll(
            TreeScope.Descendants,
            names);
        List<TrayIconReport> icons = [];

        foreach (AutomationElement match in matches)
        {
            AutomationElement.AutomationElementInformation information = match.Current;
            icons.Add(new TrayIconReport(
                information.Name,
                information.ControlType.ProgrammaticName,
                information.ClassName,
                information.IsOffscreen,
                RectangleReport.From(information.BoundingRectangle)));
        }

        List<SystemTrayControlReport> systemTrayControls = [];
        AutomationElementCollection systemTrayDescendants = root.FindAll(
            TreeScope.Descendants,
            Condition.TrueCondition);
        foreach (AutomationElement descendant in systemTrayDescendants)
        {
            AutomationElement.AutomationElementInformation information =
                descendant.Current;
            if (!information.ClassName.StartsWith(
                    "SystemTray.",
                    StringComparison.Ordinal))
            {
                continue;
            }

            systemTrayControls.Add(new SystemTrayControlReport(
                information.ControlType.ProgrammaticName,
                information.ClassName,
                information.AutomationId,
                information.IsOffscreen,
                information.Name.Contains(
                    "hidden icons",
                    StringComparison.OrdinalIgnoreCase) ||
                information.Name.Contains(
                    "隐藏的图标",
                    StringComparison.Ordinal),
                information.Name.Contains(
                    "TabCloser",
                    StringComparison.Ordinal),
                information.Name.Contains(
                    "(paused)",
                    StringComparison.Ordinal),
                RectangleReport.From(information.BoundingRectangle)));
        }

        HashSet<int> helperProcessIds = Process
            .GetProcessesByName("TabCloser")
            .Select(process => process.Id)
            .ToHashSet();
        OrCondition menuNames = new(
            new PropertyCondition(
                AutomationElement.NameProperty,
                "Double-click a Chrome tab to close it"),
            new PropertyCondition(AutomationElement.NameProperty, "Enabled"),
            new PropertyCondition(
                AutomationElement.NameProperty,
                "Start with Windows"),
            new PropertyCondition(AutomationElement.NameProperty, "Exit"));
        AutomationElementCollection menuMatches = root.FindAll(
            TreeScope.Descendants,
            menuNames);
        List<TrayMenuItemReport> menuItems = [];
        foreach (AutomationElement menuMatch in menuMatches)
        {
            AutomationElement.AutomationElementInformation information =
                menuMatch.Current;
            if (!helperProcessIds.Contains(information.ProcessId))
            {
                continue;
            }

            ToggleState? toggleState = menuMatch.TryGetCurrentPattern(
                    TogglePattern.Pattern,
                    out object? pattern) &&
                pattern is TogglePattern togglePattern
                    ? togglePattern.Current.ToggleState
                    : null;
            menuItems.Add(new TrayMenuItemReport(
                information.Name,
                information.ControlType.ProgrammaticName,
                information.ClassName,
                information.IsEnabled,
                information.IsOffscreen,
                toggleState?.ToString(),
                RectangleReport.From(information.BoundingRectangle)));
        }

        return new TrayIconDiagnosticReport(
            "Complete",
            icons.Count,
            icons,
            systemTrayControls,
            menuItems);
    }

    private static HitTestBenchmarkReport BenchmarkHitTest(
        nint rootWindow,
        ScreenPoint point)
    {
        const int iterations = 20;
        ChromeTabHitTester hitTester = new();
        List<double> durationsMilliseconds = [];
        string? expectedIdentity = null;
        int accepted = 0;
        bool stableIdentity = true;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            long started = Stopwatch.GetTimestamp();
            TabTarget? hit = hitTester.HitTest(point);
            durationsMilliseconds.Add(
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);

            if (hit is null || hit.RootWindow != rootWindow.ToInt64())
            {
                stableIdentity = false;
                continue;
            }

            accepted++;
            expectedIdentity ??= hit.Identity;
            stableIdentity &= string.Equals(
                expectedIdentity,
                hit.Identity,
                StringComparison.Ordinal);
        }

        return new HitTestBenchmarkReport(
            "Complete",
            rootWindow.ToInt64(),
            point,
            iterations,
            accepted,
            stableIdentity && accepted == iterations,
            durationsMilliseconds);
    }

    private static DiagnosticReport Inspect(nint rootWindow)
    {
        AutomationElement root = AutomationElement.FromHandle(rootWindow);
        ChromeTabHitTester hitTester = new();
        List<TabItemReport> tabItems = [];
        AutomationElementCollection documents = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Document));
        AutomationElementCollection tabs = root.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.TabItem));
        foreach (AutomationElement tab in tabs)
        {
            tabItems.Add(InspectTabItem(tab, rootWindow, hitTester));
        }

        return new DiagnosticReport(
            "Complete",
            rootWindow.ToInt64(),
            root.Current.ProcessId,
            NativeMethods.GetDpiForWindow(rootWindow),
            RectangleReport.From(root.Current.BoundingRectangle),
            documents.Count,
            tabs.Count,
            tabItems);
    }

    private static TabItemReport InspectTabItem(
        AutomationElement tab,
        nint rootWindow,
        ChromeTabHitTester hitTester)
    {
        AutomationElement.AutomationElementInformation information = tab.Current;
        List<AncestorReport> ancestors = [];
        AutomationElement? ancestor = TreeWalker.RawViewWalker.GetParent(tab);
        for (int depth = 0;
             ancestor is not null && depth < MaximumAncestorDepth;
             depth++)
        {
            ancestors.Add(AncestorReport.From(ancestor.Current));
            ancestor = TreeWalker.RawViewWalker.GetParent(ancestor);
        }

        AutomationRect bounds = information.BoundingRectangle;
        List<RectangleReport> buttonBounds = [];
        AutomationElementCollection buttons = tab.FindAll(
            TreeScope.Descendants,
            new PropertyCondition(
                AutomationElement.ControlTypeProperty,
                ControlType.Button));
        foreach (AutomationElement button in buttons)
        {
            AutomationRect buttonRectangle = button.Current.BoundingRectangle;
            if (!buttonRectangle.IsEmpty)
            {
                buttonBounds.Add(RectangleReport.From(buttonRectangle));
            }
        }

        ScreenPoint? bodyPoint = FindBodyPoint(bounds, buttonBounds);
        TabTarget? bodyHit = bodyPoint is ScreenPoint point
            ? hitTester.HitTest(point)
            : null;
        bool closeButtonRejected = true;
        if (buttonBounds.Count > 0)
        {
            RectangleReport button = buttonBounds[0];
            ScreenPoint buttonPoint = new(
                checked((int)Math.Floor((button.Left + button.Right) / 2)),
                checked((int)Math.Floor((button.Top + button.Bottom) / 2)));
            closeButtonRejected = hitTester.HitTest(buttonPoint) is null;
        }

        return new TabItemReport(
            tab.GetRuntimeId(),
            information.ProcessId,
            information.NativeWindowHandle,
            information.AutomationId,
            information.ClassName,
            information.FrameworkId,
            information.IsOffscreen,
            RectangleReport.From(bounds),
            ancestors.Any(ancestorReport =>
                ancestorReport.ControlType == "ControlType.Tab"),
            ancestors.Any(ancestorReport =>
                ancestorReport.ControlType == "ControlType.Document"),
            ancestors.Any(ancestorReport =>
                ancestorReport.NativeWindowHandle ==
                unchecked((int)rootWindow.ToInt64())),
            ancestors.Select(ancestorReport => ancestorReport.ControlType).ToArray(),
            buttonBounds,
            bodyPoint,
            bodyHit is not null,
            closeButtonRejected);
    }

    private static ScreenPoint? FindBodyPoint(
        AutomationRect bounds,
        IReadOnlyList<RectangleReport> buttons)
    {
        if (bounds.IsEmpty || bounds.Width < 1 || bounds.Height < 1)
        {
            return null;
        }

        int left = checked((int)Math.Ceiling(bounds.Left));
        int right = checked((int)Math.Floor(bounds.Right - 1));
        int y = checked((int)Math.Floor((bounds.Top + bounds.Bottom) / 2));
        if (right < left)
        {
            return null;
        }

        int width = right - left;
        int[] candidates =
        [
            left + (width / 2),
            left + (width / 4),
            left + ((width * 3) / 4),
            left,
            right,
        ];
        foreach (int x in candidates)
        {
            ScreenPoint point = new(x, y);
            if (!buttons.Any(button => button.Contains(point)))
            {
                return point;
            }
        }

        return null;
    }

    private sealed record DiagnosticReport(
        string Status,
        long RootWindow,
        int RootProcessId,
        uint RootDpi,
        RectangleReport RootBounds,
        int DocumentCount,
        int TabItemCount,
        IReadOnlyList<TabItemReport> TabItems);

    private sealed record HitTestBenchmarkReport(
        string Status,
        long RootWindow,
        ScreenPoint Point,
        int Iterations,
        int Accepted,
        bool StableIdentity,
        IReadOnlyList<double> DurationsMilliseconds);

    private sealed record TrayIconDiagnosticReport(
        string Status,
        int MatchCount,
        IReadOnlyList<TrayIconReport> Matches,
        IReadOnlyList<SystemTrayControlReport> SystemTrayControls,
        IReadOnlyList<TrayMenuItemReport> MenuItems);

    private sealed record TrayIconReport(
        string Name,
        string ControlType,
        string ClassName,
        bool IsOffscreen,
        RectangleReport Bounds);

    private sealed record SystemTrayControlReport(
        string ControlType,
        string ClassName,
        string AutomationId,
        bool IsOffscreen,
        bool IsHiddenIconsControl,
        bool MatchesHelperName,
        bool IsPausedName,
        RectangleReport Bounds);

    private sealed record TrayMenuItemReport(
        string Name,
        string ControlType,
        string ClassName,
        bool IsEnabled,
        bool IsOffscreen,
        string? ToggleState,
        RectangleReport Bounds);

    private sealed record TabItemReport(
        int[] RuntimeId,
        int ProcessId,
        int NativeWindowHandle,
        string AutomationId,
        string ClassName,
        string FrameworkId,
        bool IsOffscreen,
        RectangleReport Bounds,
        bool HasTabAncestor,
        bool HasDocumentAncestor,
        bool ReachesRootWindow,
        IReadOnlyList<string> AncestorControlTypes,
        IReadOnlyList<RectangleReport> DescendantButtonBounds,
        ScreenPoint? BodyPoint,
        bool BodyHitAccepted,
        bool DescendantButtonHitRejected);

    private sealed record AncestorReport(
        string ControlType,
        int NativeWindowHandle,
        string AutomationId,
        string ClassName)
    {
        public static AncestorReport From(
            AutomationElement.AutomationElementInformation information) =>
            new(
                information.ControlType.ProgrammaticName,
                information.NativeWindowHandle,
                information.AutomationId,
                information.ClassName);
    }

    private sealed record RectangleReport(
        double Left,
        double Top,
        double Right,
        double Bottom)
    {
        public static RectangleReport From(AutomationRect rectangle) => new(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);

        public bool Contains(ScreenPoint point) =>
            point.X >= Left &&
            point.X < Right &&
            point.Y >= Top &&
            point.Y < Bottom;
    }
}
