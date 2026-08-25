using TabCloser.Core;

namespace TabCloser.Core.Tests;

internal static class Program
{
    private static readonly DoubleClickConfiguration Configuration = new(
        MaximumDelayMilliseconds: 500,
        RectangleWidth: 4,
        RectangleHeight: 4);

    private static readonly TabTarget FirstTab = new(
        "window-1:tab-1",
        RootWindow: 1,
        new ScreenRectangle(0, 0, 100, 40));

    public static int Main()
    {
        (string Name, Action Test)[] tests =
        [
            (nameof(CompletesForSameTab), CompletesForSameTab),
            (nameof(HonorsExactBoundaries), HonorsExactBoundaries),
            (nameof(UsesDownToDownTiming), UsesDownToDownTiming),
            (nameof(SlowClickBecomesNextCandidate), SlowClickBecomesNextCandidate),
            (nameof(RejectsMovementBetweenClicks), RejectsMovementBetweenClicks),
            (nameof(RejectsMovementWithinClick), RejectsMovementWithinClick),
            (nameof(RejectsDifferentTabOrWindow), RejectsDifferentTabOrWindow),
            (nameof(InjectedAndModifiedClicksResetSequence), InjectedAndModifiedClicksResetSequence),
            (nameof(ResetClearsCandidate), ResetClearsCandidate),
            (nameof(TripleClickCompletesOnlyOnce), TripleClickCompletesOnlyOnce),
            (nameof(HandlesTimestampWraparound), HandlesTimestampWraparound),
            (nameof(RejectsFullTimestampCycle), RejectsFullTimestampCycle),
            (nameof(RejectsMismatchedEventRoots), RejectsMismatchedEventRoots),
            (nameof(RejectsZeroEventRoot), RejectsZeroEventRoot),
            (nameof(RejectsMismatchedHitRoot), RejectsMismatchedHitRoot),
            (nameof(RejectsReversedMonotonicClick), RejectsReversedMonotonicClick),
            (nameof(AssemblerCreatesCompleteClick), AssemblerCreatesCompleteClick),
            (nameof(AssemblerResetsOnInterruptedInput), AssemblerResetsOnInterruptedInput),
            (nameof(AssemblerCombinesSafetyFlags), AssemblerCombinesSafetyFlags),
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

    private static void CompletesForSameTab()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(Click(100, 10, 10), FirstTab, Configuration));
        True(detector.Register(Click(350, 11, 9), FirstTab, Configuration));
    }

