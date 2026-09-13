using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using TabCloser.Windows.Interop;

namespace TabCloser.Windows.Diagnostics;

internal sealed class DiagnosticRecorder : IDisposable
{
    private const long MaximumLogBytes = 32 * 1024 * 1024;
    private readonly object _gate = new();
    private readonly RuntimeDiagnostics _diagnostics;
    private readonly StreamWriter _writer;
    private readonly System.Threading.Timer _timer;
    private NativeMethods.NativePoint? _lastCursor;
    private bool _disposed;
    private bool _stopped;

    internal DiagnosticRecorder(RuntimeDiagnostics diagnostics, string? directory = null)
    {
        _diagnostics = diagnostics;
        directory ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TabCloser", "Diagnostics");
        Directory.CreateDirectory(directory);
        LogPath = Path.Combine(directory,
            $"session-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Environment.ProcessId}-{Guid.NewGuid():N}.jsonl");
        _writer = new StreamWriter(new FileStream(
            LogPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true,
        };
        _timer = new System.Threading.Timer(Sample, null, Timeout.Infinite, Timeout.Infinite);
        try
        {
            _writer.WriteLine(JsonSerializer.Serialize(new
            {
                Kind = "Start",
                SchemaVersion = 4,
                Utc = DateTimeOffset.UtcNow,
                ProcessId = Environment.ProcessId,
                BuildId = typeof(DiagnosticRecorder).Module.ModuleVersionId,
                OsVersion = Environment.OSVersion.Version.ToString(),
                SampleIntervalMilliseconds = 1000,
                MaximumLogBytes,
            }));
            _timer.Change(1000, 1000);
        }
        catch
        {
            _timer.Dispose();
            _writer.Dispose();
            throw;
        }
    }

    internal string LogPath { get; }

    internal void SampleNow() => Sample(null);

    private void Sample(object? state)
    {
        // Sampling is independent of both the hook/UI thread and the UIA worker.
        if (!Monitor.TryEnter(_gate))
        {
            return;
        }

        try
        {
            if (_disposed || _stopped)
            {
                return;
            }

            if (_writer.BaseStream.Position >= MaximumLogBytes)
            {
                _writer.WriteLine("{\"Kind\":\"LogLimitReached\"}");
                _stopped = true;
                return;
            }

            WritePendingReports();

            bool? cursorChanged = null;
            if (NativeMethods.GetCursorPos(out NativeMethods.NativePoint cursor))
            {
                if (_lastCursor is NativeMethods.NativePoint previous)
                {
                    cursorChanged = cursor.X != previous.X || cursor.Y != previous.Y;
                }

                _lastCursor = cursor;
            }
            else
            {
                _lastCursor = null;
            }

            _writer.WriteLine(JsonSerializer.Serialize(new
            {
                Kind = "Sample",
                Runtime = _diagnostics.Snapshot(),
                Cs2Running = IsCs2Running(),
                Foreground = ReadForegroundCategory(),
                CursorChanged = cursorChanged,
                ModifierHeld = NativeMethods.HasModifierKeyDown(),
                MouseButtonHeld = NativeMethods.HasMouseButtonDown(),
            }));
        }
        catch (Exception exception)
        {
            _diagnostics.Error("Recorder", exception);
            _stopped = true;
        }
        finally
        {
            Monitor.Exit(_gate);
        }
    }

    private static bool? IsCs2Running()
    {
        try
        {
            Process[] processes = Process.GetProcessesByName("cs2");
            bool running = processes.Length != 0;
            foreach (Process process in processes)
            {
                process.Dispose();
            }

            return running;
        }
        catch
        {
            return null;
        }
    }

    private void WritePendingReports()
    {
        foreach (WorkerLifecycleReport report in _diagnostics.DrainWorkerReports())
        {
            _writer.WriteLine(JsonSerializer.Serialize(new
            {
                Kind = "WorkerLifecycle",
                Result = report,
            }));
        }

        foreach (InputSendReport report in _diagnostics.DrainSendReports())
        {
            _writer.WriteLine(JsonSerializer.Serialize(new
            {
                Kind = "SendInput",
                Result = report,
            }));
        }
    }

    private static string ReadForegroundCategory()
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(
                NativeMethods.GetForegroundWindow(), out uint processId);
            if (processId == Environment.ProcessId)
            {
                return "TabCloser";
            }

            using Process process = Process.GetProcessById(checked((int)processId));
            return process.ProcessName.ToLowerInvariant() switch
            {
                "chrome" => "Chrome",
                "cs2" => "CS2",
                _ => "Other",
            };
        }
        catch
        {
            return "Unavailable";
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Dispose();
            try
            {
                if (!_stopped)
                {
                    WritePendingReports();
                    _writer.WriteLine(JsonSerializer.Serialize(new
                    {
                        Kind = "Stop",
                        Runtime = _diagnostics.Snapshot(),
                    }));
                }

                _writer.Dispose();
            }
            catch
            {
                // Logging failure must not prevent application shutdown.
            }
        }
    }
}
