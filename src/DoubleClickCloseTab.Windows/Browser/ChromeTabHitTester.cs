using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;
using DoubleClickCloseTab.Core;
using DoubleClickCloseTab.Windows.Interop;
using AutomationPoint = System.Windows.Point;
using AutomationRect = System.Windows.Rect;

namespace DoubleClickCloseTab.Windows.Browser;

internal sealed class ChromeTabHitTester
{
    private const int MaximumAncestorDepth = 32;
    private const int TopChromeHeightDip = 96;
    private readonly PropertyCondition _buttonCondition = new(
        AutomationElement.ControlTypeProperty,
        ControlType.Button);

    public ChromeTabHitTester()
    {
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
            nint rootWindow = GetChromeRootWindow(point, out uint processId);
            if (rootWindow == nint.Zero)
            {
                return null;
            }

            AutomationElement leaf = AutomationElement.FromPoint(
                new AutomationPoint(point.X, point.Y));
            AutomationElement? tab = FindTabAncestor(leaf);
            if (tab is null || !HasNativeTabListAncestor(tab, rootWindow))
            {
                return null;
            }

            AutomationElement.AutomationElementInformation tabInformation = tab.Current;
            if (tabInformation.ProcessId != processId ||
                !string.Equals(
                    tabInformation.ClassName,
                    "Tab",
                    StringComparison.Ordinal))
            {
                return null;
            }

            AutomationRect bounds = tabInformation.BoundingRectangle;
            if (bounds.IsEmpty || !bounds.Contains(new AutomationPoint(point.X, point.Y)) ||
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
                $"{rootWindow.ToInt64():X}:{string.Join('.', runtimeId)}");
            return new TabTarget(
                identity,
                rootWindow.ToInt64(),
                new ScreenRectangle(
                    bounds.Left,
                    bounds.Top,
                    bounds.Right,
                    bounds.Bottom));
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

    private static nint GetChromeRootWindow(ScreenPoint point, out uint processId)
    {
        processId = 0;
        nint window = NativeMethods.WindowFromPoint(
            new NativeMethods.NativePoint(point.X, point.Y));
        nint root = NativeMethods.GetAncestor(window, NativeMethods.GetAncestorRoot);
        if (root == nint.Zero || !IsInsideTopChromeBand(root, point))
        {
            return nint.Zero;
        }

        StringBuilder className = new(capacity: 256);
        if (NativeMethods.GetClassNameW(root, className, className.Capacity) == 0 ||
            !string.Equals(
                className.ToString(),
                "Chrome_WidgetWin_1",
                StringComparison.Ordinal))
        {
            return nint.Zero;
        }

        NativeMethods.GetWindowThreadProcessId(root, out processId);
        if (processId == 0)
        {
            return nint.Zero;
        }

        using Process process = Process.GetProcessById((int)processId);
        return string.Equals(process.ProcessName, "chrome", StringComparison.OrdinalIgnoreCase)
            ? root
            : nint.Zero;
    }

    private static bool IsInsideTopChromeBand(nint root, ScreenPoint point)
    {
        if (!NativeMethods.GetWindowRect(
                root,
                out NativeMethods.NativeRectangle rectangle))
        {
            return false;
        }

        uint dpi = NativeMethods.GetDpiForWindow(root);
        long bandHeight = (long)TopChromeHeightDip * (dpi == 0 ? 96 : dpi) / 96;
        return point.X >= rectangle.Left &&
               point.X < rectangle.Right &&
               point.Y >= rectangle.Top &&
               point.Y < Math.Min((long)rectangle.Bottom, rectangle.Top + bandHeight);
    }
}
