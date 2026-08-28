using TabCloser.Core;

namespace TabCloser.Windows.Browser;

internal enum BrowserKind
{
    Chrome,
    Edge,
}

internal enum TabStripOrientation
{
    Unknown,
    Horizontal,
    Vertical,
}

internal static class BrowserTabPolicy
{
    internal const int TopBrowserChromeHeightDip = 96;
    internal const string ChromiumRootWindowClass = "Chrome_WidgetWin_1";

    internal static BrowserKind? ClassifyRoot(
        string processName,
        string windowClassName)
    {
        if (!string.Equals(
                windowClassName,
                ChromiumRootWindowClass,
                StringComparison.Ordinal))
        {
            return null;
        }

        if (string.Equals(processName, "chrome", StringComparison.OrdinalIgnoreCase))
        {
            return BrowserKind.Chrome;
        }

        return string.Equals(processName, "msedge", StringComparison.OrdinalIgnoreCase)
            ? BrowserKind.Edge
            : null;
    }

    internal static bool IsExpectedTabClass(BrowserKind browser, string className) =>
        browser switch
        {
            BrowserKind.Chrome => string.Equals(
                className,
                "Tab",
                StringComparison.Ordinal),
            BrowserKind.Edge => string.Equals(
                className,
                "EdgeTab",
                StringComparison.Ordinal),
            _ => false,
        };

    internal static bool IsExpectedTabStripClass(
        BrowserKind browser,
        string className) =>
        browser == BrowserKind.Chrome ||
        (browser == BrowserKind.Edge &&
         string.Equals(
             className,
             "EdgeTabStripRegionView",
             StringComparison.Ordinal));

    internal static string IdentityPrefix(BrowserKind browser) =>
        browser switch
        {
            BrowserKind.Chrome => "chrome",
            BrowserKind.Edge => "edge",
            _ => throw new ArgumentOutOfRangeException(nameof(browser)),
        };

    internal static bool IsEdgeTarget(TabTarget target) =>
        target.Identity.StartsWith("edge:", StringComparison.Ordinal);

    internal static bool IsPointInsideTopBand(
        ScreenRectangle rootBounds,
        uint dpi,
        ScreenPoint point)
    {
        if (!IsValid(rootBounds))
        {
            return false;
        }

        double bandBottom = GetTopBandBottom(rootBounds, dpi);
        return point.X >= rootBounds.Left &&
               point.X < rootBounds.Right &&
               point.Y >= rootBounds.Top &&
               point.Y < bandBottom;
    }

    internal static bool IsSupportedTabStrip(
        BrowserKind browser,
        TabStripOrientation orientation,
        ScreenRectangle tabStripBounds,
        ScreenRectangle rootBounds,
        uint dpi)
    {
        if (browser != BrowserKind.Edge ||
            orientation == TabStripOrientation.Vertical ||
            !IsValid(tabStripBounds) ||
            !IsValid(rootBounds))
        {
            return false;
        }

        double width = tabStripBounds.Right - tabStripBounds.Left;
        double height = tabStripBounds.Bottom - tabStripBounds.Top;
        double rootWidth = rootBounds.Right - rootBounds.Left;
        double bandBottom = GetTopBandBottom(rootBounds, dpi);

        // Edge does not consistently expose UIA orientation for its native
        // strip. Its outer tab-list region must therefore also prove that it
        // is a wide, shallow region wholly contained in the top chrome band.
        return orientation != TabStripOrientation.Vertical &&
               tabStripBounds.Left >= rootBounds.Left &&
               tabStripBounds.Right <= rootBounds.Right &&
               tabStripBounds.Top >= rootBounds.Top &&
               tabStripBounds.Bottom <= bandBottom &&
               width >= height * 2 &&
               width >= rootWidth / 3;
    }

    internal static bool IsTabContainedByStrip(
        ScreenRectangle tabBounds,
        ScreenRectangle tabStripBounds) =>
        IsValid(tabBounds) &&
        IsValid(tabStripBounds) &&
        tabBounds.Left >= tabStripBounds.Left &&
        tabBounds.Right <= tabStripBounds.Right &&
        tabBounds.Top >= tabStripBounds.Top &&
        tabBounds.Bottom <= tabStripBounds.Bottom;

    private static double GetTopBandBottom(ScreenRectangle rootBounds, uint dpi)
    {
        uint effectiveDpi = dpi == 0 ? 96u : dpi;
        double bandHeight = TopBrowserChromeHeightDip * effectiveDpi / 96d;
        return Math.Min(rootBounds.Bottom, rootBounds.Top + bandHeight);
    }

    private static bool IsValid(ScreenRectangle rectangle) =>
        double.IsFinite(rectangle.Left) &&
        double.IsFinite(rectangle.Top) &&
        double.IsFinite(rectangle.Right) &&
        double.IsFinite(rectangle.Bottom) &&
        rectangle.Right > rectangle.Left &&
        rectangle.Bottom > rectangle.Top;
}
