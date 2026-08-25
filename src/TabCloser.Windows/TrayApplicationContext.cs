using TabCloser.Windows.Input;

namespace TabCloser.Windows;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly Icon _applicationIcon;
    private readonly TabCloseService _service;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _enabledItem;
    private readonly ToolStripMenuItem _startupItem;

    public TrayApplicationContext()
    {
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

        _startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = ReadStartupState(),
            CheckOnClick = true,
        };
        _startupItem.CheckedChanged += OnStartupChanged;

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
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        _notifyIcon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = _applicationIcon,
            Text = "TabCloser",
            Visible = true,
        };
    }

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _notifyIcon.ContextMenuStrip?.Dispose();
        _notifyIcon.Dispose();
        _applicationIcon.Dispose();
        _service.Dispose();
        base.ExitThreadCore();
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
            return StartupRegistration.IsEnabled();
        }
        catch
        {
            return false;
        }
    }
}
