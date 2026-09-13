using TabCloser.Windows.Input;
using TabCloser.Windows.Diagnostics;

namespace TabCloser.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SingleInstance _singleInstance;
    private readonly Icon _applicationIcon;
    private readonly TabCloseService _service;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _restoreTimer;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _startupItem;
    private readonly RuntimeDiagnostics? _diagnostics;
    private bool _exiting;
    private bool _automaticRecoveryInProgress;

    public TrayApplicationContext(
        SingleInstance singleInstance,
        bool startedWithWindows,
        RuntimeDiagnostics? diagnostics = null)
    {
        _diagnostics = diagnostics;
        _singleInstance = singleInstance;
        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? (Icon)SystemIcons.Application.Clone();
        _service = new TabCloseService(diagnostics);
        _service.Start();

        _enabledItem = new ToolStripMenuItem("Enabled")
        {
            Checked = true,
            CheckOnClick = true,
        };
        _enabledItem.CheckedChanged += OnEnabledChanged;

        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = ReadStartupState(),
            CheckOnClick = true,
        };
        _startupItem.CheckedChanged += OnStartupChanged;

        ToolStripMenuItem hideTrayIconItem = new("Hide tray icon...");
        hideTrayIconItem.Click += OnHideTrayIcon;

        ToolStripMenuItem exitItem = new("Exit");
        exitItem.Click += (_, _) => ExitThread();

        ContextMenuStrip menu = new();
        menu.Items.Add(new ToolStripMenuItem("Double-click a Chrome tab to close it")
        {
            Enabled = false,
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_startupItem);
        if (diagnostics is not null)
        {
            menu.Items.Add(new ToolStripMenuItem("Diagnostic session v4 (auto recovery)")
            {
                Enabled = false,
            });
            ToolStripMenuItem restartWorkerItem = new("Restart worker (diagnostic)");
            restartWorkerItem.Click += OnRestartWorker;
            menu.Items.Add(restartWorkerItem);
        }
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(hideTrayIconItem);
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _applicationIcon,
            Text = "TabCloser",
            Visible = ReadInitialTrayVisibility(startedWithWindows),
        };

        _restoreTimer = new System.Windows.Forms.Timer
        {
            Interval = 200,
        };
        _restoreTimer.Tick += OnRestoreTimerTick;
        _restoreTimer.Start();
    }

    protected override void ExitThreadCore()
    {
        _exiting = true;
        _restoreTimer.Stop();
        _restoreTimer.Tick -= OnRestoreTimerTick;
        _restoreTimer.Dispose();
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _applicationIcon.Dispose();
        _service.Dispose();
        base.ExitThreadCore();
    }

    private async void OnRestartWorker(object? sender, EventArgs eventArgs)
    {
        if (sender is not ToolStripMenuItem item || _exiting)
        {
            return;
        }

        item.Enabled = false;
        try
        {
            WorkerRestartResult result = await _service.RestartWorkerForDiagnosticsAsync();
            if (_exiting)
            {
                return;
            }

            string message = result == WorkerRestartResult.Restarted
                ? "A replacement worker thread was started in the same TabCloser process. " +
                  "The mouse hook was not restarted.\n\nWait 10 seconds, then try two fresh " +
                  "double-clicks on disposable Chrome tabs. This does not confirm recovery yet."
                : $"Worker restart result: {result}.\n\nIf the restart timed out or failed, " +
                  "tab closing remains suspended. No second worker was intentionally started " +
                  "alongside the old one. Keep the app running and report this result.";
            MessageBox.Show(message, "TabCloser diagnostic worker test",
                MessageBoxButtons.OK, result == WorkerRestartResult.Restarted
                    ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            _diagnostics?.Error("RestartWorkerMenu", exception);
            if (!_exiting)
            {
                MessageBox.Show("The diagnostic action failed. Keep the app running and report this result.",
                    "TabCloser diagnostics", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
        finally
        {
            if (!_exiting)
            {
                item.Enabled = true;
            }
        }
    }

    private void OnHideTrayIcon(object? sender, EventArgs eventArgs)
    {
        DialogResult result = MessageBox.Show(
            "TabCloser will keep running while its tray icon is hidden.\n\n" +
            "This choice is remembered. If Start with Windows is enabled, " +
            "TabCloser will start hidden after sign-in. Launch TabCloser.exe " +
            "again to show the icon.",
            "Hide tray icon?",
            MessageBoxButtons.OKCancel,
            MessageBoxIcon.Information);

        if (result == DialogResult.OK)
        {
            try
            {
                if (_startupItem.Checked && !StartupRegistration.RefreshIfEnabled())
                {
                    throw new InvalidOperationException(
                        "The startup registration changed. Enable Start with Windows again, then retry.");
                }

                TrayIconSettings.SetHidden(hidden: true);
                _notifyIcon.Visible = false;
            }
            catch (Exception exception)
            {
                MessageBox.Show(
                    $"The hidden tray setting could not be saved.\n\n{exception.Message}",
                    "TabCloser",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }

    private async void OnRestoreTimerTick(object? sender, EventArgs eventArgs)
    {
        _diagnostics?.UiPulse();
        if (_singleInstance.ConsumeTrayIconRestoreRequest())
        {
            _notifyIcon.Visible = true;
            TryClearHiddenState(showWarning: true);
        }

        if (_exiting || _automaticRecoveryInProgress)
        {
            return;
        }

        _automaticRecoveryInProgress = true;
        try
        {
            WorkerRestartResult? result = await _service.RecoverWorkerIfRequestedAsync();
            if (!_exiting && result is WorkerRestartResult.TimedOut or WorkerRestartResult.Failed)
            {
                _notifyIcon.Text = "TabCloser (recovery stopped)";
                _notifyIcon.ShowBalloonTip(5000, "TabCloser recovery stopped",
                    "The worker could not be restarted safely. Exit and relaunch TabCloser to resume.",
                    ToolTipIcon.Warning);
            }
        }
        catch (Exception exception)
        {
            _diagnostics?.Error("AutomaticRecovery", exception);
        }
        finally
        {
            _automaticRecoveryInProgress = false;
        }
    }

    private void OnEnabledChanged(object? sender, EventArgs eventArgs)
    {
        _service.SetEnabled(_enabledItem.Checked);
        _notifyIcon.Text = _enabledItem.Checked
            ? "TabCloser"
            : "TabCloser (paused)";
    }

    private void OnStartupChanged(object? sender, EventArgs eventArgs)
    {
        try
        {
            StartupRegistration.SetEnabled(_startupItem.Checked);
        }
        catch (Exception exception)
        {
            _startupItem.CheckedChanged -= OnStartupChanged;
            _startupItem.Checked = !_startupItem.Checked;
            _startupItem.CheckedChanged += OnStartupChanged;

            MessageBox.Show(
                $"The startup setting could not be changed.\n\n{exception.Message}",
                "TabCloser",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private static bool ReadStartupState()
    {
        try
        {
            bool isEnabled = StartupRegistration.IsEnabled();
            if (isEnabled && !StartupRegistration.RefreshIfEnabled())
            {
                return false;
            }

            return isEnabled;
        }
        catch
        {
            return false;
        }
    }

    private static bool ReadInitialTrayVisibility(bool startedWithWindows)
    {
        if (startedWithWindows)
        {
            try
            {
                return !TrayIconSettings.IsHidden();
            }
            catch
            {
                return true;
            }
        }

        TryClearHiddenState(showWarning: true);
        return true;
    }

    private static void TryClearHiddenState(bool showWarning)
    {
        try
        {
            TrayIconSettings.SetHidden(hidden: false);
        }
        catch (Exception exception)
        {
            if (showWarning)
            {
                MessageBox.Show(
                    $"The tray icon was restored, but the saved setting could not be changed.\n\n" +
                    exception.Message,
                    "TabCloser",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }
    }
}