    private static void HonorsExactBoundaries()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(Click(100, 10, 10), FirstTab, Configuration));
        True(detector.Register(Click(600, 12, 12), FirstTab, Configuration));

        detector.Reset();
        False(detector.Register(Click(700, 10, 10, upX: 12, upY: 12), FirstTab, Configuration));
        True(detector.Register(Click(800, 10, 10, upX: 12, upY: 12), FirstTab, Configuration));
    }

    private static void UsesDownToDownTiming()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(
            Click(100, 10, 10, upTimestamp: 590),
            FirstTab,
            Configuration));
        True(detector.Register(Click(600, 10, 10), FirstTab, Configuration));

        detector.Reset();
        False(detector.Register(
            Click(100, 10, 10, upTimestamp: 550),
            FirstTab,
            Configuration));
        False(detector.Register(Click(601, 10, 10), FirstTab, Configuration));
    }

    private static void SlowClickBecomesNextCandidate()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(Click(100, 10, 10), FirstTab, Configuration));
        False(detector.Register(Click(601, 10, 10), FirstTab, Configuration));
        True(detector.Register(Click(700, 10, 10), FirstTab, Configuration));
    }

    private static void RejectsMovementBetweenClicks()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(Click(100, 10, 10), FirstTab, Configuration));
        False(detector.Register(Click(200, 13, 10), FirstTab, Configuration));
    }

    private static void RejectsMovementWithinClick()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(
            Click(100, 10, 10, maximumTravelX: 3),
            FirstTab,
            Configuration));
        False(detector.Register(Click(200, 10, 10), FirstTab, Configuration));
        True(detector.Register(
            Click(300, 10, 10, maximumTravelX: 2, maximumTravelY: 2),
            FirstTab,
            Configuration));

        detector.Reset();
        False(detector.Register(
            Click(400, 10, 10, upX: 13),
            FirstTab,
            Configuration));
        False(detector.Register(Click(450, 10, 10), FirstTab, Configuration));
    }

    private static void RejectsDifferentTabOrWindow()
    {
        DoubleClickDetector detector = new();
        TabTarget secondTab = FirstTab with { Identity = "window-1:tab-2" };
        TabTarget secondWindow = FirstTab with { RootWindow = 2 };

        False(detector.Register(Click(100, 10, 10), FirstTab, Configuration));
        False(detector.Register(Click(200, 10, 10), secondTab, Configuration));
        False(detector.Register(Click(300, 10, 10), secondWindow, Configuration));
    }

    private static void InjectedAndModifiedClicksResetSequence()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(Click(100, 10, 10), FirstTab, Configuration));
        False(detector.Register(
            Click(200, 10, 10) with { IsInjected = true },
            FirstTab,
            Configuration));
        False(detector.Register(Click(250, 10, 10), FirstTab, Configuration));
        False(detector.Register(
            Click(300, 10, 10) with { HasModifiers = true },
            FirstTab,
            Configuration));
        False(detector.Register(Click(350, 10, 10), FirstTab, Configuration));
    }

    private static void ResetClearsCandidate()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(Click(100, 10, 10), FirstTab, Configuration));
        detector.Reset();
        False(detector.Register(Click(200, 10, 10), FirstTab, Configuration));
    }

    private static void TripleClickCompletesOnlyOnce()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(Click(100, 10, 10), FirstTab, Configuration));
        True(detector.Register(Click(200, 10, 10), FirstTab, Configuration));
        False(detector.Register(Click(300, 10, 10), FirstTab, Configuration));
    }

    private static void HandlesTimestampWraparound()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(
            Click(
                uint.MaxValue - 10,
                10,
                10,
                upTimestamp: uint.MaxValue,
                downMonotonicTimestampMilliseconds: 1_000,
                upMonotonicTimestampMilliseconds: 1_010),
            FirstTab,
            Configuration));
        True(detector.Register(
            Click(5, 10, 10, downMonotonicTimestampMilliseconds: 1_016),
            FirstTab,
            Configuration));
    }

    private static void RejectsFullTimestampCycle()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(
            Click(100, 10, 10, downMonotonicTimestampMilliseconds: 1_000),
            FirstTab,
            Configuration));
        False(detector.Register(
            Click(
                200,
                10,
                10,
                downMonotonicTimestampMilliseconds: 1_000 + (1L << 32) + 100),
            FirstTab,
            Configuration));
    }

    private static void RejectsMismatchedEventRoots()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(
            Click(100, 10, 10, upRootWindow: 2),
            FirstTab,
            Configuration));
        False(detector.Register(Click(200, 10, 10), FirstTab, Configuration));
    }

    private static void RejectsZeroEventRoot()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(
            Click(100, 10, 10, downRootWindow: 0),
            FirstTab,
            Configuration));
        False(detector.Register(Click(200, 10, 10), FirstTab, Configuration));
    }

    private static void RejectsMismatchedHitRoot()
    {
        DoubleClickDetector detector = new();
        TabTarget mismatchedTarget = FirstTab with { RootWindow = 2 };
        False(detector.Register(
            Click(100, 10, 10),
            mismatchedTarget,
            Configuration));
        False(detector.Register(Click(200, 10, 10), FirstTab, Configuration));
    }

    private static void RejectsReversedMonotonicClick()
    {
        DoubleClickDetector detector = new();
        False(detector.Register(
            Click(
                100,
                10,
                10,
                downMonotonicTimestampMilliseconds: 1_000,
                upMonotonicTimestampMilliseconds: 999),
            FirstTab,
            Configuration));
        False(detector.Register(Click(200, 10, 10), FirstTab, Configuration));
    }

    private static void AssemblerCreatesCompleteClick()
    {
        MouseClickAssembler assembler = new();
        ClickAssemblyResult down = assembler.Register(ButtonEvent(
            MouseButtonEventKind.LeftDown,
            timestamp: 100,
            x: 10,
            y: 11));
        False(down.ResetSequence);
        Null(down.Click);

        ClickAssemblyResult up = assembler.Register(ButtonEvent(
            MouseButtonEventKind.LeftUp,
            timestamp: 120,
            x: 12,
            y: 10,
            maximumTravelX: 2,
            maximumTravelY: 1));
        MouseClick click = Required(up.Click);
        Equal((uint)100, click.DownTimestamp);
        Equal((uint)120, click.UpTimestamp);
        Equal(1L, click.DownRootWindow);
        Equal(1L, click.UpRootWindow);
        Equal(100L, click.DownInputSequence);
        Equal(120L, click.InputSequence);
        Equal(120L, click.PointerRevision);
        Equal(2, click.MaximumTravelX);
    }

    private static void AssemblerResetsOnInterruptedInput()
    {
        MouseClickAssembler assembler = new();
        assembler.Register(ButtonEvent(MouseButtonEventKind.LeftDown, 100, 10, 10));
        ClickAssemblyResult other = assembler.Register(ButtonEvent(
            MouseButtonEventKind.OtherButton,
            110,
            10,
            10));
        True(other.ResetSequence);

        ClickAssemblyResult orphanedUp = assembler.Register(ButtonEvent(
            MouseButtonEventKind.LeftUp,
            120,
            10,
            10));
        True(orphanedUp.ResetSequence);
        Null(orphanedUp.Click);
    }

    private static void AssemblerCombinesSafetyFlags()
    {
        MouseClickAssembler assembler = new();
        assembler.Register(ButtonEvent(
            MouseButtonEventKind.LeftDown,
            100,
            10,
            10) with
        { IsInjected = true });
        ClickAssemblyResult result = assembler.Register(ButtonEvent(
            MouseButtonEventKind.LeftUp,
            120,
            10,
            10) with
        { HasModifiers = true });

        MouseClick click = Required(result.Click);
        True(click.IsInjected);
        True(click.HasModifiers);
    }

    private static MouseClick Click(
        uint downTimestamp,
        int x,
        int y,
        uint? upTimestamp = null,
        int? upX = null,
        int? upY = null,
        int maximumTravelX = 0,
        int maximumTravelY = 0,
        long? downMonotonicTimestampMilliseconds = null,
        long? upMonotonicTimestampMilliseconds = null,
        long downRootWindow = 1,
        long? upRootWindow = null,
        long? downInputSequence = null,
        long? inputSequence = null,
        long? pointerRevision = null) =>
        new(
            new ScreenPoint(x, y),
            new ScreenPoint(upX ?? x, upY ?? y),
            downTimestamp,
            upTimestamp ?? unchecked(downTimestamp + 10),
            downMonotonicTimestampMilliseconds ?? downTimestamp,
            upMonotonicTimestampMilliseconds ??
                downMonotonicTimestampMilliseconds ??
                unchecked(downTimestamp + 10),
            IsInjected: false,
            HasModifiers: false,
            downRootWindow,
            upRootWindow ?? downRootWindow,
            downInputSequence ?? downTimestamp,
            inputSequence ?? unchecked(downTimestamp + 10),
            pointerRevision ?? unchecked(downTimestamp + 10),
            maximumTravelX,
            maximumTravelY);

    private static MouseButtonEvent ButtonEvent(
        MouseButtonEventKind kind,
        uint timestamp,
        int x,
        int y,
        int maximumTravelX = 0,
        int maximumTravelY = 0) =>
        new(
            kind,
            new ScreenPoint(x, y),
            timestamp,
            timestamp,
            IsInjected: false,
            HasModifiers: false,
            RootWindow: 1,
            InputSequence: timestamp,
            PointerRevision: timestamp,
            maximumTravelX,
            maximumTravelY);

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

    private static void Null<T>(T? value)
    {
        if (value is not null)
        {
            throw new InvalidOperationException("Expected null.");
        }
    }

    private static T Required<T>(T? value)
        where T : struct
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected a value.");
        }

        return value.Value;
    }

    private static void Equal<T>(T expected, T actual)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
        {
            throw new InvalidOperationException(
                $"Expected {expected}, but found {actual}.");
        }
    }
}
