using TabCloser.Windows.Input;

namespace TabCloser.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly SingleInstance _singleInstance;
    private readonly Icon _applicationIcon;
    private readonly TabCloseService _service;
    private readonly NotifyIcon _notifyIcon;
    private readonly System.Windows.Forms.Timer _restoreTimer;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _edgeItem;
    private readonly ToolStripMenuItem _startupItem;

    public TrayApplicationContext(SingleInstance singleInstance, bool startedWithWindows)
    {
        _singleInstance = singleInstance;
        _applicationIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath)
            ?? (Icon)SystemIcons.Application.Clone();
        _service = new TabCloseService();
        _service.Start();

        _enabledItem = new ToolStripMenuItem("Enabled")
        {
            Checked = true,
            CheckOnClick = true,
        };
        _enabledItem.CheckedChanged += OnEnabledChanged;

        _edgeItem = new ToolStripMenuItem("Microsoft Edge top tabs (experimental)")
        {
            Checked = false,
            CheckOnClick = true,
        };
        _edgeItem.CheckedChanged += OnEdgeSupportChanged;

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
        menu.Items.Add(new ToolStripMenuItem("Double-click a supported top tab to close it")
        {
            Enabled = false,
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_enabledItem);
        menu.Items.Add(_edgeItem);
        menu.Items.Add(_startupItem);
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

    private void OnRestoreTimerTick(object? sender, EventArgs eventArgs)
    {
        if (_singleInstance.ConsumeTrayIconRestoreRequest())
        {
            _notifyIcon.Visible = true;
            TryClearHiddenState(showWarning: true);
        }
    }

    private void OnEnabledChanged(object? sender, EventArgs eventArgs)
    {
        _service.SetEnabled(_enabledItem.Checked);
        _notifyIcon.Text = _enabledItem.Checked
            ? "TabCloser"
            : "TabCloser (paused)";
    }

    private void OnEdgeSupportChanged(object? sender, EventArgs eventArgs)
    {
        bool requestedState = _edgeItem.Checked;
        if (requestedState)
        {
            DialogResult confirmation = MessageBox.Show(
                "Edge can also close tabs on double-click in some regions and " +
                "profiles. TabCloser cannot detect that per-profile setting.\n\n" +
                "Before enabling Edge for this session, check every Edge profile " +
                "you will use. If a profile shows 'Use double-click to close " +
                "browser tabs' under Settings > Appearance, turn it off. If both " +
                "features are enabled, two tabs might close.\n\n" +
                "TabCloser supports only Edge's normal horizontal top tabs. " +
                "Its experimental classifier is designed to ignore vertical tabs.",
                "Enable experimental Edge support?",
                MessageBoxButtons.OKCancel,
                MessageBoxIcon.Warning);
            if (confirmation != DialogResult.OK)
            {
                SetEdgeItemCheckedWithoutEvent(checkedState: false);
                return;
            }
        }

        _service.SetEdgeEnabled(requestedState);
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

    private void SetEdgeItemCheckedWithoutEvent(bool checkedState)
    {
        _edgeItem.CheckedChanged -= OnEdgeSupportChanged;
        _edgeItem.Checked = checkedState;
        _edgeItem.CheckedChanged += OnEdgeSupportChanged;
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
