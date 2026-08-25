using DoubleClickCloseTab.Windows.Input;
using DoubleClickCloseTab.Windows.Interop;

namespace DoubleClickCloseTab.Windows.Tests;

internal static class Program
{
    public static int Main()
    {
        (string Name, Action Test)[] tests =
        [
            (nameof(CompleteBatchSucceeds), CompleteBatchSucceeds),
            (nameof(ZeroInputsNeedsNoRecovery), ZeroInputsNeedsNoRecovery),
            (nameof(PartialBatchReleasesMiddleButton), PartialBatchReleasesMiddleButton),
            (nameof(FailedRecoveryDoesNotLoop), FailedRecoveryDoesNotLoop),
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
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
        {
            throw new InvalidOperationException(
                $"Expected {expected}, but found {actual}.");
        }
    }
}
