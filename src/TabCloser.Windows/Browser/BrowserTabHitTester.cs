using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using TabCloser.Core;
using TabCloser.Windows.Interop;
using AutomationPoint = System.Windows.Point;
using AutomationRect = System.Windows.Rect;

namespace TabCloser.Windows.Browser;

internal sealed class BrowserTabHitTester
{
    private const int MaximumAncestorDepth = 32;
    private readonly Func<BrowserKind, bool> _isBrowserEnabled;
    private readonly PropertyCondition _buttonCondition = new(
        AutomationElement.ControlTypeProperty,
        ControlType.Button);

    public BrowserTabHitTester(Func<BrowserKind, bool>? isBrowserEnabled = null)
    {
        _isBrowserEnabled = isBrowserEnabled ?? (static _ => true);

        try
        {
            _ = AutomationElement.RootElement.Current.ControlType;
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or
            COMException or
            InvalidOperationException)
        {
            // The first real query will retry and fail closed if UIA is unavailable.
        }
    }

    public TabTarget? HitTest(ScreenPoint point)
    {
        try
        {
            BrowserRoot? browserRoot = GetBrowserRootWindow(point);
            if (browserRoot is not BrowserRoot root)
            {
                return null;
            }

            if (!_isBrowserEnabled(root.Browser))
            {
                return null;
            }

            AutomationElement leaf = AutomationElement.FromPoint(
                new AutomationPoint(point.X, point.Y));
            AutomationElement? tab = FindTabAncestor(leaf);
            if (tab is null)
            {
                return null;
            }

            AutomationElement? edgeTabStrip = null;
            if (root.Browser == BrowserKind.Chrome)
            {
                if (!HasNativeTabListAncestor(tab, root.Window))
                {
                    return null;
                }
            }
            else
            {
                edgeTabStrip = FindNativeTabStripAncestor(
                    tab,
                    root.Window,
                    root.ProcessId);
                if (edgeTabStrip is null)
                {
                    return null;
                }
            }

            AutomationElement.AutomationElementInformation tabInformation = tab.Current;
            if (tabInformation.ProcessId != root.ProcessId ||
                !BrowserTabPolicy.IsExpectedTabClass(
                    root.Browser,
                    tabInformation.ClassName))
            {
                return null;
            }

            AutomationRect bounds = tabInformation.BoundingRectangle;
            ScreenRectangle tabBounds = ToScreenRectangle(bounds);

            if (edgeTabStrip is not null)
            {
                AutomationElement.AutomationElementInformation stripInformation =
                    edgeTabStrip.Current;
                ScreenRectangle stripBounds =
                    ToScreenRectangle(stripInformation.BoundingRectangle);
                if (stripInformation.ProcessId != root.ProcessId ||
                    !BrowserTabPolicy.IsExpectedTabStripClass(
                        root.Browser,
                        stripInformation.ClassName) ||
                    !BrowserTabPolicy.IsSupportedTabStrip(
                        root.Browser,
                        ReadOrientation(edgeTabStrip),
                        stripBounds,
                        root.Bounds,
                        root.Dpi) ||
                    !BrowserTabPolicy.IsTabContainedByStrip(
                        tabBounds,
                        stripBounds))
                {
                    return null;
                }
            }

            if (bounds.IsEmpty ||
                !bounds.Contains(new AutomationPoint(point.X, point.Y)) ||
                IsPointOverDescendantButton(tab, point))
            {
                return null;
            }

            int[] runtimeId = tab.GetRuntimeId();
            if (runtimeId.Length == 0)
            {
                return null;
            }

            string identity = string.Create(
                CultureInfo.InvariantCulture,
                $"{BrowserTabPolicy.IdentityPrefix(root.Browser)}:{root.Window.ToInt64():X}:{string.Join('.', runtimeId)}");
            return new TabTarget(
                identity,
                root.Window.ToInt64(),
                tabBounds);
        }
        catch (Exception exception) when (
            exception is ElementNotAvailableException or
            COMException or
            InvalidOperationException or
            ArgumentException or
            Win32Exception)
        {
            return null;
        }
    }

    private static AutomationElement? FindTabAncestor(AutomationElement leaf)
    {
        AutomationElement? current = leaf;

        for (int depth = 0; current is not null && depth < MaximumAncestorDepth; depth++)
        {
            ControlType type = current.Current.ControlType;
            if (type == ControlType.Button)
            {
                return null;
            }

            if (type == ControlType.TabItem)
            {
                return current;
            }

            current = TreeWalker.RawViewWalker.GetParent(current);
        }

        return null;
    }

    private static AutomationElement? FindNativeTabStripAncestor(
        AutomationElement tab,
        nint rootWindow,
        uint processId)
    {
        AutomationElement? current = TreeWalker.RawViewWalker.GetParent(tab);
        AutomationElement? outermostTabStrip = null;

        for (int depth = 0; current is not null && depth < MaximumAncestorDepth; depth++)
        {
            AutomationElement.AutomationElementInformation information = current.Current;
            if (information.ProcessId != processId ||
                information.ControlType == ControlType.Document)
            {
                return null;
            }

            if (information.ControlType == ControlType.Tab)
            {
                outermostTabStrip = current;
            }

            if (information.NativeWindowHandle != 0 &&
                information.NativeWindowHandle ==
                unchecked((int)rootWindow.ToInt64()))
            {
                return outermostTabStrip;
            }

            current = TreeWalker.RawViewWalker.GetParent(current);
        }

        return null;
    }

    private static bool HasNativeTabListAncestor(
        AutomationElement tab,
        nint rootWindow)
    {
        AutomationElement? current = TreeWalker.RawViewWalker.GetParent(tab);
        bool foundTabList = false;

        for (int depth = 0; current is not null && depth < MaximumAncestorDepth; depth++)
        {
            AutomationElement.AutomationElementInformation information = current.Current;
            if (information.ControlType == ControlType.Document)
            {
                return false;
            }

            foundTabList |= information.ControlType == ControlType.Tab;
            if (information.NativeWindowHandle != 0 &&
                information.NativeWindowHandle == unchecked((int)rootWindow.ToInt64()))
            {
                return foundTabList;
            }

            current = TreeWalker.RawViewWalker.GetParent(current);
        }

        return false;
    }

    private bool IsPointOverDescendantButton(
        AutomationElement tab,
        ScreenPoint point)
    {
        AutomationElementCollection buttons = tab.FindAll(
            TreeScope.Descendants,
            _buttonCondition);
        AutomationPoint automationPoint = new(point.X, point.Y);

        foreach (AutomationElement button in buttons)
        {
            AutomationRect bounds = button.Current.BoundingRectangle;
            if (!bounds.IsEmpty && bounds.Contains(automationPoint))
            {
                return true;
            }
        }

        return false;
    }

    private static BrowserRoot? GetBrowserRootWindow(ScreenPoint point)
    {
        nint window = NativeMethods.WindowFromPoint(
            new NativeMethods.NativePoint(point.X, point.Y));
        nint root = NativeMethods.GetAncestor(window, NativeMethods.GetAncestorRoot);
        if (root == nint.Zero ||
            !NativeMethods.GetWindowRect(
                root,
                out NativeMethods.NativeRectangle nativeBounds))
        {
            return null;
        }

        ScreenRectangle bounds = new(
            nativeBounds.Left,
            nativeBounds.Top,
            nativeBounds.Right,
            nativeBounds.Bottom);
        uint dpi = NativeMethods.GetDpiForWindow(root);
        if (!BrowserTabPolicy.IsPointInsideTopBand(bounds, dpi, point))
        {
            return null;
        }

        StringBuilder className = new(capacity: 256);
        if (NativeMethods.GetClassNameW(root, className, className.Capacity) == 0)
        {
            return null;
        }

        NativeMethods.GetWindowThreadProcessId(root, out uint processId);
        if (processId == 0)
        {
            return null;
        }

        using Process process = Process.GetProcessById((int)processId);
        BrowserKind? browser = BrowserTabPolicy.ClassifyRoot(
            process.ProcessName,
            className.ToString());
        return browser is BrowserKind supportedBrowser
            ? new BrowserRoot(supportedBrowser, root, processId, bounds, dpi)
            : null;
    }

    private static TabStripOrientation ReadOrientation(AutomationElement tabStrip)
    {
        object value = tabStrip.GetCurrentPropertyValue(
            AutomationElement.OrientationProperty,
            ignoreDefaultValue: true);
        if (ReferenceEquals(value, AutomationElement.NotSupported))
        {
            return TabStripOrientation.Unknown;
        }

        return value is OrientationType orientation
            ? orientation switch
            {
                OrientationType.Horizontal => TabStripOrientation.Horizontal,
                OrientationType.Vertical => TabStripOrientation.Vertical,
                _ => TabStripOrientation.Unknown,
            }
            : TabStripOrientation.Unknown;
    }

    private static ScreenRectangle ToScreenRectangle(AutomationRect rectangle) =>
        new(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);

    private readonly record struct BrowserRoot(
        BrowserKind Browser,
        nint Window,
        uint ProcessId,
        ScreenRectangle Bounds,
        uint Dpi);
}
